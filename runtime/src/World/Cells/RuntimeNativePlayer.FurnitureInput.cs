using Godot;

namespace OpenNV.Runtime.World.Cells;

internal partial class RuntimeNativePlayer
{
    // Flat and XR activation reach the same authoritative seat, controls and
    // exit KF. A request is not a completed departure or restored movement.
    private bool DispatchSourceFurnitureActivation()
    {
        if (_furniturePhase == 0) return false;
        var accepted = !_modalInput && _activationEnabled && IsInsideTree() && CanProcess() && !GetTree().Paused &&
            _furnitureError is null && _playerPhysical?.Failure is null && RequestFurnitureExit();
        if (!accepted) RejectActivation();
        GD.Print($"OPENNV_NATIVE_PLAYER_ACTION action=furniture-exit requested={accepted} phase={_furniturePhase} reference={_furnitureReference}");
        return true;
    }
}
