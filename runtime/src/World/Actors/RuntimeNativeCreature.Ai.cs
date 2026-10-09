using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.World.Actors;

internal sealed partial class RuntimeNativeCreature
{
    private FalloutPluginStack? _aiRecords;
    private FalloutQuestState? _aiQuests;
    private FalloutGameTime? _aiClock;
    private FalloutGlobalState? _aiGlobals;
    private FalloutFormKey? _failedPackage;
    private FalloutScheduleTime? _aiScheduleTime;
    private FalloutReferenceWorld? _aiWorld;
    private FalloutReferenceInstance? _aiState;
    private Func<FalloutFormKey?>? _currentPackageQuery;
    private Func<FalloutActorPackageAssignment?>? _packageAssignmentCapture;
    private FalloutPluginRecord? _aiPackage;
    private FalloutFollowPackage? _followPackage;
    private FalloutGuardPackage? _guardPackage;
    private FalloutTravelProgress? _guardProgress;
    private FalloutDialoguePackage? _dialoguePackage;
    private FalloutPackageEvents? _packageEvents;
    private bool _dialogueRequested;
    private bool _initialPackageSelected;
    private bool _evaluateRequested = true;
    private double _packageClock;
    private long _aiQuestRevision = -1;
    private string? _aiError;
    private FalloutFormKey? _waitingForTarget;
    internal Action<FalloutDialoguePackage, Action>? BeginPackageDialogue { get; set; }
    internal bool FollowingPlayer => _aiState?.PlayerTeammate == true && _aiError is null &&
        _packageEvents is { Error: null, Done: false } && _followPackage is not null &&
        _aiPackage?.FormKey == _followPackage.Form && _followPackage.Target == _aiRecords?.RuntimeFormKey(0x14) &&
        _aiWorld?.IsEnabled(Appearance.Reference!.Value) == true && Combat?.Dead == false && !_aiState.Restrained;
    internal object AiState => new
    {
        package = _aiPackage?.FormKey.ToString(),
        scriptPackage = _aiState?.ScriptPackage,
        pendingChoice = _aiState?.PendingPackageChoice,
        failedSelection = _failedPackage?.ToString(),
        follow = _followPackage,
        dialogue = _dialoguePackage,
        dialogueRequested = _dialogueRequested,
        motion = _aiState?.PackageMotion,
        travel = TravelState,
        guard = _guardPackage is null ? null : new { source = _guardPackage, progress = _guardProgress },
        eventIdle = _aiState?.PackageIdle,
        collection = _collection is null ? null : new { source = _collectionSource?.Form.ToString(), state = _collection.Capture(), active = _collectionIdle?.Form.ToString(), clock = _collectionClock?.Capture(), failure = _collectionFailure },
        sandbox = _sandboxSource is null ? null : new { source = _sandboxSource, atLocation = _sandbox?.AtLocation, selected = _sandbox?.Selected, failure = _sandbox?.Failure },
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
        (Combat ?? throw new NotSupportedException("Sandbox action binding requires the actual creature body/controller."))
            .BindSandboxActions(this);
        world.BindActorAlert(Appearance.Reference!.Value, Activity);
        _aiState.QueryCurrentPackage = _currentPackageQuery = () => _aiState.PendingPackageChoice is { } choice
            ? choice.Bind(records, _aiState)?.FormKey : _aiPackage?.FormKey;
        _packageEvents = new(DispatchPackageEvent);
        if (world.UnloadedPackages is { } unloaded)
            unloaded.BindNative(Appearance.Reference!.Value, _packageEvents);
        else if (_aiState.DeferredPackageContinuation is { } deferred)
            deferred.BindNative(records, _aiState, _packageEvents);
        _aiState.CapturePackageAssignment = _packageAssignmentCapture = () => _initialPackageSelected
            ? FalloutActorPackageAssignment.Capture(records, _packageEvents, _boundScriptPackageRevision) : _aiState.PackageAssignment;
        RestoreScriptPackageLifecycle();
        RestoreFollowLifecycleBeforeSelection();
        RestoreSandboxLifecycleBeforeSelection();
        BindCollectionCapture();
        BindFollowMotionCapture();
        RestoreEventIdle();
    }

