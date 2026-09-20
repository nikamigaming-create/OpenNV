using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Actors;

internal sealed partial class RuntimeNativePlayerActor
{
    private Node3D? _projectileNode;
    private NativeActorMuzzle? _muzzle;
    internal object? MuzzleState => _muzzle?.State;

    private Node3D ProjectileNode() => _projectileNode ??= _weaponNodes.SingleOrDefault(node =>
        node.GetMeta("opennv_nif_source_name", "").AsString().Equals("ProjectileNode", StringComparison.OrdinalIgnoreCase)) ??
        throw new NotSupportedException("Held source weapon has no unique ProjectileNode.");

    internal Transform3D ProjectileTransform() => _equippedObject!.Socket(Skeleton, "ProjectileNode");
    private Transform3D ProjectileTransformInSkeleton() => Skeleton.Node.GlobalTransform.AffineInverse() * ProjectileTransform();
    internal Transform3D ShellTransform() => _equippedObject!.Socket(Skeleton, "ShellCasingNode");

    private Transform3D WeaponNodeInSkeleton(Node3D source)
    {
        var local = Transform3D.Identity;
        for (Node3D? node = source; node != _weaponAttachment; node = node.GetParent() as Node3D)
        {
            if (node is null) throw new InvalidOperationException("Weapon node left its attachment.");
            local = node.Transform * local;
        }
        return Skeleton.Node.GetBoneGlobalPose(Skeleton.BoneIndex("Weapon")) * local;
    }

    internal void PrepareMuzzle(FalloutProjectile projectile)
    {
        _muzzle ??= new(_records, _content, ProjectileNode(), Skeleton.UnitsToMetres);
        _muzzle.Prepare(projectile, _first && _xrLeftArm is null);
    }
    internal void FlashMuzzle() => _muzzle?.Flash();
    private void AdvanceMuzzle(double delta) => _muzzle?.Advance(delta);
}
