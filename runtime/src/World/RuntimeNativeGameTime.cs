using Godot;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World;

/// <summary>Adapts the shared simulation clock to Godot's pause and frame lifecycle.</summary>
internal sealed partial class RuntimeNativeGameTime : Node
{
    private readonly FalloutGameTime _clock;
    private string? _error;
    private Func<bool>? _restOwnsClock;
    internal void BindSourceRestClock(Func<bool> ownsClock)
    {
        ArgumentNullException.ThrowIfNull(ownsClock);
        if (_restOwnsClock is not null) throw new InvalidOperationException("Game time already has a rest clock owner.");
        _restOwnsClock = ownsClock;
    }
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
        // The coordinator may parent this clock while asynchronous source
        // construction is still loading. Its real process phase starts only
        // after the world-time, rest and effect consumers have been attached.
        ProcessMode = ProcessModeEnum.Disabled;
        ProcessPriority = int.MinValue;
    }

    internal void StartSourceGameplayFrames()
    {
        if (!IsInsideTree() || IsQueuedForDeletion() || ProcessMode != ProcessModeEnum.Disabled ||
            _restOwnsClock is null || _restWorldTime is null || _consumeSourceEffectFrame is null || _error is not null)
            throw new InvalidOperationException("Gameplay frames require the living constructed source clock and its actual consumers.");
        ProcessMode = ProcessModeEnum.Always;
    }

    public override void _Process(double delta)
    {
        if (_error is not null || GetTree().Paused) return;
        try
        {
            if (_restOwnsClock?.Invoke() == true) return;
            AdvanceCurrentCumulativeWorldTime((float)delta);
            _clock.AdvanceSimulation((float)delta);
        }
        catch (Exception error)
        {
            _error = error.Message;
            GetTree().Paused = true;
            GD.PushError($"OPENNV_GAME_TIME_UNBOUND {error.Message}");
        }
    }
}
