using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Formats.Gamebryo;

internal static class FalloutNifAuthoredRagdoll
{
    // Authored XRGB rotates the first ordered child of the actor's source
    // scene root. Neither a bone name nor the placed reference selects it.
    internal static FalloutNifNode BindAccumulationRoot(FalloutNifFile skeleton)
    {
        if (skeleton.Roots.Count != 1 || skeleton.Roots[0] < 0 ||
            skeleton.ReadObject(skeleton.Roots[0]) is not FalloutNifNode root)
            throw new NotSupportedException("Authored XRGB has no unique source scene root.");
        if (root.Children.Length == 0 || root.Children[0] < 0)
            throw new InvalidDataException("Authored XRGB scene root has no first ordered child.");
        if (skeleton.ReadObject(root.Children[0]) is not FalloutNifNode child)
            throw new NotSupportedException("Authored XRGB first ordered child is not a skeleton node.");
        return child;
    }

    internal static IReadOnlyList<(string Bone, FalloutAuthoredRagdollBone Pose)> Bind(
        FalloutNifFile skeleton, FalloutAuthoredRagdoll authored)
    {
        var bodies = new List<(string Name, byte Part)>();
        var visited = new HashSet<int>();
        void Visit(int index)
        {
            if (index < 0) return;
            if (!visited.Add(index)) throw new InvalidDataException("Authored ragdoll skeleton hierarchy is not a tree.");
            if (skeleton.Blocks[index].TypeName is not ("NiNode" or "NiBone" or "BSFadeNode")) return;
            var node = skeleton.ReadNode(index);
            if (node.CollisionObject >= 0)
            {
                var collision = (FalloutNifCollisionObject)skeleton.ReadObject(node.CollisionObject);
                var body = (FalloutNifRigidBody)skeleton.ReadObject(collision.Body);
                bodies.Add((node.Name, (byte)(body.Filter.Flags & 31)));
            }
            foreach (var child in node.Children) Visit(child);
        }
        foreach (var root in skeleton.Roots) Visit(root);
        if (bodies.Count != authored.Bones.Count || !bodies.Select(body => body.Part).SequenceEqual(authored.Bones.Select(bone => bone.Part)))
            throw new InvalidDataException("XRGD body order/part numbers differ from the winning skeleton.");
        return bodies.Select((body, index) => (body.Name, authored.Bones[index])).ToArray();
    }
}
