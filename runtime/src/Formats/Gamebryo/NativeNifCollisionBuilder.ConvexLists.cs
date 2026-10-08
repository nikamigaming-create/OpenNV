using Godot;

namespace OpenNV.Runtime.Formats.Gamebryo;

internal static partial class NativeNifCollisionBuilder
{
    private static void BuildConvexList(FalloutNifFile source, FalloutNifConvexListShape list, float unitsToMetres,
        float mass, Transform3D transform, PhysicsBody3D owner, List<CollisionShape3D> output,
        ref int triangles, HashSet<int> active)
    {
        if (list.Children.Length == 0)
            throw new InvalidDataException($"NIF convex list shape {list.Block.Index} is empty.");
        if (list.Radius < 0)
            throw new InvalidDataException($"NIF convex list shape {list.Block.Index} has a negative shell radius.");
        foreach (var child in list.Children)
        {
            var first = output.Count;
            // A convex collection is the authored union of its independent
            // leaves. Joining their vertices into one hull would fill its gaps.
            // The body continues to own mass, filtering and every partial node.
            BuildShape(source, child, unitsToMetres, mass, transform, owner, output, ref triangles, active, convexOnly: true);
            for (var index = first; index < output.Count; ++index)
            {
                var node = output[index];
                var path = node.HasMeta("opennv_nif_convex_list_path") ? node.GetMeta("opennv_nif_convex_list_path").AsInt32Array() : [];
                var materials = node.HasMeta("opennv_havok_compound_materials") ? node.GetMeta("opennv_havok_compound_materials").AsInt32Array() : [];
                var radii = node.HasMeta("opennv_havok_compound_radii") ? node.GetMeta("opennv_havok_compound_radii").AsFloat32Array() : [];
                node.SetMeta("opennv_nif_convex_list_path", new[] { list.Block.Index }.Concat(path).ToArray());
                node.SetMeta("opennv_havok_compound_materials", new[] { unchecked((int)list.Material) }.Concat(materials).ToArray());
                node.SetMeta("opennv_havok_compound_radii", new[] { list.Radius }.Concat(radii).ToArray());
                node.SetMeta("opennv_convex_query_boundary", "native-independent-leaves;Havok-agent/cached-AABB/closest-point-optimization-unverified");
            }
        }
    }

    private static void RequireCompoundMaterial(CollisionShape3D? shape)
    {
        if (shape?.HasMeta("opennv_havok_compound_materials") != true) return;
        if (!shape.HasMeta("opennv_havok_material"))
            throw new NotSupportedException("Compound convex hit has no source leaf material.");
        var leaf = shape.GetMeta("opennv_havok_material").AsUInt32();
        if (shape.GetMeta("opennv_havok_compound_materials").AsInt32Array().Any(value => unchecked((uint)value) != leaf))
            throw new NotSupportedException("Compound convex hit has independently different parent/leaf materials; native precedence is unbound.");
    }
}
