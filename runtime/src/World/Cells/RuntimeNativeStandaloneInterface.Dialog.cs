using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.Presentation.Ui;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class RuntimeNativeStandaloneInterface : IFalloutStandaloneDialogChildren
{
    string IFalloutStandaloneDialogChildren.Owner => "actual-native-Dialog-source-prefix/" + GetInstanceId();
    string IFalloutStandaloneDialogChildren.Source => _state.Source.Identity;
    internal void OpenDialog(NativeOwnedDialogueMenu menu, FalloutStandaloneInterfaceObject dialog, FalloutFormKey speaker)
    {
        if (_retired || !IsInstanceValid(this) || !IsInsideTree() || !IsInstanceValid(menu) ||
            !menu.IsInsideTree() || menu.IsQueuedForDeletion() || menu.GetViewport() != GetViewport())
            throw new InvalidOperationException("Actual Dialog opener lost its current source/presentation parent.");
        _state.OpenDialog(dialog, speaker, this);
    }
    void IFalloutStandaloneDialogChildren.StoreMenuSceneByte(FalloutStandaloneDialogInvocation invocation, byte value)
    {
        invocation.Require(FalloutStandaloneDialogStep.SceneByteEntered);
        if (value != 1 || invocation.Owner != _state)
            throw new InvalidDataException("Original menu push changed its independent scene child byte.");
        // The original source condition first queries another living scene
        // object. Godot mouse visibility, pause and this manager's existence
        // cannot prove that child's pointer chain, byte or absent arm.
        _state.EnterDialogBoundary(invocation, "menu-push-original-scene-dependent-byte-query-and-store");
    }
    void IFalloutStandaloneDialogChildren.StoreTileSelectionLocus(FalloutStandaloneDialogInvocation invocation, Guid tile, uint valueBits)
    {
        invocation.Require(FalloutStandaloneDialogStep.SelectionCleared);
        if (invocation.Owner != _state || tile == Guid.Empty || valueBits != 0)
            throw new InvalidDataException("Source selection clear changed its original returned tile/store.");
        _state.EnterDialogBoundary(invocation, "source-selected-input-tile-locus-writer");
    }
    void IFalloutStandaloneDialogChildren.PrepareActors(FalloutStandaloneDialogInvocation invocation, FalloutFormKey speaker)
    {
        invocation.Require(FalloutStandaloneDialogStep.ActorPreparationEntered);
        if (invocation.Owner != _state || speaker.ObjectId == 0)
            throw new InvalidDataException("Original Dialog actor preparation changed its actual source speaker.");
        _state.EnterDialogBoundary(invocation, "Dialog-speaker-effect-process-virtual-preparation");
    }
}
