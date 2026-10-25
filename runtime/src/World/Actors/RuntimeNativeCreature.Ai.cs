using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.World.Actors;

internal sealed partial class RuntimeNativeCreature
{
    private const string DialogueCaptureBlocker = "Dialogue package continuation has no cold restoration owner.";
    private FalloutPluginStack? _aiRecords;
    private FalloutQuestState? _aiQuests;
    private FalloutGameTime? _aiClock;
    private FalloutGlobalState? _aiGlobals;
    private FalloutFormKey? _failedPackage;
    private FalloutScheduleTime? _aiScheduleTime;
    private FalloutReferenceWorld? _aiWorld;
    private FalloutReferenceInstance? _aiState;
    private Func<FalloutFormKey?>? _currentPackageQuery;
    private Func<int>? _sittingQuery;
    private Func<FalloutActorPackageAssignment?>? _packageAssignmentCapture;
    private FalloutPluginRecord? _aiPackage;
    private FalloutFollowPackage? _followPackage;
    private FalloutGuardPackage? _guardPackage;
    private FalloutTravelProgress? _guardProgress;
    private FalloutSandboxPackage? _sandboxPackage;
    private FalloutWanderProgress? _sandboxWander;
    private FalloutDialoguePackage? _dialoguePackage;
    private FalloutPackageEvents? _packageEvents;
    private bool _dialogueRequested;
    private bool _initialPackageSelected;
    private bool _packageSelectionAttempted;
    private long _packageSelectionAttempts;
    private bool _evaluateRequested = true;
    private double _packageClock;
    private long _aiQuestRevision = -1;
    private string? _aiError;
    private FalloutFormKey? _waitingForTarget;
    internal Action<FalloutDialoguePackage, Action>? BeginPackageDialogue { get; set; }
    internal bool FollowingPlayer => _aiState?.PlayerTeammate == true && _aiError is null &&
        _followPackage?.Target == _aiRecords?.RuntimeFormKey(0x14) && Combat?.Dead == false && !_aiState.Restrained;
    internal object AiState => new
    {
        package = _aiPackage?.FormKey.ToString(),
        failedSelection = _failedPackage?.ToString(),
        selectionAttempts = _packageSelectionAttempts,
        follow = _followPackage,
        accompanyPortal = new
        {
            door = _accompanyDoor?.ToString(),
            requested = _accompanyPortalRequested,
            transferred = _accompanyPortalTransferred,
            wait = _accompanyPortalWait
        },
        dialogue = _dialoguePackage,
        dialogueRequested = _dialogueRequested,
        motion = _aiState?.PackageMotion,
        travel = TravelState,
        guard = _guardPackage is null ? null : new { source = _guardPackage, progress = _guardProgress },
        sandbox = _sandboxPackage is null ? null : new { source = _sandboxPackage, wander = _sandboxWander, dialogue = Combat?.SandboxObservation },
        patrol = PatrolState,
        eventIdle = _aiState?.PackageIdle,
        packageStarts = _aiState?.PackageStarts,
        packageEvents = _packageEvents is null ? null : new
        {
            _packageEvents.Done,
            _packageEvents.Revision,
            _packageEvents.LastEvent,
            _packageEvents.Error
        },
        waitingForTarget = _waitingForTarget?.ToString(),
        error = _aiError,
        scheduleTime = _aiScheduleTime,
        evaluationPolicy = "hour-changes-and-ten-second-poll;retail-cadence-unmatched"
    };

