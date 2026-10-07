using System.Diagnostics;
using System.Threading.Channels;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.Presentation.Rendering;

internal partial class RuntimeNativeExteriorLod : Node3D
{
    private sealed record Tile(string? Path, FalloutNifFile? Source, long Bytes, Exception? Error);
    private sealed record Payload(FalloutLodBlock Block, Tile Terrain, Tile Objects);
    private sealed record Resident(Node3D Root, Node3D? Terrain, Node3D? Objects, ShaderMaterial[] Materials,
        long Bytes, bool TerrainComplete, bool ObjectsComplete)
    {
        internal ulong LastUse;
    }
    private FalloutExteriorLod _catalog = null!;
    private RuntimeLiveContentSource _source = null!;
    private float _units;
    private readonly Dictionary<FalloutLodBlock, Resident> _cache = [];
    private readonly HashSet<FalloutLodBlock> _failed = [];
    private readonly Dictionary<string, string> _errors = [];
    private IReadOnlyList<FalloutLodBlock> _selected = [];
    private Task? _read;
    private ChannelReader<Payload>? _uploads;
    private readonly CancellationTokenSource _readCancellation = new();
    private ImageTexture _mask = null!;
    private Vector4 _detailBounds;
    private Vector3 _lastSelection = new(float.PositiveInfinity, 0, 0);
    private float _updateTime;
    private ulong _generation;
    private long _residentBytes;
    private int _detailRadius;
    private bool _completeTerrainCover, _completeObjectCover;
    internal const long CacheBudgetBytes = 512L * 1024 * 1024;
    internal float ViewDistanceMeters => _catalog.LoadDistance * _units;
    internal object State => new
    {
        sourceBlocks = _catalog.Blocks.Count,
        residentBlocks = _cache.Count,
        selectedBlocks = _selected.Count,
        failedBlocks = _failed.Count,
        completeTerrainCover = _completeTerrainCover,
        completeObjectCover = _completeObjectCover,
        residentBytes = _residentBytes,
        cacheBudgetBytes = CacheBudgetBytes,
        loading = _read is not null || _uploads is not null,
        pendingUploads = _uploads?.Count ?? 0,
        preparationWorkers = FalloutContentWorkers.Concurrency,
        preparedQueueLimit = FalloutContentWorkers.Concurrency * 2,
        errors = _errors.ToArray(),
        lastUploadMilliseconds = _lastUploadMilliseconds,
        maximumUploadMilliseconds = _maximumUploadMilliseconds
    };
    private double _lastUploadMilliseconds;
    private double _maximumUploadMilliseconds;

    internal void Configure(FalloutPluginStack records, RuntimeLiveContentSource source, FalloutExteriorGridScene grid, float units)
    {
        _source = source; _units = units;
        var worldName = FalloutExteriorLod.WorldName(records, grid.Scene.Cell.Worldspace ??
            throw new InvalidDataException("Exterior LOD has no worldspace."));
        var settings = FalloutInstallationSettings.Read(source);
        _catalog = new(worldName, source.ResourcePathsUnder($"meshes/landscape/lod/{worldName}"),
            settings.Number("TerrainManager", "fBlockLoadDistance"), settings.Number("TerrainManager", "fSplitDistanceMult"),
            settings.Number("TerrainManager", "fBlockMorphDistanceMult"));
        Name = "OwnedExteriorLod";
        SetMeta("opennv_source_lod_world", worldName);
        SetMeta("opennv_lod_parity", "unverified-ordinary-transitions-texture-blends-and-pixels");
        SetDetailGrid(grid);
    }

    internal void SetDetailGrid(FalloutExteriorGridScene grid)
    {
        _detailRadius = grid.Radius;
        var cells = grid.Cells.Select(cell => cell.Coordinates ?? throw new InvalidDataException("LOD near grid is not spatial.")).ToArray();
        var minX = cells.Min(cell => cell.X); var maxX = cells.Max(cell => cell.X);
        var minY = cells.Min(cell => cell.Y); var maxY = cells.Max(cell => cell.Y);
        using var image = Image.CreateEmpty(maxX - minX + 1, maxY - minY + 1, false, Image.Format.R8);
        image.Fill(Colors.Black);
        foreach (var cell in cells) image.SetPixel(cell.X - minX, maxY - cell.Y, Colors.White);
        _mask = ImageTexture.CreateFromImage(image);
        _detailBounds = new(minX * 4096 * _units, -(maxY + 1) * 4096 * _units,
            (maxX + 1) * 4096 * _units, -minY * 4096 * _units);
        foreach (var resident in _cache.Values) BindDetail(resident.Materials);
    }

