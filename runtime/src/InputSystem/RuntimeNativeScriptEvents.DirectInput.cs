using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.InputSystem;

internal sealed partial class RuntimeNativeScriptEvents
{
    private FalloutDirectInputState? _directInput;
    internal void BindDirectInput(FalloutDirectInputState state)
    {
        if (_directInput is not null) throw new InvalidOperationException("Script key events already have their complete native device owner.");
        state.RequireCurrent(); _directInput = state; state.GameEdges += OnDeviceKeys;
    }
    private void OnDeviceKeys(IReadOnlyList<(int Key, bool Down)> edges)
    {
        if (!Active || invoker() is not { } invoke) return;
        foreach (var (key, down) in edges)
            if (_savePreparationPaused) events.ObserveSavePausedKey(key, down);
            else events.Key(key, down, invoke);
    }
    private void UnbindDirectInput()
    {
        if (_directInput is not { } state) return;
        state.GameEdges -= OnDeviceKeys; _directInput = null;
    }
}
