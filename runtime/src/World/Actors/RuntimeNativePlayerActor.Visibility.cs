using Godot;

namespace OpenNV.Runtime.World.Actors;

internal sealed partial class RuntimeNativePlayerActor
{
    private readonly List<(GeometryInstance3D Mesh, GeometryInstance3D.ShadowCastingSetting Source, uint Layers)> _shadowMeshes = [];
    private bool? _eyeVisible;
    private bool _castsWorldShadow;
    internal IReadOnlyList<Area3D> BodyContacts { get; private set; } = [];

    // Eye visibility changes neither authored visibility (caps, equipment,
    // animation channels) nor the complete third-person contact skeleton.
    internal void SetViewPolicy(bool eyeVisible, bool castsWorldShadow)
    {
        if (_eyeVisible == eyeVisible && _castsWorldShadow == castsWorldShadow) return;
        if (_shadowMeshes.Count == 0)
            _shadowMeshes.AddRange(FindChildren("*", "", true, false).OfType<GeometryInstance3D>()
                .Select(mesh => (mesh, mesh.CastShadow, mesh.Layers)));
        _eyeVisible = eyeVisible; _castsWorldShadow = castsWorldShadow;
        foreach (var (mesh, source, layers) in _shadowMeshes)
        {
            mesh.CastShadow = !castsWorldShadow ? GeometryInstance3D.ShadowCastingSetting.Off :
                !eyeVisible && source != GeometryInstance3D.ShadowCastingSetting.Off
                    ? GeometryInstance3D.ShadowCastingSetting.ShadowsOnly : source;
            // ShadowsOnly has no equivalent when shadow casting is disabled.
            // Mask that eye pass without changing authored animation visibility.
            mesh.Layers = !eyeVisible && (!castsWorldShadow || source == GeometryInstance3D.ShadowCastingSetting.Off) ? 0 : layers;
        }
    }

    internal void ConfigureBodyContacts(uint collisionLayer)
    {
        if (_first || BodyContacts.Count != 0) throw new InvalidOperationException("Player hit volumes require one complete world skeleton.");
        BodyContacts = RuntimeNativeActorContacts.Configure(this, Skeleton, collisionLayer);
    }

    internal void UseTrackedWristShadow(RuntimeNativePlayerActor firstPerson)
    {
        // The live device's cuff can enlarge independently of anatomy. Its
        // actual geometry casts that shadow; the world outfit keeps its sleeve.
        if (WristDevice() is { } worldDevice) worldDevice.Root.Visible = false;
        if (firstPerson.WristDevice() is not { } trackedDevice) return;
        foreach (var (mesh, source, _) in firstPerson._shadowMeshes)
            if (trackedDevice.Root.IsAncestorOf(mesh)) mesh.CastShadow = source;
    }

    internal object BodyVisibilityState => new
    {
        eyeVisible = _eyeVisible,
        worldShadows = _castsWorldShadow,
        shadowSurfaces = _shadowMeshes.Count(value => value.Mesh.IsVisibleInTree() && value.Mesh.Layers != 0 &&
            value.Mesh.CastShadow is GeometryInstance3D.ShadowCastingSetting.On or GeometryInstance3D.ShadowCastingSetting.DoubleSided or GeometryInstance3D.ShadowCastingSetting.ShadowsOnly),
        contacts = BodyContacts.Select(area => new
        {
            bone = area.GetMeta("opennv_nif_collision_bone").AsString(),
            layer = area.CollisionLayer,
            shapes = area.GetChildren().OfType<CollisionShape3D>().Count(shape => !shape.Disabled),
            position = new[] { area.GlobalPosition.X, area.GlobalPosition.Y, area.GlobalPosition.Z }
        }).ToArray()
    };
}
