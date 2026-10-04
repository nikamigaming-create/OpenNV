using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.World.Actors;

internal partial class RuntimeNativeNpc
{
    private FalloutDialoguePackage? _dialoguePackage;
    private bool _dialoguePackageRequested;
    private bool _dialogueWaitReached;
    private bool _dialogueNativeMovement;
    private Vector3 _dialogueWaitPosition;
    private bool _dialogueWaitPositionPending;
    private (Vector3 Authored, Vector3 Floor)? _dialogueTargetDestination;
    internal Action<FalloutDialoguePackage, Action>? BeginPackageDialogue { get; set; }
    internal Func<FalloutFormKey, Node3D?>? ResolveDialogueTarget { get; set; }
    internal Func<bool>? PackageSpeechBusy { get; set; }
    internal Func<bool>? NpcDialogueActive { get; set; }

    private void BeginDialoguePackage(FalloutPluginRecord package, FalloutPlacedReference? wait, bool initializing)
    {
        var dialogue = FalloutDialoguePackage.Read(package);
        if (dialogue.TriggerLocation is not null && _aiStack!.RuntimeFormId(dialogue.Target) != 0x14 && dialogue.ControlsTargetMovement)
            throw new NotSupportedException("Dialogue trigger location needs target movement ownership.");
        var motion = _aiWorld?.Get(Appearance.Reference!.Value).PackageMotion;
        var restored = initializing && motion?.Package == package.FormKey && motion.DialogueCompleted;
        if (restored && (dialogue.Type != 1 || wait is not null))
            throw new InvalidDataException("Saved dialogue completion differs from its supported source procedure.");
        // An unowned furniture/object waiting location cannot publish a running
        // dialogue procedure before its movement owner is admitted. The failed
        // source selection still retains its explicit pre-begin continuation.
        if (wait is not null) RequireTravelMarker(package, wait);
        _dialoguePackage = dialogue; _dialoguePackageRequested = restored;
        _dialogueNativeMovement = wait is null;
        _dialogueWaitReached = wait is null;
        _dialogueWaitPositionPending = !IsInsideTree();
        if (!_dialogueWaitPositionPending) _dialogueWaitPosition = GlobalPosition;
        _dialogueTargetDestination = null;
        _aiPackage = package;
        if (_aiWorld is { } world)
        {
            var state = world.Get(Appearance.Reference!.Value);
            state.ProcedureCaptureBlocker = restored ? null : FalloutActorDialogueContinuation.CaptureBlocker;
            if (!restored && motion?.Package == package.FormKey) state.PackageMotion = motion with { DialogueCompleted = false };
        }
        if (wait is not null) StartTravel(package, wait);
        if (restored) _packageEvents!.Restore(_packageIdleSource!, true);
        else _packageEvents!.Change(_packageIdleSource);
        CompletePendingTravel();
    }

    private void ClearDialoguePackage()
    {
        if (_dialoguePackage is not null && _aiWorld is { } world)
        {
            var state = world.Get(Appearance.Reference!.Value);
            state.DialogueContinuation = null;
            if (state.ProcedureCaptureBlocker == FalloutActorDialogueContinuation.CaptureBlocker) state.ProcedureCaptureBlocker = null;
        }
        _dialoguePackage = null; _dialoguePackageRequested = false; _dialogueNativeMovement = false;
        _dialogueTargetDestination = null;
    }

