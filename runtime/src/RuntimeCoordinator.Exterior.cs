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
    private FalloutExteriorGridScene? _nativeGridPending;
    private Queue<(string Source, Action Upload)>? _nativeGridUploads;
    private readonly List<Node3D> _nativeGridStaged = [];
    private readonly Dictionary<FalloutFormKey, Node3D> _nativeGridModels = [];
    private (int X, int Y)? _nativeGridFailed;
    private double _nativeGridUploadMilliseconds;
    private double _nativeGridMaximumUploadMilliseconds;
    private string? _nativeGridMaximumUploadSource;
    private double _nativeGridCommitMilliseconds;
    private (int X, int Y)? _nativeGridTarget;
    private string? _nativeGridError;
    private readonly HashSet<(int X, int Y)> _nativeWalkableGrid = [];
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
        target = _nativeGridTarget is { } target ? new[] { target.X, target.Y } : null,
        preparedAhead = _nativeGridUploads?.Count == 0 && _nativeGridPending is not null,
        lastUploadMilliseconds = _nativeGridUploadMilliseconds,
        maximumUploadMilliseconds = _nativeGridMaximumUploadMilliseconds,
        maximumUploadSource = _nativeGridMaximumUploadSource,
        lastCommitMilliseconds = _nativeGridCommitMilliseconds,
        error = _nativeGridError
    };

    private void AdvanceNativeExteriorStreaming(double delta)
    {
        if (_nativeStreamRoot is not null && _nativeStreamRoot != _nativeCurrentCellRoot)
        {
            foreach (var node in _nativeGridStaged.Where(GodotObject.IsInstanceValid)) node.QueueFree();
            _nativeGridStaged.Clear(); _nativeGridModels.Clear(); _nativeGridUploads = null; _nativeGridPending = null;
            _nativeGridRead = null; _nativeStreamRoot = null; _nativeGridFailed = null; _nativeGridTarget = null;
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
                _nativeGridRead = null; _nativeGridTarget = null;
            }
            if (_nativeGridRead is { IsCompleted: true } read)
            {
                _nativeGridRead = null;
                StageNativeExteriorGrid(read.GetAwaiter().GetResult());
            }
            if (_nativeGridUploads is { } uploads)
            {
                var started = Stopwatch.GetTimestamp();
                while (uploads.TryDequeue(out var upload))
                {
                    upload.Upload();
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
            _nativeGridRead = Task.Run(() =>
            {
                var grid = ResolveExterior(world, [(target.Item1 + .5f) * 4096, (target.Item2 + .5f) * 4096, position.Y / units]);
                var lands = grid.Cells.Where(cell => !resident.Contains(cell.Coordinates!.Value)).ToDictionary(cell => cell.FormKey,
                    cell => FalloutLandscapeTransportResolver.ResolveCell(_nativePluginStack!, cell, grid.PersistentCell));
                var models = new Dictionary<string, FalloutNifFile>(StringComparer.OrdinalIgnoreCase);
                foreach (var path in lands.Values.SelectMany(land => land.Textures.Values)
                    .SelectMany(texture => new[] { texture.DiffusePath, texture.NormalPath }).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase))
                    _ = content.TryRead(path, null, out _, out _);
                foreach (var path in grid.Scene.References.Select(reference => grid.Scene.BaseObjects[reference.Base])
                    .Where(model => model.Signature is not ("NPC_" or "CREA") && model.ModelPath is not null)
                    .Select(model => model.ModelPath!).Distinct(StringComparer.OrdinalIgnoreCase).Where(path => !residentModels.Contains(path)))
                {
                    try
                    {
                        if (!content.TryRead(path, null, out var bytes, out _)) continue;
                        var nif = FalloutNifFile.Read(bytes);
                        foreach (var block in nif.Blocks.Where(block => block.TypeName == "BSShaderTextureSet"))
                            foreach (var texture in ((FalloutNifShaderTextureSet)nif.ReadObject(block.Index)).Textures.Where(texture => texture.Length != 0))
                                _ = content.TryRead(FalloutNifSurfaceInputs.TexturePath(texture), null, out _, out _);
                        models.Add(path, nif);
                    }
                    // Prefetch does not admit or substitute a model. The ordinary
                    // reference owner still reports any enabled source failure.
                    catch (Exception error) when (error is IOException or NotSupportedException) { }
                }
                return new PreparedExteriorGrid(grid, lands, models);
            });
        }
        catch (Exception error)
        {
            _nativeGridError = error.Message; _nativeGridFailed = _nativeGridTarget ?? coordinates;
            _nativeGridRead = null; _nativeGridUploads = null; _nativeGridPending = null;
            _nativeGridTarget = null;
            foreach (var node in _nativeGridStaged.Where(GodotObject.IsInstanceValid)) node.QueueFree();
            _nativeGridStaged.Clear(); _nativeGridModels.Clear();
            GD.PushError($"OPENNV_EXTERIOR_STREAM_FAIL cell={coordinates}: {error.Message}");
        }
    }

    private void StageNativeExteriorGrid(PreparedExteriorGrid prepared)
    {
        var grid = prepared.Grid;
        var root = _nativeCurrentCellRoot!;
        _nativeGridPending = grid; _nativeGridUploads = new();
        var oldLand = root.GetChildren().OfType<RuntimeNativeLandscapeTransport>().Select(land => land.Source.ActiveCell).ToHashSet();
        foreach (var cell in grid.Cells.Where(cell => !oldLand.Contains(cell.FormKey)))
            _nativeGridUploads.Enqueue((cell.FormKey.ToString(), () =>
            {
                var land = RuntimeNativeLandscapeTransportBuilder.Build(prepared.Landscapes[cell.FormKey],
                    _configuration.World.GameUnitsToMeters, _nativeLandscapeTextures);
                root.AddChild(land); GamebryoReferenceEnableRuntime.Apply(land, false); _nativeGridStaged.Add(land);
            }
            ));
        var previous = _nativeActiveCell!.References.Select(reference => reference.FormKey).ToHashSet();
        foreach (var reference in grid.Scene.References.Where(reference => !previous.Contains(reference.FormKey)))
            _nativeGridUploads.Enqueue((reference.FormKey.ToString(), () =>
            {
                var before = root.GetChildCount();
                try
                {
                    var model = grid.Scene.BaseObjects[reference.Base].ModelPath;
                    PlaceNativeReference(root, grid.Scene, reference, observe: false,
                        preparedModel: model is null ? null : prepared.Models.GetValueOrDefault(model));
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
                    while (root.GetChildCount() > before) root.GetChild(before).Free();
                    _nativeReferenceDivergences[reference.FormKey.ToString()] = error.Message;
                    GD.PushError($"OPENNV_NATIVE_REFERENCE_DIVERGENCE reference={reference.FormKey}: {error.Message}");
                }
            }
            ));
    }

    private void CommitNativeExteriorGrid()
    {
        var started = Stopwatch.GetTimestamp();
        var root = _nativeCurrentCellRoot!; var grid = _nativeGridPending!;
        var previous = _nativeActiveCell!.Cell.FormKey;
        var retained = grid.Cells.Select(cell => cell.FormKey).ToHashSet();
        var sky = new FalloutSkyLightingState(_nativePluginStack!, _nativeSkyLighting!.DaytimeExtension);
        sky.Restore(_nativeSkyLighting.Capture());
        var position = _nativePlayer!.GlobalPosition / _configuration.World.GameUnitsToMeters;
        sky.EnterCell(grid.Scene.Cell, _nativeGlobals, [position.X, -position.Z, position.Y]);
        _nativeReferences!.LoadCell(grid.Scene);
        _nativeReferences.UnloadCell(previous);
        _nativeActiveCell = grid.Scene;
        _nativeSkyLighting.Restore(sky.Capture());
        _nativeReferencePresentation!.SetResidency(grid.Scene.References, reference => MaterializeNativeReference(root, grid.Scene, reference));
        foreach (var (key, model) in _nativeGridModels) _nativeReferencePresentation.Register(key, model);
        foreach (var land in root.GetChildren().OfType<RuntimeNativeLandscapeTransport>())
        {
            if (retained.Contains(land.Source.ActiveCell)) GamebryoReferenceEnableRuntime.Apply(land, true);
            else { GamebryoReferenceEnableRuntime.Apply(land, false); land.QueueFree(); }
        }
        _nativeReferenceEvents!.SetResidency(grid.Scene, root);
        _parityObservations.ReplaceScope("world/active-cell", grid.Scene.References.Select(reference =>
            ($"{grid.Scene.Cell.FormKey}/{reference.FormKey}", ParityCategoryFor(grid.Scene.BaseObjects[reference.Base].Signature),
                NativeReferenceState(reference, grid.Scene.BaseObjects[reference.Base], "source"))));
        foreach (var reference in grid.Scene.References)
        {
            var enabled = _nativeReferences.IsEnabled(reference.FormKey);
            if (!enabled || _nativeReferencePresentation.Nodes.ContainsKey(reference.FormKey))
                _parityObservations.Observe("world/active-cell", $"{grid.Scene.Cell.FormKey}/{reference.FormKey}",
                    NativeReferenceState(reference, grid.Scene.BaseObjects[reference.Base], enabled ? "resident-presentation" : "disabled"));
        }
        foreach (var reference in _nativeReferenceEvents.BoundTriggers)
            _parityObservations.Observe("world/active-cell", $"{grid.Scene.Cell.FormKey}/{reference.FormKey}",
                NativeReferenceState(reference, grid.Scene.BaseObjects[reference.Base], "source-primitive-contact-owner"));
        var environment = root.GetChildren().OfType<RuntimeNativeExteriorEnvironment>().Single();
        foreach (var node in _nativeGridStaged) environment.Register(node);
        root.GetChildren().OfType<RuntimeNativeExteriorLod>().Single().SetDetailGrid(grid);
        foreach (var actor in _nativeGridModels.Values.OfType<RuntimeNativeNpc>())
            actor.BeginPackageDialogue = (package, completed) => _nativeOpeningStageDriver!.RequestPackageDialogue(actor.Appearance.Reference!.Value, package, completed);
        foreach (var actor in _nativeReferencePresentation.Actors) actor.UpdateResidentScene(grid.Scene);
        _nativeWalkableGrid.Clear(); foreach (var cell in grid.Cells) _nativeWalkableGrid.Add(cell.Coordinates!.Value);
        _nativeGridUploads = null; _nativeGridPending = null; _nativeGridStaged.Clear(); _nativeGridModels.Clear();
        _nativeGridFailed = null; _nativeGridTarget = null;
        var models = grid.Scene.References.Select(reference => grid.Scene.BaseObjects[reference.Base])
            .Where(value => value.ModelPath is not null).Select(value => value.ModelPath!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var path in _nativeNifPrototypes.Keys.Where(path => !models.Contains(path)).ToArray())
        {
            _nativeNifPrototypes[path].Scene.Root.Free(); _nativeNifPrototypes.Remove(path);
        }
        var textures = root.GetChildren().OfType<RuntimeNativeLandscapeTransport>().Where(land => !land.IsQueuedForDeletion())
            .SelectMany(land => land.Source.Textures.Values).SelectMany(texture => new[] { texture.DiffusePath, texture.NormalPath })
            .Where(path => path is not null).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var path in _nativeLandscapeTextures.Keys.Where(path => !textures.Contains(path)).ToArray()) _nativeLandscapeTextures.Remove(path);
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
