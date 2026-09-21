using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Presentation.Rendering;
using OpenNV.Runtime.World.Cells;
using OpenNV.Runtime.World.Actors;
using System.Diagnostics;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime;

public partial class RuntimeCoordinator
{
    private FalloutExteriorGrid? _nativeExteriorGrid;
    private readonly Dictionary<string, ImageTexture> _nativeLandscapeTextures = new(StringComparer.OrdinalIgnoreCase);
    private Node3D? _nativeStreamRoot;
    private sealed record PreparedExteriorGrid(FalloutExteriorGridScene Grid, IReadOnlyDictionary<FalloutFormKey, FalloutLandscapeTransport> Landscapes,
        IReadOnlyDictionary<string, FalloutNifFile> Models);
    private Task<PreparedExteriorGrid>? _nativeGridRead;
    private CancellationTokenSource? _nativeGridReadCancellation;
    private FalloutExteriorGridScene? _nativeGridPending;
    private Queue<(string Source, Func<bool> Upload)>? _nativeGridUploads;
    private readonly List<Node3D> _nativeGridStaged = [];
    private readonly Dictionary<FalloutFormKey, Node3D> _nativeGridModels = [];
    private (int X, int Y)? _nativeGridFailed;
    private double _nativeGridUploadMilliseconds;
    private double _nativeGridMaximumUploadMilliseconds;
    private string? _nativeGridMaximumUploadSource;
    private double _nativeGridCommitMilliseconds;
    private IReadOnlyDictionary<string, double>? _nativeGridCommitPhases;
    private (int X, int Y)? _nativeGridTarget;
    private string? _nativeGridError;
    private readonly HashSet<(int X, int Y)> _nativeWalkableGrid = [];
    private readonly Dictionary<FalloutFormKey, ulong> _nativeLandLastUse = [];
    private readonly Queue<HashSet<string>> _nativeRecentModelSets = [];
    private ulong _nativeGridGeneration;
    private bool NativeCollisionResident(Vector3 position)
    {
        if (_nativeActiveCell?.Cell.Worldspace is null) return true;
        var width = 4096 * _configuration.World.GameUnitsToMeters;
        var radius = _configuration.Player.CapsuleRadiusMeters;
        for (var x = (int)MathF.Floor((position.X - radius) / width); x <= (int)MathF.Floor((position.X + radius) / width); x++)
            for (var y = (int)MathF.Floor((-position.Z - radius) / width); y <= (int)MathF.Floor((-position.Z + radius) / width); y++)
                if (!_nativeWalkableGrid.Contains((x, y))) return false;
        return true;
    }
    private object NativeStreamingState => new
    {
        reading = _nativeGridRead is not null,
        uploadsRemaining = _nativeGridUploads?.Count ?? 0,
        residentCells = _nativeWalkableGrid.Count,
        warmReferences = _nativeReferencePresentation?.WarmNodeCount ?? 0,
        terrainCacheCells = _nativeCurrentCellRoot?.GetChildren().OfType<RuntimeNativeLandscapeTransport>().Count() ?? 0,
        target = _nativeGridTarget is { } target ? new[] { target.X, target.Y } : null,
        preparedAhead = _nativeGridUploads?.Count == 0 && _nativeGridPending is not null,
        lastUploadMilliseconds = _nativeGridUploadMilliseconds,
        maximumUploadMilliseconds = _nativeGridMaximumUploadMilliseconds,
        maximumUploadSource = _nativeGridMaximumUploadSource,
        preparingNpcs = _nativeGridNpcPreparations.Count,
        lastCommitMilliseconds = _nativeGridCommitMilliseconds,
        lastCommitPhasesMilliseconds = _nativeGridCommitPhases,
        error = _nativeGridError
    };

