using OpenNV.Runtime.Formats.Gamebryo;

internal static partial class CellGraphAudit
{
    private sealed class ConvexListProjectionBoundary(int shape, object[] declarations)
        : NotSupportedException("Convex-list floor projection is uninspected; an independent-leaf native implementation owner exists, with native admission and contacts unverified.")
    {
        internal int Shape { get; } = shape;
        internal object[] Declarations { get; } = declarations;
    }

    // Declaration context only. Do not construct a hull, emit floor triangles,
    // infer native validity or substitute this inspection for BuildConvexList.
    private static object[] ConvexListDeclarationContext(FalloutNifFile source, FalloutNifConvexListShape root)
    {
        var rows = new List<object>(); var active = new HashSet<int>();
        Walk(root.Block.Index, [], []);
        return rows.ToArray();

        void Walk(int index, int[] parentPath, int[] ordinals)
        {
            if (!active.Add(index)) throw new InvalidDataException("Collision declaration graph cycle at " + index);
            try
            {
                var obj = source.ReadObject(index);
                var path = parentPath.Append(index).ToArray();
                int[] children = obj switch
                {
                    FalloutNifConvexListShape list => list.Children.ToArray(),
                    FalloutNifListShape list => list.Children.ToArray(),
                    FalloutNifConvexTransformShape transform => [transform.Child],
                    FalloutNifMoppShape mopp => [mopp.Child],
                    _ => []
                };
                var role = obj switch
                {
                    FalloutNifConvexListShape => "convex-union",
                    FalloutNifListShape => "list",
                    FalloutNifConvexTransformShape => "child-transform",
                    FalloutNifMoppShape => "mopp-child",
                    _ => "uninspected-leaf"
                };
                uint? material = obj switch
                {
                    FalloutNifConvexListShape list => list.Material, FalloutNifListShape list => list.Material,
                    FalloutNifConvexTransformShape transform => transform.Material,
                    FalloutNifConvexVerticesShape convex => convex.Material, FalloutNifBoxShape box => box.Material,
                    FalloutNifSphereShape sphere => sphere.Material, FalloutNifCapsuleShape capsule => capsule.Material,
                    _ => null
                };
                rows.Add(new { block = index, type = obj.Block.TypeName, role, shapePath = path,
                    sourceChildOrdinals = ordinals, childReferences = children, sourceMaterial = material,
                    sourceMatrix = obj is FalloutNifConvexTransformShape declaredTransform ? declaredTransform.MatrixRowMajor.ToArray() : null,
                    nativeAdmission = "unverified", mathProjection = "uninspected" });
                for (var ordinal = 0; ordinal < children.Length; ordinal++)
                    Walk(children[ordinal], path, ordinals.Append(ordinal).ToArray());
            }
            finally { active.Remove(index); }
        }
    }
}