    internal void EvaluatePackages(bool reset)
    {
        if (_aiRecords is null) throw new NotSupportedException("Creature has no package owner.");
        if (_aiState?.ScriptError is { } error) throw new NotSupportedException(error);
        _aiError = null;
        // Consume selection now; the native body applies it on its ordinary
        // physics step. Saving cannot redraw its predicates on restoration.
        _aiWorld!.QueueActorPackageChoice(Appearance.Reference!.Value, SelectSourcePackage(reevaluateScript: true));
        _evaluateRequested = true;
    }

    public override void _ExitTree()
    {
        RetainFollowMotion();
        RetainCollection();
        RetireSandboxNativeActionOnExit();
        if (_aiState is { } state && ReferenceEquals(state.QueryCurrentPackage, _currentPackageQuery))
        {
            if (_packageEvents is not null) _aiWorld?.UnloadedPackages?.Retain(Appearance.Reference!.Value, _packageEvents);
            state.QueryCurrentPackage = null;
            if (ReferenceEquals(state.CapturePackageAssignment, _packageAssignmentCapture)) state.CapturePackageAssignment = null;
        }
    }

    private Func<FalloutFormKey, FalloutFormKey, double>? _aiItemCount;

    internal float PackageCondition(FalloutCondition condition) => condition.Function == 159
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
            58 or 59 or 79 or 546 => _aiQuests!.Evaluate(condition),
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

