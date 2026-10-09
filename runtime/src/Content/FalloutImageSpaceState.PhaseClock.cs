using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Content;

internal sealed partial class FalloutImageSpaceState
{
    private FalloutImageSpacePhaseClock? _phaseClock;
    internal void ConstructDoubleVisionClock(FalloutDoubleVisionPhase source, FalloutGameTime time, Guid process)
    {
        if (_phaseClock is not null) throw new InvalidOperationException("An actual image phase factory still owns the previous cache.");
        _phaseClock = new(source, time, process);
    }
    internal void BindDoubleVisionClock(FalloutDoubleVisionPhase source, FalloutGameTime time, Guid process)
    {
        if (_phaseClock is null) throw new NotSupportedException("Image phase has no actual current factory construction.");
        if (_phaseClock.Source != source) throw new InvalidDataException("Image phase renderer changed its selected source declaration.");
        _phaseClock.BindWriter(time, process);
    }
    internal void SampleDoubleVisionClock(FalloutGameTime time, Guid process) =>
        (_phaseClock ?? throw new NotSupportedException("Image phase has no actual source clock factory.")).Sample(time, process);
    private float DoubleVisionAngle(FalloutDoubleVisionPhase phase, float hour)
    {
        if (_phaseClock is null) return phase.Angle(hour); // Pure composition of explicitly supplied source inputs.
        if (_phaseClock.Source != phase) throw new InvalidDataException("Image effect changed its actual source clock consumer.");
        return phase.AngleSeconds(_phaseClock.Seconds);
    }
    internal FalloutImageSpacePhaseClockSnapshot CaptureDoubleVisionClock() =>
        (_phaseClock ?? throw new NotSupportedException("Current save has no actual image phase clock.")).Capture();
    internal void RestoreDoubleVisionClock(FalloutImageSpacePhaseClockSnapshot saved, FalloutDoubleVisionPhase source,
        FalloutGameTime time, Guid process)
    {
        if (_phaseClock is not null) throw new InvalidOperationException("Image phase cache cannot restore over an actual live factory.");
        var clock = new FalloutImageSpacePhaseClock(source, time, process);
        clock.Restore(saved); _phaseClock = clock;
    }
    internal void RetireDoubleVisionClock()
    {
        if (_phaseClock is not { } clock) return;
        clock.Retire();
        _phaseClock = null;
    }
}
