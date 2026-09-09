using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.World.Actors;

internal partial class RuntimeNativeNpc
{
    private FalloutDialoguePackage? _dialoguePackage;
    private bool _dialoguePackageRequested;
    private bool _dialogueWaitReached;
    internal Action<FalloutDialoguePackage, Action>? BeginPackageDialogue { get; set; }

    private void BeginDialoguePackage(FalloutPluginRecord package, FalloutPlacedReference wait)
    {
        var dialogue = FalloutDialoguePackage.Read(package);
        if (package.ReadSubrecords().Any(field => field.Signature == "PLD2"))
            throw new NotSupportedException("Dialogue trigger location needs target movement ownership.");
        if (dialogue.Type != 0) throw new NotSupportedException("Dialogue SayTo package completion is unbound.");
        _dialoguePackage = dialogue; _dialoguePackageRequested = false;
        _dialogueWaitReached = false;
        _aiPackage = package;
        StartTravel(package, wait);
        _packageEvents!.Change(_packageIdleSource);
    }

    private void AdvanceDialoguePackage()
    {
        if (_dialoguePackage is not { } dialogue || _dialoguePackageRequested || _aiError is not null) return;
        if (!_dialogueWaitReached)
        {
            if (_travelActive) return;
            _dialogueWaitReached = true;
        }
        var target = _aiStack!.RuntimeFormId(dialogue.Target) == 0x14
            ? GetTree().Root.FindChildren("*", "", true, false).OfType<RuntimeNativePlayer>().Single().GlobalPosition
            : GetTree().Root.FindChildren("*", "", true, false).OfType<RuntimeNativeNpc>()
                .Single(actor => actor.Appearance.Reference == dialogue.Target).GlobalPosition;
        if (GlobalPosition.DistanceTo(target) > dialogue.ActivationDistance * Skeleton.UnitsToMetres)
        {
            if (!_travelActive)
                StartTravelTo(_aiPackage!, dialogue.Target, new(Basis, GetParent<Node3D>().ToLocal(target)), "dialogue-target");
            return;
        }
        _travelActive = false;
        PlayLocomotion(false);
        _dialoguePackageRequested = true;
        (BeginPackageDialogue ?? throw new NotSupportedException("Dialogue package has no conversation owner."))(dialogue, () =>
        {
            if (_dialoguePackage?.Form == dialogue.Form) _packageEvents!.Complete();
        });
    }
}
