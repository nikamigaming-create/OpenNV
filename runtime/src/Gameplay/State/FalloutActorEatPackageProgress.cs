using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal enum FalloutEatContinuationAction { ResumeMeal, SelectFood, HoldOccupied, Wander }

// The standalone Eat assignment retains its location/seat intent independently
// of the shared ingestion/IDLE continuation and accepted movement requests.
internal sealed record FalloutActorEatPackageProgress(FalloutFormKey Cell, FalloutFormKey? Seat = null,
    bool Furniture = false, float RetrySeconds = 0, float NoFoodSeconds = 20)
{
    internal static FalloutEatContinuationAction NextAction(FalloutFormKey package, FalloutActorPackageMotion? motion,
        bool occupied, Func<bool> hasFood)
    {
        if (motion?.Package == package && motion.EatPackage is not null && motion.Eat is not null)
            return FalloutEatContinuationAction.ResumeMeal;
        if (hasFood()) return FalloutEatContinuationAction.SelectFood;
        return occupied ? FalloutEatContinuationAction.HoldOccupied : FalloutEatContinuationAction.Wander;
    }

    internal (FalloutActorEatPackageProgress Progress, bool Retry) AdvanceIdle(double delta)
    {
        ValidateDelta(delta);
        return RetrySeconds <= 0 ? (this, true) : (this with { RetrySeconds = RetrySeconds - (float)delta }, false);
    }

    internal FalloutActorEatPackageProgress AfterIdleAttempt(float randomUnit)
    {
        if (!float.IsFinite(randomUnit) || randomUnit < 0 || randomUnit > 1)
            throw new InvalidDataException("Eat retry random value is outside its source unit interval.");
        return this with { RetrySeconds = 1 + 5 * randomUnit };
    }

    // This clock requests another procedure admission; it never consumes food
    // and never suspends an accepted Wander movement request.
    internal (FalloutActorEatPackageProgress Progress, bool ReturnToEat) AdvanceNoFood(double delta, bool ready)
    {
        ValidateDelta(delta);
        if (NoFoodSeconds <= 0 && ready) return (this with { NoFoodSeconds = 20 }, true);
        var next = NoFoodSeconds - (float)delta;
        if (!float.IsFinite(next)) throw new InvalidDataException("Eat fallback clock overflowed.");
        return (this with { NoFoodSeconds = next }, false);
    }

    private static void ValidateDelta(double delta)
    {
        if (!double.IsFinite(delta) || delta < 0 || delta > float.MaxValue)
            throw new InvalidDataException("Eat update delta is invalid.");
    }

    internal void Validate()
    {
        static bool Form(FalloutFormKey value) => !string.IsNullOrWhiteSpace(value.OwnerPlugin) &&
            value.ObjectId is > 0 and <= FalloutFormKey.ObjectIdMask;
        if (!Form(Cell) || Seat is { } seat && !Form(seat) || Furniture && Seat is null ||
            !float.IsFinite(RetrySeconds) || !float.IsFinite(NoFoodSeconds))
            throw new InvalidDataException("Standalone Eat has invalid retained location, seat or timer state.");
    }
}