    internal async Task PrepareInitialSelection(Vector3 viewPosition)
    {
        if (!GetTree().Paused || CanProcess() || !viewPosition.IsFinite())
            throw new InvalidOperationException("Initial distant-world preparation requires the loading pause and finite destination view.");
        // The ordinary process owner is paused with gameplay. Pump that same
        // bounded queue between loading frames before releasing player input.
        _updateTime = .05f;
        ProcessAt(viewPosition, 0);
        while (_read is not null || _uploads is not null)
        {
            _readCancellation.Token.ThrowIfCancellationRequested();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            ProcessAt(viewPosition, 0);
        }
        ProcessAt(viewPosition, 0);
        if (_selected.Count != 0 && _cache.Count == 0)
            throw new InvalidDataException("The selected distant world has no admitted geometry; see its source failures.");
        GD.Print($"OPENNV_NATIVE_LOD_PREPARED selected={_selected.Count} resident={_cache.Count} " +
            $"failed={_failed.Count} terrainCover={_completeTerrainCover} objectCover={_completeObjectCover} " +
            $"sourceFailures={_errors.Count} parity=unverified");
    }

    public override void _Process(double delta)
    {
        var camera = GetViewport().GetCamera3D();
        if (camera is null) return;
        camera.Far = Math.Max(camera.Far, ViewDistanceMeters);
        ProcessAt(camera.GlobalPosition, delta);
    }

