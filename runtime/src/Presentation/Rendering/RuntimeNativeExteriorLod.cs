using System.Diagnostics;
using System.Collections.Concurrent;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.Presentation.Rendering;

internal partial class RuntimeNativeExteriorLod : Node3D
{
    private sealed record Payload(FalloutLodBlock Block, FalloutNifFile Terrain, FalloutNifFile? Objects, long Bytes);
    private sealed record ReadResult(FalloutLodBlock Block, Payload? Payload, Exception? Error);
    private sealed record Resident(Node3D Root, ShaderMaterial[] Materials, long Bytes)
    {
        internal ulong LastUse;
    }
    private FalloutExteriorLod _catalog = null!;
    private RuntimeLiveContentSource _source = null!;
    private float _units;
    private readonly Dictionary<FalloutLodBlock, Resident> _cache = [];
    private readonly Dictionary<string, string> _errors = [];
    private IReadOnlyList<FalloutLodBlock> _selected = [];
    private Task? _read;
    private ConcurrentQueue<ReadResult>? _uploads;
    private ImageTexture _mask = null!;
    private Vector4 _detailBounds;
    private Vector3 _lastSelection = new(float.PositiveInfinity, 0, 0);
    private float _updateTime;
    private ulong _generation;
    private long _residentBytes;
    internal const long CacheBudgetBytes = 256L * 1024 * 1024;
    internal float ViewDistanceMeters => _catalog.LoadDistance * _units;
    internal bool IsPrepared => _read is null && _uploads is null && _cache.Count != 0;
    internal object State => new
    {
        residentBlocks = _cache.Count,
        selectedBlocks = _selected.Count,
        residentBytes = _residentBytes,
        cacheBudgetBytes = CacheBudgetBytes,
        loading = _read is not null || _uploads is not null,
        pendingUploads = _uploads?.Count ?? 0,
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

    public override void _Process(double delta)
    {
        var camera = GetViewport().GetCamera3D();
        if (camera is null) return;
        camera.Far = Math.Max(camera.Far, ViewDistanceMeters);
        var position = camera.GlobalPosition;
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
            while (uploads.TryDequeue(out var result))
            {
                try
                {
                    if (result.Error is { } failure) Report(result.Block.Terrain, failure);
                    else Upload(result.Payload!);
                }
                catch (Exception error) { Report(result.Block.Terrain, error); }
                changed = true;
                _lastUploadMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                _maximumUploadMilliseconds = Math.Max(_maximumUploadMilliseconds, _lastUploadMilliseconds);
                if (_lastUploadMilliseconds >= 3) break;
            }
            if (changed) Commit();
            if (uploads.IsEmpty && _read is null) _uploads = null;
        }
        _updateTime += (float)delta;
        if (_updateTime < .05f) return;
        _updateTime = 0;
        foreach (var resident in _cache.Values.Where(entry => entry.Root.Visible))
            foreach (var material in resident.Materials) material.SetShaderParameter("lod_camera_xz", new Vector2(position.X, position.Z));
        if (_read is not null || _uploads is not null || position.DistanceSquaredTo(_lastSelection) < 64) return;
        _lastSelection = position;
        _selected = _catalog.Select(position.X / _units, -position.Z / _units);
        var missing = _catalog.PreparationOrder(_selected, _cache.Keys.ToHashSet(), position.X / _units, -position.Z / _units);
        if (missing.Count == 0) { Commit(); return; }
        // CPU reads/decompression stay off the render thread. Uploads retain
        // the preceding complete cover until the new selection is constructed.
        var output = new ConcurrentQueue<ReadResult>();
        _uploads = output;
        _read = Task.Run(() =>
        {
            FalloutNifFile Read(string path, out int size)
            {
                if (!_source.TryRead(path, null, out var bytes, out _)) throw new FileNotFoundException(path);
                size = bytes.Length; return FalloutNifFile.Read(bytes);
            }
            foreach (var block in missing)
            {
                try
                {
                    var terrain = Read(block.Terrain, out var terrainBytes);
                    var objectBytes = 0;
                    var objects = block.Objects is { } path ? Read(path, out objectBytes) : null;
                    output.Enqueue(new(block, new(block, terrain, objects, terrainBytes + objectBytes), null));
                }
                catch (Exception error) { output.Enqueue(new(block, null, error)); }
            }
        });
    }

    private void Upload(Payload payload)
    {
        var root = new Node3D { Name = $"LOD_{payload.Block.Level}_{payload.Block.X}_{payload.Block.Y}", Visible = false };
        var bytes = payload.Bytes;
        try
        {
            var terrain = RuntimeNativeNifMeshBuilder.Build(payload.Terrain, _units, unboundPropertyFreeLod: geometry =>
                Report($"{payload.Block.Terrain}#geometry{geometry.Block.Index}",
                    new NotSupportedException("Property-free LOD geometry requires its world material owner; this draw remains missing.")));
            root.AddChild(terrain.Root); bytes += terrain.Vertices * 80L;
            if (payload.Objects is { } objects)
            {
                try
                {
                    var model = RuntimeNativeNifMeshBuilder.Build(objects, _units);
                    root.AddChild(model.Root); bytes += model.Vertices * 80L;
                }
                catch (Exception error) { Report(payload.Block.Objects!, error); }
            }
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
            _cache.Add(payload.Block, new(root, materials, bytes));
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
        var cover = _catalog.ResidentCover(_selected, _cache.Keys.ToHashSet()).ToHashSet();
        foreach (var (block, resident) in _cache)
        {
            resident.Root.Visible = cover.Contains(block);
            if (resident.Root.Visible) resident.LastUse = _generation;
        }
        foreach (var (block, resident) in _cache.Where(pair => !pair.Value.Root.Visible).OrderBy(pair => pair.Value.LastUse).ToArray())
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
