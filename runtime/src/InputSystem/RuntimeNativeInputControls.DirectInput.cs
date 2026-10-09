using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.InputSystem;

internal sealed partial class RuntimeNativeInputControls
{
    private FalloutDirectInputState? _deviceState;
    internal void BindDirectInput(FalloutDirectInputState state)
    {
        if (_deviceState is not null || !ReferenceEquals(state.Controls, controls))
            throw new InvalidOperationException("Native control filtering requires the same source binding owner exactly once.");
        state.RequireCurrent(); _deviceState = state; ProcessMode = ProcessModeEnum.Always; ProcessPriority = int.MinValue + 1;
    }
    public override void _Process(double delta)
    {
        if (_deviceState is not { } state) return;
        FalloutDirectInputSnapshot sample;
        try { sample = state.Capture(); }
        catch (InvalidOperationException)
        {
            // Losing acquisition releases presentation actions. The C# device
            // snapshot remains unavailable, with its original refusal retained.
            for (var control = 0; control < FalloutInputControls.Count; ++control)
                foreach (var action in Actions(control)) Input.ActionRelease(action);
            return;
        }
        if (!sample.Acquired)
        {
            for (var control = 0; control < FalloutInputControls.Count; ++control)
                foreach (var action in Actions(control)) Input.ActionRelease(action);
            return;
        }
        for (var control = 0; control < FalloutInputControls.Count; ++control)
        {
            var keyboard = controls.Get((uint)control); var mouse = controls.Get((uint)control, 1);
            bool Down(int code) => code == -1 ? false : (uint)code < sample.Keys.Count ? sample.Keys[code].Game :
                throw new NotSupportedException("Source control binding requires an input code outside the complete keyboard/mouse owner: " + code);
            var down = Down(keyboard) || Down(mouse);
            foreach (var action in Actions(control))
                if (down) Input.ActionPress(action); else Input.ActionRelease(action);
        }
    }
}
