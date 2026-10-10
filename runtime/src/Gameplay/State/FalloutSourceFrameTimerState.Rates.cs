using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutSourceFrameTimerRateState(FalloutSourceFrameTimer Source, Guid Process,
    long Epoch, long Sequence, float Current, float Target);

internal sealed partial class FalloutSourceFrameTimerState
{
    // Read only already constructed cells. Reading the current rate is not an
    // OS counter sample and does not certify a returned original Main update.
    internal FalloutSourceFrameTimerRateState ReadRates()
    {
        RequireHealthy();
        return new(_source, _process, _epoch, _sequence, _rate, _target);
    }
}
