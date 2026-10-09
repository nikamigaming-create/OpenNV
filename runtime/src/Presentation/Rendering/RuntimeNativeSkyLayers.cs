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
    private readonly Dictionary<string, Texture2D> _textures = new(StringComparer.OrdinalIgnoreCase);
    private FalloutFormKey? _weather;
    private FalloutPluginSubrecord[] _weatherFields = [];
    private readonly double[] _cloudOffsets = new double[4];
    private float[] _cloudRates = new float[4];

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
        if (path.Equals(FalloutSkyCloudResource.Path, StringComparison.OrdinalIgnoreCase))
        {
            if (_sourceCloudResource is not null) throw new InvalidOperationException("Sky attempted to publish a second Clouds field factory.");
            _sourceCloudResource = FalloutSkyCloudResource.Read(bytes, path);
        }
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
                        var cloudResource = _sourceCloudResource ?? throw new InvalidDataException("Cloud geometry has no source field factory.");
                        if (!mesh.HasMeta("opennv_nif_geometry_block") || !material.HasMeta("opennv_sky_property_block"))
                            throw new InvalidDataException("Cloud geometry/property omitted their actual NIF block identities.");
                        var geometryBlock = mesh.GetMeta("opennv_nif_geometry_block").AsInt32();
                        var propertyBlock = material.GetMeta("opennv_sky_property_block").AsInt32();
                        var cloudSlot = cloudResource.Slots.SingleOrDefault(slot => slot.Geometry == geometryBlock && slot.Property == propertyBlock) ??
                            throw new InvalidDataException("Cloud native geometry/property has no exact authored child-list slot.");
                        if (_clouds.Any(row => row.Layer == cloudSlot.Slot))
                            throw new InvalidDataException("A source cloud slot was published by two actual native meshes.");
                        _clouds.Add((mesh, material, cloudSlot.Slot));
                        break;
                }
            }
        }
        GD.Print($"OPENNV_NATIVE_SKY_RESOURCE source={identity} surfaces={scene.Surfaces} vertices={scene.Vertices} pixels=unverified");
    }

    public override void _Process(double delta)
    {
        ProcessSourceMoonCalendar();
        if (GetViewport().GetCamera3D() is { } camera) GlobalPosition = camera.GlobalPosition;
        for (var layer = 0; layer < _cloudOffsets.Length; layer++)
            _cloudOffsets[layer] = (_cloudOffsets[layer] + delta * _cloudRates[layer]) % 1.0;
        foreach (var (_, material, layer) in _clouds)
            material.SetShaderParameter("uv_offset_y", (float)_cloudOffsets[layer]);
    }

    internal void Sample(FalloutPluginStack records, FalloutWeatherLighting weather, FalloutWeatherTimeWeights weights, float multiplier)
    {
        if (_weather != weather.Form)
        {
            _weather = weather.Form;
            _weatherFields = records.GetEffective(weather.Form).ReadSubrecords().ToArray();
            var motion = FalloutWeatherMotion.Read(records.GetEffective(weather.Form),
                FalloutGameSettingFloats.ReadRetained(records, "fWeatherCloudSpeedMax", nameof(RuntimeNativeSkyLayers)));
            _cloudRates = motion.CloudUvPerSecond;
            SetMeta("opennv_source_wind_speed", motion.WindSpeed);
            SetMeta("opennv_source_cloud_uv_rates", _cloudRates);
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
        if (colors.Length != 4 * weather.TimeSamples * 4 || weights.First < 0 || weights.First >= weather.TimeSamples ||
            weights.Second < 0 || weights.Second >= weather.TimeSamples)
            throw new InvalidDataException("Weather cloud colors changed their actual source time-sample extent.");
        string[] slots = ["DNAM", "CNAM", "ANAM", "BNAM"];
        foreach (var (mesh, material, layer) in _clouds)
        {
            var declaration = _weatherFields.SingleOrDefault(field => field.Signature == slots[layer]).Data;
            var path = declaration.IsEmpty ? string.Empty : FalloutDialogueTopic.Text(declaration.Span).Replace('/', '\\');
            mesh.Visible = path.Length != 0;
            if (!mesh.Visible) continue;
            if (!path.StartsWith("textures\\", StringComparison.OrdinalIgnoreCase)) path = "textures\\" + path;
            if (!_textures.TryGetValue(path, out var texture)) _textures.Add(path, texture = NativeOwnedMediaLoader.LoadTexture(path));
            var at = layer * weather.TimeSamples * 4;
            float Channel(int channel) => (colors.Span[at + weights.First * 4 + channel] * weights.FirstWeight +
                colors.Span[at + weights.Second * 4 + channel] * weights.SecondWeight) / 255f;
            material.SetShaderParameter("cloud_map", texture);
            RecordSourceCloudPrimary(layer, path);
            material.SetShaderParameter("cloud_color_encoded", new Vector3(Channel(0), Channel(1), Channel(2)));
            material.SetShaderParameter("sky_upper_encoded", upper);
            material.SetShaderParameter("sky_lower_encoded", lower);
            material.SetShaderParameter("rgb_multiplier", multiplier);
        }
    }
}