    private void AdvanceNativeExteriorStreaming(double delta)
    {
        if (_nativeStreamRoot is not null && _nativeStreamRoot != _nativeCurrentCellRoot)
        {
            foreach (var node in _nativeGridStaged.Where(GodotObject.IsInstanceValid)) node.QueueFree();
            _nativeGridStaged.Clear(); _nativeGridModels.Clear(); _nativeGridUploads = null; _nativeGridPending = null;
            CancelNativeGridRead(); _nativeStreamRoot = null; _nativeGridFailed = null; _nativeGridTarget = null;
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
                foreach (var node in _nativeGridStaged.Where(GodotObject.IsInstanceValid)) node.QueueFree();
                _nativeGridStaged.Clear(); _nativeGridModels.Clear(); _nativeGridUploads = null; _nativeGridPending = null;
                CancelNativeGridRead(); _nativeGridTarget = null;
            }
            if (_nativeGridRead is { IsCompleted: true } read)
            {
                _nativeGridRead = null;
                StageNativeExteriorGrid(read.GetAwaiter().GetResult());
            }
            if (_nativeGridUploads is { } uploads)
            {
                var started = Stopwatch.GetTimestamp();
                // A pending decode or partial body yields. Visit each queued
                // item at most once this frame, without waiting on a worker.
                var remaining = uploads.Count;
                while (remaining-- > 0 && uploads.TryDequeue(out var upload))
                {
                    if (!upload.Upload()) uploads.Enqueue(upload);
                    _nativeGridUploadMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                    if (_nativeGridUploadMilliseconds > _nativeGridMaximumUploadMilliseconds)
                    { _nativeGridMaximumUploadMilliseconds = _nativeGridUploadMilliseconds; _nativeGridMaximumUploadSource = upload.Source; }
                    if (_nativeGridUploadMilliseconds >= 3) break;
                }
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
            _nativeStreamRoot = _nativeCurrentCellRoot;
            _nativeGridTarget = target;
            _nativeGridError = null;
            var resident = _nativeWalkableGrid.ToHashSet();
            var residentModels = _nativeNifPrototypes.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var content = RuntimeLiveContentSource.Current!;
            _nativeGridReadCancellation = new();
            var cancellation = _nativeGridReadCancellation.Token;
            _nativeGridRead = Task.Run(async () =>
            {
                var (grid, lands) = await FalloutContentWorkers.Run(() =>
                {
                    var resolved = ResolveExterior(world, [(target.Item1 + .5f) * 4096, (target.Item2 + .5f) * 4096, position.Y / units]);
                    var landscapes = resolved.Cells.Where(cell => !resident.Contains(cell.Coordinates!.Value)).ToDictionary(cell => cell.FormKey,
                        cell =>
                        {
                            cancellation.ThrowIfCancellationRequested();
                            return FalloutLandscapeTransportResolver.ResolveCell(_nativePluginStack!, cell, resolved.PersistentCell);
                        });
                    foreach (var path in landscapes.Values.SelectMany(land => land.Textures.Values)
                        .SelectMany(texture => new[] { texture.DiffusePath, texture.NormalPath }).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase))
                    {
                        cancellation.ThrowIfCancellationRequested();
                        _ = content.TryRead(path, null, out _, out _);
                    }
                    return (resolved, landscapes);
                }, cancellation);
                var models = new Dictionary<string, FalloutNifFile>(StringComparer.OrdinalIgnoreCase);
                var paths = grid.Scene.References.Select(reference => grid.Scene.BaseObjects[reference.Base])
                    .Where(model => model.Signature is not ("NPC_" or "CREA") && model.ModelPath is not null)
                    .Select(model => model.ModelPath!).Distinct(StringComparer.OrdinalIgnoreCase).Where(path => !residentModels.Contains(path)).ToArray();
                var prepared = await Task.WhenAll(paths.Select(path => FalloutContentWorkers.Run(() =>
                {
                    try
                    {
                        if (!content.TryRead(path, null, out var bytes, out _)) return (Path: path, Nif: (FalloutNifFile?)null);
                        var nif = FalloutNifFile.Read(bytes);
                        foreach (var block in nif.Blocks.Where(block => block.TypeName == "BSShaderTextureSet"))
                            foreach (var texture in ((FalloutNifShaderTextureSet)nif.ReadObject(block.Index)).Textures.Where(texture => texture.Length != 0))
                                _ = content.TryRead(FalloutNifSurfaceInputs.TexturePath(texture), null, out _, out _);
                        return (Path: path, Nif: (FalloutNifFile?)nif);
                    }
                    // Prefetch does not admit or substitute a model. The ordinary
                    // reference owner still reports any enabled source failure.
                    catch (Exception error) when (error is IOException or NotSupportedException) { return (Path: path, Nif: (FalloutNifFile?)null); }
                }, cancellation)));
                foreach (var item in prepared)
                    if (item.Nif is not null) models.Add(item.Path, item.Nif);
                return new PreparedExteriorGrid(grid, lands, models);
            });
        }
        catch (Exception error)
        {
            _nativeGridError = error.Message; _nativeGridFailed = _nativeGridTarget ?? coordinates;
            CancelNativeGridRead(); _nativeGridUploads = null; _nativeGridPending = null;
            _nativeGridTarget = null;
            foreach (var node in _nativeGridStaged.Where(GodotObject.IsInstanceValid)) node.QueueFree();
            _nativeGridStaged.Clear(); _nativeGridModels.Clear();
            GD.PushError($"OPENNV_EXTERIOR_STREAM_FAIL cell={coordinates}: {error.Message}");
        }
    }

    private void CancelNativeGridRead()
    {
        foreach (var preparation in _nativeGridNpcPreparations) preparation.Dispose();
        _nativeGridNpcPreparations.Clear();
        _nativeGridReadCancellation?.Cancel();
        _nativeGridReadCancellation?.Dispose(); _nativeGridReadCancellation = null;
        if (_nativeGridRead is { } abandoned)
            _ = abandoned.ContinueWith(task => { _ = task.Exception; }, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        _nativeGridRead = null;
    }

    public override void _ExitTree()
    {
        CancelNativeGridRead();
        if (!_retiringNativeSession) return;
        // Reload has drained source workers. Detached prototypes are outside
        // the scene tree and must be released with the retired record owners.
        foreach (var prototype in _nativeNifPrototypes.Values) prototype.Scene.Root.Free();
        _nativeNifPrototypes.Clear();
        _nativePrewarmedInitialCellRoot?.Free(); _nativePrewarmedInitialCellRoot = null;
        _nativeReferences?.Dispose(); _nativePluginStack?.Dispose();
    }

    private void StageNativeExteriorGrid(PreparedExteriorGrid prepared)
    {
        var grid = prepared.Grid with { Scene = _nativeReferences!.ComposeResidency(prepared.Grid.Scene, prepared.Grid.Cells) };
        var root = _nativeCurrentCellRoot!;
        _nativeGridPending = grid; _nativeGridUploads = new();
        var oldLand = root.GetChildren().OfType<RuntimeNativeLandscapeTransport>().Select(land => land.Source.ActiveCell).ToHashSet();
        foreach (var cell in grid.Cells.Where(cell => !oldLand.Contains(cell.FormKey)))
            _nativeGridUploads.Enqueue((cell.FormKey.ToString(), () =>
            {
                var land = RuntimeNativeLandscapeTransportBuilder.Build(prepared.Landscapes[cell.FormKey],
                    _configuration.World.GameUnitsToMeters, _nativeLandscapeTextures);
                root.AddChild(land); GamebryoReferenceEnableRuntime.Apply(land, false); _nativeGridStaged.Add(land);
                return true;
            }
            ));
        var previous = _nativeActiveCell!.References.Select(reference => reference.FormKey).ToHashSet();
        foreach (var reference in grid.Scene.References.Where(reference => !previous.Contains(reference.FormKey) &&
            _nativeReferencePresentation?.HasPrepared(reference.FormKey) != true))
        {
            ExteriorNpcPreparation? npc = null;
            _nativeGridUploads.Enqueue((reference.FormKey.ToString(), () =>
            {
                var before = root.GetChildCount();
                RuntimeNativeNpc? preparedNpc = null;
                try
                {
                    var model = grid.Scene.BaseObjects[reference.Base].ModelPath;
                    if (grid.Scene.BaseObjects[reference.Base].Signature == "NPC_" && _nativeReferences!.IsEnabled(reference.FormKey))
                    {
                        if (npc is null)
                        {
                            // Bound both jobs and completed bodies awaiting GPU
                            // publication; worker slots alone do not bound memory.
                            if (_nativeGridNpcPreparations.Count >= FalloutContentWorkers.Concurrency) return false;
                            npc = PrepareExteriorNpc(reference);
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
                    PlaceNativeReference(root, grid.Scene, reference, observe: false,
                        preparedModel: model is null ? null : prepared.Models.GetValueOrDefault(model), preparedNpc: preparedNpc);
                    if (preparedNpc is not null && GodotObject.IsInstanceValid(preparedNpc) && preparedNpc.GetParent() is null) preparedNpc.Free();
                    var nodes = root.GetChildren().Skip(before).OfType<Node3D>().ToArray();
                    if (nodes.Length > 1) throw new InvalidDataException("A source reference must have one presentation root.");
                    foreach (var node in nodes)
                    {
                        GamebryoReferenceEnableRuntime.Apply(node, false);
                        _nativeGridStaged.Add(node); _nativeGridModels.Add(reference.FormKey, node);
                    }
                }
                catch (Exception error) when (error is IOException or InvalidDataException or NotSupportedException or InvalidOperationException)
                {
                    if (preparedNpc is not null && GodotObject.IsInstanceValid(preparedNpc) && preparedNpc.GetParent() is null) preparedNpc.Free();
                    while (root.GetChildCount() > before) root.GetChild(before).Free();
                    _nativeReferenceDivergences[reference.FormKey.ToString()] = error.Message;
                    if (grid.Scene.BaseObjects[reference.Base].Signature == "NPC_")
                        _nativeActorDivergences[reference.FormKey.ToString()] = error.Message;
                    GD.PushError($"OPENNV_NATIVE_REFERENCE_DIVERGENCE reference={reference.FormKey}: {error.Message}");
                }
                ReleaseExteriorNpc(npc); npc = null;
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
        _nativeReferences!.LoadCell(grid.Scene);
        _nativeReferences.UnloadCell(previous);
        _nativeActiveCell = grid.Scene;
        _nativeSkyLighting.Restore(sky.Capture());
        Mark("world-and-weather");
        var center = grid.Scene.Cell.Coordinates!.Value;
        DiscoverNativeCellReferences(grid.Scene);
        Mark("source-discovery");
        _nativeReferencePresentation!.SetResidency(grid.Scene.References, reference => MaterializeNativeReference(root, grid.Scene, reference),
            reference => Math.Abs((int)MathF.Floor(reference.Position[0] / 4096) - center.X) <= grid.Radius + 1 &&
                Math.Abs((int)MathF.Floor(reference.Position[1] / 4096) - center.Y) <= grid.Radius + 1);
        Mark("reference-residency");
        foreach (var (key, model) in _nativeGridModels) _nativeReferencePresentation.Register(key, model);
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
        _nativeReferenceEvents!.SetResidency(grid.Scene, root);
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
        foreach (var path in _nativeNifPrototypes.Keys.Where(path => !warmModels.Contains(path)).ToArray())
        {
            _nativeNifPrototypes[path].Scene.Root.Free(); _nativeNifPrototypes.Remove(path);
        }
        Mark("prototype-eviction");
        var textures = root.GetChildren().OfType<RuntimeNativeLandscapeTransport>().Where(land => !land.IsQueuedForDeletion())
            .SelectMany(land => land.Source.Textures.Values).SelectMany(texture => new[] { texture.DiffusePath, texture.NormalPath })
            .Where(path => path is not null).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var path in _nativeLandscapeTextures.Keys.Where(path => !textures.Contains(path)).ToArray()) _nativeLandscapeTextures.Remove(path);
        Mark("texture-prune");
        _nativeGridCommitMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        GD.Print($"OPENNV_EXTERIOR_STREAM_READY previous={previous} active={grid.Scene.Cell.FormKey} cells={grid.Cells.Count} overlap=retained commitMs={_nativeGridCommitMilliseconds:F3}");
    }

    private Node3D? MaterializeNativeReference(Node3D root, FalloutCellScene cell, FalloutPlacedReference reference)
    {
        Node3D? Find() => root.GetChildren().OfType<Node3D>().SingleOrDefault(node => !node.IsQueuedForDeletion() &&
            (node is RuntimeNativeNpc actor ? actor.Appearance.Reference == reference.FormKey :
                node.GetMeta("opennv_reference_form_key", "").AsString() == reference.FormKey.ToString()));
        if (Find() is { } existing) return existing;
        var before = root.GetChildCount();
        try { PlaceNativeReference(root, cell, reference, materializeDisabled: true); return Find(); }
        catch { while (root.GetChildCount() > before) root.GetChild(before).Free(); throw; }
    }
    private FalloutExteriorGridScene ResolveExterior(FalloutFormKey world, float[] position)
    {
        _nativeExteriorGrid ??= new(_nativePluginStack!);
        var settings = FalloutInstallationSettings.Read(RuntimeLiveContentSource.Current!);
        var diameter = checked((int)settings.Unsigned("General", "uGridsToLoad"));
        diameter = Math.Max(diameter, _configuration.World.MinimumExteriorGridDiameter);
        return _nativeExteriorGrid.Resolve(world, _nativeExteriorGrid.PersistentCell(world),
            position[0], position[1], diameter);
    }

    private void AddExteriorLandscape(Node3D root, FalloutExteriorGridScene grid)
    {
        foreach (var cell in grid.Cells)
            root.AddChild(RuntimeNativeLandscapeTransportBuilder.Build(
                FalloutLandscapeTransportResolver.ResolveCell(_nativePluginStack!, cell, grid.PersistentCell),
                _configuration.World.GameUnitsToMeters, _nativeLandscapeTextures));
        root.SetMeta("opennv_exterior_grid_cells", grid.Cells.Count);
        root.SetMeta("opennv_exterior_grid_radius", grid.Radius);
        var lod = new RuntimeNativeExteriorLod();
        lod.Configure(_nativePluginStack!, RuntimeLiveContentSource.Current!, grid, _configuration.World.GameUnitsToMeters);
        root.AddChild(lod);
    }

    private void AddExteriorEnvironment(Node3D root, FalloutCellDefinition cell)
    {
        var sky = new RuntimeNativeExteriorEnvironment { Name = "ExteriorSkyOwner" };
        var imageSpace = FalloutImageSpaceReader.ForCell(_nativePluginStack!, cell.FormKey) ??
            FalloutImageSpaceReader.ForWorld(_nativePluginStack!, cell.Worldspace ??
                throw new InvalidDataException("Exterior environment has no worldspace."));
        sky.Compositor = AddNativeImageSpace(root, imageSpace);
        sky.ImageSpace = imageSpace;
        sky.Configure(_nativePluginStack!, _nativeSkyLighting!, () => _nativeGameTime!.Hour, _configuration.World.GameUnitsToMeters, this);
        root.AddChild(sky);
        var wind = new RuntimeNativeWind { Name = "ExteriorWindOwner" };
        FalloutFormKey? windWeather = null;
        float windSpeed = 0;
        wind.Configure(() =>
        {
            var weather = _nativeSkyLighting!.ActiveWeather.Form;
            if (windWeather != weather)
            {
                windSpeed = FalloutWeatherMotion.Read(_nativePluginStack!.GetEffective(weather),
                    FalloutGameSettingFloats.Read(_nativePluginStack, "fWeatherCloudSpeedMax")).WindSpeed;
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
        var weights = FalloutWeatherTimeWeights.Sample(sky.Climate, _nativeGameTime!.Hour, sky.DaytimeExtension);
        var rgb = sky.ActiveWeather.Sample(weights, 3);
        return new(rgb[0], rgb[1], rgb[2]);
    }
}
