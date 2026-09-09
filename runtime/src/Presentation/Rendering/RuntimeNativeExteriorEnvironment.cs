using System.Buffers.Binary;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Presentation.Rendering;

internal partial class RuntimeNativeExteriorEnvironment : Node
{
    private FalloutSkyLightingState _sky = null!;
    private FalloutPluginStack _records = null!;
    private Func<float> _hour = null!;
    private float _units;
    private RuntimeNativeSkyLayers _layers = null!;
    private DirectionalLight3D _sun = null!;
    private Godot.Environment _environment = null!;
    private readonly HashSet<GeometryInstance3D> _meshes = [];
    private Node _scene = null!;
    private Node? _sceneRoot;
    private SceneTree? _tree;
    private float _lastHour = -1;
    private Vector3 _ambient, _fog, _range;
    private bool _lightingBound;
    internal Compositor? Compositor { get; set; }
    internal FalloutImageSpace? ImageSpace { get; set; }
    internal int RegisteredSurfaces => _meshes.Count;

    internal void Configure(FalloutPluginStack records, FalloutSkyLightingState sky, Func<float> hour, float units, Node? sceneRoot = null)
    { _records = records; _sky = sky; _hour = hour; _units = units; _sceneRoot = sceneRoot; }

    internal void Register(Node root)
    {
        BindAdded(root);
        foreach (var mesh in root.FindChildren("*", "", true, false).OfType<GeometryInstance3D>()) BindAdded(mesh);
    }

    private void BindAdded(Node node)
    {
        if (node is not GeometryInstance3D mesh || !_scene.IsAncestorOf(mesh) || mesh.GetViewport() != _scene.GetViewport() || !_meshes.Add(mesh)) return;
        if (_lightingBound) NativeNifMaterialEnvironment.Bind(mesh, _ambient, _fog, _range, _units);
    }

    private void RemoveSurface(Node node) { if (node is GeometryInstance3D mesh) _meshes.Remove(mesh); }

    public override void _Ready()
    {
        _scene = _sceneRoot ?? GetParent(); _tree = GetTree();
        _tree.NodeAdded += BindAdded; _tree.NodeRemoved += RemoveSurface;
        _layers = new RuntimeNativeSkyLayers { Name = "OwnedSkyLayers" };
        _layers.Build(_records, _sky, _units);
        AddChild(_layers);
        _environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = Colors.Black,
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightEnergy = 1,
            TonemapMode = Godot.Environment.ToneMapper.Linear,
            FogEnabled = true,
            FogMode = Godot.Environment.FogModeEnum.Depth,
        };
        AddChild(new WorldEnvironment { Name = "OwnedExteriorEnvironment", Environment = _environment, Compositor = Compositor });
        _sun = new DirectionalLight3D
        {
            Name = "OwnedExteriorSun",
            ShadowEnabled = true,
            DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel4Splits
        };
        AddChild(_sun);
        Register(_scene);
        SetMeta("opennv_exterior_presentation_unverified", "clouds,celestial-projection,weather-transition,region-edge-blend,LOD,matched-pixels");
        Sample();
    }

    public override void _ExitTree()
    {
        if (_tree is not null) { _tree.NodeAdded -= BindAdded; _tree.NodeRemoved -= RemoveSurface; }
        _tree = null; _meshes.Clear();
    }

    public override void _Process(double delta)
    {
        if (Math.Abs(_hour() - _lastHour) > 1f / 3600) Sample();
    }

    private void Sample()
    {
        var hour = _lastHour = _hour();
        var weather = _sky.ActiveWeather;
        var weights = FalloutWeatherTimeWeights.Sample(_sky.Climate, hour, _sky.DaytimeExtension);
        Color ColorAt(int kind) { var rgb = weather.Sample(weights, kind); return new(rgb[0], rgb[1], rgb[2]); }
        var ambient = ColorAt(3); var fog = ColorAt(1);
        _layers.Sample(_records, weather, weights, ImageSpace?.RawTraits[8] ?? 1f);
        var solarAngle = (hour - 6) * MathF.PI / 12;
        var direction = new Vector3(MathF.Cos(solarAngle), MathF.Sin(solarAngle), 0).Normalized();
        _sun.Basis = RetailLighting.DirectionalLightBasis(direction);
        _sun.LightColor = RetailLighting.GodotLightColor(ColorAt(4));
        _sun.LightEnergy = direction.Y > 0 ? ImageSpace?.RawTraits[11] ?? 1 : 0;
        _environment.AmbientLightColor = ambient;
        _environment.FogLightColor = fog;
        var declaration = _records.GetEffective(weather.Form).ReadSubrecords().Single(field => field.Signature == "FNAM").Data;
        if (declaration.Length != 24) throw new InvalidDataException("Exterior weather fog declaration requires six floats.");
        var values = new float[6];
        for (var index = 0; index < values.Length; index++)
        {
            values[index] = BinaryPrimitives.ReadSingleLittleEndian(declaration.Span[(index * 4)..]);
            if (!float.IsFinite(values[index])) throw new InvalidDataException("Weather fog is non-finite.");
        }
        var night = (weights.First == 3 ? weights.FirstWeight : 0) + (weights.Second == 3 ? weights.SecondWeight : 0);
        var near = Mathf.Lerp(values[0], values[2], night);
        var far = Mathf.Lerp(values[1], values[3], night);
        var power = Mathf.Lerp(values[4], values[5], night);
        if (far <= near || power <= 0) throw new InvalidDataException("Weather fog interval is invalid.");
        _environment.FogDepthBegin = Math.Max(0, near * _units);
        _environment.FogDepthEnd = far * _units;
        _environment.FogDepthCurve = power;
        var nextAmbient = new Vector3(ambient.R, ambient.G, ambient.B);
        var nextFog = new Vector3(fog.R, fog.G, fog.B);
        var nextRange = new Vector3(near, far, power);
        if (!_lightingBound || _ambient != nextAmbient || _fog != nextFog || _range != nextRange)
        {
            _ambient = nextAmbient; _fog = nextFog; _range = nextRange; _lightingBound = true;
            foreach (var mesh in _meshes) NativeNifMaterialEnvironment.Bind(mesh, _ambient, _fog, _range, _units);
        }
        SetMeta("opennv_source_weather", weather.Form.ToString());
    }
}
