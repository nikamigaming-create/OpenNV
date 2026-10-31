using Godot;

namespace OpenNV.Runtime.Formats.Gamebryo;

// APP_TIME is shared by model instances and survives cell/session reloads.
// Native observation distinguishes it from wall time: a paused world freezes
// this clock and resuming does not include the paused interval.
internal sealed partial class RuntimeNifApplicationClock : Node
{
    private static RuntimeNifApplicationClock? _owner;
    private static double _seconds;

    internal RuntimeNifApplicationClock()
    {
        Name = "NifApplicationClock";
        ProcessMode = ProcessModeEnum.Always;
        ProcessPriority = int.MinValue;
    }

    public override void _EnterTree()
    {
        if (_owner is not null) throw new InvalidOperationException("Application animation clock already has a live owner.");
        _owner = this;
    }

    public override void _ExitTree()
    {
        if (ReferenceEquals(_owner, this)) _owner = null;
    }

    public override void _Process(double delta)
    {
        if (GetTree().Paused) return;
        if (!double.IsFinite(delta) || delta < 0 || !double.IsFinite(_seconds + delta))
            throw new InvalidDataException("Application animation clock received an invalid frame interval.");
        _seconds += delta;
    }

    internal static double Read() => _owner is not null ? _seconds :
        throw new InvalidOperationException("APP_TIME animation has no shared application clock.");
}
