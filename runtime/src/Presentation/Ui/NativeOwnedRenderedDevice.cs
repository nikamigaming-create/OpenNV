using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.World.Actors;

namespace OpenNV.Runtime.Presentation.Ui;

/// <summary>Direct NIF rendered-menu device and its independent projection.</summary>
internal sealed partial class NativeOwnedRenderedDevice : Control
{
    private readonly FalloutInstallationSettings _settings;
    private readonly FalloutNifFile _source;
    private readonly Node3D _model;
    private readonly Node3D _pickModel;
    private readonly RuntimeNativePlayerActor? _player;
    private readonly TextureRect _image;
    private readonly bool _pipBoy;
    internal string ScreenName => _pipBoy ? "pipboyscreen:0" : "Screen:0";
    internal SubViewport View { get; }
    internal Camera3D Camera { get; }
    internal Node3D Model => _model;
    internal FalloutNifFile Source => _source;

    internal NativeOwnedRenderedDevice(string modelPath, FalloutInstallationSettings settings, bool pipBoy = false, RuntimeNativePlayerActor? player = null)
    {
        Name = "OwnedRenderedDevice";
        ProcessMode = ProcessModeEnum.Always;
        _settings = settings;
        _pipBoy = pipBoy;
        var content = RuntimeLiveContentSource.Current ?? throw new InvalidOperationException("Owned device content is absent.");
        if (!content.TryRead(modelPath, null, out var bytes, out var identity)) throw new FileNotFoundException(modelPath);
        _source = FalloutNifFile.Read(bytes);
        _player = player;
        var model = player is null ? RuntimeNativeNifMeshBuilder.Build(_source, 1) : null;
        model?.Root.SetMeta("opennv_source_model", modelPath);
        View = new SubViewport
        {
            Name = "DeviceView",
            OwnWorld3D = true,
            TransparentBg = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
        };
        AddChild(View);
        _model = (Node3D?)player ?? model!.Root;
        View.AddChild(_model);
        _pickModel = player is null ? _model : player.Actor.Parts.Single(part => part.Root.FindChildren("*", "", true, false)
            .OfType<MeshInstance3D>().Any(mesh => mesh.GetMeta("opennv_nif_source_name", "").AsString() == ScreenName)).Root;
        Surface = new(modelPath, _pickModel, ScreenName);
        Camera = new Camera3D { Name = "DeviceCamera", Current = true };
        // Native NiCamera columns denote direction, up, right. The identity
        // rendered-menu camera therefore looks along native +X with +Y up.
        Camera.Basis = new Basis(Vector3.Up, Vector3.Forward, Vector3.Left);
        View.AddChild(Camera);
        _image = new TextureRect
        {
            Name = "DevicePixels",
            Texture = View.GetTexture(),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(_image);
        SetMeta("opennv_device_source", identity);
        SetMeta("opennv_device_surfaces", model?.Surfaces ?? player!.Actor.Parts.Sum(part => part.Surfaces));
        SetMeta("opennv_device_unbound", "render-target-composition,screen-effects,shadow-and-specular-selection");
        Resized += Layout;
    }

    public override void _Ready() => Layout();

    private void Layout()
    {
        if (!IsInsideTree() || Size.X <= 0 || Size.Y <= 0) return;
        View.Size = new Vector2I(Math.Max(1, (int)Size.X), Math.Max(1, (int)Size.Y));
        _image.Size = Size;
        if (_pipBoy) { LayoutPipBoy(); return; }
        var projection = FalloutRenderedMenuProjection.Read(_settings, Size.X / Size.Y, characterCreation: true);
        Camera.KeepAspect = Camera3D.KeepAspectEnum.Height;
        Camera.Fov = 2 * MathF.Atan(projection.VerticalSlope) * 180 / MathF.PI;
        Camera.Near = projection.Near; Camera.Far = projection.Far;
        // The native menu protocol uses two 1.57-radian axis rotations. Keep
        // this engine convention separate from the model's own source transform.
        var c = MathF.Cos(1.57f); var s = MathF.Sin(1.57f);
        var rotation = new[] { c * c, s, -c * s, -s * c, c, s * s, s, 0f, c };
        _model.Transform = new Transform3D(GamebryoCoordinate.ConvertBasis(rotation, projection.ModelScale, "rendered menu"),
            GamebryoCoordinate.ConvertVector(new(projection.Depth, projection.VerticalOffset, projection.HorizontalOffset)));
        BindAuthoredLights(_source, _model, Camera);
        SetMeta("opennv_device_projection", new Vector2(projection.HorizontalSlope, projection.VerticalSlope));
    }

    internal NativeOwnedDeviceSurface Surface { get; }
    internal MeshInstance3D Geometry(string sourceName) => Surface.Geometry(sourceName);
    internal Vector2? PickScreen(Vector2 position) => Surface.PickScreen(Camera.ProjectRayOrigin(position), Camera.ProjectRayNormal(position));

    private void LayoutPipBoy()
    {
        if (_player is not null)
        {
            var projection = FalloutCameraProjection.FromReferenceFov(_settings.Number("Display", "fPipboy1stPersonFOV"),
                _settings.Number("Display", "fNearDistance"));
            Camera.Projection = Camera3D.ProjectionType.Perspective; Camera.KeepAspect = Camera3D.KeepAspectEnum.Height;
            Camera.Fov = projection.VerticalFovDegrees; Camera.Near = projection.NearGameUnits * _player.Skeleton.UnitsToMetres;
            Camera.Far = 1000 * _player.Skeleton.UnitsToMetres;
            Camera.Transform = _player.SourceCamera;
            BindAuthoredLights(_source, _pickModel, Camera);
            SetMeta("opennv_device_projection", "owned-first-person-skeleton-camera-and-pipboy-FOV; matched-retail-unverified");
            return;
        }
        var screen = Geometry(ScreenName);
        var arrays = screen.Mesh.SurfaceGetArrays(0);
        var vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array().Select(screen.ToGlobal).ToArray();
        var normals = arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
        var uvs = arrays[(int)Mesh.ArrayType.TexUV].AsVector2Array();
        if (vertices.Length == 0 || normals.Length != vertices.Length || uvs.Length != vertices.Length)
            throw new InvalidDataException("Pip-Boy screen lacks a complete source frame.");
        var center = vertices.Aggregate(Vector3.Zero, (sum, point) => sum + point) / vertices.Length;
        var normal = normals.Select(value => (screen.GlobalBasis * value).Normalized())
            .Aggregate(Vector3.Zero, (sum, point) => sum + point).Normalized();
        var meanU = uvs.Average(uv => uv.X);
        var tangent = vertices.Select((point, index) => (point - center) * (uvs[index].X - meanU))
            .Aggregate(Vector3.Zero, (sum, point) => sum + point);
        var right = (tangent - normal * tangent.Dot(normal)).Normalized();
        var up = right.Cross(-normal).Normalized();
        var height = vertices.Max(point => point.Dot(up)) - vertices.Min(point => point.Dot(up));
        var width = vertices.Max(point => point.Dot(right)) - vertices.Min(point => point.Dot(right));
        Camera.Projection = Camera3D.ProjectionType.Orthogonal;
        Camera.KeepAspect = Camera3D.KeepAspectEnum.Height;
        Camera.Size = MathF.Max(height * 1.45f, width * 1.45f / (Size.X / Size.Y));
        Camera.Near = 0.01f; Camera.Far = Camera.Size * 12;
        Camera.GlobalPosition = center + normal * Camera.Size * 4;
        Camera.LookAt(center, up);
        BindAuthoredLights(_source, _model, Camera);
        SetMeta("opennv_device_projection", "source-screen-UV-frame; retail-arm-animation-and-projection-unbound");
        foreach (var mesh in _model.FindChildren("*", "", true, false).OfType<MeshInstance3D>().Where(mesh => mesh.Mesh is not null))
            for (var surface = 0; surface < mesh.Mesh.GetSurfaceCount(); surface++)
                if (mesh.GetActiveMaterial(surface) is ShaderMaterial { ResourceName: NativeNifEffectMaterial.ResourceIdentity } material)
                    material.SetShaderParameter("source_store_encoded", true);
    }

    internal void PosePipBoy(double seconds, float raised = 1, double? buttonSeconds = null)
    {
        _player?.PosePipBoy(seconds, raised, buttonSeconds); LayoutPipBoy();
    }

    internal void SelectSection(int section)
    {
        // Names are the rendered-menu model interface used by the engine.
        // Geometry, transforms and textures remain the original NIF data.
        string[] names = ["SexGlow:0", "RaceGlow:0", "FaceGlow:0", "BodyGlow:0"];
        if (section < 0 || section >= names.Length) throw new ArgumentOutOfRangeException(nameof(section));
        for (var index = 0; index < names.Length; index++) Geometry(names[index]).Visible = index == section;
    }

    internal static void BindAuthoredLights(FalloutNifFile source, Node3D model, Camera3D camera)
    {
        var nodes = model.FindChildren("*", "", true, false).OfType<Node3D>().ToArray();
        var cameraInverse = camera.GlobalTransform.AffineInverse();
        // Rendered-menu lights use three times the owning model's world bound.
        var radius = FalloutNifBounds.ReadStatic(source).Radius * model.GlobalBasis.Scale.X * 3;
        var lights = source.Blocks.Where(block => block.TypeName == "NiPointLight")
            .Select(block => (FalloutNifPointLight)source.ReadObject(block.Index))
            .Where(light => light.Light.SwitchState).Select(light =>
            {
                var node = nodes.Single(node => node.HasMeta("opennv_nif_block") &&
                    node.GetMeta("opennv_nif_block").AsInt32() == light.Block.Index);
                var rgb = light.Light.Diffuse;
                return new NativeNifPointLight(cameraInverse * node.GlobalPosition,
                    new Vector3(rgb.R, rgb.G, rgb.B) * light.Light.Dimmer,
                    radius);
            }).ToArray();
        model.SetMeta("opennv_menu_light_radius", radius);
        foreach (var mesh in nodes.OfType<MeshInstance3D>().Where(mesh => mesh.Mesh is not null))
            for (var surface = 0; surface < mesh.Mesh.GetSurfaceCount(); surface++)
                if (mesh.GetActiveMaterial(surface) is ShaderMaterial material &&
                    material.ResourceName == NativeNifLightingMaterial.ResourceIdentity)
                    NativeNifPointLighting.Bind(material, lights, 1, storeEncoded: true);
    }
}
