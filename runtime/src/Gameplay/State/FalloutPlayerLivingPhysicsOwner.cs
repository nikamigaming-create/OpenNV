using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Physics;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutPlayerPhysicsFrameCall(Guid Main, long MainOrdinal,
    long Entered, long Changed, string Step, bool Returned, string? Failure);
internal sealed record FalloutPlayerPhysicsCaptureRefusal(string Lane, string Owner,
    string ExceptionType, string Message);

internal sealed record FalloutPlayerLivingPhysicsSnapshot(string Schema, string Source, string Stack,
    Guid CapturedProcess, Guid Factory, long Revision, FalloutActorPhysicalAdmissionFields Fields,
    FalloutActorPhysicalRegistrationTrace? Admission, IReadOnlyList<FalloutLivingModelSnapshot> Models,
    FalloutActorPenetrationSnapshot Utility, FalloutPlayerPhysicsFrameCall? Frame, string? Failure,
    FalloutLivingWorldSnapshot? World = null, FalloutPenetrationClockSnapshot? Clock = null,
    IReadOnlyList<FalloutPlayerPhysicsCaptureRefusal>? CaptureRefusals = null)
{
    internal const string CurrentSchema = "opennv-Player-living-physics/v2";
    internal bool CompleteCapture => CaptureRefusals is null || CaptureRefusals.Count == 0;

    internal void Validate()
    {
        if (Schema != CurrentSchema || string.IsNullOrWhiteSpace(Source) || string.IsNullOrWhiteSpace(Stack) ||
            CapturedProcess == Guid.Empty || Factory == Guid.Empty || Revision < 0 || Fields is null || Models is null ||
            Models.Select(model => model.Key).Distinct(StringComparer.Ordinal).Count() != Models.Count ||
            Failure is not null && string.IsNullOrWhiteSpace(Failure))
            throw new InvalidDataException("Player physics lost its actual source, factory or retained fields.");
        Fields.Validate(); FalloutActorPenetrationUtility.Validate(Utility);
        if (CaptureRefusals is not null && CaptureRefusals.Any(refusal => refusal is null ||
            refusal.Lane is not ("model" or "world" or "clock") || string.IsNullOrWhiteSpace(refusal.Owner) ||
            string.IsNullOrWhiteSpace(refusal.ExceptionType) || string.IsNullOrWhiteSpace(refusal.Message)))
            throw new InvalidDataException("Player physics capture lost its explicit unavailable child.");
        World?.Validate();
        if (Admission is not null) FalloutActorPhysicalRegistrationPredicate.Validate(Admission);
        if (Clock is { } clock)
        {
            var sourceClock = FalloutPenetrationClockSource.ForExecutable(clock.EngineSha256);
            if (Admission is not null && clock.EngineSha256 != Admission.Source.Main.EngineSha256 ||
                string.IsNullOrWhiteSpace(clock.Phase) ||
                clock.Initialized != (clock.Frequency is not null && clock.TicksPerMillisecondBits is not null) ||
                clock.Initialized && clock.TicksPerMillisecondBits != sourceClock.TicksPerMillisecondBits(clock.Frequency!.Value))
                throw new InvalidDataException("Player physics clock changed its source or retained frequency conversion.");
        }
        foreach (var model in Models)
        {
            if (string.IsNullOrWhiteSpace(model.Key) || !FalloutAdvancementRuntimeReceipt.Digest(model.SourceSha256) ||
                model.Bodies is null || model.Bodies.Select(body => body.Key).Distinct(StringComparer.Ordinal).Count() != model.Bodies.Count)
                throw new InvalidDataException("Player physics omitted its living model source or distinct current bodies.");
            if (model.RagdollBinding is { } binding)
            {
                binding.Validate();
                if (binding.Skeleton.Source != model.SourceSha256 || binding.Bodies.Any(body =>
                    !model.Bodies.Any(current => current.Key == body.CurrentBody)))
                    throw new InvalidDataException("Player physics changed its actual ragdoll/model body association.");
            }
            foreach (var body in model.Bodies.Where(body => body.ShapeInfo is not null))
                body.ShapeInfo!.Validate(body, model.RagdollBinding ??
                    throw new InvalidDataException("Ragdoll current shape-info omitted its actual body/resource binding."));
            if (model.PhysicalSkeleton is { } skeleton)
            {
                skeleton.Descriptor.Validate();
                if (model.RagdollBinding is not { Returned: true } actual || skeleton.Load != actual.Load || skeleton.Instance == Guid.Empty ||
                    skeleton.Descriptor.Source != model.SourceSha256 || skeleton.NativeBodies.Count != skeleton.Descriptor.Bones.Count ||
                    skeleton.NativeJoints.Count != skeleton.Descriptor.Constraints.Count ||
                    skeleton.Descriptor.Bones.Select((bone, index) => (bone, index)).Any(row => !actual.Bodies.Any(body =>
                        body.Body == row.bone.Body && body.Name == row.bone.Name && body.NativeBody == skeleton.NativeBodies[row.index])) ||
                    skeleton.Descriptor.Constraints.Select((joint, index) => (joint, index)).Any(row => !actual.Joints.Any(joint =>
                        joint.Constraint == row.joint && joint.NativeJoint == skeleton.NativeJoints[row.index])))
                    throw new InvalidDataException("Physical skeleton borrowed another actual ragdoll body/joint instance or descriptor source.");
            }
            if (model.Drive is { } drive)
                drive.Validate(model.PhysicalSkeleton ?? throw new InvalidDataException("Ragdoll drive omitted its actual physical instance."));
            model.Controller?.Validate(model);
            model.BodyPartPreparation?.Validate(model);
            model.FootIk?.Validate(model);
            model.DeathInitialization?.Validate(model);
            if (model.Arms is { } arms)
            {
                arms.Validate();
                if (model.PhysicalSkeleton is not { } armPhysical || arms.Load != armPhysical.Load)
                    throw new InvalidDataException("Ragdoll arm fields borrowed another actual physical initialization.");
                if (arms.ExistingAnimation is { } prior && model.AnimationSkeleton?.Descriptor is { } selected && prior != selected.Identity)
                    throw new InvalidDataException("Ragdoll arm fields borrowed another shared animation descriptor.");
            }
            if (model.AnimationSkeleton is { } animation)
                animation.Validate(model.PhysicalSkeleton ?? throw new InvalidDataException("Ragdoll animation omitted its actual physical instance."));
            if (model.Mapping is { } mapping)
            {
                if (model.PhysicalSkeleton is not { } physical || model.AnimationSkeleton is not { Returned: true, Descriptor: { } descriptor } ||
                    mapping.Load != physical.Load || mapping.Returned == (mapping.Failure is not null) ||
                    mapping.Returned != (mapping.Mappings is not null) || mapping.Cached && !mapping.Returned)
                    throw new InvalidDataException("Ragdoll mapper lost its actual source instance or failed constructor prefix.");
                mapping.Mappings?.Validate(physical.Descriptor, descriptor);
            }
        }
        if (Frame is { } call && (call.Main == Guid.Empty || call.MainOrdinal < 1 || call.Entered < 1 ||
            call.Changed <= call.Entered || call.Changed > Revision || string.IsNullOrWhiteSpace(call.Step) ||
            call.Returned == (call.Failure is not null) || call.Failure is not null && call.Failure != Failure))
            throw new InvalidDataException("Player physical update lost its entered call or committed failure.");
        if (CompleteCapture && Frame is { Returned: true } && (Admission is not { Returned: true } || Models.Count != 2 ||
            Utility.Failure is not null || Utility.PendingSystems.Count != 0 || Failure is not null))
            throw new InvalidDataException("Player physics returned without its actual published models and completed utility.");
        if (World is not null)
        {
            var modelsByBody = Models.SelectMany(model => model.Bodies.Select(body => (body.Key, model.SourceSha256, body.Motion)))
                .ToDictionary(body => body.Key, StringComparer.Ordinal);
            if (World.Bodies.Any(body => !modelsByBody.TryGetValue(body.Key, out var model) ||
                body.SourceSha256 != model.SourceSha256 || body.Motion != model.Motion))
                throw new InvalidDataException("Player physics world changed a model source or current body motion.");
            if (CompleteCapture && Frame is { Returned: true } && World.Bodies.Count != modelsByBody.Count)
                throw new InvalidDataException("Returned Player physics omitted a current living model body from its world.");
        }
        if (CompleteCapture && Frame is { Returned: true } && Models.Any(model => model.Bodies.Count != 0) &&
            (World is null || Clock is not { Initialized: true, Failure: null }))
            throw new InvalidDataException("Nonempty Player physics returned without its current world and retained source clock.");
    }
}

internal interface IFalloutPlayerLivingPhysicsOwner
{
    Guid Process { get; }
    string Source { get; }
    string Stack { get; }
    Guid Factory { get; }
    void RequireCurrent();
    void Update(Guid main, long ordinal);
    FalloutPlayerLivingPhysicsSnapshot Capture();
}
