using Godot;

namespace OpenNV.Runtime.World.Actors;

// A partition can retain weights on both sides of an authored cut. Redirect
// only those crossing bindings onto its own side, preserving their cut-time
// skinning transforms. Vertex weights, source meshes and materials stay intact.
internal static class RuntimeNativeDismemberSkin
{
    internal static void Separate(IEnumerable<MeshInstance3D> meshes, Skeleton3D skeleton, byte part, int root,
        IReadOnlyList<Transform3D> cutPose)
    {
        if (cutPose.Count != skeleton.GetBoneCount()) throw new InvalidDataException("Cut pose differs from its source skeleton.");
        var parent = skeleton.GetBoneParent(root);
        if (parent < 0) throw new NotSupportedException("A severable limb requires a parent bone.");
        var changes = new List<(Skin Skin, int Bind, int Bone, Transform3D Pose)>();
        foreach (var mesh in meshes)
        {
            var skin = mesh.Skin!;
            var tagged = mesh.HasMeta("opennv_nif_body_part");
            var tag = tagged ? mesh.GetMeta("opennv_nif_body_part").AsInt32() : -1;
            var detached = tag == part || tag == part + 100;
            for (var bind = 0; bind < skin.GetBindCount(); bind++)
            {
                var bone = skin.GetBindBone(bind);
                if (bone < 0 || bone >= cutPose.Count) throw new InvalidDataException("Skin binding has no source bone.");
                var inside = DescendsFrom(skeleton, bone, root);
                // Untagged accessories that lie wholly on one side already
                // follow that side. Mixed untagged skins need a source owner.
                if (!tagged)
                {
                    if (Enumerable.Range(0, skin.GetBindCount()).Any(index => DescendsFrom(skeleton, skin.GetBindBone(index), root) != inside))
                        throw new NotSupportedException("An untagged skin crosses a severed source limb.");
                    break;
                }
                if (inside == detached) continue;
                var anchor = detached ? root : parent;
                changes.Add((skin, bind, anchor, Rebind(cutPose[bone], cutPose[anchor], skin.GetBindPose(bind))));
            }
        }
        foreach (var change in changes)
        {
            change.Skin.SetBindBone(change.Bind, change.Bone);
            change.Skin.SetBindName(change.Bind, skeleton.GetBoneName(change.Bone));
            change.Skin.SetBindPose(change.Bind, change.Pose);
        }
    }

    internal static Transform3D Rebind(Transform3D previousBone, Transform3D anchor, Transform3D binding) =>
        anchor.AffineInverse() * previousBone * binding;

    internal static bool DescendsFrom(Skeleton3D skeleton, int bone, int parent)
    {
        for (var current = bone; current >= 0; current = skeleton.GetBoneParent(current))
            if (current == parent) return true;
        return false;
    }
}
