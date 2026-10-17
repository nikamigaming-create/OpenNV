using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.World.Physics;

internal sealed record FalloutRagdollTrackingConstruction(short AnimationBone, short PhysicalBone,
    uint MaximumAngle, FalloutPhysicalVector3 FirstVector, FalloutPhysicalVector3 SecondVector,
    FalloutNifQuaternion Rotation, ushort Flags, uint FirstScalar, uint SecondScalar)
{
    internal static FalloutRagdollTrackingConstruction Construct() =>
        new(-1, -1, 0x3f860a92, default, default, new(1, 0, 0, 0), 1, 0, 0);
}

internal sealed record FalloutLivingRagdollControllerSnapshot(Guid Identity, FalloutReferenceModelDataInvocation Load,
    Guid Model, int Mode, FalloutRagdollAnimationPose RootPose, bool Ready,
    FalloutRagdollTrackingConstruction FirstTracking, FalloutRagdollTrackingConstruction SecondTracking,
    Guid? PhysicalInstance, Guid? Drive, Guid? AnimationPose, bool Returned, string? Failure, bool Retired,
    FalloutRagdollMatrixInverse? ModelAxes = null, FalloutRagdollIkFields? Ik = null,
    uint? CollisionGroup = null, long CollisionGroupStores = 0, string? DeathAssetPath = null, long DeathPathStores = 0,
    FalloutRagdollFinalFields? FinalFields = null)
{
    internal void Validate(FalloutLivingModelSnapshot model)
    {
        if (Identity == Guid.Empty || Model == Guid.Empty || Load is null || Load.Call == Guid.Empty || Mode != 2 ||
            RootPose != new FalloutRagdollAnimationPose(default, new(1, 0, 0, 0), 0x3f800000) ||
            FirstTracking != FalloutRagdollTrackingConstruction.Construct() || SecondTracking != FalloutRagdollTrackingConstruction.Construct() ||
            Ready != (PhysicalInstance is not null && Drive is not null) || !Ready && (PhysicalInstance is not null || Drive is not null) ||
            Returned && (!Ready || Failure is not null || AnimationPose is null) || !Returned && (AnimationPose is not null || ModelAxes is not null) ||
            Failure is not null && string.IsNullOrWhiteSpace(Failure))
            throw new InvalidDataException("Ragdoll controller lost its independent source fields or constructor prefix.");
        if (Ready && (model.PhysicalSkeleton is not { } physical || physical.Load != Load || physical.Instance != PhysicalInstance ||
            model.Drive is not { Returned: true, Failure: null } drive || drive.Load != Load || drive.Identity != Drive))
            throw new InvalidDataException("Ragdoll controller borrowed another current physical drive or instance.");
        if (Returned && (model.AnimationSkeleton is not { Returned: true, Failure: null } animation || animation.Load != Load || animation.Identity != AnimationPose ||
            model.Mapping is not { Returned: true, Failure: null } || model.RagdollTemplate is not { Restored: true, Failure: null }))
            throw new InvalidDataException("Ragdoll controller returned without its real animation, mapping or restored bodies.");
        ModelAxes?.Validate();
        if (ModelAxes is { } axes && !axes.Input.SequenceEqual(FalloutRagdollMatrixInverse.Identity))
            throw new InvalidDataException("Ragdoll model axes borrowed another matrix instead of the actual caller's independent identity field.");
        if (Ik is not { } ik) throw new InvalidDataException("Ragdoll controller omitted its independent IK fields.");
        ik.Validate(model, Load);
        if (CollisionGroupStores is not (0 or 1) || (CollisionGroup is not null) != (CollisionGroupStores == 1) ||
            CollisionGroup is > ushort.MaxValue || CollisionGroupStores == 1 && !Returned)
            throw new InvalidDataException("Ragdoll collision group lost its independent source field writer.");
        if (DeathPathStores < 0 || (DeathAssetPath is not null) != (DeathPathStores != 0) || DeathPathStores != 0 && CollisionGroupStores != 1)
            throw new InvalidDataException("Ragdoll death-path field lost its actual constructor null or source string store.");
        (FinalFields ?? throw new InvalidDataException("Ragdoll controller omitted its independent final fields.")).Validate();
    }
}

