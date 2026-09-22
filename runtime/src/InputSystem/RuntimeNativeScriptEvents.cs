using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.InputSystem;

// One native input/frame adapter for shared extension event state. It keeps
// executing MenuMode callbacks while the ordinary scene tree is paused.
internal sealed partial class RuntimeNativeScriptEvents(FalloutScriptEvents events,
    Func<FalloutUserFunctionInvoker?> invoker) : Node
{
    internal bool Active { get; set; }

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        ProcessPriority = int.MinValue + 2;
    }

    public override void _Process(double delta)
    {
        if (Active && invoker() is { } invoke) events.Advance(delta, !GetTree().Paused, invoke);
    }

    public override void _Input(InputEvent input)
    {
        if (!Active || invoker() is not { } invoke) return;
        foreach (var (key, down) in NativeScriptKeys.Read(input)) events.Key(key, down, invoke);
    }
}
