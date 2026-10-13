using System.Runtime.CompilerServices;

namespace OpenNV.Runtime.World.Physics;

// The finite source stores are shared by headless physics and native adapters.
// They do not certify solver, x87 exceptional-number or canonical-memory parity.
internal static class FalloutPenetrationSourceMath
{
    internal const string TerminalSuffix = " Nub";
    internal static readonly FalloutPhysicalVector3 InitialDirection = FalloutPhysicalVector3.FromValues(0, 0, 1);

    internal static FalloutPenetrationOrderedGraph Order(FalloutPenetrationConnectedGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        var count = graph.Bodies.Count;
        if (graph.Depths.Count != count || graph.Parents.Count != count || graph.Constraints.Count != count - 1)
            throw new InvalidDataException("Physical depth ordering requires the complete validated connected graph.");
        var indices = Enumerable.Range(0, count).ToArray();
        var depths = graph.Depths.ToArray();
        // This is the observed pairwise swap sequence. A stable sort changes
        // equal-depth order when a shallower body displaces an earlier peer.
        for (var first = 0; first < count - 1; first++)
            for (var second = first + 1; second < count; second++)
                if (depths[first] > depths[second])
                {
                    (depths[first], depths[second]) = (depths[second], depths[first]);
                    (indices[first], indices[second]) = (indices[second], indices[first]);
                }
        var currentIndex = new int[count];
        for (var index = 0; index < count; index++) currentIndex[indices[index]] = index;
        var byChild = new Dictionary<IFalloutPenetrationBody, IFalloutPenetrationConstraint>(ReferenceEqualityComparer.Instance);
        foreach (var constraint in graph.Constraints)
            if (!byChild.TryAdd(constraint.First, constraint))
                throw new InvalidDataException("Physical ordering has duplicate parent constraints.");
        var bodies = indices.Select(index => graph.Bodies[index]).ToArray();
        var parents = indices.Select(index => graph.Parents[index] == -1 ? -1 : currentIndex[graph.Parents[index]]).ToArray();
        if (parents.Length == 0 || parents[0] != -1 || parents.Skip(1).Any(parent => parent == -1))
            throw new InvalidDataException("Physical ordering has no unique root for its retained constraint array.");
        var constraints = bodies.Skip(1).Select(body => byChild[body]).ToArray();
        var result = new FalloutPenetrationOrderedGraph(Array.AsReadOnly(bodies), Array.AsReadOnly(constraints), Array.AsReadOnly(parents));
        FalloutPenetrationGraph.ValidateOrder(graph, result); return result;
    }

    internal static FalloutPenetrationNormalizedVector Normalize(FalloutPhysicalVector3 vector)
    {
        RequireFinite(vector);
        var square = Dot(vector, vector);
        if (!float.IsFinite(square)) throw new NotSupportedException("Physical normalization overflow requires its exceptional-number producer.");
        var length = StoreSquareRoot(square);
        var reciprocal = square == 0 ? 0 : Divide(1, length);
        return new(BitConverter.SingleToUInt32Bits(length), FalloutPenetrationImpulse.Multiply(vector, reciprocal));
    }

    internal static uint DotBits(FalloutPhysicalVector3 left, FalloutPhysicalVector3 right)
    { RequireFinite(left); RequireFinite(right); return BitConverter.SingleToUInt32Bits(Dot(left, right)); }

    private static float Dot(FalloutPhysicalVector3 left, FalloutPhysicalVector3 right) =>
        FalloutPhysicalFloat32.Add(FalloutPhysicalFloat32.Add(
            FalloutPhysicalFloat32.Multiply(left.YValue, right.YValue),
            FalloutPhysicalFloat32.Multiply(left.XValue, right.XValue)),
            FalloutPhysicalFloat32.Multiply(left.ZValue, right.ZValue));

    internal static FalloutPhysicalVector3 Rotate(FalloutPhysicalRotation rotation, FalloutPhysicalVector3 vector) =>
        FalloutCharacterControllerTransformState.RotateOffset(vector, rotation);

    internal static FalloutPhysicalVector3 InverseRotate(FalloutPhysicalRotation rotation, FalloutPhysicalVector3 vector) =>
        FalloutPhysicalVector3.FromValues(Dot(rotation.Column0, vector), Dot(rotation.Column1, vector), Dot(rotation.Column2, vector));

    internal static FalloutCharacterControllerProxyTransform Relative(FalloutCharacterControllerProxyTransform child,
        FalloutCharacterControllerProxyTransform parent)
    {
        RequireFinite(child.Translation); RequireFinite(parent.Translation);
        return new(new(InverseRotate(parent.Rotation, child.Rotation.Column0),
            InverseRotate(parent.Rotation, child.Rotation.Column1), InverseRotate(parent.Rotation, child.Rotation.Column2)),
            InverseRotate(parent.Rotation, FalloutPenetrationImpulse.Subtract(child.Translation, parent.Translation)));
    }

    internal static FalloutPhysicalVector3 TransformPoint(FalloutCharacterControllerProxyTransform transform, FalloutPhysicalVector3 point) =>
        FalloutPenetrationImpulse.Add(Rotate(transform.Rotation, point), transform.Translation);

    private static void RequireFinite(FalloutPhysicalVector3 vector)
    {
        if (!float.IsFinite(vector.XValue) || !float.IsFinite(vector.YValue) || !float.IsFinite(vector.ZValue))
            throw new NotSupportedException("Physical arithmetic requires its finite current source operands.");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static float StoreSquareRoot(float square) => (float)Math.Sqrt((double)square);
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static float Divide(float numerator, float denominator) => numerator / denominator;
}
