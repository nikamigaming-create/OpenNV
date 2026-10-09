using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Actors;

internal sealed partial class RuntimeNativeCreature : IRuntimeNativeFollower
{
    private Func<FalloutActorPackageMotion?>? _followMotionCapture;
    CharacterBody3D IRuntimeNativeFollower.FollowerBody => this;
    RuntimeNativeActorCombat IRuntimeNativeFollower.FollowerCombat => Combat ?? throw new NotSupportedException("Follower has no real native combat/body owner.");
    FalloutFormKey IRuntimeNativeFollower.FollowerReference => Appearance.Reference!.Value;
    bool IRuntimeNativeFollower.FollowingPlayer => FollowingPlayer;

    private void BindFollowMotionCapture()
    {
        if (_aiState is not { } state) return;
        state.CapturePackageMotion = _followMotionCapture = CaptureFollowMotion;
    }

    private FalloutActorPackageMotion? CaptureFollowMotion()
    {
        if (_followPackage is null) return _aiState!.PackageMotion;
        if (_aiState!.PendingPackageChoice is not null || _aiState.ScriptPackage?.Pending == true ||
            _packageEvents is not { Error: null, Done: false } lifecycle)
            throw new NotSupportedException("Follow has an entered package election or failed lifecycle suffix.");
        return (Combat ?? throw new NotSupportedException("Follow has no actual native motion capture owner."))
            .CaptureFollowMotion(_followPackage, new(_packageClock, _aiScheduleTime, _aiQuestRevision,
                null, _evaluateRequested, Activity.Capture(), lifecycle.Revision, lifecycle.LastEvent, lifecycle.LastPackage));
    }

    private void RestoreFollowLifecycleBeforeSelection()
    {
        if (_aiState is not { PackageMotion: { Follow: { } progress } motion, PackageAssignment: { } assignment } state ||
            motion.Package != assignment.Package) return;
        if (state.ScriptPackage?.Pending == true || state.PendingPackageChoice is not null)
            throw new NotSupportedException("Cold Follow has a different entered package election owner.");
        var records = _aiRecords ?? throw new NotSupportedException("Cold Follow has no actual selected records.");
        var record = records.GetEffective(assignment.Package);
        FalloutFollowPackage.Read(record).ValidateContinuation(records, state.Reference, progress);
        if (assignment.Done || _packageEvents?.Error is not null)
            throw new InvalidDataException("Continuous Follow cannot restore a completed or failed package lifecycle.");
        if (_packageEvents is { Active: null }) assignment.Bind(records, _packageEvents);
        if (_packageEvents?.Active?.Form != assignment.Package)
            throw new InvalidDataException("Cold Follow differs from its original package event owner.");
        progress.Election!.RestoreHistory(_packageEvents!);
    }

    private void RestoreFollowElection(FalloutFollowProgress progress, double elapsed)
    {
        var election = progress.Election ?? throw new InvalidDataException("Saved Follow has no actual election clock.");
        Activity.Restore(election.Activity);
        _packageClock = election.PollRemaining - elapsed; _aiScheduleTime = election.ScheduleTime;
        _aiQuestRevision = election.QuestRevision; _evaluateRequested = election.EvaluateRequested;
    }

    private void RetainFollowMotion()
    {
        if (_aiState is not { } state || !ReferenceEquals(state.CapturePackageMotion, _followMotionCapture)) return;
        try { state.PackageMotion = _followMotionCapture!(); }
        catch (Exception error)
        {
            state.ProcedureCaptureBlocker ??= "Follow eviction has no complete native continuation: " + error.Message;
            GD.PushError($"OPENNV_NATIVE_FOLLOW_RETIREMENT_REFUSED reference={state.Reference}: {error.Message}");
        }
        finally { state.CapturePackageMotion = null; }
    }
}
