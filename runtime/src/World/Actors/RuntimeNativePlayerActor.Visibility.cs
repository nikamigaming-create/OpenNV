using Godot;

namespace OpenNV.Runtime.World.Actors;

internal sealed partial class RuntimeNativePlayerActor
{
    // Reserved only for this player's camera-obstructing geometry. Lights and
    // other cameras retain the complete actor; the XR eyes exclude this layer.
    internal const uint SelfHeadLayer = 1u << 19;
    private readonly List<(GeometryInstance3D Mesh, GeometryInstance3D.ShadowCastingSetting Source, uint Layers)> _shadowMeshes = [];
    private readonly HashSet<GeometryInstance3D> _selfExcludedMeshes = [];
    private bool _trackedBodyView;
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
            if (eyeVisible && _trackedBodyView && _selfExcludedMeshes.Contains(mesh)) mesh.Layers = SelfHeadLayer;
        }
    }

    internal void ShowTrackedBody(RuntimeNativePlayerActor firstPerson)
    {
        if (_first || !firstPerson._first) throw new InvalidOperationException("Tracked body requires the complete world outfit and authored first-person attachments.");
        for (var index = 0; index < Actor.Parts.Count; index++)
        {
            var part = Actor.Appearance.Models[index];
            // FNV BMDT: head/hair, headband through mask, and mouth object.
            // xEdit's published wbDefinitionsFNV BMDT contract supplies the bits.
            const uint headSlots = 0x00017e03;
            var head = (part.BipedSlots & headSlots) != 0 || part.Role is "head" or "ears" or "mouth" or
                "teeth-lower" or "teeth-upper" or "tongue" or "eye-left" or "eye-right" or "head-addon";
            if (head)
            {
                if ((part.BipedSlots & 4) != 0)
                    throw new NotSupportedException("Combined head/body equipment needs a source partition visibility binding.");
                foreach (var mesh in Actor.Parts[index].Root.FindChildren("*", "", true, false).OfType<GeometryInstance3D>())
                    _selfExcludedMeshes.Add(mesh);
            }
        }
        // The original first-person weapon/device retain their authored action
        // channels. The world weapon supplies the shadow, with no second eye draw.
        foreach (var mesh in _weaponNodes.OfType<GeometryInstance3D>()) _selfExcludedMeshes.Add(mesh);
        foreach (var part in firstPerson.Actor.Parts)
            if (firstPerson.WristDevice() is not { } device || part.Root != device.Root)
                foreach (var mesh in part.Root.FindChildren("*", "", true, false).OfType<GeometryInstance3D>())
                { mesh.Layers = 0; mesh.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off; }
        _trackedBodyView = true; _eyeVisible = null;
        _xrLeftLeg = new(Skeleton, true); _xrRightLeg = new(Skeleton, false);
        _xrSpine = new(Skeleton);
        SetViewPolicy(true, true);
        UseTrackedWristShadow(firstPerson);
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
        trackedFullBody = _trackedBodyView,
        selfExcludedSurfaces = _selfExcludedMeshes.Count,
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
