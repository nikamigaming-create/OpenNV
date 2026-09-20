using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Formats.Gamebryo;

internal static class FalloutNifAuthoredRagdoll
{
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
