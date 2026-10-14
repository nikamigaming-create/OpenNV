using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutActorProcessRuntimeState
{
    internal FalloutSourceFrameTimerState RequirePlayerControlTimer()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_cachedTimerNativeOwner is null || _cachedTimerNativeThread != Environment.CurrentManagedThreadId)
            throw new NotSupportedException("source-Player-controls-actual-Main-timer-publication-unbound");
        var timer = _cachedSourceTimer ?? throw new NotSupportedException("source-Player-controls-current-Main-delta-owner-unbound");
        var current = timer.ReadScaledDelta();
        if (current.Process != _process || current.Source != _source.ExecutableSha256)
            throw new InvalidOperationException("Source controls selected another actual cached Main timer.");
        return timer;
    }
    internal FalloutRendererClockState RequirePlayerControlModeOwner() =>
        (_mainCachedTail ?? throw new NotSupportedException("source-Player-controls-current-Main-clock-constructor-unbound"))
            .RequireControlModeOwner(_process);
}
