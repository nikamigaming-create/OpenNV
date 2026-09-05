using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.Presentation.Ui;

/// <summary>Direct NIF rendered-menu device and its independent projection.</summary>
internal sealed partial class NativeOwnedRenderedDevice : Control
{
    private readonly FalloutInstallationSettings _settings;
    private readonly FalloutNifFile _source;
    private readonly Node3D _model;
    private readonly TextureRect _image;
    private sealed record PickSurface(MeshInstance3D Mesh, Vector3[] Vertices, int[] Indices, Vector2[] Uvs,
        BaseMaterial3D.CullModeEnum Cull, bool Occludes);
    private PickSurface[]? _pickSurfaces;
    internal SubViewport View { get; }
    internal Camera3D Camera { get; }
    internal Node3D Model => _model;
    internal FalloutNifFile Source => _source;

    internal NativeOwnedRenderedDevice(string modelPath, FalloutInstallationSettings settings)
    {
        Name = "OwnedRenderedDevice";
        ProcessMode = ProcessModeEnum.Always;
        _settings = settings;
        var content = RuntimeLiveContentSource.Current ?? throw new InvalidOperationException("Owned device content is absent.");
        if (!content.TryRead(modelPath, null, out var bytes, out var identity)) throw new FileNotFoundException(modelPath);
        _source = FalloutNifFile.Read(bytes);
        var model = RuntimeNativeNifMeshBuilder.Build(_source, 1);
        model.Root.SetMeta("opennv_source_model", modelPath);
        View = new SubViewport
        {
            Name = "DeviceView",
            OwnWorld3D = true,
            TransparentBg = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
        };
        AddChild(View);
        View.AddChild(model.Root);
        _model = model.Root;
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
        SetMeta("opennv_device_surfaces", model.Surfaces);
        SetMeta("opennv_device_unbound", "render-target-composition,screen-effects,shadow-and-specular-selection");
        Resized += Layout;
    }

    public override void _Ready() => Layout();

    private void Layout()
    {
        if (!IsInsideTree() || Size.X <= 0 || Size.Y <= 0) return;
        View.Size = new Vector2I(Math.Max(1, (int)Size.X), Math.Max(1, (int)Size.Y));
        _image.Size = Size;
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

    internal MeshInstance3D Geometry(string sourceName) => _model.FindChildren("*", "", true, false)
        .OfType<MeshInstance3D>().Single(mesh => mesh.HasMeta("opennv_nif_source_name") &&
            mesh.GetMeta("opennv_nif_source_name").AsString() == sourceName);

    internal Vector2? PickScreen(Vector2 position)
    {
        var origin = Camera.ProjectRayOrigin(position); var direction = Camera.ProjectRayNormal(position);
        var distance = float.PositiveInfinity; Vector2? result = null;
        _pickSurfaces ??= _model.FindChildren("*", "", true, false).OfType<MeshInstance3D>()
            .Where(mesh => mesh.Mesh is not null).SelectMany(mesh => Enumerable.Range(0, mesh.Mesh.GetSurfaceCount()).Select(index =>
            {
                var arrays = mesh.Mesh.SurfaceGetArrays(index);
                var material = mesh.GetActiveMaterial(index);
                var geometry = _source.ReadGeometry(mesh.GetMeta("opennv_nif_geometry_block").AsInt32());
                var properties = geometry.Properties.Where(block => block >= 0).Select(_source.ReadObject).ToArray();
                var alpha = properties.OfType<FalloutNifAlphaProperty>().SingleOrDefault();
                var depthWrites = properties.Select(property => property switch
                {
                    FalloutNifShaderProperty shader => (bool?)((shader.ShaderFlags2 & 1) != 0),
                    FalloutNifNoLightingProperty shader => (shader.ShaderFlags2 & 1) != 0,
                    _ => null,
                }).Where(value => value.HasValue).SingleOrDefault() ?? true;
                // Blended overlays which do not write depth decorate the screen;
                // they must not turn its visible controls into an opaque hit wall.
                var occludes = depthWrites || alpha is null ||
                    FalloutNifAlphaState.Read(alpha.Flags, alpha.Threshold).Blend == FalloutNifBlendMode.Opaque;
                var cull = material switch
                {
                    BaseMaterial3D standard => standard.CullMode,
                    ShaderMaterial shader when shader.Shader.Code.Contains("cull_disabled", StringComparison.Ordinal) => BaseMaterial3D.CullModeEnum.Disabled,
                    ShaderMaterial shader when shader.Shader.Code.Contains("cull_front", StringComparison.Ordinal) => BaseMaterial3D.CullModeEnum.Front,
                    ShaderMaterial shader when shader.Shader.Code.Contains("cull_back", StringComparison.Ordinal) => BaseMaterial3D.CullModeEnum.Back,
                    _ => throw new NotSupportedException("Rendered-menu surface has no input culling contract."),
                };
                return new PickSurface(mesh, arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array(),
                    arrays[(int)Mesh.ArrayType.Index].AsInt32Array(), arrays[(int)Mesh.ArrayType.TexUV].AsVector2Array(), cull, occludes);
            })).ToArray();
        var screen = Geometry("Screen:0");
        foreach (var surface in _pickSurfaces.Where(surface => surface.Mesh.IsVisibleInTree() && (surface.Mesh == screen || surface.Occludes)))
        {
            var mesh = surface.Mesh;
            var inverse = mesh.GlobalTransform.AffineInverse();
            var localOrigin = inverse * origin; var localDirection = inverse.Basis * direction;
            var vertices = surface.Vertices; var indices = surface.Indices; var uvs = surface.Uvs;
            for (var triangle = 0; triangle < indices.Length; triangle += 3)
            {
                var a = indices[triangle]; var b = indices[triangle + 1]; var c = indices[triangle + 2];
                var first = vertices[b] - vertices[a]; var second = vertices[c] - vertices[a];
                var cross = localDirection.Cross(second); var determinant = first.Dot(cross);
                if (MathF.Abs(determinant) < 1e-8f) continue;
                // Godot's front-face triangle winding is clockwise.
                if (surface.Cull == BaseMaterial3D.CullModeEnum.Back && determinant > 0 ||
                    surface.Cull == BaseMaterial3D.CullModeEnum.Front && determinant < 0) continue;
                var offset = localOrigin - vertices[a]; var u = offset.Dot(cross) / determinant;
                if (u is < 0 or > 1) continue;
                var q = offset.Cross(first); var v = localDirection.Dot(q) / determinant;
                if (v < 0 || u + v > 1) continue;
                var hit = second.Dot(q) / determinant;
                if (hit < 0 || hit >= distance) continue;
                distance = hit;
                SetMeta("opennv_pointer_surface", mesh.Name.ToString());
                result = mesh == screen && uvs.Length == vertices.Length
                    ? uvs[a] * (1 - u - v) + uvs[b] * u + uvs[c] * v : null;
            }
        }
        return result;
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
