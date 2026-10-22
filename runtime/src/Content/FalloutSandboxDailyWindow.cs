namespace OpenNV.Runtime.Content;

// This is the actor's sampled daily window, not a package schedule declaration.
// Retain the sampled hours: reevaluating a package must not redraw its mealtimes.
internal sealed record FalloutSandboxDailyWindow(int Slot, uint Day, float StartHour, float DurationHours)
{
    internal FalloutSandboxActivity Activity => Slot == 0 ? FalloutSandboxActivity.Sleep : FalloutSandboxActivity.Eat;

    internal void Validate()
    {
        if (Slot is < 0 or > 3 || !float.IsFinite(StartHour) || StartHour < 0 || StartHour >= 24 ||
            !float.IsFinite(DurationHours) || DurationHours < 0 || !float.IsFinite(StartHour + DurationHours))
            throw new InvalidDataException("Sandbox daily window has invalid retained clock values.");
        _ = End();
    }

    private (uint Day, float Hour) End()
    {
        var hour = StartHour + DurationHours;
        var days = Math.Floor((double)hour / 24);
        if (days > uint.MaxValue - Day)
            throw new InvalidDataException("Sandbox daily window exceeds the day clock.");
        return (Day + (uint)days, (float)(hour - days * 24));
    }

    internal bool Contains(uint day, float hour)
    {
        Validate(); ValidateHour(hour);
        var end = End();
        return day >= Day && day <= end.Day && (day != Day || hour >= StartHour) &&
            (day != end.Day || hour < end.Hour);
    }

    internal FalloutSandboxActivity? EligibleActivity(uint day, float hour, bool foodAvailable, bool bedAvailable)
        => Contains(day, hour) && (Slot == 0 ? bedAvailable : foodAvailable) ? Activity : null;

    internal static FalloutSandboxDailyWindow Refresh(FalloutSandboxDailyWindow? retained, uint day, float hour,
        FalloutNumericGameSettings settings, Func<float, float, float> randomRange)
        => Refresh(retained, day, hour, slot =>
        {
            var stem = slot switch { 0 => "SleepStart", 1 => "Breakfast", 2 => "Lunch", 3 => "Dinner", _ => throw new ArgumentOutOfRangeException(nameof(slot)) };
            var duration = slot == 0 ? "SleepDuration" : "MealDuration";
            var minimum = settings.Float("fSandbox" + stem + "Min");
            var maximum = settings.Float("fSandbox" + stem + "Max");
            if (!float.IsFinite(minimum) || !float.IsFinite(maximum) || minimum < 0 || minimum > 24 || maximum < 0 || maximum > 24)
                throw new InvalidDataException("Sandbox daily start settings are outside the day clock.");
            var start = Draw(minimum, maximum < minimum ? maximum + 24 : maximum);
            if (start >= 24) start -= 24;
            return (start, Draw(settings.Float("fSandbox" + duration + "Min"), settings.Float("fSandbox" + duration + "Max")));

            float Draw(float low, float high)
            {
                if (!float.IsFinite(low) || !float.IsFinite(high) || low < 0 || high < low)
                    throw new InvalidDataException("Sandbox daily settings have invalid random bounds.");
                var value = randomRange(low, high);
                if (!float.IsFinite(value) || value < low || value > high)
                    throw new InvalidDataException("Sandbox daily random value is outside its requested interval.");
                return value;
            }
        });

    internal static FalloutSandboxDailyWindow Refresh(FalloutSandboxDailyWindow? retained, uint day, float hour,
        Func<int, (float Start, float Duration)> drawWindow)
    {
        ValidateHour(hour);
        var expiredOnThisDay = false;
        if (retained is not null)
        {
            retained.Validate();
            var end = retained.End();
            if (day < end.Day || day == end.Day && hour < end.Hour) return retained;
            expiredOnThisDay = day == end.Day;
        }
        FalloutSandboxDailyWindow? selected = null;
        var nearest = float.PositiveInfinity;
        for (var slot = 0; slot < 4; slot++)
        {
            var sample = drawWindow(slot);
            var candidate = new FalloutSandboxDailyWindow(slot, day, sample.Start, sample.Duration);
            candidate.Validate();
            var tomorrow = hour >= sample.Start + sample.Duration;
            if (expiredOnThisDay && !tomorrow && retained!.Slot == slot) continue;
            var distance = (tomorrow ? sample.Start + 24 : sample.Start) - hour;
            if (distance > 0 && distance >= nearest) continue;
            if (tomorrow) candidate = candidate with { Day = checked(day + 1) };
            selected = candidate;
            nearest = distance;
            if (distance <= 0) break;
        }
        return selected ?? retained ?? throw new InvalidOperationException("Sandbox daily selection produced no window.");
    }

    private static void ValidateHour(float hour)
    {
        if (!float.IsFinite(hour) || hour < 0 || hour >= 24)
            throw new InvalidDataException("Sandbox current hour is outside the day clock.");
    }
}