    internal void ConfigureAi(FalloutPluginStack records, FalloutQuestState quests, FalloutReferenceWorld world,
        FalloutGameTime? clock = null, FalloutGlobalState? globals = null,
        Func<FalloutFormKey, FalloutFormKey, double>? itemCount = null)
    {
        _aiRecords = records; _aiQuests = quests; _aiWorld = world;
        _aiClock = clock; _aiGlobals = globals;
        _aiItemCount = itemCount;
        _aiState = world.Get(Appearance.Reference!.Value);
        world.BindActorAlert(Appearance.Reference!.Value, Activity);
        _aiState.QueryCurrentPackage = _currentPackageQuery = () => _aiPackage?.FormKey;
        _aiState.QuerySitting = _sittingQuery = () => _aiState.FurnitureContinuation is null ? 0 :
            throw new NotSupportedException("Creature furniture continuation has no physical sitting owner.");
        _packageEvents = new(DispatchPackageEvent);
        if (_aiState.SandboxAdmissionFailure is { } stoppedAdmission)
        {
            world.UnloadedPackages?.BindNative(Appearance.Reference!.Value, _packageEvents);
            RestoreSandboxAdmissionFailure(stoppedAdmission);
        }
        else if (_aiState.SandboxMotionFailure is { } stoppedSandbox)
        {
            world.UnloadedPackages?.BindNative(Appearance.Reference!.Value, _packageEvents);
            RestoreSandboxMotionFailure(stoppedSandbox);
        }
        else if (world.UnloadedPackages is { } unloaded)
            unloaded.BindNative(Appearance.Reference!.Value, _packageEvents);
        else if (_aiState.DeferredPackageContinuation is { } deferred)
            deferred.BindNative(records, _aiState, _packageEvents);
        _aiState.CapturePackageAssignment = _packageAssignmentCapture = () => _initialPackageSelected
            ? FalloutActorPackageAssignment.Capture(records, _packageEvents) : _aiState.PackageAssignment;
        RestoreEventIdle();
        RestoreScriptIdle();
        BindScriptIdleCapture();
        BindSandboxMotionFailureCapture();
        BindSandboxAdmissionCapture();
        _sandboxAdmissionConfigurationComplete = true;
    }

    internal void EvaluatePackages(bool reset)
    {
        if (_aiRecords is null) throw new NotSupportedException("Creature has no package owner.");
        if (_aiState?.ScriptError is { } error) throw new NotSupportedException(error);
        if (SandboxMotionError is { } stopped) throw new NotSupportedException(stopped);
        if (SandboxAdmissionError is { } admission) throw new NotSupportedException(admission);
        _aiError = null;
        // Script evaluation may precede native enable/materialization in the
        // same source event. Evaluate on the next resident physics step.
        _evaluateRequested = true;
    }

    internal void ResumePackagesAfterCombat()
    {
        // An external EVP still reports a retained source refusal. Ending an
        // unrelated engagement returns to that stopped owner without replaying
        // selection or promoting its existing failure into a combat failure.
        if (SandboxMotionError is not null || SandboxAdmissionError is not null) return;
        EvaluatePackages(false);
    }

    private bool _sourceBindingsRetired;
    public override void _ExitTree() => RetireSourceBindings();

    internal void RetireSourceBindings()
    {
        if (_sourceBindingsRetired) return;
        Exception? retirementFailure = null;
        var detachedAdmission = HasDetachedSandboxAdmission;
        if (detachedAdmission) ReleaseDetachedSandboxAdmissionBindings();
        else
        {
            try { RetireScriptIdleCapture(); }
            catch (Exception error) { retirementFailure = error; }
            try { RetireSandboxMotionFailureCapture(); }
            catch (Exception error) { retirementFailure ??= error; }
            try { RetireSandboxAdmissionCapture(); }
            catch (Exception error) { retirementFailure ??= error; }
        }
        _sourceBindingsRetired = true;
        if (_aiState is { } state && ReferenceEquals(state.QueryCurrentPackage, _currentPackageQuery))
        {
            try
            {
                if (!detachedAdmission && _packageEvents is not null)
                    _aiWorld?.UnloadedPackages?.Retain(Appearance.Reference!.Value, _packageEvents);
            }
            catch (Exception error) { retirementFailure ??= error; }
            finally
            {
                state.QueryCurrentPackage = null;
                if (ReferenceEquals(state.QuerySitting, _sittingQuery)) state.QuerySitting = null;
                if (ReferenceEquals(state.CapturePackageAssignment, _packageAssignmentCapture)) state.CapturePackageAssignment = null;
            }
        }
        if (retirementFailure is not null)
            throw new InvalidOperationException("Creature source retirement could not retain its continuation.", retirementFailure);
    }

