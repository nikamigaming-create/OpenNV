using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.Presentation.Ui;

internal sealed record NativeOwnedNifMenuModel(string Path, FalloutNifFile Source, Transform3D Transform);
internal sealed record NativeOwnedNifMenuView(Transform3D Camera, Func<float, float> HorizontalSlope,
    float Near, float Far, float LightRadiusMultiple, Vector3 FallbackLightColor);
internal sealed record NativeOwnedNifMenuTarget(string Geometry, Vector2 Center, Rect2 Bounds, bool InFront);

// Shared rendered chargen surface: source NIFs, controller managers, material
// slots, authored lights, and triangle picking. Menu state stays with its owner.
internal sealed partial class NativeOwnedNifMenuSurface : Control
{
    private readonly NativeOwnedNifMenuView _definition;
    private readonly List<(NativeOwnedNifMenuModel Declaration, Node3D Pose, Node3D Model)> _models = [];
    private readonly Dictionary<string, MeshInstance3D> _geometry = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Texture2D> _textures = new(StringComparer.OrdinalIgnoreCase);
    private readonly TextureRect _pixels;
    internal SubViewport View { get; }
    internal Camera3D Camera { get; }
    internal IReadOnlyCollection<string> GeometryNames => _geometry.Keys;
    internal string? LastPicked { get; private set; }