    private void ProcessAt(Vector3 position, double delta)
    {
        NativeExteriorDetailBlend.SetRegion(position, 4096 * _units, _detailRadius, _completeTerrainCover, _completeObjectCover);
        RenderingServer.GlobalShaderParameterSet("opennv_lod_camera_xz", new Vector2(position.X, position.Z));
        if (_read is { IsCompleted: true } read)
        {
            _read = null;
            try { read.GetAwaiter().GetResult(); }
            catch (Exception error) { Report("source-read", error); }
        }
        if (_uploads is { } uploads)
        {
            var started = Stopwatch.GetTimestamp();
            var changed = false;
            while (uploads.TryRead(out var result))
            {
                try { Upload(result); }
                catch (Exception error) { Report(result.Block.Terrain, error); }
                changed = true;
                _lastUploadMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                _maximumUploadMilliseconds = Math.Max(_maximumUploadMilliseconds, _lastUploadMilliseconds);
                if (_lastUploadMilliseconds >= 3) break;
            }
            if (changed) Commit();
            if (uploads.Count == 0 && _read is null) _uploads = null;
        }
        _updateTime += (float)delta;
        if (_updateTime < .05f) return;
        _updateTime = 0;
        if (_read is not null || _uploads is not null || position.DistanceSquaredTo(_lastSelection) < 64) return;
        _lastSelection = position;
        _selected = _catalog.Select(position.X / _units, -position.Z / _units);
        var missing = _catalog.PreparationOrder(_selected, _cache.Keys.Concat(_failed).ToHashSet(), position.X / _units, -position.Z / _units);
        if (missing.Count == 0) { Commit(); return; }
        // CPU reads/decompression stay off the render thread. Uploads retain
        // the preceding complete cover until the new selection is constructed.
        var output = Channel.CreateBounded<Payload>(new BoundedChannelOptions(FalloutContentWorkers.Concurrency * 2)
        { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
        _uploads = output.Reader;
        var cancellation = _readCancellation.Token;
        _read = Task.Run(async () =>
        {
            Tile Read(string? path)
            {
                if (path is null) return new(null, null, 0, null);
                byte[]? bytes = null;
                try
                {
                    if (!_source.TryRead(path, null, out bytes, out _)) throw new FileNotFoundException(path);
                    return new(path, FalloutNifFile.Read(bytes), bytes.Length, null);
                }
                catch (Exception error) { return new(path, null, bytes?.Length ?? 0, error); }
            }
            try
            {
                await Parallel.ForEachAsync(missing, new ParallelOptions
                { MaxDegreeOfParallelism = FalloutContentWorkers.Concurrency, CancellationToken = cancellation }, async (block, token) =>
                {
                    var result = await FalloutContentWorkers.Run(() => new Payload(block, Read(block.Terrain), Read(block.Objects)), token);
                    await output.Writer.WriteAsync(result, token);
                });
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            finally { output.Writer.TryComplete(); }
        });
    }

    public override void _ExitTree() => _readCancellation.Cancel();

    internal Task StopSourceReads()
    {
        SetProcess(false);
        _readCancellation.Cancel();
        return _read ?? Task.CompletedTask;
    }

    private void Upload(Payload payload)
    {
        var root = new Node3D { Name = $"LOD_{payload.Block.Level}_{payload.Block.X}_{payload.Block.Y}", Visible = false };
        var bytes = payload.Terrain.Bytes + payload.Objects.Bytes;
        try
        {
            (Node3D? Root, bool Complete) Build(Tile tile, bool terrain)
            {
                if (tile.Error is { } failure) { Report(tile.Path!, failure); return (null, false); }
                if (tile.Source is null) return (null, false);
                try
                {
                    var complete = true;
                    Action<FalloutNifGeometry>? omitted = terrain ? geometry =>
                    {
                        complete = false;
                        Report($"{tile.Path}#geometry{geometry.Block.Index}",
                            new NotSupportedException("Property-free LOD geometry requires its world material owner; this draw remains missing."));
                    }
                    : null;
                    var model = RuntimeNativeNifMeshBuilder.Build(tile.Source, _units, unboundPropertyFreeLod: omitted);
                    if (model.Surfaces == 0) { model.Root.Free(); return (null, false); }
                    root.AddChild(model.Root); bytes += model.Vertices * 80L;
                    return (model.Root, complete);
                }
                catch (Exception error) { Report(tile.Path!, error); return (null, false); }
            }
            var terrain = Build(payload.Terrain, true);
            var objects = Build(payload.Objects, false);
            if (terrain.Root is null && objects.Root is null) { root.Free(); _failed.Add(payload.Block); return; }
            var meshes = root.FindChildren("*", "", true, false).OfType<MeshInstance3D>().ToArray();
            var materials = meshes.SelectMany(mesh => Enumerable.Range(0, mesh.Mesh.GetSurfaceCount()).Select(mesh.GetActiveMaterial))
                .OfType<ShaderMaterial>().Where(material => material.ResourceName == NativeNifLodMaterial.ResourceIdentity).Distinct().ToArray();
            foreach (var mesh in meshes) mesh.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            var morphEnd = payload.Block.Level * 2 * 4096 * _catalog.SplitMultiplier * _units;
            foreach (var material in materials)
            {
                material.SetShaderParameter("morph_range", new Vector2(morphEnd * _catalog.MorphMultiplier, morphEnd));
                bytes += material.GetMeta("opennv_lod_texture_bytes", 0L).AsInt64();
            }
            BindDetail(materials);
            AddChild(root);
            GetParent().GetChildren().OfType<RuntimeNativeExteriorEnvironment>().SingleOrDefault()?.Register(root);
            _cache.Add(payload.Block, new(root, terrain.Root, objects.Root, materials, bytes, terrain.Complete, objects.Complete));
            _residentBytes += bytes;
        }
        catch { root.Free(); throw; }
    }

    private void BindDetail(IEnumerable<ShaderMaterial> materials)
    {
        foreach (var material in materials)
        {
            material.SetShaderParameter("detail_mask", _mask);
            material.SetShaderParameter("detail_bounds", _detailBounds);
            material.SetShaderParameter("detail_mask_enabled", true);
        }
    }

    private void Commit()
    {
        ++_generation;
        var terrainCover = _catalog.ResidentCover(_selected, _cache.Where(pair => pair.Value.Terrain is not null).Select(pair => pair.Key).ToHashSet()).ToHashSet();
        var objectCover = _catalog.ResidentCover(_selected, _cache.Where(pair => pair.Value.Objects is not null).Select(pair => pair.Key).ToHashSet()).ToHashSet();
        _completeTerrainCover = _catalog.HasCompleteCover(_selected,
            _cache.Where(pair => pair.Value.TerrainComplete).Select(pair => pair.Key).ToHashSet());
        _completeObjectCover = _catalog.HasCompleteCover(_selected,
            _cache.Where(pair => pair.Value.ObjectsComplete).Select(pair => pair.Key).ToHashSet());
        foreach (var (block, resident) in _cache)
        {
            if (resident.Terrain is { } terrain) terrain.Visible = terrainCover.Contains(block);
            if (resident.Objects is { } objects) objects.Visible = objectCover.Contains(block);
            resident.Root.Visible = terrainCover.Contains(block) || objectCover.Contains(block);
            if (resident.Root.Visible) resident.LastUse = _generation;
        }
        // A selected child is staged behind its coarse parent until every
        // sibling is available. Evicting that invisible child during the
        // upload batch starves refinement and can strand the coarse cover.
        var demanded = _selected.ToHashSet();
        foreach (var (block, resident) in _cache.Where(pair => !pair.Value.Root.Visible && !demanded.Contains(pair.Key))
            .OrderBy(pair => pair.Value.LastUse).ToArray())
        {
            if (_residentBytes <= CacheBudgetBytes) break;
            _cache.Remove(block); _residentBytes -= resident.Bytes; resident.Root.Free();
        }
        SetMeta("opennv_lod_resident_blocks", _cache.Count);
        SetMeta("opennv_lod_missing_blocks", _selected.Count(block => !_cache.ContainsKey(block)));
    }

    private void Report(string path, Exception error)
    {
        if (_errors.TryGetValue(path, out var prior) && prior == error.Message) return;
        _errors[path] = error.Message;
        GD.PushError($"OPENNV_LOD_DIVERGENCE source={path}: {error.Message}");
    }
}
