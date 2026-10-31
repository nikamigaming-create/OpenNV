using Godot;
using OpenNV.Runtime.Content;
using NumericsVector3 = System.Numerics.Vector3;

namespace OpenNV.Runtime.Presentation.Rendering;

// Retains definitions only while the actual decoded LAND owner exists. Grass
// batches have their own source camera-distance/frustum admission, not a warm ring.
internal partial class RuntimeNativeGrassOwner : Node
{
    private sealed record SourceCell(RuntimeNativeLandscapeTransport Node, float Water, IReadOnlyList<FalloutGrassArea> Areas);
    private sealed record LiveArea(FalloutFormKey Cell, byte Quadrant, RuntimeNativeGrassDraw Draw, int Count);
    private readonly Dictionary<(int X, int Y), SourceCell> _cells = [];
    private readonly Dictionary<(FalloutFormKey Grass, uint Area), LiveArea> _live = [];
    private readonly Dictionary<string, RuntimeNativeGrassModel> _models = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<FalloutFormKey, ShaderMaterial> _materials = [];
    private RuntimeLiveContentSource _content = null!;
    private FalloutPluginStack _records = null!;
    private FalloutInstallationSettings _ini = null!;
    private FalloutGrassSettings _settings = null!;
    private FalloutGrassAreas _areas = null!;
    private FalloutGrassPopulation _population = null!;
    private RuntimeNativeExteriorEnvironment _environment = null!;
    private Node3D _root = null!;
    private Shader _program = null!;
    private float _units, _maximumWind, _start, _fade, _minimumStart, _defaultFov;
    private NumericsVector3? _lastEye, _lastForward;
    private bool _dirty = true, _disposed;
    internal string? Error { get; private set; }
    private RuntimeNativeGrassFrame? _lastFrame;
    internal object State => new
    {
        error = Error,
        sourceCells = _cells.Count,
        batches = _live.Count,
        instances = _live.Values.Sum(value => value.Count),
        models = _models.Keys.ToArray(),
        grasses = _live.GroupBy(value => value.Key.Grass).Select(group => new
        {
            form = group.Key.ToString(),
            definition = group.First().Value.Draw.Source,
            batches = group.Count(),
            instances = group.Sum(value => value.Value.Count)
        }).ToArray(),
        frame = _lastFrame is not { } frame ? null : new
        {
            frame.GameHour,
            wind = new[] { frame.WindDirection.X, frame.WindDirection.Y, frame.WindMagnitude },
            ambient = new[] { frame.Ambient.X, frame.Ambient.Y, frame.Ambient.Z },
            diffuseDirection = new[] { frame.DiffuseDirection.X, frame.DiffuseDirection.Y, frame.DiffuseDirection.Z },
            diffuseColor = new[] { frame.DiffuseColor.X, frame.DiffuseColor.Y, frame.DiffuseColor.Z },
            frame.DirectionalScale,
            fog = new[] { frame.FogColor.X, frame.FogColor.Y, frame.FogColor.Z }
        }
    };

    internal void Configure(Node3D root, RuntimeLiveContentSource content, FalloutPluginStack records,
        RuntimeNativeExteriorEnvironment environment, float units)
    {
        _root = root; _content = content; _records = records; _environment = environment; _units = units;
        _ini = FalloutInstallationSettings.Read(content);
        _settings = FalloutGrassSettings.Read(_ini); _areas = new(records);
        var terrain = new FalloutGrassLandscape((x, y) => _cells.TryGetValue((x, y), out var cell) ? cell.Node.Source : null);
        _population = new(_settings, terrain);
        _maximumWind = Math.Max(MathF.Abs(_ini.Number("Grass", "fGrassWindMagnitudeMin")), MathF.Abs(_ini.Number("Grass", "fGrassWindMagnitudeMax")));
        _start = _ini.Number("Grass", "fGrassStartFadeDistance");
        _fade = _ini.Number("Grass", "fGrassFadeRange");
        _minimumStart = _ini.Number("Grass", "fGrassMinStartFadeDistance");
        _defaultFov = FalloutCameraProjection.Read(_ini).ReferenceHorizontalFovDegrees;
        _program = new() { Code = RuntimeNativeGrassShader.Code };
    }

    internal void Register(IEnumerable<RuntimeNativeLandscapeTransport> nodes)
    {
        foreach (var node in nodes)
        {
            var land = node.Source;
            if (_cells.TryGetValue(land.ActiveCoordinates, out var existing))
            {
                if (!ReferenceEquals(existing.Node, node)) throw new InvalidOperationException("Grass LAND coordinate has two live owners.");
                continue;
            }
            var cell = FalloutCellSceneReader.ReadDefinition(_records, land.ActiveCell);
            _cells.Add(land.ActiveCoordinates, new(node, FalloutGrassWater.Read(_records, cell), _areas.Build(land, _settings)));
            node.TreeExiting += () => RetireLand(node);
            _dirty = true;
        }
    }

    private void RetireLand(RuntimeNativeLandscapeTransport node)
    {
        if (_disposed || !_cells.TryGetValue(node.Source.ActiveCoordinates, out var current) || !ReferenceEquals(current.Node, node)) return;
        RetireWhere(value => value.Cell == node.Source.ActiveCell);
        _cells.Remove(node.Source.ActiveCoordinates); _dirty = true;
    }