internal sealed record FalloutRagdollIkFields(uint RayHeight, long RayHeightStores, bool FootAvailable, long AvailabilityStores,
    bool FootEnabled, long FootEnableStores, bool ArmEnabled, long ArmEnableStores)
{
    internal static FalloutRagdollIkFields Construct() => new(0, 0, false, 0, false, 0, false, 0);
    internal void Validate(FalloutLivingModelSnapshot model, FalloutReferenceModelDataInvocation load)
    {
        if (RayHeightStores < 0 || AvailabilityStores is not (0 or 1) || FootEnableStores is not (0 or 1) || ArmEnableStores is not (0 or 1) ||
            RayHeightStores == 0 && RayHeight != 0 || AvailabilityStores == 0 && FootAvailable ||
            FootEnableStores == 0 && FootEnabled || ArmEnableStores == 0 && ArmEnabled || ArmEnableStores > FootEnableStores)
            throw new InvalidDataException("Ragdoll IK flags lost an independent constructor or ordered source writer.");
        if (AvailabilityStores == 1 && (model.FootIk is not { AvailabilityWritten: true } foot || foot.Load != load || foot.Available != FootAvailable))
            throw new InvalidDataException("Ragdoll IK availability borrowed another source solver construction.");
        var written = model.FootIk?.Legs?.Where(leg => leg.RayHeight is not null).ToArray() ?? [];
        if (written.Length != RayHeightStores || written.Length != 0 && written[^1].RayHeight != RayHeight)
            throw new InvalidDataException("Ragdoll ray height lost its actual ordered current ankle-radius stores.");
        if (FootEnableStores == 1 && (model.FootIk is not { Returned: true } returned || returned.Load != load || FootEnabled != FootAvailable))
            throw new InvalidDataException("Ragdoll foot enable did not consume its returned current availability.");
        if (ArmEnableStores == 1 && (model.Arms is not { } arms || arms.Load != load || ArmEnabled != (arms.Availability != 0)))
            throw new InvalidDataException("Ragdoll arm enable did not consume its actual prior-animation availability field.");
    }
}

// This is the live controller of the existing physical model. Its operations
// borrow the same body, drive and animation owners; they never rebuild bodies
// or infer a successful constructor from a source record count.
internal sealed partial class FalloutLivingRagdollController
{
    private readonly FalloutLivingActorModel _model;
    private readonly FalloutReferenceModelDataInvocation _load;
    private readonly Guid _identity = Guid.NewGuid();
    private readonly FalloutRagdollAnimationPose _rootPose = new(default, new(1, 0, 0, 0), 0x3f800000);
    private readonly FalloutRagdollTrackingConstruction _firstTracking = FalloutRagdollTrackingConstruction.Construct();
    private readonly FalloutRagdollTrackingConstruction _secondTracking = FalloutRagdollTrackingConstruction.Construct();
    private bool _ready, _returned, _retired;
    private Guid? _physical, _drive, _animation;
    private string? _failure;
    private FalloutRagdollMatrixInverse? _modelAxes;
    private FalloutRagdollIkFields _ik = FalloutRagdollIkFields.Construct();
    private uint? _collisionGroup;
    private long _collisionGroupStores;
    private string? _deathAssetPath;
    private long _deathPathStores;

    internal FalloutLivingRagdollController(FalloutLivingActorModel model, FalloutReferenceModelDataInvocation load)
    {
        model.RequireCurrent();
        if (load.Call == Guid.Empty) throw new InvalidDataException("Ragdoll controller requires its actual model-load invocation.");
        _model = model; _load = load;
    }

    internal Guid Identity => _identity;
    internal void PublishDrive(FalloutLivingRagdollSkeletonSnapshot physical, FalloutLivingRagdollDriveSnapshot drive)
    {
        RequireCurrent();
        if (_ready || _returned || _failure is not null || physical.Load != _load || drive.Load != _load ||
            drive is not { Returned: true, Retired: false, Failure: null })
            throw new InvalidOperationException("Ragdoll controller readiness requires its same returned live physical drive exactly once.");
        drive.Validate(physical);
        _physical = physical.Instance; _drive = drive.Identity; _ready = true;
    }

    internal void Return(FalloutActorLogicalScene graph)
    {
        RequireCurrent();
        if (_returned || _failure is not null || !_ready || !ReferenceEquals(graph, _model.Source.LogicalScene))
            throw new InvalidOperationException("Ragdoll controller return lost its actual drive, graph or entered constructor.");
        try
        {
            var physical = _model.CapturePhysicalSkeleton() ?? throw new InvalidDataException("Ragdoll controller lost its physical instance.");
            var drive = _model.CaptureRagdollDrive() ?? throw new InvalidDataException("Ragdoll controller lost its physical drive.");
            var animation = _model.CaptureAnimationSkeleton() ?? throw new InvalidDataException("Ragdoll controller lost its animation pose.");
            var mapping = _model.CaptureRagdollMapping() ?? throw new InvalidDataException("Ragdoll controller lost its shared mappers.");
            var arms = _model.CaptureRagdollArms() ?? throw new InvalidDataException("Ragdoll controller lost its prior-animation arm fields.");
            drive.Validate(physical); animation.Validate(physical); arms.Validate();
            if (physical.Load != _load || physical.Instance != _physical || drive.Identity != _drive || arms.Load != _load ||
                animation is not { Returned: true, Descriptor: { } descriptor, Failure: null } ||
                mapping is not { Returned: true, Mappings: { } maps, Failure: null } || mapping.Load != _load ||
                _model.Capture().RagdollTemplate is not { Restored: true, Failure: null } template || template.Load != _load)
                throw new InvalidDataException("Ragdoll controller cannot return an unfinished or foreign child.");
            maps.Validate(physical.Descriptor, descriptor);
            var logical = graph.Capture();
            if (logical.RagdollTemplatePose is not { Applied: true, Restored: true, Failure: null } pose || pose.Load != _load ||
                logical.RagdollWorldUpdates is not [{ Returned: true, RestoredPose: false, BoundsUpdated: true },
                { Returned: true, RestoredPose: true, BoundsUpdated: true }])
                throw new InvalidDataException("Ragdoll controller returned before its original pose, worlds or bounds were restored.");
            _animation = animation.Identity; _returned = true;
        }
        catch (Exception error) { _failure ??= error.Message; throw; }
    }

