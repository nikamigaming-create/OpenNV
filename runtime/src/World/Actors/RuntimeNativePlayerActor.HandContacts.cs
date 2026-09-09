using Godot;
using OpenNV.Runtime.Presentation.OpenXR;

namespace OpenNV.Runtime.World.Actors;

internal sealed partial class RuntimeNativePlayerActor
{
    internal void BindHandContact(NativeXrHandContact contact, RuntimeNativePlayerActor world, bool left)
    {
        var name = left ? "Bip01 L Hand" : "Bip01 R Hand";
        var areas = world.BodyContacts.Where(area => area.GetMeta("opennv_nif_collision_bone", "").AsString() == name).ToArray();
        if (areas.Length == 0) throw new NotSupportedException($"Source skeleton has no {name} Havok contact.");
        foreach (var area in areas)
            foreach (var shape in area.GetChildren().OfType<CollisionShape3D>())
                contact.AddShape(shape.Shape, area.Transform * shape.Transform);
        if (left || Weapon is null) return;
        var source = _weaponRoot!.FindChildren("*", nameof(CollisionShape3D), true, false).OfType<CollisionShape3D>().ToArray();
        var owner = this;
        if (source.Length == 0)
        {
            // The first-person variant may omit pickup Havok. Keep the winning
            // world model's authored shapes in model space, then attach them to
            // the actual first-person Weapon bone and its original model root.
            source = world._weaponRoot!.FindChildren("*", nameof(CollisionShape3D), true, false).OfType<CollisionShape3D>().ToArray();
            owner = world;
        }
        if (source.Length == 0) throw new NotSupportedException($"Equipped {Weapon.Form} has no authored weapon contact shapes.");
        var sourceModel = owner.Skeleton.Node.GetBoneGlobalPose(owner.Skeleton.BoneIndex("Weapon")) * owner._weaponRoot!.Transform;
        var handFromModel = Skeleton.Node.GetBoneGlobalPose(Skeleton.BoneIndex(name)).AffineInverse() *
            Skeleton.Node.GetBoneGlobalPose(Skeleton.BoneIndex("Weapon")) * _weaponRoot.Transform;
        foreach (var shape in source)
            contact.AddShape(shape.Shape, handFromModel * sourceModel.AffineInverse() * owner.WeaponNodeInSkeleton(shape), weapon: true);
    }
}
