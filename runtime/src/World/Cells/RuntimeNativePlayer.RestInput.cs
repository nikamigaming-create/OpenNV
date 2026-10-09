using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.InputSystem;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal partial class RuntimeNativePlayer
{
    internal event Action? OpenSourceWaitMenu;
    internal event Action<FalloutRestRequest>? OpenSourceSleepMenu;
    private bool SourceRestInput(InputEvent input)
    {
        if (_modalInput || !_movementEnabled || _furniturePhase != 0 || _sourceCamera is not null ||
            input is InputEventKey { Echo: true } || !InputMap.HasAction(RuntimeNativeInputControls.Action(FalloutSleepWaitSource.RestControl)) ||
            !input.IsActionPressed(RuntimeNativeInputControls.Action(FalloutSleepWaitSource.RestControl))) return false;
        (OpenSourceWaitMenu ?? throw new NotSupportedException("Selected Rest control has no living source wait-menu owner."))();
        return true;
    }
    // XR adapters forward the actual user Rest action to this same request.
    internal void RequestSourceWaitMenu() =>
        (OpenSourceWaitMenu ?? throw new NotSupportedException("XR Rest action has no living source wait-menu owner."))();
}
