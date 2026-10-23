using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.Presentation.Ui;

internal sealed record NativeOwnedNifMenuModel(string Path, FalloutNifFile Source, Transform3D Transform);
internal sealed record NativeOwnedNifMenuView(Transform3D Camera, Func<float, float> HorizontalSlope,
    float Near, float Far, float LightRadiusMultiple, Vector3 FallbackLightColor,
    bool ModelRelativeFallbackLight = false);
internal sealed record NativeOwnedNifMenuTarget(string Geometry, Vector2 Center, Rect2 Bounds, bool InFront);
internal sealed record NativeOwnedNifMenuModelHandle(Node3D Pose, Node3D Root)
{
    internal RuntimeNifControllerPlayer Animation => Root.FindChildren("*", "", true, false)
        .OfType<RuntimeNifControllerPlayer>().Single();
}

// Shared rendered chargen surface: source NIFs, controller managers, material
// slots, authored lights, and triangle picking. Menu state stays with its owner.
internal sealed partial class NativeOwnedNifMenuSurface : Control
{
    private readonly NativeOwnedNifMenuView _definition;
    private readonly List<(NativeOwnedNifMenuModel Declaration, Node3D Pose, Node3D Model, FalloutNifSphereBound Bound)> _models = [];
    private readonly Dictionary<string, MeshInstance3D> _geometry = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Texture2D> _textures = new(StringComparer.OrdinalIgnoreCase);
    private readonly TextureRect _pixels;
    internal SubViewport View { get; }
    internal Camera3D Camera { get; }
    internal Node3D ModelsRoot { get; }
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
            ModelsRoot = new() { Name = "SourceMenuModels" };
            View.AddChild(ModelsRoot);
            var identities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var declaration in models)
            {
                if (!identities.Add(declaration.Path.Replace('\\', '/')))
                    throw new InvalidDataException($"Rendered menu model identity is ambiguous: {declaration.Path}.");
                var pose = new Node3D { Name = "SourceMenuPose", Transform = declaration.Transform };
                ModelsRoot.AddChild(pose);
                var model = RuntimeNativeNifMeshBuilder.Build(declaration.Source, 1).Root;
                pose.AddChild(model); _models.Add((declaration, pose, model, FalloutNifBounds.ReadStatic(declaration.Source)));
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

    internal NativeOwnedNifMenuModelHandle Model(string path)
    {
        var model = _models.Single(model => model.Declaration.Path.Replace('\\', '/')
            .Equals(path.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase));
        return new(model.Pose, model.Model);
    }

    internal void SetExtent(Vector2 extent)
    {
        if (!IsInsideTree() || extent.X <= 0 || extent.Y <= 0) return;
        Size = extent; View.Size = new(Math.Max(1, (int)extent.X), Math.Max(1, (int)extent.Y)); _pixels.Size = extent;
        var horizontal = _definition.HorizontalSlope(extent.X / extent.Y);
        if (!float.IsFinite(horizontal) || horizontal <= 0) throw new InvalidDataException("Rendered source menu slope is invalid.");
        Camera.KeepAspect = Camera3D.KeepAspectEnum.Height;
        Camera.Fov = MathF.Atan(horizontal * extent.Y / extent.X) * 360 / MathF.PI;
        Camera.Near = _definition.Near; Camera.Far = _definition.Far;
        RefreshLighting();
        SetMeta("opennv_menu_projection", new Vector2(horizontal, horizontal * extent.Y / extent.X));
    }

    internal void RefreshLighting()
    {
        if (!IsInsideTree()) return;
        var inverse = Camera.GlobalTransform.AffineInverse();
        foreach (var (declaration, pose, model, bound) in _models)
        {
            var radius = bound.Radius * Math.Abs(pose.GlobalBasis.Scale.X) * _definition.LightRadiusMultiple;
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
            {
                var position = GamebryoCoordinate.ConvertVector(new(radius, 0, 0));
                if (_definition.ModelRelativeFallbackLight)
                {
                    // Opt-in first-party illumination for transformed models.
                    // Keep the source material and attenuation, but anchor the
                    // fallback to actual posed geometry, not the static NIF
                    // bound: managed channels can move meshes away from it.
                    var current = nodes.OfType<MeshInstance3D>().Where(mesh => mesh.Mesh is not null)
                        .Select(mesh => mesh.GlobalTransform * mesh.GetAabb()).Aggregate((left, right) => left.Merge(right));
                    radius = current.Size.Length() * .5f * _definition.LightRadiusMultiple;
                    position = current.GetCenter() + Camera.GlobalBasis.Z * (radius * .5f);
                }
                lights = [new(inverse * position, _definition.FallbackLightColor, radius)];
            }
            foreach (var mesh in nodes.OfType<MeshInstance3D>())
                for (var surface = 0; surface < mesh.Mesh.GetSurfaceCount(); surface++)
                    if (mesh.GetActiveMaterial(surface) is ShaderMaterial material && material.ResourceName == NativeNifLightingMaterial.ResourceIdentity)
                        NativeNifPointLighting.Bind(material, lights, 1, storeEncoded: true);
            model.SetMeta("opennv_menu_light_radius", radius); model.SetMeta("opennv_menu_authored_light_count", sourceLights.Length);
        }
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
        ProjectedTargets(active);

    internal string? Pick(Vector2 position, Func<string, MeshInstance3D, bool> active) =>
        PickProjected(position, active);
}
