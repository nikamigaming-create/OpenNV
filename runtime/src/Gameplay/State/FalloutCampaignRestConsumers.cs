using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

// These inputs belong to living source producers. Registration alone cannot
// admit a rest fact; the observer must report the actual reached source branch.
internal sealed record FalloutCampaignRestConsumers(
    Func<FalloutRestRequest, FalloutRestFact, FalloutRestObservation> Observe,
    Action<FalloutRestRequest> AfterPlayerHours,
    Action<FalloutRestRequest> BeforeCancelHours,
    Action<FalloutRestHour> HourPrelude,
    Action<float> WorldHourSeconds,
    Action<FalloutRestHour> HourEffects,
    Action<FalloutRestRequest> CompleteSleep,
    Action<FalloutRestRequest, bool> EndWorldProcessing,
    Func<FalloutInterfaceFadeForceRetirement> FadeForceRetirement)
{
    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(Observe);
        ArgumentNullException.ThrowIfNull(AfterPlayerHours);
        ArgumentNullException.ThrowIfNull(BeforeCancelHours);
        ArgumentNullException.ThrowIfNull(HourPrelude);
        ArgumentNullException.ThrowIfNull(WorldHourSeconds);
        ArgumentNullException.ThrowIfNull(HourEffects);
        ArgumentNullException.ThrowIfNull(CompleteSleep);
        ArgumentNullException.ThrowIfNull(EndWorldProcessing);
        ArgumentNullException.ThrowIfNull(FadeForceRetirement);
    }
}