    internal void RestoreSourceBindings()
    {
        if (!IsInsideTree() || _sourceBindingsRetired || _aiState is not { } state ||
            state.QueryCurrentPackage is not null && !ReferenceEquals(state.QueryCurrentPackage, _currentPackageQuery)) return;
        state.QueryCurrentPackage ??= _currentPackageQuery;
        state.QuerySitting ??= _sittingQuery;
        state.CapturePackageAssignment ??= _packageAssignmentCapture;
        RebindSandboxAdmissionCapture();
        Combat?.RestoreSourceBindings();
    }

    private Func<FalloutFormKey, FalloutFormKey, double>? _aiItemCount;

    internal float PackageCondition(FalloutCondition condition) => condition.Function == 382
        ? (_aiWorld ?? throw new NotSupportedException("Creature note query has no shared world owner."))
            .EvaluatePlayerNoteCondition(condition)!.Value
        : condition.Function is 573 or 574 or 575
        ? (_aiWorld ?? throw new NotSupportedException("Creature reputation query has no shared world owner."))
            .EvaluateReputationCondition(condition)!.Value
        : condition.Function == 159
        ? FalloutAiPackages.Sitting(_aiRecords ?? throw new NotSupportedException("Creature sitting query has no source owner."),
            condition, Appearance.Reference!.Value, PackageSitting)
        : condition.Function == 47
        ? OpenNV.Runtime.Gameplay.State.FalloutActorInventoryConditions.ItemCount(condition, Appearance.Reference!.Value, _aiItemCount)
        : condition.Function is 1 or 14 or 32
        ? ReferenceCondition(condition)
        : condition.RunOn != 0 && condition.Function is not (50 or 84 or 161 or 289)
        ? throw new NotSupportedException($"Creature package condition {condition.Owner.FormKey}/{condition.Function}/{condition.RunOn} has no subject owner.")
        : condition.Function switch
        {
            25 => Combat?.PackageMoving == true ? 1 : 0,
            161 => FalloutAiPackages.IsCurrentPackage(condition, Appearance.Reference!.Value, _aiPackage?.FormKey,
                reference => _aiWorld!.CurrentPackage(reference)) ? 1 : 0,
            18 => (_aiClock ?? throw new NotSupportedException("Creature time query has no simulation clock.")).Hour,
            74 => (_aiGlobals ?? throw new NotSupportedException("Creature global query has no state owner.")).Get(condition.FormArgument1),
            84 => (_aiWorld ?? throw new NotSupportedException("Creature death count has no shared death history."))
                .GetDeadCount(condition.FormArgument1),
            71 => _aiWorld!.ActorFactions(Appearance.Reference!.Value).GetValueOrDefault(condition.FormArgument1, (sbyte)-1) >= 0 ? 1 : 0,
            73 => _aiWorld!.ActorFactions(Appearance.Reference!.Value).GetValueOrDefault(condition.FormArgument1, (sbyte)-1),
            56 => FalloutAiPackages.QuestRunning(condition, _aiQuests!),
            58 or 59 or 79 or 420 or 421 or 546 => _aiQuests!.Evaluate(condition),
            35 => _aiWorld!.IsEnabled(Appearance.Reference!.Value) ? 0 : 1,
            36 when condition.Argument1 == 0 => Combat!.PackagePlayer?.ModalInput == true ? 1 : 0,
            50 => FalloutAiPackages.HasTalkedToPlayer(condition, Appearance.Reference!.Value,
                reference => _aiWorld!.Get(reference).TalkedToPlayer) ? 1 : 0,
            53 => (float)_aiWorld!.ReadVariable(_aiQuests!, condition.FormArgument1, condition.Argument2),
            63 => Activity.Attacked ? 1 : 0,
            72 => Appearance.Creature == condition.FormArgument1 ? 1 : 0,
            91 => Activity.Alerted ? 1 : 0,
            101 => Activity.WeaponDrawn ? 1 : 0,
            107 => Combat?.KnockedDown == true ? 2 : 0,
            110 => Combat?.ScriptCombatPackage ?? (_aiPackage is null ? 0 : _sandboxPackage is not null ? 36 :
                throw new NotSupportedException("Creature current package code needs its active procedure owner.")),
            182 => (Combat ?? throw new NotSupportedException("Creature equipment query has no live actor owner."))
                .PackageIsEquipped(condition.FormArgument1) ? 1 : 0,
            244 => _aiState!.Restrained ? 1 : 0,
            286 => Activity.Sneaking ? 1 : 0,
            287 => Activity.Running ? 1 : 0,
            289 => (Combat ?? throw new NotSupportedException("Creature combat query has no engagement owner."))
                .IsInCombat(FalloutAiPackages.ConditionSubject(condition, Appearance.Reference!.Value)) ? 1 : 0,
            300 when condition.RunOn == 0 => _aiWorld!.IsInInterior(Appearance.Reference!.Value) ? 1 : 0,
            _ => throw new NotSupportedException($"Creature package condition {condition.Owner.FormKey}/{condition.Function} is unbound.")
        };