    private void AdvanceDialogueTarget(double delta)
    {
        try { AdvanceDialoguePackage(delta); }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException or InvalidOperationException or IOException)
        {
            Combat?.StopPackageMotion(); _aiError = error.Message; _failedPackage = _aiPackage?.FormKey;
            GD.PushError($"OPENNV_NATIVE_DIALOGUE_PACKAGE_DIVERGENCE reference={Appearance.Reference}: {_aiError}");
        }
    }

    private void AdvanceDialoguePackage(double delta = 0)
    {
        if (_dialoguePackage is not { } dialogue || _dialoguePackageRequested || _aiError is not null) return;
        if (_dialogueWaitPositionPending)
        {
            _dialogueWaitPosition = GlobalPosition;
            _dialogueWaitPositionPending = false;
        }
        if (PackageSpeechBusy?.Invoke() == true) { Combat?.StopPackageMotion(); return; }
        if (!_dialogueWaitReached)
        {
            if (_travelActive) return;
            _dialogueWaitReached = true;
        }
        if (_dialogueNativeMovement && (Combat is null || Combat.OwnsPose || !Combat.PackageMovementReady || _conversationTarget is not null)) return;
        var playerTarget = _aiStack!.RuntimeFormId(dialogue.Target) == 0x14;
        if (playerTarget && Combat?.PackagePlayerCell is { } playerCell && playerCell != _aiCell!.Cell.FormKey) return;
        if (!playerTarget && _aiWorld?.IsEnabled(dialogue.Target) == false) return;
        var targetNode = ResolveDialogueTarget is { } resolve ? resolve(dialogue.Target) : playerTarget ?
            Combat?.PackagePlayer ?? GetTree().Root.FindChildren("*", "", true, false).OfType<RuntimeNativePlayer>()
                .SingleOrDefault(player => player.CollisionResident) :
            GetTree().Root.FindChildren("*", "", true, false).OfType<Node3D>().SingleOrDefault(actor =>
                actor is RuntimeNativeNpc npc && npc.Appearance.Reference == dialogue.Target ||
                actor is RuntimeNativeCreature creature && creature.Appearance.Reference == dialogue.Target);
        if (targetNode is null)
        {
            if (!playerTarget && _aiWorld?.Get(dialogue.Target).Cell == _aiCell!.Cell.FormKey)
                throw new NotSupportedException($"Resident dialogue target {dialogue.Target} has no source presentation owner.");
            return;
        }
        if (!targetNode.IsVisibleInTree()) return;
        var target = targetNode.GlobalPosition;
        var talkingActivator = !playerTarget && _aiStack.GetEffective(dialogue.Target).Signature == "REFR" &&
            _aiStack.GetEffective(FalloutDialogueTopic.RequiredForm(_aiStack.GetEffective(dialogue.Target), "NAME")).Signature == "TACT";
        var approach = target;
        if (talkingActivator && _dialogueNativeMovement)
        {
            if (_dialogueTargetDestination is not { } previous || !previous.Authored.IsEqualApprox(target))
                _dialogueTargetDestination = (target, Combat!.ProjectPackageDestination(target));
            approach = _dialogueTargetDestination.Value.Floor;
        }
        var distance = dialogue.ActivationDistance * Skeleton.UnitsToMetres;
        var waitForTarget = dialogue.TriggerLocation is not null && _packageIdleSource!.LocationType is not null &&
            _packageIdleSource.LocationRadius == 0;
        // PLDT controls the speaker's wait position. Its zero radius does not
        // remove the independent PLD2 condition on the dialogue target.
        if (dialogue.TriggerLocation is { } trigger && !DialogueTriggerContains(trigger, target)) return;
        if (_dialogueNativeMovement)
        {
            Combat!.AdvancePackageMotion(_aiPackage!, waitForTarget ? _dialogueWaitPosition : approach,
                waitForTarget ? 0 : distance, dialogue.Running, delta, dialogue.WeaponDrawn, requireArrivalHeight: true);
            if (!IsOnFloor()) return;
        }
        var origin = GlobalPosition;
        if (talkingActivator)
        {
            origin = HeadTargetPoint ?? throw new NotSupportedException("Talking activator reach has no source head pose.");
        }
        if (origin.DistanceTo(target) > distance)
        {
            if (!waitForTarget && !_dialogueNativeMovement && !_travelActive)
            {
                StartTravelTo(_aiPackage!, dialogue.Target, new(Basis, GetParent<Node3D>().ToLocal(target)), "dialogue-target");
                CompletePendingTravel();
            }
            return;
        }
        if (Combat?.PackagePlayer?.ModalInput == true || PackageSpeechBusy?.Invoke() == true) return;
        _travelProgress?.Cancel();
        PlayLocomotion(false);
        _dialoguePackageRequested = true;
        (BeginPackageDialogue ?? throw new NotSupportedException("Dialogue package has no conversation owner."))(dialogue, () =>
        {
            // The same Form can be selected again after another package. Its
            // previous audio/conversation must not complete that newer owner.
            if (!IsInstanceValid(this) || !IsInsideTree() || !ReferenceEquals(_dialoguePackage, dialogue) ||
                CurrentPackage != dialogue.Form) return;
            if (_dialogueNativeMovement && dialogue.Type == 1) Combat!.CompleteDialoguePackage();
            _packageEvents!.Complete();
            if (ReferenceEquals(_dialoguePackage, dialogue) && _dialogueNativeMovement && dialogue.Type == 1 && _aiWorld is { } world)
                world.Get(Appearance.Reference!.Value).ProcedureCaptureBlocker = null;
        });
    }

    private bool DialogueTriggerContains(FalloutDialogueTriggerLocation trigger, Vector3 target)
    {
        if (trigger.Type == 2) return target.DistanceTo(_dialogueWaitPosition) <= trigger.Radius * Skeleton.UnitsToMetres;
        var world = _aiWorld ?? throw new NotSupportedException("Dialogue trigger has no shared placement owner.");
        var placement = trigger.Type == 3 ? world.EditorPlacement(Appearance.Reference!.Value) : world.Placement(trigger.Reference!.Value);
        if (placement.Cell != _aiCell!.Cell.FormKey) return false;
        var point = GetParent<Node3D>().ToGlobal(new Vector3(placement.Position[0], placement.Position[2], -placement.Position[1]) * Skeleton.UnitsToMetres);
        return target.DistanceTo(point) <= trigger.Radius * Skeleton.UnitsToMetres;
    }
}
