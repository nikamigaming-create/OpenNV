using Godot;
using OpenNV.Runtime.InputSystem;

namespace OpenNV.Runtime.World.Cells;

internal partial class RuntimeNativePlayer
{
    internal Action<byte>? UseInventoryHotkey { get; set; }

    private bool InventoryHotkeyInput(InputEvent input)
    {
        if (_modalInput || _furniturePhase != 0 || _sourceCamera is not null ||
            !GetMeta("opennv_source_pipboy_enabled", false).AsBool()) return false;
        for (byte slot = 1; slot <= 8; slot++)
        {
            // NV reserves the second binding for ammunition swap.
            if (slot == 2 && _inputControls?.Get(18) is not null) continue;
            var pressed = _inputControls is not null
                ? input.IsActionPressed(RuntimeNativeInputControls.Action(16 + slot), allowEcho: false)
                : input is InputEventKey { Pressed: true, Echo: false } key && key.PhysicalKeycode == Key.Key1 + slot - 1;
            if (!pressed) continue;
            try { UseInventoryHotkey?.Invoke(slot); }
            catch (Exception error) { GD.PushError($"OPENNV_INVENTORY_HOTKEY_FAILED slot={slot}: {error.Message}"); }
            return true;
        }
        return false;
    }
}
