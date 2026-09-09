using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Presentation.Rendering;

namespace OpenNV.Runtime.World.Actors;

internal sealed partial class RuntimeNativePlayerActor
{
    private Node3D? _projectileNode, _muzzleFlash;
    private string? _muzzlePath;
    private NativeNifEffectPlayback? _muzzlePlayback;
    private OmniLight3D? _muzzleLight;
    private FalloutFormKey? _muzzleLightForm;
    internal object MuzzleState => new
    {
        path = _muzzlePath,
        visible = _muzzleFlash?.Visible ?? false,
        seconds = _muzzlePlayback?.Remaining,
        particles = _muzzlePlayback?.Particles.Select(value => new { value.ActiveCount, value.BirthCount, value.EmissionEnabled }).ToArray(),
        light = _muzzleLight is null ? null : new { form = _muzzleLightForm?.ToString(), _muzzleLight.Visible, _muzzleLight.OmniRange },
        unbound = "addon-audio,shared-master-emitter-pooling,muzzle-light-shadow-selection,flat-world-muzzle-light,retail-billboard-motion-match"
    };

    private Node3D ProjectileNode() => _projectileNode ??= _weaponNodes.SingleOrDefault(node =>
        node.GetMeta("opennv_nif_source_name", "").AsString().Equals("ProjectileNode", StringComparison.OrdinalIgnoreCase)) ??
        throw new NotSupportedException("Held source weapon has no unique ProjectileNode.");

    internal Transform3D ProjectileTransform() => Skeleton.Node.GlobalTransform * ProjectileTransformInSkeleton();

    private Transform3D ProjectileTransformInSkeleton() => WeaponNodeInSkeleton(ProjectileNode());

    internal Transform3D ShellTransform() => Skeleton.Node.GlobalTransform * WeaponNodeInSkeleton(
        _weaponNodes.SingleOrDefault(node => node.GetMeta("opennv_nif_source_name", "").AsString()
            .Equals("ShellCasingNode", StringComparison.OrdinalIgnoreCase)) ??
        throw new NotSupportedException("Held source weapon has no unique ShellCasingNode."));

    private Transform3D WeaponNodeInSkeleton(Node3D source)
    {
        var local = Transform3D.Identity;
        for (Node3D? node = source; node != _weaponAttachment; node = node.GetParent() as Node3D)
        {
            if (node is null) throw new InvalidOperationException("Projectile node left its weapon attachment.");
            local = node.Transform * local;
        }
        return Skeleton.Node.GetBoneGlobalPose(Skeleton.BoneIndex("Weapon")) * local;
    }

    internal void PrepareMuzzle(FalloutProjectile projectile)
    {
        var node = ProjectileNode();
        var path = (projectile.Flags & 8) == 0 ? null : projectile.MuzzleFlash;
        if (_muzzlePath == path && _muzzleLightForm == projectile.MuzzleLight) return;
        _muzzleLight?.Free(); _muzzleLight = null; _muzzleLightForm = null;
        _muzzleFlash?.Free(); _muzzleFlash = null; _muzzlePlayback = null;
        _muzzlePath = null;
        if (path is null) return;
        var scene = RuntimeNativeNifMeshBuilder.Build(FalloutNifFile.Read(Read(path)), Skeleton.UnitsToMetres);
        try
        {
            NativeNifCollisionBuilder.BindAnimatedAttachment(scene.Root);
            node.AddChild(scene.Root);
            _muzzlePlayback = new(scene.Root, projectile.MuzzleSeconds, _first && _xrLeftArm is null);
            if (projectile.MuzzleLight is { } lightForm)
            {
                var source = FalloutCellSceneReader.ReadLight(_records.GetEffective(lightForm));
                FalloutPlacedLightResolver.RequireStaticPoint(source, lightForm);
                var rgb = FalloutPlacedLightResolver.NormalizeLightColor(source.ColorRgb);
                _muzzleLight = new OmniLight3D
                {
                    Name = "SourceMuzzleLight",
                    Visible = false,
                    LightColor = RetailLighting.GodotLightColor(new(rgb[0], rgb[1], rgb[2])),
                    LightEnergy = source.Intensity,
                    OmniRange = source.RadiusGameUnits * Skeleton.UnitsToMetres,
                    OmniAttenuation = RetailLighting.GodotOmniDecayForRetailRemap,
                };
                node.AddChild(_muzzleLight); _muzzleLightForm = lightForm;
            }
            _muzzleFlash = scene.Root; _muzzlePath = path;
        }
        catch { scene.Root.Free(); _muzzleFlash = null; _muzzlePath = null; _muzzlePlayback = null; throw; }
    }

    internal void FlashMuzzle()
    {
        _muzzlePlayback?.Start();
        if (_muzzleLight is not null) _muzzleLight.Visible = _muzzlePlayback is { Remaining: > 0 };
    }

    private void AdvanceMuzzle(double delta)
    {
        _muzzlePlayback?.Advance(delta);
        if (_muzzleLight is not null) _muzzleLight.Visible = _muzzlePlayback is { Remaining: > 0 };
    }
}
