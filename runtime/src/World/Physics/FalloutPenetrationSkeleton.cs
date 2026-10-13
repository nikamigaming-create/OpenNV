namespace OpenNV.Runtime.World.Physics;

internal sealed record FalloutPenetrationBone(string Name, int Parent, int Body,
    FalloutCharacterControllerProxyTransform ReferencePose, bool Terminal);

// This skeleton comes from current physical bodies, not visible animation names.
internal sealed class FalloutPenetrationSkeleton
{
    private static readonly FalloutPhysicalRotation IdentityRotation = new(
        FalloutPhysicalVector3.FromValues(1, 0, 0), FalloutPhysicalVector3.FromValues(0, 1, 0),
        FalloutPhysicalVector3.FromValues(0, 0, 1));

    private FalloutPenetrationSkeleton(IReadOnlyList<FalloutPenetrationBone> bones) => Bones = bones;
    internal IReadOnlyList<FalloutPenetrationBone> Bones { get; }

    internal static FalloutPenetrationSkeleton Construct(FalloutPenetrationOrderedGraph graph,
        IFalloutPenetrationBackend backend)
    {
        var bones = new List<FalloutPenetrationBone>();
        var transforms = graph.Bodies.Select(body => body.ReadTransform()).ToArray();
        for (var index = 0; index < graph.Bodies.Count; index++)
        {
            var parent = graph.Parents[index];
            var pose = parent == -1 ? transforms[index] : backend.MakeRelativeTransform(transforms[index], transforms[parent]);
            bones.Add(new(graph.Bodies[index].Name, parent, index, pose, false));
        }
        for (var index = 0; index < graph.Bodies.Count; index++)
        {
            var body = graph.Bodies[index];
            var hasParent = body.IncomingConstraints.Any(constraint =>
                constraint.DataType is { } type && type != FalloutPenetrationGraph.ContactConstraintType);
            if (!hasParent) continue;
            var hasChild = body.OutgoingConstraints.Any(constraint =>
                constraint.DataType is { } type && type != FalloutPenetrationGraph.ContactConstraintType);
            if (hasChild) continue;
            var translation = TerminalTranslation(body.ReadTerminalShape(), backend);
            bones.Add(new(backend.TerminalBoneName(body.Name), index, index,
                new(IdentityRotation, translation), true));
        }
        return new(bones.AsReadOnly());
    }

    internal static FalloutPenetrationSkeleton Restore(IReadOnlyList<FalloutPenetrationBone> bones, int bodyCount)
    {
        if (bones is null || bones.Count < bodyCount || bodyCount < 1)
            throw new InvalidDataException("Saved physical skeleton has no complete structural body mapping.");
        for (var index = 0; index < bones.Count; index++)
        {
            var bone = bones[index];
            if (bone is null || bone.Name is null || bone.Parent < -1 || bone.Parent >= index ||
                bone.Body < 0 || bone.Body >= bodyCount || bone.Terminal != (index >= bodyCount) ||
                (index < bodyCount && bone.Body != index) || (bone.Terminal && bone.Parent != bone.Body))
                throw new InvalidDataException("Saved physical skeleton changed its topological body/terminal mapping.");
        }
        return new(Array.AsReadOnly(bones.ToArray()));
    }

    private static FalloutPhysicalVector3 TerminalTranslation(FalloutPenetrationTerminalShape shape,
        IFalloutPenetrationBackend backend)
    {
        var radius = BitConverter.UInt32BitsToSingle(shape.RadiusBits);
        if (shape.Kind == 6)
        {
            var difference = FalloutPenetrationImpulse.Subtract(shape.EndpointA, shape.EndpointB);
            var normalized = backend.Normalize(difference);
            return FalloutPenetrationImpulse.Add(difference, FalloutPenetrationImpulse.Multiply(normalized.Direction, radius));
        }
        return shape.Kind is 2 or 5 or 7
            ? FalloutPhysicalVector3.FromValues(FalloutPhysicalFloat32.Multiply(radius, 2f), 0, 0)
            : default;
    }
}
