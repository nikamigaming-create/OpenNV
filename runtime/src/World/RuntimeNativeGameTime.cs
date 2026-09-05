using Godot;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World;

/// <summary>Adapts the shared simulation clock to Godot's pause and frame lifecycle.</summary>
internal sealed partial class RuntimeNativeGameTime : Node
{
    private readonly FalloutGameTime _clock;
    private string? _error;
    internal object State => new
    {
        hour = _clock.Hour,
        hourBits = BitConverter.SingleToInt32Bits(_clock.Hour),
        timeScale = _clock.TimeScale,
        daysPassed = _clock.DaysPassed,
        clock = _clock.Capture(),
        error = _error,
    };

    internal RuntimeNativeGameTime(FalloutGameTime clock)
    {
        _clock = clock;
        Name = "NativeGameTime";
        ProcessMode = ProcessModeEnum.Always;
        ProcessPriority = int.MinValue;
    }

    public override void _Process(double delta)
    {
        if (_error is not null || GetTree().Paused) return;
        try { _clock.AdvanceSimulation((float)delta); }
        catch (Exception error)
        {
            _error = error.Message;
            GetTree().Paused = true;
            GD.PushError($"OPENNV_GAME_TIME_UNBOUND {error.Message}");
        }
    }
}
