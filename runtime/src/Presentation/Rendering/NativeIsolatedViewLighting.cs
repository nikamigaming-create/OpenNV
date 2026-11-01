using Godot;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.World.Actors;

namespace OpenNV.Runtime.Presentation.Rendering;

/// <summary>Publishes the live world's lighting inputs into an isolated first-person view.</summary>
internal sealed partial class NativeIsolatedViewLighting : Node
{
    private Camera3D _worldCamera = null!, _viewCamera = null!;
    private Func<Node3D?> _model = null!;
    private Func<IReadOnlyList<NativeNifPointLight>>? _additional;
    private float _worldUnits, _viewUnits;
    private readonly HashSet<Light3D> _lights = [];
    private SceneTree? _tree;
    private Node3D? _boundModel;
    private MeshInstance3D[] _meshes = [];
    private bool _meshDirty = true;
    private string? _error;
    private int _points, _directional, _maximumPerMaterial, _localLights, _unboundedSkins;
    internal object State => new
    {
        points = _points,
        directional = _directional,
        maximumPointsPerMaterial = _maximumPerMaterial,
        targetLocalLights = _localLights,
        skinsWithoutSourceBounds = _unboundedSkins,
        worldUnitsToMetres = _worldUnits,
        viewUnitsToMetres = _viewUnits,
        error = _error,
        owner = "live-world-light-state;world-camera-view-transform;source-radius-overlap",
        unbound = "retail-light-selection-and-shadow-visibility;specular;first-person-fog;matched-pixels"
    };

    internal void Configure(Camera3D worldCamera, Camera3D viewCamera, Func<Node3D?> model,
        float worldUnits, float viewUnits, Func<IReadOnlyList<NativeNifPointLight>>? additional = null)
    {
        if (!float.IsFinite(worldUnits) || worldUnits <= 0 || !float.IsFinite(viewUnits) || viewUnits <= 0)
            throw new InvalidDataException("Isolated-view lighting requires positive source/view unit scales.");
        _worldCamera = worldCamera; _viewCamera = viewCamera; _model = model;
        _worldUnits = worldUnits; _viewUnits = viewUnits; _additional = additional;
        Name = "SourceWorldLightingBridge"; ProcessMode = ProcessModeEnum.Always;
    }

    public override void _Ready()
    {
        _tree = GetTree();
        foreach (var light in _tree.Root.FindChildren("*", "Light3D", true, false).OfType<Light3D>()) _lights.Add(light);
        _tree.NodeAdded += Added; _tree.NodeRemoved += Removed;
    }

    private void Added(Node node)
    {
        if (node is Light3D light) _lights.Add(light);
        if (node is MeshInstance3D && _boundModel?.IsAncestorOf(node) == true) _meshDirty = true;
    }
    private void Removed(Node node)
    {
        if (node is Light3D light) _lights.Remove(light);
        if (node is MeshInstance3D) _meshDirty = true;
    }
    public override void _ExitTree()
    {
        if (_tree is not null) { _tree.NodeAdded -= Added; _tree.NodeRemoved -= Removed; }
        _tree = null; _lights.Clear(); _meshes = [];
    }

    public override void _Process(double delta)
    {
        try { Publish(); _error = null; RemoveMeta("opennv_isolated_lighting_error"); }
        catch (Exception error)
        {
            if (_error != error.Message) GD.PushError("OPENNV_ISOLATED_LIGHTING_UNBOUND " + error.Message);
            _error = error.Message; SetMeta("opennv_isolated_lighting_error", error.Message);
        }
    }

