using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.World.Physics;

internal sealed record FalloutFootIkSolverSnapshot(Guid Identity, Guid Model, Guid Skeleton, FalloutFootIkSetup Setup,
    IReadOnlyList<int> Bodies, IReadOnlyList<ulong> NativeBodies, uint FirstAccumulator, uint SecondAccumulator,
    FalloutPhysicalVector3 GroundNormal, uint Correction, bool HasGround,
    IReadOnlyList<FalloutPhysicalVector3> Vectors, bool Retired);
internal sealed record FalloutFootIkLeg(string Name, FalloutNifSceneKey? Hip, FalloutNifSceneKey? Knee,
    FalloutNifSceneKey? Ankle, FalloutLivingShapeInfo? AnkleShape, bool PointerWritten,
    FalloutFootIkSolverSnapshot? Solver, int? PhysicalAnkle, uint? RayHeight = null);

internal sealed class FalloutLivingFootIkSolver
{
    private readonly FalloutLivingActorModel _model;
    private readonly FalloutRagdollAnimationDescriptor _skeleton;
    private readonly FalloutFootIkSetup _setup;
    private readonly IReadOnlyList<FalloutLivingPhysicalBody> _bodies;
    private readonly Guid _identity = Guid.NewGuid();
    private readonly uint _firstAccumulator = 0, _secondAccumulator = 0, _correction = 0;
    private readonly FalloutPhysicalVector3 _groundNormal;
    private readonly bool _hasGround = false;
    private readonly IReadOnlyList<FalloutPhysicalVector3> _vectors = new FalloutPhysicalVector3[3];
    private bool _retired;

    internal FalloutLivingFootIkSolver(FalloutLivingActorModel model, FalloutRagdollAnimationDescriptor skeleton,
        FalloutFootIkSetup setup, IReadOnlyList<FalloutLivingPhysicalBody> bodies)
    {
        model.RequireCurrent(); skeleton.Validate();
        var indices = new[] { setup.Hip, setup.Knee, setup.Ankle };
        if (setup.Skeleton != skeleton.Identity || bodies.Count != 3 ||
            !ReferenceEquals(model.CaptureAnimationSkeleton()?.Descriptor, skeleton) ||
            indices.Any(index => index < 0 || index >= skeleton.Bones.Count) ||
            bodies.Where((body, index) => body.Model != model || !body.Backend.HasPhysicalObject ||
                FalloutRagdollSkeletonMappingConstruction.Name(body.Name) !=
                FalloutRagdollSkeletonMappingConstruction.Name(skeleton.Bones[indices[index]].Name)).Any())
            throw new InvalidDataException("Foot solver lost its same real physical leg and current animation skeleton.");
        _model = model; _skeleton = skeleton; _setup = setup; _bodies = bodies.ToArray();
        _groundNormal = setup.WorldUp;
    }
    internal FalloutFootIkSolverSnapshot Capture()
    {
        _model.RequireCurrent();
        return new(_identity, _model.Generation, _skeleton.Identity, _setup,
            _bodies.Select(body => body.Source.Block.Index).ToArray(), _bodies.Select(body => body.Backend.PhysicalObjectId).ToArray(),
            _firstAccumulator, _secondAccumulator, _groundNormal, _correction, _hasGround, _vectors.ToArray(), _retired);
    }
    internal void Retire() { _model.RequireCurrent(); _retired = true; }
}
