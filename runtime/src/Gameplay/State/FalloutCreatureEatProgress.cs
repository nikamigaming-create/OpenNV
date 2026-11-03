using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutCreatureEatProgress(float StartedHour, bool Arrived = false,
    FalloutCreatureEatItem? UsedItem = null, float RetrySeconds = 0)
{
    internal void Validate()
    {
        if (!float.IsFinite(StartedHour) || StartedHour is < 0 or >= 24 || !float.IsFinite(RetrySeconds) || RetrySeconds > 6 ||
            !Arrived && (UsedItem is not null || RetrySeconds != 0) || UsedItem is null && RetrySeconds != 0)
            throw new InvalidDataException("Creature Eat has an invalid retained phase or producer clock.");
        UsedItem?.Validate();
    }
}
