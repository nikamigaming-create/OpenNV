using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Presentation.Ui;

internal sealed partial class RuntimeNativeUiClock(Func<FalloutUiComponentStore?> owner) : Node
{
    private ulong _ticks;
    public override void _EnterTree() => _ticks = Time.GetTicksUsec();
    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        // Advance existing animations before this frame's scripts can start
        // new ones. The new start must not consume a preceding frame interval.
        ProcessPriority = int.MinValue;
    }
    public override void _Process(double delta)
    {
        var now = Time.GetTicksUsec();
        owner()?.AdvanceAnimations((now - _ticks) / 1_000_000d);
        _ticks = now;
    }
}