    private void SelectPackage(double elapsed)
    {
        var previousFailure = _failedPackage;
        _failedPackage = null;
        var retained = !_initialPackageSelected && _aiState!.PackageMotion?.Package == _aiState.PackageAssignment?.Package ? _aiState.PackageMotion : null;
        var restoreTravel = retained?.Travel is not null;
        var restoreGuard = retained?.Guard is not null;
        var restoreFollow = retained?.Follow is not null;
        var restoreSandbox = retained?.Sandbox is not null;
        var restoreCollection = !_initialPackageSelected && _aiState!.PackageCollection is { } collection && _aiState.PackageAssignment?.Package == collection.IdleState.Package;
        var choice = _aiState!.PendingPackageChoice;
        var selected = restoreTravel || restoreGuard || restoreFollow || restoreSandbox || restoreCollection ? _aiRecords!.GetEffective(restoreCollection ? _aiState!.PackageCollection!.IdleState.Package : retained!.Package) :
            choice is not null ? choice.Bind(_aiRecords!, _aiState) : SelectSourcePackage(reevaluateScript: _initialPackageSelected);
        var selectionRevision = ScriptPackageRevision;
        _observedScriptPackageRevision = _bindingScriptPackageRevision = selectionRevision;
        if (_aiError is not null && selected is not null && previousFailure == selected.FormKey)
        { _failedPackage = previousFailure; return; }
        _aiError = null;
        if (_aiPackage?.FormKey == selected?.FormKey && _aiState.ScriptPackage?.Pending != true)
        {
            if (ReferenceEquals(_aiState.PendingPackageChoice, choice)) _aiState.PendingPackageChoice = null;
            return;
        }
        _failedPackage = selected?.FormKey;
        var source = selected is null ? null : FalloutScriptPackage.Read(selected);
        FalloutFollowPackage? follow = null;
        FalloutDialoguePackage? dialogue = null;
        FalloutTravelPackage? travel = null;
        FalloutGuardPackage? guard = null;
        FalloutSandboxPackage? sandbox = null;
        if (source is not null)
        {
            if (source.Procedure == 1)
            {
                follow = FalloutFollowPackage.Read(selected!);
                follow.RequireActorTarget(_aiRecords!, Appearance.Reference!.Value);

                if (restoreFollow) follow.ValidateContinuation(_aiRecords!, Appearance.Reference!.Value, retained!.Follow!);
            }
            else if (source.Procedure == 6) travel = FalloutTravelPackage.Read(selected!, ownsIdleCollection: true);
            else if (source.Procedure == 14) guard = FalloutGuardPackage.Read(selected!, ownsIdleCollection: true);
            else if (source.Procedure == 12) sandbox = FalloutSandboxPackage.Read(selected!);
            else if (source.Procedure == 15)
            {
                dialogue = FalloutDialoguePackage.Read(selected!);
                if (source.LocationType is not null || selected!.ReadSubrecords().Any(field => field.Signature == "PLD2"))
                    throw new NotSupportedException("Creature dialogue start/end location is unbound.");
            }
            else throw new NotSupportedException($"Creature package {source.Form} procedure {source.Procedure} is unbound.");
        }
        var restoredDialogue = !_initialPackageSelected && _aiState.ScriptPackage?.Pending != true && dialogue?.Type == 1 && _aiState!.PackageMotion is { DialogueCompleted: true } motion && motion.Package == source!.Form;
        if (_sandboxSource is not null && (_sandboxSource.Form != selected?.FormKey || _aiState.ScriptPackage?.Pending == true) && !ClearSandbox())
        {
            if (choice is null && selectionRevision == ScriptPackageRevision && _aiState.PendingPackageChoice is null)
                _aiWorld!.QueueActorPackageChoice(Appearance.Reference!.Value, selected);
            _evaluateRequested = true;
            return;
        }
        if (!restoreTravel && !restoreGuard && !restoreFollow && !restoreSandbox && !restoreCollection &&
            ReferenceEquals(_aiState.PendingPackageChoice, choice)) _aiState.PendingPackageChoice = null;
        if (!restoreTravel && !restoreGuard && !restoreFollow && !restoreSandbox && !restoreCollection && _packageEvents!.Active is { } active &&
            (active.Form != selected?.FormKey || _aiState.ScriptPackage?.Pending == true))
            _packageEvents.Change(null);
        if (selectionRevision != ScriptPackageRevision || !restoreTravel && !restoreGuard && !restoreFollow && !restoreSandbox && !restoreCollection &&
            _aiState.PendingPackageChoice is not null) { _evaluateRequested = true; return; }
        if (_followPackage is not null && _followPackage.Form != selected?.FormKey) Combat!.RetireFollowRoute();
        if (_sandboxSource is not null && _sandboxSource.Form != selected?.FormKey) ClearSandbox();
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
        _initialPackageSelected = true;
        if (restoreFollow || restoreSandbox || restoreCollection)
        {
            if (_packageEvents!.Active?.Form != source!.Form || (restoreFollow || restoreSandbox) && _packageEvents.Done)
                throw new InvalidDataException("Cold Follow lost its bound original lifecycle.");
        }
        else if (restoreTravel || restoreGuard || restoredDialogue) _packageEvents!.Restore(source!, restoreGuard ? false : restoreTravel ? _travelProgress!.Complete : true);
        else _packageEvents!.Change(source);
        _aiPackage = selected; _followPackage = follow; _dialoguePackage = dialogue; _dialogueRequested = restoredDialogue;
        if (sandbox is not null) BeginSandbox(selected!, restoreSandbox);
        BeginCollection(source, restoreFollow || restoreGuard || restoreTravel || restoreSandbox || restoreCollection);
        if (travel is null && guard is null && follow is null && sandbox is null)
            _aiState!.ProcedureCaptureBlocker = dialogue is not null && !restoredDialogue ? "Dialogue package continuation has no cold restoration owner." : null;
        if (!restoredDialogue && _aiState!.PackageMotion is { DialogueCompleted: true } previousMotion)
            _aiState.PackageMotion = previousMotion with { DialogueCompleted = false };
        if (follow is not null)
        {
            if (restoreFollow) RestoreFollowElection(retained!.Follow!, elapsed);
            else Combat!.BeginFollowObservation();
        }
        if (restoreSandbox) RestoreSandboxElection(retained!.Sandbox!, elapsed);
        if (restoreCollection && !restoreFollow && !restoreSandbox) RestoreCollectionElection(elapsed);
        if (restoreTravel && !restoreCollection) _evaluateRequested = true;
        _failedPackage = null;
        GD.Print($"OPENNV_CREATURE_PACKAGE reference={Appearance.Reference} package={source?.Form} procedure={source?.Procedure}");
    }

