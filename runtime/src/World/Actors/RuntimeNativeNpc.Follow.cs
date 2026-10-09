using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Actors;

internal partial class RuntimeNativeNpc : IRuntimeNativeFollower
{
    private FalloutFollowPackage? _followPackage;
    private PackageSelection? _restoredFollowSelection;
    private Func<FalloutActorPackageMotion?>? _followMotionCapture;
    private bool _followTargetObserved;
    internal bool FollowingPlayer => _aiReferenceState?.PlayerTeammate == true && _aiError is null &&
        _packageEvents is { Error: null, Done: false } && _aiPackage?.FormKey == _followPackage?.Form &&
        _followPackage is not null && _followPackage.Target == _aiStack?.RuntimeFormKey(0x14) &&
        _aiWorld?.IsEnabled(Appearance.Reference!.Value) == true && Combat?.Dead == false && !_aiReferenceState.Restrained;
    CharacterBody3D IRuntimeNativeFollower.FollowerBody => this;
    RuntimeNativeActorCombat IRuntimeNativeFollower.FollowerCombat => Combat ?? throw new NotSupportedException("Follower has no real native combat/body owner.");
    FalloutFormKey IRuntimeNativeFollower.FollowerReference => Appearance.Reference!.Value;
    bool IRuntimeNativeFollower.FollowingPlayer => FollowingPlayer;
    private object? FollowState => _followPackage is null ? null : new
    {
        source = _followPackage,
        targetObserved = _followTargetObserved,
        progress = _aiReferenceState?.PackageMotion?.Follow,
        owner = "actual-source-actor-native-capsule-KF",
        unowned = new[] { "non-reference-targets", "start-end-locations", "entered-route-door-cold", "active-native-search-cold", "retail-cadence-and-pace" }
    };

    private void RestoreFollowLifecycleBeforeSelection()
    {
        if (_aiReferenceState is not { PackageMotion: { Follow: { } progress } motion } state ||
            state.PackageAssignment is not { } assignment || motion.Package != assignment.Package) return;
        if (state.ScriptPackage?.Pending == true || state.PendingPackageChoice is not null || state.PendingPackageSelection is not null)
            throw new NotSupportedException("Cold Follow has a different entered package election owner.");
        var records = _aiStack ?? throw new NotSupportedException("Cold Follow has no actual selected records.");
        var record = records.GetEffective(assignment.Package);
        var follow = FalloutFollowPackage.Read(record);
        follow.ValidateContinuation(records, state.Reference, progress);
        if (assignment.Done || _packageEvents?.Error is not null)
            throw new InvalidDataException("Continuous Follow cannot restore a completed or failed package lifecycle.");
        if (_packageEvents is { Active: null }) assignment.Bind(records, _packageEvents);
        if (_packageEvents?.Active?.Form != assignment.Package)
            throw new InvalidDataException("Cold Follow differs from its retained original package event owner.");
        progress.Election!.RestoreHistory(_packageEvents!);
        _restoredFollowSelection = new(record, FalloutScriptPackage.Read(record), assignment.ScriptPackageRevision,
            state.ScriptPackage?.Package == record.FormKey);
    }

    private void BindFollowMotionCapture()
    {
        if (_aiReferenceState is not { } state) return;
        state.CapturePackageMotion = _followMotionCapture = CaptureFollowMotion;
    }

    private FalloutActorPackageMotion? CaptureFollowMotion()
    {
        if (_followPackage is null) return _aiReferenceState!.PackageMotion;
        if (_requestedSelection is not null || _aiReferenceState!.PendingPackageChoice is not null ||
            _aiReferenceState.ScriptPackage?.Pending == true || _packageEvents is not { Error: null, Done: false } lifecycle)
            throw new NotSupportedException("Follow has an entered package election or failed lifecycle suffix.");
        return (Combat ?? throw new NotSupportedException("Follow has no actual native motion capture owner."))
            .CaptureFollowMotion(_followPackage, new(_aiPollRemaining, _aiScheduleTime, _aiQuestRevision,
                _aiActivityRevision, false, Activity.Capture(), lifecycle.Revision, lifecycle.LastEvent, lifecycle.LastPackage));
    }

    private void BeginFollow(FalloutPluginRecord record, bool initializing)
    {
        var follow = FalloutFollowPackage.Read(record);
        follow.RequireActorTarget(_aiStack!, Appearance.Reference!.Value);
        var retained = _aiReferenceState?.PackageMotion;
        var progress = initializing && retained?.Package == record.FormKey ? retained.Follow : null;
        if (progress is not null) follow.ValidateContinuation(_aiStack!, Appearance.Reference!.Value, progress);
        // The event owner commits before native target observation. A failed
        // native suffix cannot be presented as a successful start or replay.
        _aiPackage = record; _followPackage = follow; _followTargetObserved = false;
        if (progress is null)
        {
            _packageEvents!.Change(_packageIdleSource);
            (Combat ?? throw new NotSupportedException("Follow has no actual native body owner.")).BeginFollowObservation();
        }
        else
        {
            var election = progress.Election!;
            Activity.Restore(election.Activity);
            _aiPollRemaining = election.PollRemaining; _aiScheduleTime = election.ScheduleTime;
            _aiQuestRevision = election.QuestRevision; _aiActivityRevision = election.ActivityRevision ?? -1;
        }
        GD.Print($"OPENNV_NATIVE_FOLLOW_BOUND reference={Appearance.Reference} package={record.FormKey} cold={progress is not null}");
    }

    private void AdvanceFollow(double delta)
    {
        if (_followPackage is not { } follow || Combat is null || _aiError is not null || Combat.OwnsPose ||
            !Combat.PackageMovementReady || _conversationTarget is not null) return;
        try { _followTargetObserved = Combat.AdvanceFollowPackageMotion(_aiPackage!, follow, delta); }
        catch (Exception error)
        {
            Combat.StopPackageMotion(); _aiError = error.Message; _failedPackage = _aiPackage?.FormKey;
            _aiReferenceState!.ProcedureCaptureBlocker ??= "Follow native suffix failed: " + error.Message;
            GD.PushError($"OPENNV_NATIVE_FOLLOW_DIVERGENCE reference={Appearance.Reference}: {_aiError}");
        }
    }

    private void ClearFollow()
    {
        if (_followPackage is null) return;
        Combat?.RetireFollowRoute(); _followPackage = null; _followTargetObserved = false;
    }

    private void RetainFollowMotion()
    {
        if (_aiReferenceState is not { } state || !ReferenceEquals(state.CapturePackageMotion, _followMotionCapture)) return;
        try { state.PackageMotion = _followMotionCapture!(); }
        catch (Exception error)
        {
            state.ProcedureCaptureBlocker ??= "Follow eviction has no complete native continuation: " + error.Message;
            GD.PushError($"OPENNV_NATIVE_FOLLOW_RETIREMENT_REFUSED reference={state.Reference}: {error.Message}");
        }
        finally { state.CapturePackageMotion = null; }
    }
}
