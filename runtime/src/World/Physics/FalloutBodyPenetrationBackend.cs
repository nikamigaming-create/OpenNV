namespace OpenNV.Runtime.World.Physics;

// The instance borrows actual living bodies. It owns only the derived mapping
// and query lifetime; retiring it never destroys the Actor's bodies or joints.
internal sealed class FalloutBodyPenetrationBackend(
    Func<FalloutPenetrationSystemConstruction, IFalloutPenetrationQuery> constructQuery) : IFalloutPenetrationBackend
{
    public FalloutPenetrationOrderedGraph OrderBySourceDepth(FalloutPenetrationConnectedGraph graph) => FalloutPenetrationSourceMath.Order(graph);
    public FalloutCharacterControllerProxyTransform MakeRelativeTransform(FalloutCharacterControllerProxyTransform child,
        FalloutCharacterControllerProxyTransform parent) => FalloutPenetrationSourceMath.Relative(child, parent);
    public string TerminalBoneName(string bodyName) => bodyName + FalloutPenetrationSourceMath.TerminalSuffix;
    public FalloutPenetrationNormalizedVector Normalize(FalloutPhysicalVector3 vector) => FalloutPenetrationSourceMath.Normalize(vector);
    public uint DotBits(FalloutPhysicalVector3 left, FalloutPhysicalVector3 right) => FalloutPenetrationSourceMath.DotBits(left, right);
    public FalloutPhysicalVector3 InitialImpulseDirection() => FalloutPenetrationSourceMath.InitialDirection;

    public IFalloutPenetrationSystem Construct(FalloutPenetrationSystemConstruction construction)
    {
        ArgumentNullException.ThrowIfNull(construction); ArgumentNullException.ThrowIfNull(constructQuery);
        if (construction.Graph.Bodies.Count == 0 || !construction.Graph.Bodies.Contains(construction.Root))
            throw new InvalidDataException("Penetration instance requires its actual current connected body root.");
        var query = constructQuery(construction) ?? throw new InvalidDataException("Physical query construction did not return its current owner.");
        try { return new BodySystem(construction, query); }
        catch { query.Dispose(); throw; }
    }

    private sealed class BodySystem : IFalloutPenetrationSystem
    {
        private readonly int _thread = Environment.CurrentManagedThreadId;
        private readonly FalloutPenetrationSkeleton _skeleton;
        private bool _retired;

        internal BodySystem(FalloutPenetrationSystemConstruction construction, IFalloutPenetrationQuery query)
        {
            Bodies = Array.AsReadOnly(construction.Graph.Bodies.ToArray()); _skeleton = construction.Skeleton;
            BoneToBodyIndices = Array.AsReadOnly(_skeleton.Bones.Select(bone => bone.Terminal ? -1 : bone.Body).ToArray());
            Query = query;
        }

        public IReadOnlyList<IFalloutPenetrationBody> Bodies { get; }
        public IReadOnlyList<int> BoneToBodyIndices { get; }
        public IFalloutPenetrationQuery Query { get; }

        public IReadOnlyList<FalloutPhysicalVector3> SampleBonePositions(FalloutPenetrationSkeleton skeleton)
        {
            Require();
            if (!ReferenceEquals(skeleton, _skeleton))
                throw new InvalidOperationException("Physical pose sampling cannot borrow another instance's derived skeleton.");
            var transforms = Bodies.Select(body => body.ReadTransform()).ToArray();
            return Array.AsReadOnly(_skeleton.Bones.Select(bone => bone.Terminal
                ? FalloutPenetrationSourceMath.TransformPoint(transforms[bone.Body], bone.ReferencePose.Translation)
                : transforms[bone.Body].Translation).ToArray());
        }

        private void Require()
        {
            ObjectDisposedException.ThrowIf(_retired, this);
            if (Environment.CurrentManagedThreadId != _thread)
                throw new InvalidOperationException("Physical system requires its constructing thread.");
        }

        public void Dispose()
        {
            if (_retired) return; Require(); Query.Dispose(); _retired = true;
        }
    }
}
