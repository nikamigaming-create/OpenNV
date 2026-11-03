using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal enum FalloutFindFoodPhase { Travel, Acquire, Wander, Done }

internal sealed record FalloutFindFoodProgress(FalloutFormKey Cell, FalloutFindFoodPhase Phase = FalloutFindFoodPhase.Travel,
    float RetrySeconds = 20)
{
    internal void Validate()
    {
        if (!Enum.IsDefined(Phase) || string.IsNullOrWhiteSpace(Cell.OwnerPlugin) || Cell.ObjectId is 0 or > FalloutFormKey.ObjectIdMask ||
            !float.IsFinite(RetrySeconds) || RetrySeconds > 20)
            throw new InvalidDataException("Find Food has an invalid retained region, phase or retry clock.");
    }

    internal (FalloutFindFoodProgress Progress, bool Retry) AdvanceWander(double delta, bool ready)
    {
        Validate();
        if (Phase != FalloutFindFoodPhase.Wander || !double.IsFinite(delta) || delta < 0 || delta > float.MaxValue)
            throw new InvalidDataException("Find Food retry has no valid Wander update.");
        if (RetrySeconds <= 0 && ready)
            return (this with { Phase = FalloutFindFoodPhase.Acquire, RetrySeconds = 20 }, true);
        var next = RetrySeconds - (float)delta;
        if (!float.IsFinite(next)) throw new InvalidDataException("Find Food retry clock overflowed.");
        return (this with { RetrySeconds = next }, false);
    }
}
