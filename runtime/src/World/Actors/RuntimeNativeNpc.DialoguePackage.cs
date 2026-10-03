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
    internal Action<FalloutDialoguePackage, Action>? BeginPackageDialogue { get; set; }

    private void BeginDialoguePackage(FalloutPluginRecord package, FalloutPlacedReference? wait, bool initializing)
    {
        var dialogue = FalloutDialoguePackage.Read(package);
        if (dialogue.TriggerLocation is not null && _aiStack!.RuntimeFormId(dialogue.Target) != 0x14 && (dialogue.Flags & 1) == 0)
            throw new NotSupportedException("Dialogue trigger location needs target movement ownership.");
        var motion = _aiWorld?.Get(Appearance.Reference!.Value).PackageMotion;
        var restored = initializing && motion?.Package == package.FormKey && motion.DialogueCompleted;
        if (restored && (dialogue.Type != 1 || wait is not null))
            throw new InvalidDataException("Saved dialogue completion differs from its supported source procedure.");
        _dialoguePackage = dialogue; _dialoguePackageRequested = restored;
        _dialogueNativeMovement = wait is null;
        _dialogueWaitReached = wait is null;
        _dialogueWaitPosition = GlobalPosition;
        _aiPackage = package;
        if (_aiWorld is { } world)
        {
            var state = world.Get(Appearance.Reference!.Value);
            state.ProcedureCaptureBlocker = restored ? null : "Dialogue package continuation has no cold restoration owner.";
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
            world.Get(Appearance.Reference!.Value).ProcedureCaptureBlocker = null;
        _dialoguePackage = null; _dialoguePackageRequested = false; _dialogueNativeMovement = false;
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
        if (!_dialogueWaitReached)
        {
            if (_travelActive) return;
            _dialogueWaitReached = true;
        }
        if (_dialogueNativeMovement && (Combat is null || Combat.OwnsPose || !Combat.PackageMovementReady || _conversationTarget is not null)) return;
        var playerTarget = _aiStack!.RuntimeFormId(dialogue.Target) == 0x14;
        if (playerTarget && Combat?.PackagePlayerCell is { } playerCell && playerCell != _aiCell!.Cell.FormKey) return;
        var targetNode = playerTarget
            ? GetTree().Root.FindChildren("*", "", true, false).OfType<RuntimeNativePlayer>().SingleOrDefault(player => player.CollisionResident) as Node3D
            : GetTree().Root.FindChildren("*", "", true, false).OfType<Node3D>().SingleOrDefault(actor =>
                actor is RuntimeNativeNpc npc && npc.Appearance.Reference == dialogue.Target ||
                actor is RuntimeNativeCreature creature && creature.Appearance.Reference == dialogue.Target);
        if (targetNode is null || !playerTarget && _aiWorld?.IsEnabled(dialogue.Target) == false) return;
        var target = targetNode.GlobalPosition;
        var distance = dialogue.ActivationDistance * Skeleton.UnitsToMetres;
        var waitForTarget = dialogue.TriggerLocation is not null && _packageIdleSource!.LocationType is not null &&
            _packageIdleSource.LocationRadius == 0;
        if (!waitForTarget && dialogue.TriggerLocation is { } trigger && !DialogueTriggerContains(trigger, target)) return;
        if (_dialogueNativeMovement)
        {
            Combat!.AdvancePackageMotion(_aiPackage!, waitForTarget ? _dialogueWaitPosition : target,
                waitForTarget ? 0 : distance, dialogue.Running, delta, dialogue.WeaponDrawn, requireArrivalHeight: true);
            if (!IsOnFloor()) return;
        }
        if (GlobalPosition.DistanceTo(target) > distance)
        {
            if (!waitForTarget && !_dialogueNativeMovement && !_travelActive)
            {
                StartTravelTo(_aiPackage!, dialogue.Target, new(Basis, GetParent<Node3D>().ToLocal(target)), "dialogue-target");
                CompletePendingTravel();
            }
            return;
        }
        if (Combat?.PackagePlayer?.ModalInput == true) return;
        _travelProgress?.Cancel();
        PlayLocomotion(false);
        _dialoguePackageRequested = true;
        (BeginPackageDialogue ?? throw new NotSupportedException("Dialogue package has no conversation owner."))(dialogue, () =>
        {
            // The same Form can be selected again after another package. Its
            // previous audio/conversation must not complete that newer owner.
            if (!IsInstanceValid(this) || !IsInsideTree() || !ReferenceEquals(_dialoguePackage, dialogue)) return;
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
