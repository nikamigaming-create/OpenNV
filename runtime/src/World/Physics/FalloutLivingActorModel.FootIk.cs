using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.World.Physics;

internal sealed record FalloutFootIkJointRead(int Constraint, Guid Wrapper, ulong NativeJoint, FalloutLivingJointParameters Parameters);
internal sealed partial record FalloutRagdollFootIkSnapshot(FalloutReferenceModelDataInvocation Load, IReadOnlyList<string> Nodes,
    string Step, FalloutNifSceneKey? Hip, FalloutNifSceneKey? Knee, FalloutNifSceneKey? Ankle,
    IReadOnlyList<FalloutLivingShapeInfo> Shapes, FalloutFootIkJointRead? KneeJoint, FalloutFootIkJointRead? AnkleJoint,
    bool? SetupResult, bool Returned, string? Failure, FalloutFootIkSetup? Setup = null, IReadOnlyList<FalloutFootIkSettingRead>? Settings = null,
    IReadOnlyList<FalloutFootIkLeg>? Legs = null, bool AvailabilityWritten = false, bool? Available = null);

internal sealed partial class FalloutLivingActorModel
{
    private FalloutRagdollFootIkSnapshot? _footIk;
    private bool _footIkEntered;
    private long _footIkReentry;
    private readonly List<FalloutLivingFootIkSolver> _footSolvers = [];
    internal FalloutRagdollFootIkSnapshot? CaptureFootIk() => _footIk;

