using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Presentation.Rendering;

internal partial class RuntimeNativeSkyLayers : Node3D
{
    private readonly List<ShaderMaterial> _atmosphere = [];
    private readonly List<ShaderMaterial> _stars = [];
    private readonly List<(MeshInstance3D Mesh, ShaderMaterial Material, int Layer)> _clouds = [];
    private readonly Dictionary<string, ImageTexture> _textures = new(StringComparer.OrdinalIgnoreCase);
    private FalloutFormKey? _weather;
    private FalloutPluginSubrecord[] _weatherFields = [];

    internal void Build(FalloutPluginStack records, FalloutSkyLightingState sky, float units)
    {
        // Engine sky resources are read from the same loose/BSA namespace as
        // scene NIFs. Their shader object types identify their rendering roles.
        AddModel("meshes\\sky\\atmosphere.nif", units);
        AddModel("meshes\\sky\\clouds.nif", units);
        var climate = records.GetEffective(sky.Climate.Form);
        var night = climate.ReadSubrecords().SingleOrDefault(field => field.Signature == "MODL").Data;
        if (!night.IsEmpty)
        {
            var path = FalloutDialogueTopic.Text(night.Span).Replace('/', '\\');
            if (!path.StartsWith("meshes\\", StringComparison.OrdinalIgnoreCase)) path = "meshes\\" + path;
            AddModel(path, units);
        }
        SetMeta("opennv_source_sky_layers", $"atmosphere={_atmosphere.Count};clouds={_clouds.Count};stars={_stars.Count}");
    }

    private void AddModel(string path, float units)
    {
        var content = RuntimeLiveContentSource.Current ?? throw new InvalidOperationException("Sky has no owned source.");
        if (!content.TryRead(path, null, out var bytes, out var identity)) throw new FileNotFoundException(path);
        var scene = RuntimeNativeNifMeshBuilder.Build(bytes, units);
        scene.Root.SetMeta("opennv_source_model", path);
        scene.Root.SetMeta("opennv_source_resource", identity);
        AddChild(scene.Root);
        foreach (var mesh in scene.Root.FindChildren("*", "", true, false).OfType<MeshInstance3D>().ToArray())
        {
            mesh.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            mesh.ExtraCullMargin = mesh.Mesh.GetAabb().Size.Length();
            for (var surface = 0; surface < mesh.Mesh.GetSurfaceCount(); surface++)
            {
                if (mesh.GetActiveMaterial(surface) is not ShaderMaterial material || material.ResourceName != NativeNifSkyMaterial.Identity)
                    throw new NotSupportedException($"Sky surface in {path} has no source sky shader.");
                switch (material.GetMeta("opennv_sky_object_type").AsUInt32())
                {
                    case 2: _atmosphere.Add(material); break;
                    case 5: _stars.Add(material); break;
                    case 3:
                        if (mesh.Mesh.GetSurfaceCount() != 1) throw new NotSupportedException("Cloud layer has multiple material surfaces.");
                        _clouds.Add((mesh, material, 0));
                        for (var layer = 1; layer < 4; layer++)
                        {
                            var layerMaterial = (ShaderMaterial)material.Duplicate();
                            layerMaterial.RenderPriority = -126 + layer;
                            var layerMesh = new MeshInstance3D
                            {
                                Name = $"WeatherCloudLayer{layer}",
                                Mesh = mesh.Mesh,
                                Transform = mesh.Transform,
                                MaterialOverride = layerMaterial,
                                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                                ExtraCullMargin = mesh.ExtraCullMargin
                            };
                            mesh.GetParent().AddChild(layerMesh);
                            _clouds.Add((layerMesh, layerMaterial, layer));
                        }
                        break;
                }
            }
        }
        GD.Print($"OPENNV_NATIVE_SKY_RESOURCE source={identity} surfaces={scene.Surfaces} vertices={scene.Vertices} pixels=unverified");
    }

    public override void _Process(double delta)
    {
        if (GetViewport().GetCamera3D() is { } camera) GlobalPosition = camera.GlobalPosition;
    }

    internal void Sample(FalloutPluginStack records, FalloutWeatherLighting weather, FalloutWeatherTimeWeights weights, float multiplier)
    {
        if (_weather != weather.Form)
        {
            _weather = weather.Form;
            _weatherFields = records.GetEffective(weather.Form).ReadSubrecords().ToArray();
        }
        Vector3 Color(int kind) { var rgb = weather.Sample(weights, kind); return new(rgb[0], rgb[1], rgb[2]); }
        var upper = Color(0); var lower = Color(7); var horizon = Color(8); var stars = Color(6);
        foreach (var material in _atmosphere)
        {
            material.SetShaderParameter("sky_upper_encoded", upper);
            material.SetShaderParameter("sky_lower_encoded", lower);
            material.SetShaderParameter("horizon_encoded", horizon);
            material.SetShaderParameter("rgb_multiplier", multiplier);
        }
        var night = (weights.First == 3 ? weights.FirstWeight : 0) + (weights.Second == 3 ? weights.SecondWeight : 0);
        foreach (var material in _stars) material.SetShaderParameter("stars_encoded", new Vector4(stars.X, stars.Y, stars.Z, night));
        var colors = _weatherFields.Single(field => field.Signature == "PNAM").Data;
        if (colors.Length != 96) throw new InvalidDataException("Weather clouds require four six-sample color arrays.");
        string[] slots = ["DNAM", "CNAM", "ANAM", "BNAM"];
        foreach (var (mesh, material, layer) in _clouds)
        {
            var declaration = _weatherFields.SingleOrDefault(field => field.Signature == slots[layer]).Data;
            var path = declaration.IsEmpty ? string.Empty : FalloutDialogueTopic.Text(declaration.Span).Replace('/', '\\');
            mesh.Visible = path.Length != 0;
            if (!mesh.Visible) continue;
            if (!path.StartsWith("textures\\", StringComparison.OrdinalIgnoreCase)) path = "textures\\" + path;
            if (!_textures.TryGetValue(path, out var texture)) _textures.Add(path, texture = NativeOwnedMediaLoader.LoadTexture(path));
            var at = layer * 24;
            float Channel(int channel) => (colors.Span[at + weights.First * 4 + channel] * weights.FirstWeight +
                colors.Span[at + weights.Second * 4 + channel] * weights.SecondWeight) / 255f;
            material.SetShaderParameter("cloud_map", texture);
            material.SetShaderParameter("cloud_color_encoded", new Vector3(Channel(0), Channel(1), Channel(2)));
            material.SetShaderParameter("sky_upper_encoded", upper);
            material.SetShaderParameter("sky_lower_encoded", lower);
            material.SetShaderParameter("rgb_multiplier", multiplier);
        }
    }
}