    private Node3D? TargetNode(FalloutFormKey target)
    {
        if (target == _aiRecords!.RuntimeFormKey(0x14))
            return Combat!.PackagePlayer is { CollisionResident: true } player ? player : null;
        if (_aiRecords.GetEffective(target).Signature is not ("ACHR" or "ACRE"))
            throw new NotSupportedException($"Package target {target} has no actor target owner.");
        if (!_aiWorld!.IsEnabled(target)) return null;
        var node = GetParent().GetChildren().OfType<Node3D>().SingleOrDefault(node =>
            node is RuntimeNativeCreature creature && creature.Appearance.Reference == target ||
            node is RuntimeNativeNpc npc && npc.Appearance.Reference == target);
        return node;
    }

    public override void _PhysicsProcess(double delta)
    {
        Combat?.StopPackageMotion();
        _waitingForTarget = null;
        if (_aiRecords is null || Combat is null || Combat.OwnsPose ||
            !Combat.PackageMovementReady || _conversationTarget is not null) return;
        if (_aiState?.ScriptError is { } scriptError) { _aiError = scriptError; return; }
        var selecting = false;
        try
        {
            _packageClock -= delta;
            var scheduleTime = _aiClock?.ScheduleTime();
            if (scheduleTime is { } moment) scheduleTime = moment with { Hour = MathF.Floor(moment.Hour) };
            if (!_initialPackageSelected || _aiState?.PendingPackageChoice is not null ||
                _aiState?.ScriptPackage?.Pending == true || ScriptPackageRevision != _observedScriptPackageRevision ||
                !PackageEndIdlePending && (_evaluateRequested || _packageClock <= 0 ||
                    scheduleTime != _aiScheduleTime || _aiQuestRevision != _aiQuests!.Revision))
            {
                _evaluateRequested = false; _packageClock = 10;
                _aiScheduleTime = scheduleTime;
                _aiQuestRevision = _aiQuests!.Revision;
                selecting = true;
                SelectPackage(delta);
                selecting = false;
            }
            if (_aiError is not null) return;
            if (_sandboxSource is not null) AdvanceSandbox(delta);
            else if (_travelPackage is not null) AdvanceTravel(delta);
            else if (_guardPackage is { } guard)
                _guardProgress = Combat.AdvanceGuard(_aiPackage!, guard, _guardProgress!, delta);
            else if (_followPackage is { } follow)
            {
                if (!Combat.AdvanceFollowPackageMotion(_aiPackage!, follow, delta)) _waitingForTarget = follow.Target;
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
                            if (ReferenceEquals(_dialoguePackage, dialogue) && dialogue.Type == 1) _aiState!.ProcedureCaptureBlocker = null;
                        });
                }
            }
        }
        catch (Exception error)
        {
            if (_followPackage is not null) _aiState!.ProcedureCaptureBlocker ??= "Follow native suffix failed: " + error.Message;
            var changed = _aiError != error.Message;
            _aiError = error.Message;
            if (_guardPackage is not null) _guardProgress = Combat.PackageMotion?.Guard ?? _guardProgress;
            if (!selecting) _failedPackage = _aiPackage?.FormKey;
            if (changed) GD.PushError($"OPENNV_CREATURE_AI_DIVERGENCE reference={Appearance.Reference}: {_aiError}");
        }
    }
}
