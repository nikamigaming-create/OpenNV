using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Presentation.Rendering;
using OpenNV.Runtime.World.Cells;
using OpenNV.Runtime.World.Actors;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime;

public partial class RuntimeCoordinator
{
    private FalloutExteriorGrid? _nativeExteriorGrid;
    private readonly Dictionary<string, Texture2D> _nativeLandscapeTextures = new(StringComparer.OrdinalIgnoreCase);
    private Node3D? _nativeStreamRoot;
    private sealed record PreparedExteriorGrid(FalloutExteriorGridScene Grid, IReadOnlyDictionary<FalloutFormKey, FalloutLandscapeTransport?> Landscapes,
        IReadOnlyDictionary<string, FalloutNifFile> Models);
    private Task<PreparedExteriorGrid>? _nativeGridRead;
    private readonly List<Task> _retiredGridReads = [];
    private CancellationTokenSource? _nativeGridReadCancellation;
    private FalloutExteriorGridScene? _nativeGridPending;
    private Queue<(string Source, Func<bool> Upload)>? _nativeGridUploads;
    private readonly List<Node3D> _nativeGridStaged = [];
    private readonly List<RuntimeNativeLandscapeTransportBuilder.Assembly> _nativeLandPreparations = [];
    private readonly HashSet<RuntimeNativeNifMeshBuilder.StaticAssembly> _nativeStaticPreparations = [];
    private readonly Dictionary<FalloutFormKey, Node3D> _nativeGridModels = [];
    private readonly Dictionary<FalloutFormKey, RuntimeNativeNpc> _nativeGridNpcModels = [];
    private (int X, int Y)? _nativeGridFailed;
    private readonly NativePublicationTiming _nativeGridUploadTiming = new();
    private readonly NativePublicationTiming _nativePrototypeRetirementTiming = new();
    private double _nativeGridCommitMilliseconds;
    private IReadOnlyDictionary<string, double>? _nativeGridCommitPhases;
    private (int X, int Y)? _nativeGridTarget;
    private string? _nativeGridError;
    private readonly HashSet<(int X, int Y)> _nativeWalkableGrid = [];
    // Accepted source CELL residency belongs to the scene root, including CELLs
    // which intentionally have no LAND mesh. Pending roots/grids cannot replace it.
    private readonly ConditionalWeakTable<Node3D, FalloutCellDefinition[]> _nativeExteriorCells = new();
    private IReadOnlyList<FalloutCellDefinition> NativeExteriorCells(Node3D root) =>
        _nativeExteriorCells.TryGetValue(root, out var cells) ? cells :
            throw new InvalidOperationException("Exterior root has no accepted source CELL grid.");
    private void PublishNativeExteriorCells(Node3D root, FalloutExteriorGridScene grid)
    {
        var cells = grid.Cells.ToArray();
        if (cells.Any(cell => cell.Coordinates is null || cell.Worldspace != grid.Scene.Cell.Worldspace))
            throw new InvalidDataException("Exterior residency has invalid source CELL identities.");
        _nativeExteriorCells.Remove(root);
        _nativeExteriorCells.Add(root, cells);
    }
    private readonly Dictionary<FalloutFormKey, ulong> _nativeLandLastUse = [];
    private readonly Queue<HashSet<string>> _nativeRecentModelSets = [];
    private readonly NativePrototypeRetirement<RuntimeNativeNifPrototype> _nativePrototypeRetirements = new();
    private ulong _nativePrototypeRetirementFrame = ulong.MaxValue;
    private ulong _nativeGridGeneration;
    private bool NativeCollisionResident(Vector3 position)
    {
        if (_nativeDoorLoading || _nativeActiveCell is null || _nativeCurrentCellRoot is null) return false;
        if (_nativeActiveCell.Cell.Worldspace is null) return true;
        var width = 4096 * _configuration.World.GameUnitsToMeters;
        var radius = _configuration.Player.CapsuleRadiusMeters;
        for (var x = (int)MathF.Floor((position.X - radius) / width); x <= (int)MathF.Floor((position.X + radius) / width); x++)
            for (var y = (int)MathF.Floor((-position.Z - radius) / width); y <= (int)MathF.Floor((-position.Z + radius) / width); y++)
                if (!_nativeWalkableGrid.Contains((x, y))) return false;
        return true;
    }
    private object CaptureNativeStreamingState(Node[] cellNodes) => new
    {
        reading = _nativeGridRead is not null,
        uploadsRemaining = _nativeGridUploads?.Count ?? 0,
        residentCells = _nativeWalkableGrid.Count,
        warmReferences = _nativeReferencePresentation?.WarmNodeCount ?? 0,
        terrainCacheCells = cellNodes.Count(node => node is RuntimeNativeLandscapeTransport),
        target = _nativeGridTarget is { } target ? new[] { target.X, target.Y } : null,
        preparedAhead = _nativeGridUploads?.Count == 0 && _nativeGridPending is not null,
        lastUploadMilliseconds = _nativeGridUploadTiming.LastBatchMilliseconds,
        maximumUploadMilliseconds = _nativeGridUploadTiming.MaximumBatchMilliseconds,
        maximumUploadSource = _nativeGridUploadTiming.MaximumStepSource,
        lastUploadBatchMilliseconds = _nativeGridUploadTiming.LastBatchMilliseconds,
        maximumUploadBatchMilliseconds = _nativeGridUploadTiming.MaximumBatchMilliseconds,
        lastUploadStepMilliseconds = _nativeGridUploadTiming.LastStepMilliseconds,
        lastUploadStepSource = _nativeGridUploadTiming.LastStepSource,
        maximumUploadStepMilliseconds = _nativeGridUploadTiming.MaximumStepMilliseconds,
        maximumUploadStepSource = _nativeGridUploadTiming.MaximumStepSource,
        uploadSteps = _nativeGridUploadTiming.Steps,
        faultedUploadSteps = _nativeGridUploadTiming.FaultedSteps,
        uploadBatches = _nativeGridUploadTiming.Batches,
        preparingNpcs = _nativeGridNpcPreparations.Count,
        preparedNpcBodies = _nativeGridNpcModels.Count,
        preparingCreatures = _nativeGridCreaturePreparations.Count,
        preparedCreatureBodies = _nativeGridCreatureModels.Count,
        pendingPrototypeRetirements = _nativePrototypeRetirements.Count,
        prototypeRetirementSteps = _nativePrototypeRetirementTiming.Steps,
        prototypeRetirementFaults = _nativePrototypeRetirementTiming.FaultedSteps,
        maximumPrototypeRetirementMilliseconds = _nativePrototypeRetirementTiming.MaximumStepMilliseconds,
        maximumPrototypeRetirementSource = _nativePrototypeRetirementTiming.MaximumStepSource,
        lastCommitMilliseconds = _nativeGridCommitMilliseconds,
        lastCommitPhasesMilliseconds = _nativeGridCommitPhases,
        error = _nativeGridError
    };

