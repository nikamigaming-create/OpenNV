using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.InputSystem;

// One native input/frame adapter for shared extension event state. It keeps
// executing MenuMode callbacks while the ordinary scene tree is paused.
internal sealed partial class RuntimeNativeScriptEvents(FalloutScriptEvents events,
    Func<FalloutUserFunctionInvoker?> invoker) : Node
{
    internal bool Active { get; set; }
    private double _frameSeconds;
    private bool _drawConnected;
    private bool _savePreparationPaused;
    internal bool SavePreparationPaused => _savePreparationPaused;

    internal IDisposable PauseForManualSave()
    {
        if (_savePreparationPaused) throw new InvalidOperationException("Source input/frame events already belong to a save pause.");
        var mode = ProcessMode;
        _savePreparationPaused = true;
        try { ProcessMode = ProcessModeEnum.Always; }
        catch { _savePreparationPaused = false; throw; }
        return new SavePause(() =>
        {
            try { if (GodotObject.IsInstanceValid(this)) ProcessMode = mode; }
            finally { _savePreparationPaused = false; }
        });
    }

    public override void _EnterTree()
    {
        // Dummy/headless frames cannot prove a render event. Flat and OpenXR
        // share one global pre-draw hook, independent of eye/viewport count.
        if (DisplayServer.GetName() == "headless") return;
        RenderingServer.FramePreDraw += BeforeDraw;
        _drawConnected = true;
    }

    public override void _ExitTree()
    {
        Active = false;
        UnbindDirectInput();
        if (!_drawConnected) return;
        RenderingServer.FramePreDraw -= BeforeDraw;
        _drawConnected = false;
    }

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        ProcessPriority = int.MinValue + 2;
    }

    public override void _Process(double delta)
    {
        if (_savePreparationPaused) return;
        _frameSeconds = delta;
        if (Active && invoker() is { } invoke) events.Advance(delta, !GetTree().Paused, invoke);
    }

    private void BeforeDraw()
    {
        bool Ready() => Active && !_savePreparationPaused && IsInsideTree() && CanProcess() && invoker() is not null;
        if (!Ready()) return;
        events.Render(_frameSeconds, (script, caller, arguments, seconds) =>
            // Resolve for each call: a surviving process-owned registration
            // must never capture a function delegate into a retired world.
            invoker()!(script, caller, arguments, seconds), Ready);
    }

    public override void _Input(InputEvent input)
    {
        if (_directInput is not null) return;
        if (!Active || invoker() is not { } invoke) return;
        foreach (var (key, down) in NativeScriptKeys.Read(input))
        {
            if (_savePreparationPaused) events.ObserveSavePausedKey(key, down);
            else events.Key(key, down, invoke);
        }
    }

    private sealed class SavePause(Action release) : IDisposable
    {
        private Action? _release = release;
        public void Dispose() { var release = _release; _release = null; release?.Invoke(); }
    }
}