    internal void RequireReturned()
    {
        RequireCurrent();
        if (!_returned || !_ready || _failure is not null)
            throw new InvalidOperationException("Actor cannot publish an unfinished ragdoll controller.");
    }
    internal bool ReadReady() { RequireReturned(); return _ready; }
    internal void PublishFootRayHeight(FalloutReferenceModelDataInvocation load, uint height)
    {
        RequireReturned();
        if (load != _load || _ik.AvailabilityStores != 0 || _ik.FootEnableStores != 0)
            throw new InvalidOperationException("Foot ray-height publication left its actual constructor prefix.");
        _ik = _ik with { RayHeight = height, RayHeightStores = checked(_ik.RayHeightStores + 1) };
    }
    internal void PublishFootAvailability(FalloutReferenceModelDataInvocation load, bool available)
    {
        RequireReturned();
        if (load != _load || _ik.AvailabilityStores != 0 || _model.CaptureFootIk() is not { AvailabilityWritten: true, Legs: { } legs } foot ||
            foot.Load != load || foot.Available != available || _ik.RayHeightStores != legs.Count ||
            legs.Any(leg => !leg.PointerWritten || leg.Solver is null))
            throw new InvalidOperationException("Foot availability did not consume its actual completed solver pointer stores.");
        _ik = _ik with { FootAvailable = available, AvailabilityStores = 1 };
    }
    internal void PublishIkFeatureCopies(FalloutReferenceModelDataInvocation load)
    {
        RequireReturned();
        if (load != _load || _ik.FootEnableStores != 0 || _ik.ArmEnableStores != 0 || _model.CaptureFootIk() is not { Returned: true } foot || foot.Load != load)
            throw new InvalidOperationException("Actor IK feature copies replayed or preceded the actual foot initializer return.");
        _ik = _ik with { FootEnabled = _ik.FootAvailable, FootEnableStores = 1 };
        var arms = _model.CaptureRagdollArms() ?? throw new InvalidDataException("Actor IK feature copy lost its current arm availability.");
        arms.Validate();
        if (arms.Load != load) throw new InvalidDataException("Actor IK arm feature copy borrowed another source load.");
        _ik = _ik with { ArmEnabled = arms.Availability != 0, ArmEnableStores = 1 };
    }
    internal FalloutRagdollIkFields ReadIkFeatures() { RequireReturned(); return _ik; }
    internal void PublishCollisionGroup(FalloutReferenceModelDataInvocation load, uint currentFilter)
    {
        RequireReturned();
        if (load != _load || _collisionGroupStores != 0 || _ik.ArmEnableStores != 1)
            throw new InvalidOperationException("Ragdoll collision-group store replayed or preceded its actual IK publication.");
        _collisionGroup = currentFilter >> 16; _collisionGroupStores = 1;
    }
    internal void PublishDeathAssetPath(FalloutReferenceModelDataInvocation load, string path)
    {
        RequireReturned(); ArgumentNullException.ThrowIfNull(path);
        if (load != _load || _collisionGroupStores != 1)
            throw new InvalidOperationException("Ragdoll death path left its actual current source configuration.");
        if (_deathAssetPath == path) return;
        _deathAssetPath = path; _deathPathStores = checked(_deathPathStores + 1);
    }
    internal void ConfigureModelAxes()
    {
        RequireReturned();
        if (_modelAxes is not null) throw new InvalidOperationException("Ragdoll model-axis configuration replayed its source call.");
        _modelAxes = FalloutRagdollMatrixInverse.Evaluate(FalloutRagdollMatrixInverse.Identity);
    }
    internal FalloutLivingRagdollControllerSnapshot Capture() => new(_identity, _load, _model.Generation, 2, _rootPose,
        _ready, _firstTracking, _secondTracking, _physical, _drive, _animation, _returned, _failure, _retired, _modelAxes, _ik,
        _collisionGroup, _collisionGroupStores, _deathAssetPath, _deathPathStores, _finalFields);
    internal void Retire() { RequireCurrent(); _retired = true; }
    private void RequireCurrent()
    {
        _model.RequireCurrent();
        ObjectDisposedException.ThrowIf(_retired, this);
    }
}

internal sealed partial class FalloutLivingActorModel
{
    private FalloutLivingRagdollController? _ragdollController;
    internal FalloutLivingRagdollController BeginRagdollController(FalloutReferenceModelDataInvocation load,
        Action<FalloutReferenceModelDataInvocation> requireActual)
    {
        RequireCurrent(); requireActual(load);
        if (_ragdollController is not null || _ragdollBinding is not null)
            throw new InvalidOperationException("Ragdoll controller construction replayed or began after its children.");
        return _ragdollController = new(this, load);
    }
    internal FalloutLivingRagdollControllerSnapshot? CaptureRagdollController() => _ragdollController?.Capture();
}
