using Godot;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World;

internal sealed partial class RuntimeNativeGameTime
{
    private FalloutRestWorldTime? _restWorldTime;
    private bool _cumulativeFrameActive;
    private Action? _consumeSourceEffectFrame;

    internal void BindSourceEffectFrame(Action consume)
    {
        ArgumentNullException.ThrowIfNull(consume);
        if (_consumeSourceEffectFrame is not null || _cumulativeFrameActive)
            throw new InvalidOperationException("Native effect-frame ownership is already bound or executing.");
        _consumeSourceEffectFrame = consume;
    }

    internal void BindSourceCumulativeWorldTime(FalloutRestWorldTime owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (_restWorldTime is not null) throw new InvalidOperationException("Native game time already owns its cumulative source clock.");
        _restWorldTime = owner;
    }

    internal FalloutAdvancementActivityObservation ObserveCumulativeWorldTimeFrame() =>
        _error is not null ? new(FalloutAdvancementActivityState.Unowned, "actual-world-clock-failure:" + _error) :
        !_cumulativeFrameActive || !IsInsideTree() || IsQueuedForDeletion() || !CanProcess() || GetTree().Paused ?
            new(FalloutAdvancementActivityState.Held, "current-native-cumulative-world-time-frame-lease") :
            new(FalloutAdvancementActivityState.Satisfied, "current-native-cumulative-world-time-frame:" + Engine.GetProcessFrames());

    // Called by this actual node's ordinary _Process after its existing pause/
    // rest-clock checks and before calendar advancement. No UI timer calls it.
    internal void AdvanceCurrentCumulativeWorldTime(float simulationSeconds)
    {
        var owner = _restWorldTime ?? throw new NotSupportedException("Native game time has no cumulative source-world-time owner.");
        if (_cumulativeFrameActive) throw new InvalidOperationException("Cumulative source frame cannot reenter.");
        _cumulativeFrameActive = true;
        try
        {
            var consume = _consumeSourceEffectFrame ??
                throw new NotSupportedException("Native gameplay clock has no current effect consumer.");
            owner.AdvanceActualSourceFrame(Engine.GetProcessFrames(), simulationSeconds);
            consume();
        }
        finally { _cumulativeFrameActive = false; }
    }
}
