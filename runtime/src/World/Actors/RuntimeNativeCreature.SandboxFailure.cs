using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.World.Actors;

internal sealed partial class RuntimeNativeCreature
{
    private FalloutSandboxMotionException? _sandboxMotionException;
    private FalloutActorSandboxMotionFailure? _restoredSandboxMotion;
    private bool _sandboxMotionPosePublished;
    private Func<bool>? _sandboxMotionReady;
    private Func<FalloutActorSandboxMotionFailure>? _sandboxMotionCapture;
    private string? SandboxMotionError => _sandboxMotionException?.Message ?? _restoredSandboxMotion?.Error;

    private void BindSandboxMotionFailureCapture()
    {
        _aiState!.CanCaptureSandboxMotionFailure = _sandboxMotionReady = CanCaptureSandboxMotionFailure;
        _aiState.CaptureSandboxMotionFailure = _sandboxMotionCapture = CaptureSandboxMotionFailure;
    }

    private bool CanCaptureSandboxMotionFailure() => IsInsideTree() && !_sourceBindingsRetired &&
        (_restoredSandboxMotion is null || _sandboxMotionPosePublished) && SandboxMotionError is { } error &&
        _aiError == error && _failedPackage == _aiPackage?.FormKey && _sandboxPackage?.Form == _aiPackage?.FormKey &&
        _aiState is { } state && state.ProcedureCaptureBlocker == FalloutSandboxPackage.CaptureBlockerPrefix + error &&
        Error is null && state.ScriptError is null && !ScriptIdleActive && _eventIdleAnimation is null && _eventIdleClock is null &&
        _conversationTarget is null && state.PackageIdle is null && !Activity.InCombat &&
        Combat is { Dead: false, OwnsPose: false, PackageMoving: false, PackageOwnsPose: false, StoppedAiPoseCaptureReady: true } &&
        !state.Unconscious && !state.Restrained && !state.KnockedDown &&
        state.Engagement is null && state.PackageBindingFailure is null && state.SandboxAdmissionFailure is null &&
        state.SelectionFailure is null && state.PendingPackageSelection is null && state.StoppedRetirement is null &&
        state.FurnitureContinuation is null && state.DialogueContinuation is null && state.DeferredPackageContinuation is null &&
        _packageEvents is { Active: { } active, Done: false, Error: null, LastEvent: "POBA" } events &&
        active.Form == _aiPackage?.FormKey && events.LastPackage == active.Form &&
        _initialPackageSelected && _packageSelectionAttempted && _clock.Capture() is not null &&
        OpenNV.Runtime.SceneGraph.NodeTraversal.SelfAndDescendants<NativeOwnedAnimationSoundPlayer>(this)
            .All(sound => sound.CanCaptureSilent);

    private FalloutActorSandboxMotionFailure CaptureSandboxMotionFailure()
    {
        if (!CanCaptureSandboxMotionFailure())
            throw new NotSupportedException("Stopped Sandbox motion still has an independent continuation owner.");
        var state = _aiState!;
        var records = _aiRecords ?? throw new InvalidOperationException("Stopped Sandbox has no source records.");
        var package = _aiPackage ?? throw new InvalidOperationException("Stopped Sandbox has no source package.");
        var events = _packageEvents ?? throw new InvalidOperationException("Stopped Sandbox has no source lifecycle.");
        var prefix = _sandboxMotionException?.Prefix ?? _restoredSandboxMotion!.Prefix;
        var position = GlobalPosition; var basis = GlobalBasis; var velocity = Velocity;
        var bones = new List<FalloutActorResidualBonePose>();
        for (var index = 0; index < Skeleton.Node.GetBoneCount(); ++index)
        {
            var p = Skeleton.Node.GetBonePosePosition(index);
            var q = Skeleton.Node.GetBonePoseRotation(index);
            var s = Skeleton.Node.GetBonePoseScale(index);
            bones.Add(new(index, Skeleton.Node.GetBoneName(index).ToString(),
                [p.X, p.Y, p.Z], [q.X, q.Y, q.Z, q.W], [s.X, s.Y, s.Z]));
        }
        var result = new FalloutActorSandboxMotionFailure(state.Reference, state.Base,
            FalloutActorFurnitureContinuation.RecordHash(records.GetEffective(state.Base)), Appearance.ModelOwner,
            FalloutActorFurnitureContinuation.RecordHash(records.GetEffective(Appearance.ModelOwner)),
            package.FormKey, FalloutActorFurnitureContinuation.RecordHash(package), SandboxMotionError!, prefix.Copy(),
            [position.X, position.Y, position.Z], [basis.X.X, basis.X.Y, basis.X.Z,
                basis.Y.X, basis.Y.Y, basis.Y.Z, basis.Z.X, basis.Z.Y, basis.Z.Z], [velocity.X, velocity.Y, velocity.Z],
            new(Appearance.SkeletonPath, Skeleton.Source.Sha256, bones), _clock.Capture()!, state.PackageMotion,
            state.SoundRandom.State, Activity.Capture(), Math.Max(0, _packageClock), _aiScheduleTime,
            _aiQuestRevision, _packageSelectionAttempts, events.Revision, events.LastEvent!, events.LastPackage!.Value);
        result.Validate(records, state);
        _aiWorld!.ValidatePackageWander(state.Reference, result.Prefix.Wander);
        return result.Copy();
    }