    private int PackageSitting(FalloutFormKey subject)
    {
        var records = _aiRecords ?? throw new NotSupportedException("Creature sitting query has no source owner.");
        return records.RuntimeFormId(subject) == 0x14
            ? (Combat?.PackagePlayer ?? throw new NotSupportedException("Creature player sitting query has no native furniture owner.")).SittingState
            : (_aiWorld ?? throw new NotSupportedException("Creature sitting query has no shared reference world.")).GetSitting(subject);
    }

    private float ReferenceCondition(FalloutCondition condition)
    {
        FalloutReferencePlacement? placement = null;
        if (Combat?.PackagePlayer is { } player && Combat.PackagePlayerCell is { } cell)
        {
            var position = player.GlobalPosition / Skeleton.UnitsToMetres;
            placement = new(cell, [position.X, -position.Z, position.Y], [0, 0, 0]);
        }
        return _aiWorld!.EvaluateActorReferenceCondition(Appearance.Reference!.Value, condition, placement, Skeleton.UnitsToMetres)!.Value;
    }

    private void SelectPackage()
    {
        var previousFailure = _failedPackage;
        _failedPackage = null;
        var retained = !_initialPackageSelected ? _aiState!.PackageMotion : null;
        var assignedMotion = retained is not null && _aiState!.PackageAssignment?.Package == retained.Package &&
            _aiState.DeferredPackageContinuation?.RetainedAssignment is null;
        var restoreTravel = assignedMotion && retained!.Travel is not null;
        var restoreGuard = assignedMotion && retained!.Guard is not null;
        var restoreSandbox = assignedMotion && (retained!.Wander is not null || retained.Sandbox is not null);
        var restoreAccompany = assignedMotion && retained!.AccompanyPortal is not null;
        // A retired package may retain its last physical pose. That motion is
        // not an active assignment and must not restart its procedure on load.
        var restorePatrol = assignedMotion && retained!.Patrol is not null;
        var deferred = !_initialPackageSelected && _aiState!.DeferredPackageContinuation?.RetainedAssignment is not null
            ? _aiState.DeferredPackageContinuation : null;
        var selected = deferred is not null ? _aiRecords!.GetEffective(deferred.Assignment.Package) :
            restoreTravel || restoreGuard || restoreAccompany || restorePatrol || restoreSandbox ? _aiRecords!.GetEffective(retained!.Package) :
            FalloutAiPackages.Select(_aiRecords!, Appearance.Creature, PackageCondition, _aiState!.Templates, _aiClock,
                evaluateRunOn: true,
                eligible: package => _aiWorld!.PackageEligible(Appearance.Reference!.Value, package, _aiClock,
                    _aiPackage?.FormKey, _packageEvents?.Done == true));
        if (_aiError is not null && selected is not null && previousFailure == selected.FormKey)
        { _failedPackage = previousFailure; return; }
        _aiError = null;
        if (_aiPackage?.FormKey == selected?.FormKey)
        {
            if (_patrol is { } route && _patrolProgress?.Complete == true && _packageEvents?.Done == true)
            {
                _patrolProgress = route.Start(_ => 0); Combat!.SetPatrolProgress(_patrolProgress);
                _packageEvents.Change(null); _packageEvents.Change(FalloutScriptPackage.Read(selected!));
            }
            return;
        }
        _failedPackage = selected?.FormKey;
        var source = selected is null ? null : FalloutScriptPackage.Read(selected);
        FalloutFollowPackage? follow = null;
        FalloutDialoguePackage? dialogue = null;
        FalloutTravelPackage? travel = null;
        FalloutGuardPackage? guard = null;
        FalloutSandboxPackage? sandbox = null;
        FalloutPatrolRoute? patrol = null;
        if (source is not null)
        {
            if (source.Procedure == 1) follow = FalloutFollowPackage.Read(selected!);
            else if (source.Procedure == 7)
            {
                follow = FalloutFollowPackage.ReadAccompany(selected!);
                if (follow.DefensiveCombat && _aiRecords!.RuntimeFormId(follow.Target) == 0x14)
                    throw new NotSupportedException("Defensive Accompany of the player requires its player-hit assistance owner.");
            }
            else if (source.Procedure == 6) travel = FalloutTravelPackage.Read(selected!);
            else if (source.Procedure == 14) guard = FalloutGuardPackage.Read(selected!);
            else if (source.Procedure == 12)
            {
                sandbox = FalloutSandboxPackage.Read(selected!);
                if (sandbox.ContinueIfPlayerNear)
                    throw new NotSupportedException("Creature Sandbox requires its continuing-selection owner.");
            }
            else if (source.Procedure == 13) patrol = ReadPatrol(selected!);
            else if (source.Procedure == 15)
            {
                dialogue = FalloutDialoguePackage.Read(selected!);
                if (source.LocationType is not null || selected!.ReadSubrecords().Any(field => field.Signature == "PLD2"))
                    throw new NotSupportedException("Creature dialogue start/end location is unbound.");
            }
            else throw new NotSupportedException($"Creature package {source.Form} procedure {source.Procedure} is unbound.");
        }
        var restoredDialogue = assignedMotion && dialogue?.Type == 1 && retained is { DialogueCompleted: true } motion && motion.Package == source!.Form;
        if (travel is not null) BeginTravel(selected!, restoreTravel);
        else { _travelPackage = null; _travelProgress = null; _travelDestination = null; }
        if (guard is not null)
        {
            var progress = restoreGuard ? retained!.Guard! : guard.Start(_aiRecords!, _aiWorld!, Appearance.Reference!.Value);
            guard.Validate(_aiRecords!, _aiWorld!, Appearance.Reference!.Value, progress);
            if (progress.Cell != _aiWorld!.Placement(Appearance.Reference!.Value).Cell)
                throw new NotSupportedException("Guard requires its other-cell route owner.");
            _guardPackage = guard; _guardProgress = progress;
            _aiState!.ProcedureCaptureBlocker = restoreGuard ? null : "Guard approach has no first native motion observation.";
        }
        else { _guardPackage = null; _guardProgress = null; }
        _sandboxPackage = sandbox;
        _sandboxWander = sandbox is not null && restoreSandbox ? retained!.Wander : null;
        if (_sandboxWander is not null) _aiWorld!.ValidatePackageWander(Appearance.Reference!.Value, _sandboxWander);
        if (patrol is not null) BeginPatrol(patrol, restorePatrol);
        else { _patrol = null; _patrolProgress = null; _patrolDestination = null; }
        _initialPackageSelected = true;
        var restoreFollow = assignedMotion && follow is not null && retained!.Package == source!.Form &&
            _aiState!.PackageAssignment is { } followAssignment && followAssignment.Package == source.Form;
        if (restoreTravel || restoreGuard || restoreAccompany || restoreFollow || restorePatrol || restoreSandbox || restoredDialogue)
            _packageEvents!.Restore(source!, restoreFollow ? _aiState!.PackageAssignment!.Done :
                restorePatrol ? _aiState!.PackageAssignment?.Done == true :
                restoreGuard || restoreAccompany || restoreSandbox ? false : restoreTravel ? _travelProgress!.Complete : true);
        else _packageEvents!.Change(source);
        _aiPackage = selected; _followPackage = follow; _dialoguePackage = dialogue; _dialogueRequested = restoredDialogue;
        _accompanyDoor = null; _accompanyDoorDestination = null; _accompanyTargetRegion = null;
        _accompanyPortalRequested = false; _accompanyPortalTransferred = false; _accompanyPortalWait = null;
        if (travel is null && guard is null && patrol is null)
            _aiState!.ProcedureCaptureBlocker = dialogue is not null && !restoredDialogue ? DialogueCaptureBlocker : null;
        if (sandbox is not null && !restoreSandbox)
            _aiState!.ProcedureCaptureBlocker = FalloutSandboxPackage.CaptureBlockerPrefix + "awaiting first native movement observation.";
        if (deferred is null && !restoredDialogue && _aiState!.PackageMotion is { DialogueCompleted: true } previousMotion)
            _aiState.PackageMotion = previousMotion with { DialogueCompleted = false };
        if (deferred is not null && _aiState!.ProcedureCaptureBlocker is null)
            _aiState.ProcedureCaptureBlocker = FalloutUnloadedActorPackages.ContinuationBlocker;
        if (restoreTravel) _evaluateRequested = true;
        _failedPackage = null;
        GD.Print($"OPENNV_CREATURE_PACKAGE reference={Appearance.Reference} package={source?.Form} procedure={source?.Procedure}");
    }