    private void AdvanceNativeExteriorStreaming(double delta)
    {
        NativePublicationBudget.SetPending(false, false);
        if (_nativeStreamRoot is not null && _nativeStreamRoot != _nativeCurrentCellRoot)
        {
            RetireNativeGridPreparation();
            _nativeStreamRoot = null; _nativeGridFailed = null;
            _nativeLandLastUse.Clear(); _nativeRecentModelSets.Clear();
        }
        if (_nativePlayer is not { } player || _nativeActiveCell?.Cell.Worldspace is not { } world ||
            _nativeDoorLoading || GetTree().Paused) return;
        var units = _configuration.World.GameUnitsToMeters;
        var position = player.GlobalPosition;
        var coordinates = ((int)MathF.Floor(position.X / (4096 * units)), (int)MathF.Floor(-position.Z / (4096 * units)));
        try
        {
            // Preparation may lead the player, but CELL/gameplay ownership may
            // change only after ordinary movement reaches the requested cell.
            if (_nativeGridTarget is { } requested && coordinates != requested && coordinates != _nativeActiveCell.Cell.Coordinates)
            {
                RetireNativeGridPreparation();
            }
            if (_nativeGridRead is { IsCompleted: true } read)
            {
                _nativeGridRead = null;
                StageNativeExteriorGrid(read.GetAwaiter().GetResult());
            }
            if (_nativeGridUploads is null) AdvanceNativePrototypeRetirement();
            if (_nativeGridUploads is { } uploads)
            {
                NativePublicationBudget.SetPending(false, uploads.Count != 0);
                var started = Stopwatch.GetTimestamp();
                // A pending decode or partial body yields. Visit each queued
                // item at most once this frame, without waiting on a worker.
                try
                {
                    var remaining = uploads.Count;
                    while (remaining-- > 0 && NativePublicationBudget.TryBegin(false, out var operationStarted) && uploads.TryDequeue(out var upload))
                    {
                        var faulted = true;
                        try { if (!upload.Upload()) uploads.Enqueue(upload); faulted = false; }
                        finally { _nativeGridUploadTiming.RecordStep(upload.Source, NativePublicationBudget.Complete(operationStarted), faulted); }
                    }
                }
                finally { _nativeGridUploadTiming.RecordBatch(Stopwatch.GetElapsedTime(started).TotalMilliseconds); }
                // Give ready uploads first use of the allowance. A pending
                // worker can leave spare time for detached prototype cleanup.
                AdvanceNativePrototypeRetirement();
                if (uploads.Count == 0 && coordinates == _nativeGridPending!.Scene.Cell.Coordinates) CommitNativeExteriorGrid();
                return;
            }
            if (_nativeGridRead is not null) return;
            var target = coordinates;
            if (coordinates == _nativeActiveCell.Cell.Coordinates)
            {
                var ahead = FalloutExteriorStreamTarget.Predict(position.X, -position.Z, player.Velocity.X, -player.Velocity.Z, units);
                target = (ahead.X, ahead.Y);
            }
            if (target == _nativeActiveCell.Cell.Coordinates || target == _nativeGridFailed) return;
            var currentRoot = _nativeCurrentCellRoot ?? throw new InvalidOperationException("Exterior stream has no current CELL root.");
            _nativeStreamRoot = currentRoot;
            _nativeGridTarget = target;
            _nativeGridError = null;
            var resident = _nativeWalkableGrid.ToHashSet();
            var residentLand = currentRoot.GetChildren().OfType<RuntimeNativeLandscapeTransport>()
                .Select(land => land.Source.ActiveCell).ToHashSet();
            var residentModels = _nativeNifPrototypes.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var content = RuntimeLiveContentSource.Current!;
            _nativeGridReadCancellation = new();
            var cancellation = _nativeGridReadCancellation.Token;
            _nativeGridRead = Task.Run(async () =>
            {
                var (grid, lands) = await FalloutContentWorkers.Run(() =>
                {
                    var resolved = ResolveExterior(world, [(target.Item1 + .5f) * 4096, (target.Item2 + .5f) * 4096, position.Y / units]);
                    var landscapes = resolved.Cells.Where(cell => !resident.Contains(cell.Coordinates!.Value) || !residentLand.Contains(cell.FormKey)).ToDictionary(cell => cell.FormKey,
                        cell =>
                        {
                            cancellation.ThrowIfCancellationRequested();
                            return FalloutLandscapeTransportResolver.ResolveCell(_nativePluginStack!, cell, resolved.PersistentCell);
                        });
                    foreach (var land in landscapes.Values.OfType<FalloutLandscapeTransport>())
                    {
                        cancellation.ThrowIfCancellationRequested();
                        RuntimeNativeLandscapeTransportBuilder.Prepare(land, units);
                    }
                    foreach (var path in landscapes.Values.OfType<FalloutLandscapeTransport>().SelectMany(land => land.Textures.Values)
                        .SelectMany(texture => new[] { texture.DiffusePath, texture.NormalPath }).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase))
                    {
                        cancellation.ThrowIfCancellationRequested();
                        _ = content.TryRead(path, null, out _, out _);
                    }
                    return (resolved, landscapes);
                }, cancellation);
                var models = await PrepareStaticModels(grid.Scene, content, residentModels, units, cancellation);
                return new PreparedExteriorGrid(grid, lands, models);
            });
        }
        catch (Exception error)
        {
            _nativeGridError = error.Message; _nativeGridFailed = _nativeGridTarget ?? coordinates;
            RetireNativeGridPreparation();
            GD.PushError($"OPENNV_EXTERIOR_STREAM_FAIL cell={coordinates}: {error.Message}");
        }
    }

    private void CancelNativeGridRead()
    {
        NativePublicationBudget.SetPending(false, false);
        foreach (var preparation in _nativeLandPreparations) preparation.Dispose();
        _nativeLandPreparations.Clear();
        foreach (var preparation in _nativeStaticPreparations) preparation.Dispose();
        _nativeStaticPreparations.Clear();
        _retiredGridReads.RemoveAll(task => task.IsCompleted);
        foreach (var preparation in _nativeGridNpcPreparations)
        {
            _retiredGridReads.Add(preparation.ReadTask);
            preparation.Dispose();
        }
        _nativeGridNpcPreparations.Clear();
        foreach (var preparation in _nativeGridCreaturePreparations)
        {
            _retiredGridReads.Add(preparation.ReadTask);
            preparation.Dispose();
        }
        _nativeGridCreaturePreparations.Clear();
        foreach (var geometry in _nativeGridCreatureModels.Values) geometry.Dispose();
        _nativeGridCreatureModels.Clear();
        _nativeGridReadCancellation?.Cancel();
        _nativeGridReadCancellation?.Dispose(); _nativeGridReadCancellation = null;
        if (_nativeGridRead is { } abandoned)
        {
            _retiredGridReads.Add(abandoned);
            _ = abandoned.ContinueWith(task => { _ = task.Exception; }, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
        _nativeGridRead = null;
    }

    private void RetireNativeGridPreparation()
    {
        CancelNativeGridRead();
        // Prepared models already own native callbacks, but are not yet in
        // the resident presentation index. Retire them before a replacement
        // CELL can bind the same references; deferred deletion is too late.
        foreach (var node in _nativeGridStaged.Where(GodotObject.IsInstanceValid))
        {
            // A detached actor has never entered the tree, so Free cannot
            // deliver its normal _ExitTree binding retirement callback.
            RetireNativeActorBindings(node);
            NativeOwnedAnimationSoundPlayer.UnloadSourceLoops(node);
            node.Free();
        }
        _nativeGridStaged.Clear(); _nativeGridModels.Clear();
        foreach (var actor in _nativeGridNpcModels.Values.Where(GodotObject.IsInstanceValid)) actor.Free();
        _nativeGridNpcModels.Clear();
        _nativeGridUploads = null; _nativeGridPending = null; _nativeGridTarget = null;
    }

    public override void _ExitTree()
    {
        if (_retiringNativeSession) GD.Print("OPENNV_NATIVE_SESSION_RETIRE phase=enter");
        CancelNativeManualSave("Native session retired before the pending manual save could commit.");
        DetachNativeCloseRequest();
        CancelNativeLauncherEntry();
        CancelNativeGridRead();
        if (!_retiringNativeSession) return;
        _nativeDroppedItems?.RetireAll();
        // Session retirement has drained source workers. Detached prototypes are outside
        // the scene tree and must be released with the retired record owners.
        var retired = 0;
        foreach (var (path, prototype) in _nativeNifPrototypes)
        {
            if (_options.ContainsKey("live-harness") && retired % 16 == 0)
                GD.Print($"OPENNV_NATIVE_SESSION_RETIRE phase=prototype index={retired} source={path}");
            prototype.Scene.Root.Free(); retired++;
        }
        GD.Print($"OPENNV_NATIVE_SESSION_RETIRE phase=prototypes-freed count={retired}");
        _nativeNifPrototypes.Clear();
        _nativePrototypeRetirements.Drain(prototype => prototype.Scene.Root.Free());
        _nativePrewarmedInitialCellRoot?.Free(); _nativePrewarmedInitialCellRoot = null;
        _nativeReferences?.Dispose(); _nativePluginStack?.Dispose();
        _nativeIndexCancellation.Dispose();
        GD.Print("OPENNV_NATIVE_SESSION_RETIRE phase=session-source-views-disposed immutableData=owned-content-lifetime");
    }

    private void StageNativeExteriorGrid(PreparedExteriorGrid prepared)
    {
        var grid = prepared.Grid with { Scene = _nativeReferences!.ComposeResidency(prepared.Grid.Scene, _configuration.World.GameUnitsToMeters, prepared.Grid.Cells) };
        var root = _nativeCurrentCellRoot!;
        _nativeGridPending = grid; _nativeGridUploads = new();
        var oldLand = root.GetChildren().OfType<RuntimeNativeLandscapeTransport>().Select(land => land.Source.ActiveCell).ToHashSet();
        foreach (var cell in grid.Cells.Where(cell => !oldLand.Contains(cell.FormKey)))
        {
            if (prepared.Landscapes[cell.FormKey] is not { } source) continue;
            var preparation = new RuntimeNativeLandscapeTransportBuilder.Assembly(source,
                _configuration.World.GameUnitsToMeters, _nativeLandscapeTextures);
            _nativeLandPreparations.Add(preparation);
            _nativeGridUploads.Enqueue((cell.FormKey.ToString(), () =>
            {
                if (!preparation.Advance()) return false;
                var land = preparation.Take();
                preparation.Dispose(); _nativeLandPreparations.Remove(preparation);
                root.AddChild(land); GamebryoReferenceEnableRuntime.Apply(land, false); _nativeGridStaged.Add(land);
                return true;
            }
            ));
        }
        var previous = _nativeActiveCell!.References.Select(reference => reference.FormKey).ToHashSet();
        foreach (var reference in grid.Scene.References.Where(reference => !previous.Contains(reference.FormKey) &&
            _nativeReferencePresentation?.HasPrepared(reference.FormKey) != true))
        {
            var baseObject = grid.Scene.BaseObjects.GetValueOrDefault(reference.Base);
            if (baseObject?.ModelPath is { } modelPath && modelPath.EndsWith(".spt", StringComparison.OrdinalIgnoreCase))
            {
                _nativeReferenceDivergences[reference.FormKey.ToString()] =
                    $"Owned SpeedTree model {modelPath} procedural geometry decoder is unbound.";
                continue;
            }
            ExteriorNpcPreparation? npc = null;
            ExteriorCreaturePreparation? creature = null;
            RuntimeNativeNifMeshBuilder.StaticAssembly? staticAssembly = null;
            var staticChecked = false;
            _nativeGridUploads.Enqueue((reference.FormKey.ToString(), () =>
            {
                // Decode/upload native resources without entering the live tree.
                // Ready callbacks can start authored sounds and gameplay bindings;
                // they belong to committed residency, never preparation ahead.
                Node3D? stagedReference = null;
                RuntimeNativeNpc? preparedNpc = null;
                RuntimeNativeCreature.PreparedGeometry? preparedCreature = null;
                RuntimeNativeNifScene? preparedStatic = null;
                try
                {
                    var model = grid.Scene.BaseObjects[reference.Base].ModelPath;
                    var definition = grid.Scene.BaseObjects[reference.Base];
                    var cachedPrototype = model is null ? null : _nativeNifPrototypes.GetValueOrDefault(model);
                    var nif = model is null ? null : prepared.Models.GetValueOrDefault(model) ?? cachedPrototype?.Source;
                    var enabled = _nativeReferences!.IsEnabled(reference.FormKey);
                    if (!enabled && staticAssembly is not null)
                    { _nativeStaticPreparations.Remove(staticAssembly); staticAssembly.Dispose(); staticAssembly = null; }
                    if (definition.Signature is not ("NPC_" or "CREA") && model is not null && nif is not null &&
                        enabled)
                    {
                        if (cachedPrototype is { RequiresFreshOwners: false } && staticAssembly is not null)
                        { _nativeStaticPreparations.Remove(staticAssembly); staticAssembly.Dispose(); staticAssembly = null; }
                        if ((cachedPrototype is null || cachedPrototype.RequiresFreshOwners) && !staticChecked)
                        {
                            if (_nativeStaticPreparations.Count >= FalloutContentWorkers.Concurrency) return false;
                            staticAssembly = RuntimeNativeNifMeshBuilder.StaticAssembly.TryCreate(nif,
                                _configuration.World.GameUnitsToMeters, RuntimeLiveContentSource.Current!);
                            staticChecked = true;
                            if (staticAssembly is not null) _nativeStaticPreparations.Add(staticAssembly);
                        }
                        if (staticAssembly is not null)
                        {
                            if (!staticAssembly.Advance()) return false;
                            preparedStatic = staticAssembly.Take();
                            _nativeStaticPreparations.Remove(staticAssembly); staticAssembly.Dispose(); staticAssembly = null;
                            var prototype = new RuntimeNativeNifPrototype(nif, _configuration.World.GameUnitsToMeters, preparedStatic);
                            if (cachedPrototype is null && !prototype.RequiresFreshOwners)
                            {
                                prototype.Scene.Root.Name = $"Prototype_{reference.Base}";
                                _nativeNifPrototypes.Add(model, prototype); preparedStatic = null;
                            }
                        }
                    }
                    if (grid.Scene.BaseObjects[reference.Base].Signature == "NPC_" && _nativeReferences!.IsEnabled(reference.FormKey))
                    {
                        if (npc is null)
                        {
                            // Bound both jobs and completed bodies awaiting GPU
                            // publication; worker slots alone do not bound memory.
                            if (_nativeGridNpcPreparations.Count + _nativeGridCreaturePreparations.Count >= FalloutContentWorkers.Concurrency) return false;
                            npc = PrepareExteriorNpc(reference, _nativeGridReadCancellation!.Token);
                        }
                        if (npc is not null)
                        {
                            var armor = _nativeReferences.EquippedArmor(reference.FormKey,
                                _nativeOpeningStageDriver?.PlayerLevel ?? _nativeOpeningRestore?.State.Vitals?.Level ?? 1, _nativeGlobals);
                            if (!armor.SequenceEqual(npc.Appearance.EquippedArmor))
                            { ReleaseExteriorNpc(npc); npc = null; return false; }
                            if (!npc.Advance(this, grid.Scene, out preparedNpc)) return false;
                        }
                    }
                    if (definition.Signature == "CREA" && enabled)
                    {
                        if (creature is null)
                        {
                            if (_nativeGridCreaturePreparations.Count + _nativeGridNpcPreparations.Count >= FalloutContentWorkers.Concurrency) return false;
                            creature = PrepareExteriorCreature(reference, _nativeGridReadCancellation!.Token);
                        }
                        if (creature is not null)
                        {
                            var stage = creature.NextStage;
                            var started = Stopwatch.GetTimestamp();
                            var complete = creature.Advance(_configuration.World.GameUnitsToMeters, out preparedCreature);
                            var milliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                            if (_options.ContainsKey("live-harness") && milliseconds > 16.667)
                                GD.Print($"OPENNV_NATIVE_CREATURE_ASSEMBLY_SLOW reference={reference.FormKey} stage={stage} milliseconds={milliseconds:F3}");
                            if (!complete) return false;
                        }
                    }
                    else if (creature is not null) { ReleaseExteriorCreature(creature); creature = null; }
                    if (preparedCreature is not null)
                    {
                        _nativeGridCreatureModels.Add(reference.FormKey, preparedCreature);
                        preparedCreature = null;
                    }
                    else if (preparedNpc is not null)
                    {
                        // Keep only assembled geometry ahead of residency. The
                        // normal placement path binds AI/combat/save callbacks
                        // immediately before attachment at grid publication.
                        _nativeGridNpcModels.Add(reference.FormKey, preparedNpc);
                        preparedNpc = null;
                    }
                    else
                    {
                        stagedReference = new Node3D();
                        PlaceNativeReference(stagedReference, grid.Scene, reference, observe: false,
                            preparedModel: nif, preparedStaticRoot: preparedStatic?.Root);
                        var children = stagedReference.GetChildren();
                        Node3D[] nodes;
                        using ((Godot.Collections.Array)children) nodes = children.OfType<Node3D>().ToArray();
                        if (nodes.Length > 1) throw new InvalidDataException("A source reference must have one presentation root.");
                        foreach (var node in nodes)
                        {
                            stagedReference.RemoveChild(node);
                            _nativeGridStaged.Add(node); _nativeGridModels.Add(reference.FormKey, node);
                        }
                        if (preparedStatic is not null && nodes.Contains(preparedStatic.Root)) preparedStatic = null;
                    }
                }
                catch (Exception error) when (error is IOException or InvalidDataException or NotSupportedException or InvalidOperationException)
                {
                    if (staticAssembly is not null)
                    { _nativeStaticPreparations.Remove(staticAssembly); staticAssembly.Dispose(); staticAssembly = null; }
                    if (preparedNpc is not null && GodotObject.IsInstanceValid(preparedNpc) && preparedNpc.GetParent() is null) preparedNpc.Free();
                    preparedCreature?.Dispose();
                    _nativeReferenceDivergences[reference.FormKey.ToString()] = error.Message;
                    if (grid.Scene.BaseObjects[reference.Base].Signature == "NPC_")
                        _nativeActorDivergences[reference.FormKey.ToString()] = error.Message;
                    GD.PushError($"OPENNV_NATIVE_REFERENCE_DIVERGENCE reference={reference.FormKey}: {error.Message}");
                }
                finally
                {
                    stagedReference?.Free();
                    if (preparedStatic is not null && GodotObject.IsInstanceValid(preparedStatic.Root) && preparedStatic.Root.GetParent() is null)
                        preparedStatic.Root.Free();
                }
                ReleaseExteriorNpc(npc); npc = null;
                ReleaseExteriorCreature(creature); creature = null;
                return true;
            }
            ));
        }
    }

    private void CommitNativeExteriorGrid()
    {
        var started = Stopwatch.GetTimestamp();
        var phases = _options.ContainsKey("live-harness") ? new Dictionary<string, double>() : null;
        _nativeGridCommitPhases = phases;
        var phaseStarted = started;
        void Mark(string phase)
        {
            if (phases is null) return;
            var now = Stopwatch.GetTimestamp();
            phases.Add(phase, Stopwatch.GetElapsedTime(phaseStarted, now).TotalMilliseconds);
            phaseStarted = now;
        }
        var root = _nativeCurrentCellRoot!; var grid = _nativeGridPending!;
        var previous = _nativeActiveCell!.Cell.FormKey;
        var retained = grid.Cells.Select(cell => cell.FormKey).ToHashSet();
        ++_nativeGridGeneration;
        var sky = new FalloutSkyLightingState(_nativePluginStack!, _nativeSkyLighting!.DaytimeExtension);
        sky.Restore(_nativeSkyLighting.Capture());
        var position = _nativePlayer!.GlobalPosition / _configuration.World.GameUnitsToMeters;
        sky.EnterCell(grid.Scene.Cell, _nativeGlobals, [position.X, -position.Z, position.Y]);
        using var regionEntry = _nativeReferences!.BeginRegionCellEntry(grid.Scene.Cell, [position.X, -position.Z, position.Y]);
        grid = grid with { Scene = _nativeReferences.ComposeResidency(grid.Scene, _configuration.World.GameUnitsToMeters, grid.Cells) };
        _nativeReferences!.LoadCell(grid.Scene);
        _nativeReferences.UnloadCell(previous);
        _nativeActiveCell = grid.Scene;
        regionEntry.Commit();
        _nativeSkyLighting.Restore(sky.Capture());
        EnterNativeImposterCell(_nativeReferencePresentation!, root, grid.Scene, [position.X, -position.Z, position.Y]);
        Mark("world-and-weather");
        var center = grid.Scene.Cell.Coordinates!.Value;
        DiscoverNativeCellReferences(grid.Scene);
        Mark("source-discovery");
        _nativeReferencePresentation!.SetResidency(grid.Scene.References, reference => MaterializeNativeReference(root, grid.Scene, reference),
            reference => Math.Abs((int)MathF.Floor(reference.Position[0] / 4096) - center.X) <= grid.Radius + 1 &&
                Math.Abs((int)MathF.Floor(reference.Position[1] / 4096) - center.Y) <= grid.Radius + 1, phases);
        Mark("reference-residency");
        var references = grid.Scene.References.ToDictionary(reference => reference.FormKey);
        var npcPublicationStarted = phases is null ? 0 : Stopwatch.GetTimestamp();
        foreach (var (key, actor) in _nativeGridNpcModels.ToArray())
        {
            _nativeGridNpcModels.Remove(key);
            try
            {
                PlaceNativeReference(root, grid.Scene, references[key], observe: false, preparedNpc: actor);
                if (GodotObject.IsInstanceValid(actor) && actor.GetParent() == root)
                {
                    _nativeReferencePresentation.Register(key, actor);
                    _nativeGridStaged.Add(actor);
                }
            }
            finally
            {
                // Enable/template changes can make a prepared body unused.
                // Failed placement already retires its own acquired bindings.
                if (GodotObject.IsInstanceValid(actor) && actor.GetParent() is null) actor.Free();
            }
        }
        var creaturePublicationStarted = phases is null ? 0 : Stopwatch.GetTimestamp();
        if (phases is not null)
            phases.Add("reference-publication-npc", Stopwatch.GetElapsedTime(npcPublicationStarted, creaturePublicationStarted).TotalMilliseconds);
        foreach (var (key, geometry) in _nativeGridCreatureModels.ToArray())
        {
            _nativeGridCreatureModels.Remove(key);
            try
            {
                var childCount = root.GetChildCount();
                PlaceNativeReference(root, grid.Scene, references[key], observe: false, preparedCreature: geometry);
                var added = root.GetChildCount() - childCount;
                if (added != 0)
                {
                    if (added != 1 || root.GetChild(childCount) is not RuntimeNativeCreature actor || actor.Appearance.Reference != key)
                        throw new InvalidDataException("Creature publication did not produce its single source actor.");
                    _nativeReferencePresentation.Register(key, actor);
                    _nativeGridStaged.Add(actor);
                }
            }
            finally { geometry.Dispose(); }
        }
        var modelPublicationStarted = phases is null ? 0 : Stopwatch.GetTimestamp();
        if (phases is not null)
            phases.Add("reference-publication-creature", Stopwatch.GetElapsedTime(creaturePublicationStarted, modelPublicationStarted).TotalMilliseconds);
        double modelAttachMilliseconds = 0, modelRegisterMilliseconds = 0;
        foreach (var (key, model) in _nativeGridModels)
        {
            var modelStarted = phases is null ? 0 : Stopwatch.GetTimestamp();
            root.AddChild(model);
            if (phases is not null)
            {
                var registeredAt = Stopwatch.GetTimestamp();
                modelAttachMilliseconds += Stopwatch.GetElapsedTime(modelStarted, registeredAt).TotalMilliseconds;
                modelStarted = registeredAt;
            }
            _nativeReferencePresentation.Register(key, model);
            if (phases is not null)
                modelRegisterMilliseconds += Stopwatch.GetElapsedTime(modelStarted).TotalMilliseconds;
        }
        if (phases is not null)
        {
            phases.Add("reference-publication-models", Stopwatch.GetElapsedTime(modelPublicationStarted).TotalMilliseconds);
            phases.Add("reference-publication-model-attach", modelAttachMilliseconds);
            phases.Add("reference-publication-model-register", modelRegisterMilliseconds);
        }
        Mark("reference-publication");
        foreach (var land in root.GetChildren().OfType<RuntimeNativeLandscapeTransport>())
        {
            var active = retained.Contains(land.Source.ActiveCell);
            GamebryoReferenceEnableRuntime.Apply(land, active);
            if (active) _nativeLandLastUse[land.Source.ActiveCell] = _nativeGridGeneration;
        }
        // Keep a bounded inactive terrain fringe for boundary reversals. It has
        // no collision or gameplay residency until the normal commit enables it.
        var terrain = root.GetChildren().OfType<RuntimeNativeLandscapeTransport>().ToArray();
        foreach (var land in terrain.Where(land => !retained.Contains(land.Source.ActiveCell))
            .OrderBy(land => _nativeLandLastUse.GetValueOrDefault(land.Source.ActiveCell))
            .Take(Math.Max(0, terrain.Length - Math.Max(96, retained.Count * 2))))
        {
            _nativeLandLastUse.Remove(land.Source.ActiveCell); land.QueueFree();
        }
        Mark("terrain-residency");
        root.GetChildren().OfType<RuntimeNativeGrassOwner>().Single().Register(
            root.GetChildren().OfType<RuntimeNativeLandscapeTransport>().Where(land => !land.IsQueuedForDeletion()));
        _nativeReferenceEvents!.SetResidency(grid.Scene, root);
        _nativeReferencePresentation!.PublishImposterRegions();
        Mark("reference-events");
        ObserveNativeResidentReferences(grid.Scene);
        Mark("runtime-observation");
        var environment = root.GetChildren().OfType<RuntimeNativeExteriorEnvironment>().Single();
        foreach (var node in _nativeGridStaged) environment.Register(node);
        Mark("environment-registration");
        root.GetChildren().OfType<RuntimeNativeExteriorLod>().Single().SetDetailGrid(grid);
        Mark("lod-detail-mask");
        foreach (var actor in _nativeGridModels.Values.OfType<RuntimeNativeNpc>())
            actor.BeginPackageDialogue = (package, completed) => _nativeOpeningStageDriver!.RequestPackageDialogue(actor.Appearance.Reference!.Value, package, completed);
        foreach (var actor in _nativeReferencePresentation.Actors) actor.UpdateResidentScene(grid.Scene);
        PublishNativeExteriorCells(root, grid);
        _nativeWalkableGrid.Clear(); foreach (var cell in grid.Cells) _nativeWalkableGrid.Add(cell.Coordinates!.Value);
        _nativeGridUploads = null; _nativeGridPending = null; _nativeGridStaged.Clear(); _nativeGridModels.Clear();
        _nativeGridFailed = null; _nativeGridTarget = null;
        _nativeGridReadCancellation?.Dispose(); _nativeGridReadCancellation = null;
        Mark("actor-residency");
        var models = grid.Scene.References.Select(reference => grid.Scene.BaseObjects[reference.Base])
            .Where(value => value.ModelPath is not null).Select(value => value.ModelPath!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        _nativeRecentModelSets.Enqueue(models);
        while (_nativeRecentModelSets.Count > 3) _nativeRecentModelSets.Dequeue();
        var warmModels = _nativeRecentModelSets.SelectMany(set => set).ToHashSet(StringComparer.OrdinalIgnoreCase);
        _nativePrototypeRetirements.EnqueueCold(_nativeNifPrototypes, warmModels);
        Mark("prototype-eviction");
        var textures = root.GetChildren().OfType<RuntimeNativeLandscapeTransport>().Where(land => !land.IsQueuedForDeletion())
            .SelectMany(land => land.Source.Textures.Values).SelectMany(texture => new[] { texture.DiffusePath, texture.NormalPath })
            .Where(path => path is not null).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var path in _nativeLandscapeTextures.Keys.Where(path => !textures.Contains(path)).ToArray()) _nativeLandscapeTextures.Remove(path);
        Mark("texture-prune");
        _nativeGridCommitMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        GD.Print($"OPENNV_EXTERIOR_STREAM_READY previous={previous} active={grid.Scene.Cell.FormKey} cells={grid.Cells.Count} overlap=retained commitMs={_nativeGridCommitMilliseconds:F3}");
    }

    private void AdvanceNativePrototypeRetirement()
    {
        var frame = Engine.GetProcessFrames();
        if (_nativePrototypeRetirements.Count == 0 || _nativePrototypeRetirementFrame == frame ||
            !NativePublicationBudget.TryBegin(false, out var started)) return;
        _nativePrototypeRetirementFrame = frame;
        var source = _nativePrototypeRetirements.NextSource!; var faulted = true;
        try
        {
            _nativePrototypeRetirements.RetireNext(prototype => prototype.Scene.Root.Free());
            faulted = false;
        }
        finally { _nativePrototypeRetirementTiming.RecordStep(source, NativePublicationBudget.Complete(started), faulted); }
    }

    private Node3D? MaterializeNativeReference(Node3D root, FalloutCellScene cell, FalloutPlacedReference reference, bool observe = true)
    {
        Node3D? Find() => root.GetChildren().OfType<Node3D>().SingleOrDefault(node => !node.IsQueuedForDeletion() &&
            (node is RuntimeNativeNpc actor ? actor.Appearance.Reference == reference.FormKey :
                node.GetMeta("opennv_reference_form_key", "").AsString() == reference.FormKey.ToString()));
        if (Find() is { } existing) return existing;
        var baseObject = cell.BaseObjects.GetValueOrDefault(reference.Base);
        if (baseObject?.ModelPath is { } modelPath && modelPath.EndsWith(".spt", StringComparison.OrdinalIgnoreCase))
        {
            _nativeReferenceDivergences[reference.FormKey.ToString()] =
                $"Owned SpeedTree model {modelPath} procedural geometry decoder is unbound.";
            return null;
        }
        var before = root.GetChildCount();
        try
        {
            PlaceNativeReference(root, cell, reference, materializeDisabled: true, observe: observe);
            var materialized = Find();
            if (materialized is not null)
                root.GetChildren().OfType<RuntimeNativeExteriorEnvironment>().SingleOrDefault()?.Register(materialized);
            return materialized;
        }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException or FileNotFoundException)
        {
            while (root.GetChildCount() > before) root.GetChild(before).Free();
            _nativeReferenceDivergences[reference.FormKey.ToString()] = error.Message;
            GD.PushError($"OPENNV_NATIVE_REFERENCE_DIVERGENCE reference={reference.FormKey} base={reference.Base}: {error.Message}");
            return null;
        }
    }
    private FalloutExteriorGridScene ResolveExterior(FalloutFormKey world, float[] position)
    {
        _nativeExteriorGrid ??= new(_nativePluginStack!);
        var settings = FalloutInstallationSettings.Read(RuntimeLiveContentSource.Current!);
        var diameter = checked((int)settings.Unsigned("General", "uGridsToLoad"));
        return _nativeExteriorGrid.Resolve(world, _nativeExteriorGrid.PersistentCell(world),
            position[0], position[1], diameter);
    }

    private void AddExteriorLandscape(Node3D root, FalloutExteriorGridScene grid)
    {
        foreach (var cell in grid.Cells)
            if (FalloutLandscapeTransportResolver.ResolveCell(_nativePluginStack!, cell, grid.PersistentCell) is { } source)
                root.AddChild(RuntimeNativeLandscapeTransportBuilder.Build(source,
                    _configuration.World.GameUnitsToMeters, _nativeLandscapeTextures));
        CompleteExteriorLandscape(root, grid);
    }

    private void CompleteExteriorLandscape(Node3D root, FalloutExteriorGridScene grid)
    {
        root.SetMeta("opennv_exterior_grid_cells", grid.Cells.Count);
        root.SetMeta("opennv_exterior_grid_radius", grid.Radius);
        var lod = new RuntimeNativeExteriorLod();
        lod.Configure(_nativePluginStack!, RuntimeLiveContentSource.Current!, grid, _configuration.World.GameUnitsToMeters);
        root.AddChild(lod);
        PublishNativeExteriorCells(root, grid);
    }

    private void AddExteriorEnvironment(Node3D root, FalloutCellDefinition cell)
    {
        var sky = new RuntimeNativeExteriorEnvironment { Name = "ExteriorSkyOwner" };
        var imageSpace = FalloutImageSpaceReader.ForCell(_nativePluginStack!, cell.FormKey) ??
            FalloutImageSpaceReader.ForWorld(_nativePluginStack!, cell.Worldspace ??
                throw new InvalidDataException("Exterior environment has no worldspace."));
        sky.Compositor = AddNativeImageSpace(root, imageSpace,
            () => _nativeSkyLighting!.ImageSpaceModifiers(_nativeGameTime!.Hour), sunlightDimmer: sky.BindSunlightDimmer, skyScale: sky.BindSkyScale);
        sky.ImageSpace = imageSpace;
        var diameter = checked((int)FalloutInstallationSettings.Read(RuntimeLiveContentSource.Current!).Unsigned("General", "uGridsToLoad"));
        // Cover the complete resident square, including its diagonal when the
        // player crosses an anchor boundary. Engine defaults cut this at 100m.
        var shadowDistance = (diameter / 2 + 1) * 4096 * _configuration.World.GameUnitsToMeters * MathF.Sqrt(2);
        sky.Configure(_nativePluginStack!, _nativeSkyLighting!, () => _nativeGameTime!.Hour, _configuration.World.GameUnitsToMeters, this, shadowDistance);
        root.AddChild(sky);
        var grass = new RuntimeNativeGrassOwner { Name = "OwnedTerrainGrass" };
        grass.Configure(root, RuntimeLiveContentSource.Current!, _nativePluginStack!, sky, _configuration.World.GameUnitsToMeters);
        grass.Register(root.GetChildren().OfType<RuntimeNativeLandscapeTransport>());
        root.AddChild(grass);
        var wind = new RuntimeNativeWind { Name = "ExteriorWindOwner" };
        FalloutFormKey? windWeather = null;
        float windSpeed = 0;
        wind.Configure(() =>
        {
            var weather = _nativeSkyLighting!.ActiveWeather.Form;
            if (windWeather != weather)
            {
                windSpeed = FalloutWeatherMotion.Read(_nativePluginStack!.GetEffective(weather),
                    FalloutGameSettingFloats.ReadRetained(_nativePluginStack, "fWeatherCloudSpeedMax", nameof(RuntimeNativeWind))).WindSpeed;
                windWeather = weather;
            }
            return (windSpeed, FalloutWindForce.InitialHeading);
        }, _configuration.World.GameUnitsToMeters);
        root.AddChild(wind);
    }

    private Color NativeAmbient(FalloutCellDefinition cell)
    {
        if (cell.Lighting is { } lighting) return ByteColor(lighting.AmbientRgb);
        var sky = _nativeSkyLighting ?? throw new InvalidOperationException("Exterior ambient has no sky owner.");
        var weather = sky.ActiveWeather;
        var weights = FalloutWeatherTimeWeights.Sample(sky.Climate, _nativeGameTime!.Hour, sky.DaytimeExtension, weather.TimeSamples);
        var rgb = weather.Sample(weights, 3);
        return new(rgb[0], rgb[1], rgb[2]);
    }
}