    private void RestoreSandboxMotionFailure(FalloutActorSandboxMotionFailure saved)
    {
        var state = _aiState!;
        var records = _aiRecords ?? throw new InvalidOperationException("Stopped Sandbox has no source records.");
        saved.Validate(records, state);
        ValidateSandboxMotionPose(saved);
        if (_clock.Capture() != saved.Animation || state.SoundRandom.State != saved.SoundRandomState)
            throw new InvalidDataException("Stopped Sandbox restoration requires its exact clock and randomness.");
        _aiWorld!.ValidatePackageWander(state.Reference, saved.Prefix.Wander);
        var package = records.GetEffective(saved.Package);
        var sandbox = FalloutSandboxPackage.Read(package);
        // Restore the consumed lifecycle without invoking POBA or selecting an
        // activity/destination. The first physics step remains visibly faulted.
        if (_packageEvents is { Active: null, Revision: 0, Error: null, LastEvent: null, LastPackage: null })
            saved.RestoreEvents(records, state, _packageEvents);
        if (_packageEvents is not { Active: { } active, Done: false, Error: null } events || active.Form != saved.Package ||
            events.Revision != saved.EventRevision || events.LastEvent != saved.LastEvent || events.LastPackage != saved.LastPackage)
            throw new InvalidDataException("Stopped Sandbox source event history was not restored exactly.");
        Activity.Restore(saved.Activity);
        _restoredSandboxMotion = saved.Copy();
        _aiPackage = package; _sandboxPackage = sandbox;
        _sandboxWander = saved.Prefix.Copy().Wander;
        _aiError = saved.Error; _failedPackage = saved.Package;
        _initialPackageSelected = true; _packageSelectionAttempted = true;
        _packageSelectionAttempts = saved.SelectionAttempts;
        _evaluateRequested = false; _packageClock = saved.PollRemaining;
        _aiScheduleTime = saved.ScheduleTime; _aiQuestRevision = saved.QuestRevision;
        state.ProcedureCaptureBlocker = saved.CaptureBlocker;
        if (IsNodeReady()) PublishSandboxMotionPose();
    }

    private void ValidateSandboxMotionPose(FalloutActorSandboxMotionFailure saved)
    {
        saved.Pose.ValidateBinding(Appearance.SkeletonPath, Skeleton.Source.Sha256,
            Enumerable.Range(0, Skeleton.Node.GetBoneCount()).Select(index =>
                (Skeleton.Node.GetBoneName(index).ToString(), FalloutNifTransformComponents.None)).ToArray());
    }

    public override void _Ready()
    {
        PublishSandboxMotionPose();
        PublishScriptIdlePose();
    }

    private void PublishSandboxMotionPose()
    {
        if (_restoredSandboxMotion is not { } saved || _sandboxMotionPosePublished) return;
        ValidateSandboxMotionPose(saved);
        var p = saved.Position; var b = saved.Basis; var v = saved.Velocity;
        GlobalTransform = new(new Basis(new Vector3(b[0], b[1], b[2]), new Vector3(b[3], b[4], b[5]),
            new Vector3(b[6], b[7], b[8])), new(p[0], p[1], p[2]));
        Velocity = new(v[0], v[1], v[2]);
        foreach (var bone in saved.Pose.Bones)
        {
            var position = bone.Position!; var rotation = bone.Rotation!; var scale = bone.Scale!;
            Skeleton.Node.SetBonePosePosition(bone.Index, new(position[0], position[1], position[2]));
            Skeleton.Node.SetBonePoseRotation(bone.Index, new(rotation[0], rotation[1], rotation[2], rotation[3]));
            Skeleton.Node.SetBonePoseScale(bone.Index, new(scale[0], scale[1], scale[2]));
        }
        _sandboxMotionPosePublished = true;
    }

    private void RetireSandboxMotionFailureCapture()
    {
        if (_aiState is not { } state || !ReferenceEquals(state.CaptureSandboxMotionFailure, _sandboxMotionCapture)) return;
        try
        {
            if (SandboxMotionError is not null)
            {
                // Refuse before removing the last live pose owner. A stale
                // earlier receipt must never make a failed retirement saveable.
                state.SandboxMotionFailure = null;
                state.SandboxMotionFailure = CaptureSandboxMotionFailure();
            }
        }
        finally
        {
            state.CaptureSandboxMotionFailure = null;
            if (ReferenceEquals(state.CanCaptureSandboxMotionFailure, _sandboxMotionReady))
                state.CanCaptureSandboxMotionFailure = null;
        }
    }
}