    public override void _Process(double delta)
    {
        if (Error is not null || _disposed) return;
        try
        {
            if (!_ini.Boolean("Grass", "bDrawShaderGrass") || _start <= _minimumStart + .5f) return;
            var camera = GetViewport().GetCamera3D();
            if (camera is null) return;
            if (camera is XRCamera3D)
                throw new NotSupportedException("Grass quadrant admission requires the shared asymmetric XR eye-frustum union; flat camera FOV is not an XR substitute.");
            if (_ini.Boolean("Grass", "bGrassPointLighting")) throw new NotSupportedException("Grass point-light shader inputs have no shared owner.");
            var position = camera.GlobalPosition / _units;
            var direction = -camera.GlobalBasis.Z;
            var eye = new NumericsVector3(position.X, -position.Z, position.Y);
            var forward = NumericsVector3.Normalize(new(direction.X, -direction.Z, direction.Y));
            var aspect = GetViewport().GetVisibleRect().Size.Aspect();
            var referenceFov = new FalloutCameraProjection(camera.Fov, camera.Near / _units).ReferenceHorizontalFovDegrees;
            var view = new FalloutGrassVisibility.View(eye, forward, referenceFov, aspect,
                camera.Near / _units, camera.Far / _units, referenceFov / _defaultFov);
            if (_dirty || _lastEye is not { } previous || _lastForward is not { } previousForward ||
                new NumericsVector3(eye.X - previous.X, eye.Y - previous.Y, 0).Length() > 64 ||
                NumericsVector3.Dot(forward, previousForward) < FalloutGrassVisibility.DirectionThreshold(view))
            {
                var started = System.Diagnostics.Stopwatch.GetTimestamp();
                Update(view); _lastEye = eye; _lastForward = forward; _dirty = false;
                SetMeta("opennv_grass_population_milliseconds", System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            }
            var frame = _environment.GrassFrame(camera, _ini, _start, _fade);
            _lastFrame = frame;
            var sampled = new HashSet<FalloutFormKey>();
            foreach (var (key, live) in _live)
            {
                if (sampled.Add(key.Grass)) live.Draw.Sample(frame);
                else live.Draw.Show();
            }
            SetMeta("opennv_grass_source_cells", _cells.Count);
            SetMeta("opennv_grass_batches", _live.Count);
            SetMeta("opennv_grass_instances", _live.Values.Sum(value => value.Count));
        }
        catch (Exception error)
        {
            Error = error.Message; SetMeta("opennv_grass_error", Error);
            GD.PushError($"OPENNV_GRASS_DIVERGENCE {error}");
        }
    }

    private void Update(FalloutGrassVisibility.View view)
    {
        var centerX = FalloutGrassVisibility.CameraCell(view.Eye.X);
        var centerY = FalloutGrassVisibility.CameraCell(view.Eye.Y);
        for (var x = 0; x < 5; x++)
            for (var y = 0; y < 5; y++)
            {
                if (!_cells.TryGetValue((centerX + x - 2, centerY + y - 2), out var cell)) continue;
                var land = cell.Node.Source;
                if (x is 0 or 4 || y is 0 or 4 || !FalloutGrassVisibility.CellAdmitted(land, view.Eye, _start + _fade))
                { RetireWhere(value => value.Cell == land.ActiveCell); continue; }
                for (byte quadrant = 0; quadrant < 4; quadrant++)
                {
                    if (!FalloutGrassVisibility.QuadrantAdmitted(land, quadrant, view, _start + _fade))
                    { RetireWhere(value => value.Cell == land.ActiveCell && value.Quadrant == quadrant); continue; }
                    foreach (var area in cell.Areas.Where(area => area.Quadrant == quadrant))
                        foreach (var slot in area.Slots)
                        {
                            var key = (slot.Grass.Form, area.Key);
                            if (_live.ContainsKey(key)) continue;
                            var batch = _population.Prepare(land, area, slot, cell.Water);
                            if (batch.Instances.Length == 0) continue;
                            if (!_models.TryGetValue(slot.Grass.Model, out var model))
                                _models.Add(slot.Grass.Model, model = RuntimeNativeGrassModel.Read(_content, slot.Grass.Model));
                            if ((slot.Grass.Flags & 1) != 0 && !model.HasNormals)
                                throw new InvalidDataException($"GRAS {slot.Grass.Form} requires missing authored model normals.");
                            if (!_materials.TryGetValue(slot.Grass.Form, out var material))
                                _materials.Add(slot.Grass.Form, material = new() { Shader = _program });
                            _live.Add(key, new(land.ActiveCell, quadrant,
                                new(_root, _program, model, batch, _units, _maximumWind, material), batch.Instances.Length));
                        }
                }
            }
    }

    private void RetireWhere(Func<LiveArea, bool> predicate)
    {
        foreach (var key in _live.Where(row => predicate(row.Value)).Select(row => row.Key).ToArray())
        { _live[key].Draw.Dispose(); _live.Remove(key); }
    }

    public override void _ExitTree()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var live in _live.Values) live.Draw.Dispose();
        _live.Clear(); _cells.Clear();
        foreach (var material in _materials.Values) { material.Shader = null; material.Dispose(); }
        foreach (var model in _models.Values) model.Dispose();
        _materials.Clear(); _models.Clear(); _program?.Dispose();
    }
}
