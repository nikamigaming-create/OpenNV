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
    private FalloutPluginRecord? _aiPackage;
    private FalloutFollowPackage? _followPackage;
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
        _followPackage?.Target == _aiRecords?.RuntimeFormKey(0x14) && Combat?.Dead == false && !_aiState.Restrained;
    internal object AiState => new
    {
        package = _aiPackage?.FormKey.ToString(),
        follow = _followPackage,
        dialogue = _dialoguePackage,
        dialogueRequested = _dialogueRequested,
        motion = _aiState?.PackageMotion,
        travel = TravelState,
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
        FalloutGameTime? clock = null, FalloutGlobalState? globals = null)
    {
        _aiRecords = records; _aiQuests = quests; _aiWorld = world;
        _aiClock = clock; _aiGlobals = globals;
        _aiState = world.Get(Appearance.Reference!.Value);
        _aiState.QueryCurrentPackage = _currentPackageQuery = () => _aiPackage?.FormKey;
        _packageEvents = new(DispatchPackageEvent);
        RestoreEventIdle();
    }

    internal void EvaluatePackages(bool reset)
    {
        if (_aiRecords is null) throw new NotSupportedException("Creature has no package owner.");
        if (_aiState?.ScriptError is { } error) throw new NotSupportedException(error);
        _aiError = null;
        // Script evaluation may precede native enable/materialization in the
        // same source event. Evaluate on the next resident physics step.
        _evaluateRequested = true;
    }

    public override void _ExitTree()
    {
        if (_aiState is { } state && ReferenceEquals(state.QueryCurrentPackage, _currentPackageQuery))
            state.QueryCurrentPackage = null;
    }

    internal float PackageCondition(FalloutCondition condition) => condition.Function switch
    {
        25 => Combat?.PackageMoving == true ? 1 : 0,
        161 => FalloutAiPackages.IsCurrentPackage(condition, Appearance.Reference!.Value, _aiPackage?.FormKey,
            reference => _aiWorld!.CurrentPackage(reference)) ? 1 : 0,
        18 => (_aiClock ?? throw new NotSupportedException("Creature time query has no simulation clock.")).Hour,
        74 => (_aiGlobals ?? throw new NotSupportedException("Creature global query has no state owner.")).Get(condition.FormArgument1),
        71 => _aiWorld!.ActorFactions(Appearance.Reference!.Value).GetValueOrDefault(condition.FormArgument1, (sbyte)-1) >= 0 ? 1 : 0,
        73 => _aiWorld!.ActorFactions(Appearance.Reference!.Value).GetValueOrDefault(condition.FormArgument1, (sbyte)-1),
        58 or 59 or 79 or 546 => _aiQuests!.Evaluate(condition),
        32 when condition.RunOn == 0 => InSameCell(condition.FormArgument1) ? 1 : 0,
        35 => _aiWorld!.IsEnabled(Appearance.Reference!.Value) ? 0 : 1,
        36 when condition.Argument1 == 0 => Combat!.PackagePlayer?.ModalInput == true ? 1 : 0,
        50 => _aiState!.TalkedToPlayer ? 1 : 0,
        53 => (float)_aiWorld!.ReadVariable(_aiQuests!, condition.FormArgument1, condition.Argument2),
        63 => Activity.Attacked ? 1 : 0,
        72 => Appearance.Creature == condition.FormArgument1 ? 1 : 0,
        91 => Activity.Alerted ? 1 : 0,
        101 => Activity.WeaponDrawn ? 1 : 0,
        107 => Combat?.KnockedDown == true ? 2 : 0,
        244 => _aiState!.Restrained ? 1 : 0,
        286 => Activity.Sneaking ? 1 : 0,
        287 => Activity.Running ? 1 : 0,
        289 => Activity.InCombat ? 1 : 0,
        300 when condition.RunOn == 0 => _aiWorld!.IsInInterior(Appearance.Reference!.Value) ? 1 : 0,
        _ => throw new NotSupportedException($"Creature package condition {condition.Owner.FormKey}/{condition.Function} is unbound.")
    };

    private bool InSameCell(FalloutFormKey target)
    {
        FalloutReferencePlacement? placement = null;
        if (target == _aiRecords!.RuntimeFormKey(0x14))
        {
            var player = Combat!.PackagePlayer ?? throw new NotSupportedException("Player cell query has no resident player.");
            var position = player.GlobalPosition / Skeleton.UnitsToMetres;
            placement = new(Combat.PackagePlayerCell ?? throw new NotSupportedException("Player cell query has no active cell."),
                [position.X, -position.Z, position.Y], [0, 0, 0]);
        }
        return _aiWorld!.InSameCell(Appearance.Reference!.Value, target, placement, Skeleton.UnitsToMetres);
    }

    private void SelectPackage()
    {
        var previousFailure = _failedPackage;
        _failedPackage = null;
        var retained = !_initialPackageSelected ? _aiState!.PackageMotion : null;
        var restoreTravel = retained?.Travel is not null;
        var selected = restoreTravel ? _aiRecords!.GetEffective(retained!.Package) :
            FalloutAiPackages.Select(_aiRecords!, Appearance.Creature, PackageCondition, _aiState!.Templates, _aiClock,
                eligible: package => _aiWorld!.PackageEligible(Appearance.Reference!.Value, package, _aiClock,
                    _aiPackage?.FormKey, _packageEvents?.Done == true));
        if (_aiError is not null && selected is not null && previousFailure == selected.FormKey)
        { _failedPackage = previousFailure; return; }
        _aiError = null;
        if (_aiPackage?.FormKey == selected?.FormKey) return;
        _failedPackage = selected?.FormKey;
        var source = selected is null ? null : FalloutScriptPackage.Read(selected);
        FalloutFollowPackage? follow = null;
        FalloutDialoguePackage? dialogue = null;
        FalloutTravelPackage? travel = null;
        if (source is not null)
        {
            if (source.Procedure == 1) follow = FalloutFollowPackage.Read(selected!);
            else if (source.Procedure == 6) travel = FalloutTravelPackage.Read(selected!);
            else if (source.Procedure == 15)
            {
                dialogue = FalloutDialoguePackage.Read(selected!);
                if (source.LocationType is not null || selected!.ReadSubrecords().Any(field => field.Signature == "PLD2"))
                    throw new NotSupportedException("Creature dialogue start/end location is unbound.");
            }
            else throw new NotSupportedException($"Creature package {source.Form} procedure {source.Procedure} is unbound.");
        }
        var restoredDialogue = !_initialPackageSelected && dialogue?.Type == 1 && _aiState!.PackageMotion is { DialogueCompleted: true } motion && motion.Package == source!.Form;
        if (travel is not null) BeginTravel(selected!, restoreTravel);
        else { _travelPackage = null; _travelProgress = null; _travelDestination = null; }
        _initialPackageSelected = true;
        if (restoreTravel || restoredDialogue) _packageEvents!.Restore(source!, restoreTravel ? _travelProgress!.Complete : true);
        else _packageEvents!.Change(source);
        _aiPackage = selected; _followPackage = follow; _dialoguePackage = dialogue; _dialogueRequested = restoredDialogue;
        if (travel is null)
            _aiState!.ProcedureCaptureBlocker = dialogue is not null && !restoredDialogue ? "Dialogue package continuation has no cold restoration owner." : null;
        if (!restoredDialogue && _aiState!.PackageMotion is { DialogueCompleted: true } previousMotion)
            _aiState.PackageMotion = previousMotion with { DialogueCompleted = false };
        if (restoreTravel) _evaluateRequested = true;
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
            if (!_initialPackageSelected || !PackageEndIdlePending && !(_travelPackage?.MustReach == true && _travelProgress?.Complete == false) &&
                (_evaluateRequested || _packageClock <= 0 || scheduleTime != _aiScheduleTime || _aiQuestRevision != _aiQuests!.Revision))
            {
                _evaluateRequested = false; _packageClock = 10;
                _aiScheduleTime = scheduleTime;
                _aiQuestRevision = _aiQuests!.Revision;
                selecting = true;
                SelectPackage();
                selecting = false;
            }
            if (_aiError is not null) return;
            if (_travelPackage is not null) AdvanceTravel(delta);
            else if (_followPackage is { } follow)
            {
                var target = TargetNode(follow.Target);
                if (target is null) { _waitingForTarget = follow.Target; return; }
                var running = target switch
                {
                    RuntimeNativePlayer player => player.Activity.Running,
                    RuntimeNativeNpc npc => npc.Activity.Running,
                    RuntimeNativeCreature creature => creature.Activity.Running,
                    _ => false,
                };
                Combat.AdvancePackageMotion(_aiPackage!, target.GlobalPosition, follow.Distance * Skeleton.UnitsToMetres, running, delta);
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
        catch (Exception error) when (error is InvalidDataException or NotSupportedException or InvalidOperationException or FileNotFoundException)
        {
            var changed = _aiError != error.Message;
            _aiError = error.Message;
            if (!selecting) _failedPackage = _aiPackage?.FormKey;
            if (changed) GD.PushError($"OPENNV_CREATURE_AI_DIVERGENCE reference={Appearance.Reference}: {_aiError}");
        }
    }
}