    internal NativeOwnedNifMenuSurface(IReadOnlyList<NativeOwnedNifMenuModel> models, NativeOwnedNifMenuView definition)
    {
        try
        {
            if (models.Count == 0) throw new InvalidDataException("A rendered source menu has no model.");
            if (!float.IsFinite(definition.Near) || definition.Near <= 0 || !float.IsFinite(definition.Far) || definition.Far <= definition.Near ||
                !float.IsFinite(definition.LightRadiusMultiple) || definition.LightRadiusMultiple <= 0)
                throw new InvalidDataException("Rendered source menu view is invalid.");
            Name = "OwnedNifMenuSurface"; ProcessMode = ProcessModeEnum.Always; MouseFilter = MouseFilterEnum.Ignore;
            _definition = definition;
            View = new() { Name = "OwnedMenuView", OwnWorld3D = true, TransparentBg = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
            AddChild(View);
            foreach (var declaration in models)
            {
                var pose = new Node3D { Name = "SourceMenuPose", Transform = declaration.Transform };
                View.AddChild(pose);
                var model = RuntimeNativeNifMeshBuilder.Build(declaration.Source, 1).Root;
                pose.AddChild(model); _models.Add((declaration, pose, model));
                foreach (var mesh in model.FindChildren("*", "", true, false).OfType<MeshInstance3D>().Where(mesh => mesh.HasMeta("opennv_nif_source_name")))
                {
                    var sourceName = mesh.GetMeta("opennv_nif_source_name").AsString();
                    if (!_geometry.TryAdd(sourceName, mesh)) throw new NotSupportedException($"Rendered menu geometry is ambiguous: {sourceName}.");
                    // Dynamic menu textures are geometry-local, including NIF
                    // geometries that originally shared one material property.
                    for (var surface = 0; surface < mesh.Mesh.GetSurfaceCount(); surface++)
                        if (mesh.GetActiveMaterial(surface) is { } material)
                            mesh.SetSurfaceOverrideMaterial(surface, (Material)material.Duplicate());
                }
            }
            Camera = new() { Name = "SourceMenuCamera", Current = true, Transform = definition.Camera };
            View.AddChild(Camera);
            _pixels = new()
            {
                Name = "OwnedMenuPixels",
                Texture = View.GetTexture(),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.Scale,
                MouseFilter = MouseFilterEnum.Ignore
            };
            AddChild(_pixels);
        }
        catch { Free(); throw; }
    }

    internal RuntimeNifControllerPlayer Animation => _models.SelectMany(model => model.Model.FindChildren("*", "", true, false))
        .OfType<RuntimeNifControllerPlayer>().Single();

    internal void SetExtent(Vector2 extent)
    {
        if (!IsInsideTree() || extent.X <= 0 || extent.Y <= 0) return;
        Size = extent; View.Size = new(Math.Max(1, (int)extent.X), Math.Max(1, (int)extent.Y)); _pixels.Size = extent;
        var horizontal = _definition.HorizontalSlope(extent.X / extent.Y);
        if (!float.IsFinite(horizontal) || horizontal <= 0) throw new InvalidDataException("Rendered source menu slope is invalid.");
        Camera.KeepAspect = Camera3D.KeepAspectEnum.Height;
        Camera.Fov = MathF.Atan(horizontal * extent.Y / extent.X) * 360 / MathF.PI;
        Camera.Near = _definition.Near; Camera.Far = _definition.Far;
        var inverse = Camera.GlobalTransform.AffineInverse();
        foreach (var (declaration, pose, model) in _models)
        {
            var radius = FalloutNifBounds.ReadStatic(declaration.Source).Radius * Math.Abs(pose.GlobalBasis.Scale.X) * _definition.LightRadiusMultiple;
            var sourceLights = declaration.Source.Blocks.Where(block => block.TypeName == "NiPointLight")
                .Select(block => (FalloutNifPointLight)declaration.Source.ReadObject(block.Index)).ToArray();
            var nodes = model.FindChildren("*", "", true, false).OfType<Node3D>().ToArray();
            var lights = sourceLights.Where(light => light.Light.SwitchState).Select(light =>
            {
                var node = nodes.Single(node => node.GetMeta("opennv_nif_block", -1).AsInt32() == light.Block.Index);
                var rgb = light.Light.Diffuse;
                return new NativeNifPointLight(inverse * node.GlobalPosition, new Vector3(rgb.R, rgb.G, rgb.B) * light.Light.Dimmer, radius);
            }).ToArray();
            // The engine's rendered-menu owner adds a point light only when
            // its source tree has no light. Existing colors/dimmers survive.
            if (sourceLights.Length == 0)
                lights = [new(inverse * GamebryoCoordinate.ConvertVector(new(radius, 0, 0)), _definition.FallbackLightColor, radius)];
            foreach (var mesh in nodes.OfType<MeshInstance3D>())
                for (var surface = 0; surface < mesh.Mesh.GetSurfaceCount(); surface++)
                    if (mesh.GetActiveMaterial(surface) is ShaderMaterial material && material.ResourceName == NativeNifLightingMaterial.ResourceIdentity)
                        NativeNifPointLighting.Bind(material, lights, 1, storeEncoded: true);
            model.SetMeta("opennv_menu_light_radius", radius); model.SetMeta("opennv_menu_authored_light_count", sourceLights.Length);
        }
        SetMeta("opennv_menu_projection", new Vector2(horizontal, horizontal * extent.Y / extent.X));
    }

    internal MeshInstance3D Geometry(string name) => _geometry.TryGetValue(name, out var mesh) ? mesh :
        throw new InvalidDataException($"Owned menu geometry is absent: {name}.");
    internal void PreloadTextures(IEnumerable<string> paths)
    {
        foreach (var path in paths)
            if (!_textures.ContainsKey(path)) _textures.Add(path, NativeOwnedMediaLoader.LoadTexture(path));
    }
    internal void SetTexture(string name, string path)
    {
        var mesh = Geometry(name);
        if (!_textures.TryGetValue(path, out var texture)) _textures.Add(path, texture = NativeOwnedMediaLoader.LoadTexture(path));
        if (mesh.GetActiveMaterial(0) is not ShaderMaterial material || material.ResourceName != NativeNifLightingMaterial.ResourceIdentity)
            throw new NotSupportedException($"Owned menu dynamic material is unbound: {name}.");
        material.SetShaderParameter("base_map", texture); material.SetShaderParameter("use_base_map", true);
        mesh.SetMeta("opennv_menu_texture", path);
    }

    internal IReadOnlyList<NativeOwnedNifMenuTarget> Targets(Func<string, MeshInstance3D, bool> active) =>
        _geometry.Where(item => active(item.Key, item.Value)).Select(item =>
        {
            var mesh = item.Value; var bounds = mesh.Mesh.GetAabb();
            var points = Enumerable.Range(0, 8).Select(index => Camera.UnprojectPosition(mesh.GlobalTransform * bounds.GetEndpoint(index))).ToArray();
            var minimum = points.Aggregate((a, b) => new Vector2(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y)));
            var maximum = points.Aggregate((a, b) => new Vector2(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y)));
            var center = mesh.GlobalTransform * bounds.GetCenter();
            return new NativeOwnedNifMenuTarget(item.Key, Camera.UnprojectPosition(center), new(minimum, maximum - minimum), !Camera.IsPositionBehind(center));
        }).ToArray();

    internal string? Pick(Vector2 position, Func<string, MeshInstance3D, bool> active)
    {
        const float tolerance = 1e-5f;
        var origin = Camera.ProjectRayOrigin(position); var direction = Camera.ProjectRayNormal(position);
        var nearest = float.PositiveInfinity; string? result = null;
        foreach (var (name, mesh) in _geometry.Where(item => active(item.Key, item.Value)))
        {
            var inverse = mesh.GlobalTransform.AffineInverse(); var o = inverse * origin; var d = inverse.Basis * direction;
            var arrays = mesh.Mesh.SurfaceGetArrays(0); var vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var indices = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
            for (var index = 0; index < indices.Length; index += 3)
            {
                var a = vertices[indices[index]]; var first = vertices[indices[index + 1]] - a; var second = vertices[indices[index + 2]] - a;
                var cross = d.Cross(second); var determinant = first.Dot(cross);
                if (MathF.Abs(determinant) < 1e-8f) continue;
                var offset = o - a; var u = offset.Dot(cross) / determinant;
                if (u < -tolerance || u > 1 + tolerance) continue;
                var q = offset.Cross(first); var v = d.Dot(q) / determinant;
                if (v < -tolerance || u + v > 1 + tolerance) continue;
                var hit = second.Dot(q) / determinant;
                if (hit > 0 && hit < nearest) { nearest = hit; result = name; }
            }
        }
        LastPicked = result; return result;
    }
}
