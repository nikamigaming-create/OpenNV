using Godot;

namespace OpenNV.Runtime.Presentation.OpenXR;

internal sealed partial class NativeXrRig
{
    private double _saveHeldSeconds;
    private bool _saveHoldActive, _saveWasHeld, _aidWasHeld;
    private bool? _heldWheel;
    internal Action<bool>? OpenCombatWheel { get; set; }
    internal Func<bool>? CombatWheelOpen { get; set; }
    internal Action<bool>? CloseCombatWheel { get; set; }
    internal Action<Vector2>? SelectCombatWheel { get; set; }
    internal Action<int>? PageCombatWheel { get; set; }

    private void AdvanceCombatWheels(bool tracked, bool left, bool right, double delta)
    {
        var save = left && LeftGrip.IsButtonPressed(NativeXrActions.Save);
        var aid = left && LeftGrip.GetFloat(NativeXrActions.FingerTrigger) >= _configuration.Xr.ActionThreshold;
        if (!tracked || !left || !right)
        {
            if (_heldWheel is not null) { CloseCombatWheel?.Invoke(false); _rightInputReady = false; _snapReady = false; }
            _heldWheel = null;
            if (!tracked || !left)
            { _saveHoldActive = false; _saveWasHeld = save; _aidWasHeld = aid; return; }
        }
        if (save && !_saveWasHeld && !(Modal?.Invoke() ?? true))
        { _saveHoldActive = true; _saveHeldSeconds = 0; }
        if (save && _saveHoldActive && (_saveHeldSeconds += delta) >= .3)
        {
            _saveHoldActive = false;
            if (right) OpenCombatWheel?.Invoke(false);
            if (CombatWheelOpen?.Invoke() == true) { _heldWheel = false; _rightInputReady = false; }
        }
        if (!save && _saveWasHeld)
        {
            if (_heldWheel == false) { CloseCombatWheel?.Invoke(true); _heldWheel = null; _rightInputReady = false; _snapReady = false; }
            else if (_saveHoldActive && !(Modal?.Invoke() ?? true)) _player?.SaveGame?.Invoke();
            _saveHoldActive = false;
        }
        if (right && aid && !_aidWasHeld && !(Modal?.Invoke() ?? true))
        {
            OpenCombatWheel?.Invoke(true);
            if (CombatWheelOpen?.Invoke() == true) { _heldWheel = true; _rightInputReady = false; }
        }
        if (!aid && _aidWasHeld && _heldWheel == true)
        { CloseCombatWheel?.Invoke(true); _heldWheel = null; _rightInputReady = false; _snapReady = false; }
        _saveWasHeld = save; _aidWasHeld = aid;
        var next = Edge("wheel-next", RightGrip.IsButtonPressed(NativeXrActions.Grab));
        var previous = Edge("wheel-previous", RightGrip.IsButtonPressed(NativeXrActions.Reload));
        if (CombatWheelOpen?.Invoke() != true) return;
        if (next) PageCombatWheel?.Invoke(1);
        if (previous) PageCombatWheel?.Invoke(-1);
        var direction = RightGrip.GetVector2(NativeXrActions.Turn);
        SelectCombatWheel?.Invoke(new(direction.X, -direction.Y));
    }
}