    internal void InitializeFootIk(FalloutReferenceModelDataInvocation load, FalloutActorLogicalScene graph, Func<string, FalloutIniValue> readSetting,
        Action<FalloutReferenceModelDataInvocation> requireActual)
    {
        if (_footIkEntered) { ++_footIkReentry; throw new InvalidOperationException("Foot IK reentered its actual constructor child."); }
        RequireCurrent(); requireActual(load); _ragdollController?.RequireReturned();
        if (_footIk is not null || _ragdollBodyParts is not { Enumerated: true, Prime: false, Failure: null } parts || parts.Load != load ||
            _physicalSkeleton is not { } physical || physical.Load != load || !ReferenceEquals(graph, Source.LogicalScene))
            throw new InvalidOperationException("Foot IK lost its actual returned controller, body-part list or current model.");
        var shapes = new List<FalloutLivingShapeInfo>();
        _footIk = new(load, parts.IkNodes.ToArray(), "admission", null, null, null, shapes.AsReadOnly(), null, null, null, false, null);
        var fault = _footIkReentry; _footIkEntered = true;
        try
        {
            if (parts.IkNodes.Count == 0) { Return(null); return; }
            _footIk = _footIk with { Setup = FalloutFootIkSetup.Construct() };
            Step("first-current-leg-chain");
            var parent = graph.Objects.Single(node => node.Key == physical.Parent);
            var hip = Find(parent, parts.IkNodes[0]);
            _footIk = _footIk with { Hip = hip?.Key };
            if (hip?.Source is not FalloutNifNode || hip.Children.Count == 0 || hip.Children[0] is not { } knee || knee.Source is not FalloutNifNode)
            { Return(false); return; }
            _footIk = _footIk with { Knee = knee.Key };
            if (knee.Children.Count == 0 || knee.Children[0] is not { } ankle)
            { Return(false); return; }
            _footIk = _footIk with { Ankle = ankle.Key };
            foreach (var (node, role) in new[] { (hip, "hip"), (knee, "knee"), (ankle, "ankle") })
            {
                Step(role + "-current-shape-info");
                var collision = FirstCollision(node);
                if (collision is null || Body(collision) is not { } body) { Return(false); return; }
                shapes.Add(body.ReadRagdollShapeInfo(load, requireActual));
                _footIk = _footIk with { Shapes = shapes.ToArray() };
            }
            Step("current-knee-constraint-head");
            var kneeConstraint = ConstraintHead(knee);
            Step("current-ankle-constraint-head");
            var ankleConstraint = ConstraintHead(ankle);
            if (kneeConstraint is null || ankleConstraint is null) { Return(false); return; }
            Step("current-knee-limited-hinge-data");
            var kneeData = LimitedHinge(kneeConstraint);
            if (kneeData is null) { Return(false); return; }
            _footIk = _footIk with { KneeJoint = Joint(kneeConstraint, kneeData) };
            Step("current-ankle-limited-hinge-data");
            var ankleData = LimitedHinge(ankleConstraint);
            if (ankleData is null)
                throw new NotSupportedException("source-foot-IK-ankle-ball-and-socket-or-malleable-fallback-unowned");
            _footIk = _footIk with { AnkleJoint = Joint(ankleConstraint, ankleData) };
            Step("current-leg-calibration");
            CalibrateFootIkLeg(kneeData, ankleData, readSetting, Check);
            _footIk = _footIk with { SetupResult = true };
            Step("source-foot-solver-construction");
            var animation = _animationSkeleton?.Descriptor ?? throw new InvalidDataException("Foot solver lost its actual animation descriptor.");
            var mappings = _ragdollMapping?.Mappings ?? throw new InvalidDataException("Foot solver lost its actual shared mapping.");
            var up = FalloutPhysicalVector3.FromValues(0, 0, 1);
            _footIk = _footIk with { Setup = _footIk.Setup! with { Skeleton = animation.Identity, WorldUp = up, ModelUp = up } };
            var ground = ReadFootIkSetting("fOriginalGroundHeightMS:FootIK", readSetting, Check);
            _footIk = _footIk with { Setup = _footIk.Setup! with { GroundHeight = FalloutFootIkMath.Bits(ground) } };
            var legs = parts.IkNodes.Select(name => new FalloutFootIkLeg(name, null, null, null, null, false, null, null)).ToArray();
            _footIk = _footIk with { Legs = legs.ToArray() };
            for (var index = 0; index < legs.Length; ++index)
            {
                Step("solver-leg-" + index + "-current-chain");
                var first = Find(parent, legs[index].Name);
                var middle = first?.Source is FalloutNifNode && first.Children.Count != 0 ? first.Children[0] : null;
                var last = middle?.Source is FalloutNifNode && middle.Children.Count != 0 ? middle.Children[0] : null;
                legs[index] = legs[index] with { Hip = first?.Key, Knee = middle?.Key, Ankle = last?.Key }; Publish();
                var firstCollision = first is null ? null : FirstCollision(first);
                var middleCollision = middle is null ? null : FirstCollision(middle);
                var lastCollision = last is null ? null : FirstCollision(last);
                var lastBody = lastCollision is null ? null : Body(lastCollision);
                var info = lastBody?.ReadRagdollShapeInfo(load, requireActual);
                legs[index] = legs[index] with { AnkleShape = info }; Publish();
                var radius = info is null ? 0 : FalloutFootIkMath.Value(info.Parameters.Radius);
                var down = FalloutFootIkMath.Add(radius, 7); var rayUp = FalloutFootIkMath.Add(radius, 4);
                _ragdollController!.PublishFootRayHeight(load, FalloutFootIkMath.Bits(down));
                legs[index] = legs[index] with { RayHeight = FalloutFootIkMath.Bits(down) }; Publish();
                _footIk = _footIk with { Setup = _footIk.Setup! with { RayDistanceUp = FalloutFootIkMath.Bits(rayUp), RayDistanceDown = FalloutFootIkMath.Bits(down) } };
                if (firstCollision is not null && middleCollision is not null && lastCollision is not null &&
                    firstCollision != middleCollision && firstCollision != lastCollision && middleCollision != lastCollision)
                {
                    var firstIndex = Bone(firstCollision.Name); _footIk = _footIk with { Setup = _footIk.Setup! with { Hip = firstIndex } };
                    var middleIndex = Bone(middleCollision.Name); _footIk = _footIk with { Setup = _footIk.Setup! with { Knee = middleIndex } };
                    var lastIndex = Bone(lastCollision.Name); _footIk = _footIk with { Setup = _footIk.Setup! with { Ankle = lastIndex } };
                    if (firstIndex != -1 && middleIndex != -1 && lastIndex != -1)
                    {
                        var actualBodies = new[] { Body(firstCollision), Body(middleCollision), lastBody };
                        if (actualBodies.Any(body => body is null)) throw new NotSupportedException("source-foot-IK-solver-current-body-binding-unowned");
                        var solver = new FalloutLivingFootIkSolver(this, animation, _footIk.Setup!, actualBodies.Select(body => body!).ToArray());
                        _footSolvers.Add(solver);
                        legs[index] = legs[index] with { PointerWritten = true, Solver = solver.Capture() }; Publish();
                    }
                }
                foreach (var mapping in mappings.AnimationToPhysical.Simple)
                    if (mapping.Source == _footIk.Setup!.Ankle)
                    { legs[index] = legs[index] with { PhysicalAnkle = mapping.Target }; Publish(); }
            }
            Step("foot-availability-publication");
            if (legs.Any(leg => !leg.PointerWritten))
                throw new NotSupportedException("source-foot-IK-solver-pointer-has-no-actual-writer");
            var available = legs.All(leg => leg.Solver is not null);
            _footIk = _footIk with { AvailabilityWritten = true, Available = available };
            _ragdollController!.PublishFootAvailability(load, available);
            Return(true);

            void Publish() { _footIk = _footIk! with { Legs = legs.ToArray() }; Check(); }
            int Bone(string name)
            {
                var folded = FalloutRagdollSkeletonMappingConstruction.Name(name);
                for (var bone = 0; bone < animation.Bones.Count; ++bone)
                    if (FalloutRagdollSkeletonMappingConstruction.Name(animation.Bones[bone].Name) == folded)
                        return bone <= short.MaxValue ? bone : throw new NotSupportedException("source-foot-IK-large-bone-index-word-conversion-unowned");
                return -1;
            }
        }
        catch (Exception error) { _footIk = _footIk with { Failure = error.Message }; throw; }
        finally { _footIkEntered = false; }

        void Check()
        {
            RequireCurrent(); requireActual(load); _ragdollController!.RequireReturned();
            if (_footIkReentry != fault) throw new InvalidOperationException("Foot IK swallowed source reentry or retirement during construction.");
        }
        void Step(string step) { Check(); _footIk = _footIk with { Step = step }; }
        void Return(bool? setup) { Step("returned"); _footIk = _footIk with { SetupResult = setup, Returned = true }; }
        FalloutNifSceneObject? Find(FalloutNifSceneObject node, string name)
        {
            RequireCurrent(); requireActual(load);
            if (node.Name == name) return node;
            foreach (var child in node.Children)
                if (child is not null && Find(child, name) is { } found) return found;
            return null;
        }
        FalloutNifSceneObject? FirstCollision(FalloutNifSceneObject node)
        {
            if (graph.ReadCurrentCollision(node) is not null) return node;
            foreach (var child in node.Children)
                if (child is not null && FirstCollision(child) is { } found) return found;
            return null;
        }
        FalloutLivingPhysicalBody? Body(FalloutNifSceneObject node)
        {
            if (node.Key.Asset != 0) throw new NotSupportedException("source-foot-IK-part-current-physical-body-unowned");
            return _scene[node.Key.Block].CollisionObject?.CurrentWrapper?.CurrentBody;
        }
        FalloutLivingPhysicalConstraint? ConstraintHead(FalloutNifSceneObject node) =>
            graph.ReadCurrentCollision(node) is null ? null : Body(node)?.LinkedConstraints.FirstOrDefault() as FalloutLivingPhysicalConstraint;
        static FalloutLivingJointParameters? LimitedHinge(FalloutLivingPhysicalConstraint constraint)
        {
            constraint.RequireCurrent(); var data = constraint.Wrapper.CurrentData;
            return constraint.Source.Header.Block.TypeName switch
            {
                "bhkLimitedHingeConstraint" => data is { Type: 2 } ? data.Joint : null,
                "bhkMalleableConstraint" => data is { Type: 13, Wrapped: { Type: 2 } wrapped } ? wrapped.Joint : null,
                _ => null
            };
        }
        static FalloutFootIkJointRead Joint(FalloutLivingPhysicalConstraint constraint, FalloutLivingJointParameters parameters)
        {
            if (!ReferenceEquals(parameters, constraint.Parameters) || !constraint.Backend.HasPhysicalObject)
                throw new InvalidDataException("Foot IK borrowed another current physical joint's angular fields.");
            parameters.Validate();
            return new(constraint.Source.Header.Block.Index, constraint.Wrapper.Instance, constraint.Backend.PhysicalObjectId, parameters);
        }
    }
}