    internal Func<FalloutFormKey, Node3D?>? ResolvePackageTarget { get; set; }

    private Node3D? TargetNode(FalloutFormKey target)
    {
        if (target == _aiRecords!.RuntimeFormKey(0x14))
            return Combat!.PackagePlayer is { CollisionResident: true } player ? player : null;
        if (_aiRecords.GetEffective(target).Signature is not ("ACHR" or "ACRE"))
            throw new NotSupportedException($"Package target {target} has no actor target owner.");
        if (!_aiWorld!.IsEnabled(target)) return null;
        if (ResolvePackageTarget is { } resolve) return resolve(target);
        var node = GetParent()?.GetChildren().OfType<Node3D>().SingleOrDefault(node =>
            node is RuntimeNativeCreature creature && creature.Appearance.Reference == target ||
            node is RuntimeNativeNpc npc && npc.Appearance.Reference == target);
        return node;
    }

    public override void _PhysicsProcess(double delta)
    {
        using var frameWork = OpenNV.Runtime.Diagnostics.RuntimeFrameWork.Measure(OpenNV.Runtime.Diagnostics.RuntimeFrameWork.Owner.CreaturePackages);
        Combat?.StopPackageMotion();
        _waitingForTarget = null;
        if (ScriptIdleActive) return;
        if (SandboxMotionError is not null) return;
        if (SandboxAdmissionError is not null || HasDetachedSandboxAdmission) return;
        if (_aiRecords is null || Combat is null || Combat.OwnsPose ||
            !Combat.PackageMovementReady || _conversationTarget is not null) return;
        if (_aiState?.ScriptError is { } scriptError) { _aiError = scriptError; return; }
        var selecting = false;
        try
        {
            _packageClock -= delta;
            var scheduleTime = _aiClock?.ScheduleTime();
            if (scheduleTime is { } moment) scheduleTime = moment with { Hour = MathF.Floor(moment.Hour) };
            // FNV Patrol retains its procedure until completion or an explicit
            // EVP request. An arrival result changing a quest variable must not
            // consume the marker's remaining wait/heading through polling.
            var patrolHolding = _content.Game == RuntimeLiveContentSource.FalloutNewVegasGame &&
                _patrol is not null && _patrolProgress?.Complete != true && !_evaluateRequested;
            if (!_packageSelectionAttempted || !PackageEndIdlePending && !(_travelPackage?.MustReach == true && _travelProgress?.Complete == false) &&
                !patrolHolding &&
                (_evaluateRequested || _packageClock <= 0 || scheduleTime != _aiScheduleTime || _aiQuestRevision != _aiQuests!.Revision))
            {
                _evaluateRequested = false; _packageClock = 10;
                _aiScheduleTime = scheduleTime;
                _aiQuestRevision = _aiQuests!.Revision;
                // A failed first selection still used this evaluation window.
                // Keep cold continuation separate: it is consumed only after
                // a successful selection. Explicit requests and changed inputs
                // may retry immediately; an unchanged failure waits for polling.
                _packageSelectionAttempted = true; ++_packageSelectionAttempts;
                selecting = true;
                SelectPackage();
                selecting = false;
            }
            if (_aiError is not null) return;
            if (_travelPackage is not null) AdvanceTravel(delta);
            else if (_sandboxPackage is { } sandbox)
                _sandboxWander = Combat.AdvanceSandboxWander(_aiPackage!, sandbox, _sandboxWander, delta);
            else if (_patrol is not null) AdvancePatrol(delta);
            else if (_guardPackage is { } guard)
                _guardProgress = Combat.AdvanceGuard(_aiPackage!, guard, _guardProgress!, delta);
            else if (_followPackage is { } follow)
            {
                if (follow.Accompany && AdvanceAccompanyPortal(follow, delta)) return;
                var target = TargetNode(follow.Target);
                if (target is null) { _waitingForTarget = follow.Target; return; }
                var running = target switch
                {
                    RuntimeNativePlayer player => player.Activity.Running,
                    RuntimeNativeNpc npc => npc.Activity.Running,
                    RuntimeNativeCreature creature => creature.Activity.Running,
                    _ => false,
                };
                if (follow.Accompany) AdvanceAccompany(follow, target, running, delta);
                else Combat.AdvancePackageMotion(_aiPackage!, target.GlobalPosition, follow.Distance * Skeleton.UnitsToMetres, running, delta);
            }
            else if (_dialoguePackage is { } dialogue && !_dialogueRequested)
            {
                var targetNode = TargetNode(dialogue.Target);
                if (targetNode is null) { _waitingForTarget = dialogue.Target; return; }
                var target = targetNode.GlobalPosition;
                var distance = dialogue.ActivationDistance * Skeleton.UnitsToMetres;
                Combat.AdvancePackageMotion(_aiPackage!, target, distance, dialogue.Running, delta, dialogue.WeaponDrawn, requireArrivalHeight: true);
                if (GlobalPosition.DistanceTo(target) <= distance && IsOnFloor() && Combat.PackagePlayer?.ModalInput != true)
                {
                    _dialogueRequested = true;
                    (BeginPackageDialogue ?? throw new NotSupportedException("Creature dialogue has no conversation owner."))
                        (dialogue, () =>
                        {
                            if (!IsInstanceValid(this) || !IsInsideTree() || !ReferenceEquals(_dialoguePackage, dialogue)) return;
                            if (dialogue.Type == 1) Combat.CompleteDialoguePackage();
                            _packageEvents!.Complete(); _evaluateRequested = true;
                            if (ReferenceEquals(_dialoguePackage, dialogue) && _packageEvents.Done &&
                                _aiState!.ProcedureCaptureBlocker == DialogueCaptureBlocker)
                                _aiState.ProcedureCaptureBlocker = null;
                        });
                }
            }
        }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException or InvalidOperationException or FileNotFoundException)
        {
            var changed = _aiError != error.Message;
            _aiError = error.Message;
            if (_guardPackage is not null) _guardProgress = Combat.PackageMotion?.Guard ?? _guardProgress;
            if (_sandboxPackage is not null)
                _aiState!.ProcedureCaptureBlocker = FalloutSandboxPackage.CaptureBlockerPrefix + error.Message;
            if (!selecting && _sandboxPackage is not null && error is FalloutSandboxMotionException stopped &&
                stopped.Package == _aiPackage?.FormKey)
                _sandboxMotionException = stopped;
            if (!selecting && _sandboxPackage is not null && error is FalloutSandboxAdmissionException admission &&
                admission.Package == _aiPackage?.FormKey && _sandboxWander is null && _aiState!.PackageMotion is null)
                _sandboxAdmissionFailure = admission;
            if (!selecting) _failedPackage = _aiPackage?.FormKey;
            if (changed) GD.PushError($"OPENNV_CREATURE_AI_DIVERGENCE reference={Appearance.Reference}: {_aiError}");
        }
    }
}