    private void Publish()
    {
        var model = _model();
        if (model is null || !IsInstanceValid(model) || !model.IsInsideTree()) return;
        if (_boundModel != model || _meshDirty)
        {
            _boundModel = model; _meshDirty = false;
            _meshes = model.FindChildren("*", "", true, false).OfType<MeshInstance3D>()
                .Where(mesh => mesh.Mesh is not null).ToArray();
        }
        var sourceWorld = _worldCamera.GetWorld3D();
        var targetWorld = _viewCamera.GetWorld3D();
        var environment = sourceWorld.Environment ?? throw new InvalidOperationException("World lighting has no current environment owner.");
        var ambient = environment.AmbientLightColor;
        var rgb = new Vector3(ambient.R, ambient.G, ambient.B);
        var worldToView = _worldCamera.GetCameraTransform().Orthonormalized().AffineInverse();
        var targetToView = _viewCamera.GetCameraTransform().Orthonormalized().AffineInverse();
        var ratio = _viewUnits / _worldUnits;
        var points = new List<(NativeNifPointLight Light, float ViewRadius, uint Mask)>();
        var directional = new List<(NativeNifDirectionalLight Light, uint Mask)>();
        _localLights = 0;
        foreach (var light in _lights)
        {
            if (!IsInstanceValid(light) || !light.IsInsideTree() || light.IsQueuedForDeletion() ||
                !light.IsVisibleInTree() || light.LightEnergy <= 0) continue;
            var lightWorld = light.GetWorld3D();
            var local = lightWorld == targetWorld && model.IsAncestorOf(light);
            if (!local && lightWorld != sourceWorld) continue;
            // Real presentation-local effects (notably the first-person muzzle)
            // have no emitter in the main world. They already use target units.
            var toView = local ? targetToView : worldToView;
            var units = local ? _viewUnits : _worldUnits;
            var scale = local ? 1 : ratio;
            if (local) _localLights++;
            // LightColor is an sRGB Godot property. Recover the encoded source
            // numeric constant that RetailLighting.GodotLightColor installed;
            // passing the property directly would apply that transfer twice.
            var color = light.LightColor.SrgbToLinear();
            var diffuse = new Vector3(color.R, color.G, color.B) * light.LightEnergy;
            if (light is OmniLight3D point)
                points.Add((new(toView * point.GlobalPosition * scale, diffuse, point.OmniRange / units),
                    point.OmniRange * scale, light.LightCullMask));
            else if (light is DirectionalLight3D)
                directional.Add((new((toView.Basis * light.GlobalBasis.Z).Normalized(), diffuse), light.LightCullMask));
            else throw new NotSupportedException($"Source light type {light.GetClass()} has no isolated-view binding.");
        }
        if (_additional is not null)
            foreach (var light in _additional()) points.Add((light, light.Radius * _viewUnits, uint.MaxValue));
        _points = points.Count; _directional = directional.Count; _maximumPerMaterial = 0; _unboundedSkins = 0;
        var materials = new Dictionary<ShaderMaterial, List<(Aabb? Bounds, uint Layers)>>();
        foreach (var mesh in _meshes)
        {
            if (!IsInstanceValid(mesh) || !mesh.IsInsideTree() || !mesh.IsVisibleInTree()) continue;
            NativeNifMaterialEnvironment.Bind(mesh, rgb, Vector3.Zero, new(100000, 200000, 1), _viewUnits);
            Aabb? bounds;
            if (mesh.Skin is null) bounds = targetToView * mesh.GlobalTransform * mesh.GetAabb();
            else if (model is RuntimeNativePlayerActor actor && actor.CurrentLightingBound(mesh) is { } posed)
            {
                var center = targetToView * new Vector3(posed.Center.X, posed.Center.Y, posed.Center.Z);
                var radius = Vector3.One * posed.Radius;
                bounds = new Aabb(center - radius, radius * 2);
            }
            else
            {
                // ArrayMesh AABBs remain in bind pose. For attachments without
                // a source posed-bound owner, retain lights conservatively;
                // the shader still applies each light's actual radius.
                bounds = null; _unboundedSkins++;
            }
            for (var index = 0; index < mesh.Mesh.GetSurfaceCount(); index++)
                if (mesh.GetActiveMaterial(index) is ShaderMaterial material && material.ResourceName is
                    NativeNifLightingMaterial.ResourceIdentity or NativeFaceGenMaterial.ResourceIdentity)
                {
                    if (!materials.TryGetValue(material, out var entries)) materials.Add(material, entries = []);
                    entries.Add((bounds, mesh.Layers));
                }
        }
        foreach (var (material, bounds) in materials)
        {
            var admitted = points.Where(point => bounds.Any(bound => (point.Mask & bound.Layers) != 0 &&
                (bound.Bounds is not { } box || Intersects(box, point.Light.ViewPosition, point.ViewRadius))))
                .Select(point => point.Light).ToArray();
            var sun = directional.Where(light => bounds.Any(bound => (light.Mask & bound.Layers) != 0))
                .Select(light => light.Light).ToArray();
            _maximumPerMaterial = Math.Max(_maximumPerMaterial, admitted.Length);
            if (material.ResourceName == NativeFaceGenMaterial.ResourceIdentity) material.SetShaderParameter("source_ambient_rgb", rgb);
            NativeNifPointLighting.Bind(material, admitted, _viewUnits, storeEncoded: true, directional: sun, isolatedWorld: true);
        }
    }

    private static bool Intersects(Aabb bounds, Vector3 point, float radius)
    {
        var end = bounds.End;
        var closest = new Vector3(Math.Clamp(point.X, bounds.Position.X, end.X),
            Math.Clamp(point.Y, bounds.Position.Y, end.Y), Math.Clamp(point.Z, bounds.Position.Z, end.Z));
        return point.DistanceSquaredTo(closest) <= radius * radius;
    }
}
