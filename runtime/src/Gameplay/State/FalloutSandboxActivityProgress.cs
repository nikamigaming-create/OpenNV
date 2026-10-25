using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

// A selected activity outlives individual movement requests. Its duration is
// measured by the source game-hour calculation, not elapsed render seconds.
internal sealed record FalloutSandboxActivityProgress(FalloutSandboxActivity Activity,
    float StartedHour, float Duration, byte[] Preferences, FalloutFormKey? Target = null)
{
    internal void Validate()
    {
        if (!Enum.IsDefined(Activity) || !float.IsFinite(StartedHour) || StartedHour is < 0 or >= 24 ||
            !float.IsFinite(Duration) || Duration < 0 || Preferences is null)
            throw new InvalidDataException("Saved Sandbox activity is invalid.");
        _ = new FalloutSandboxPreferences(Preferences);
        if (Activity == FalloutSandboxActivity.Dialogue && !Target.HasValue ||
            Activity is not (FalloutSandboxActivity.Dialogue or FalloutSandboxActivity.Eat) && Target.HasValue ||
            Target is { } target && (string.IsNullOrWhiteSpace(target.OwnerPlugin) || target.ObjectId == 0))
            throw new InvalidDataException("Saved Sandbox activity target is invalid.");
    }

    internal bool Expired(float hour)
    {
        Validate();
        return Elapsed(StartedHour, hour) > Duration;
    }

    internal static float Elapsed(float start, float current)
    {
        if (!float.IsFinite(start) || !float.IsFinite(current) || start is < 0 or >= 24 || current is < 0 or >= 24)
            throw new InvalidDataException("Sandbox activity clock is outside the day.");
        // Preserve the source's signed-byte minute components, including its
        // use of the starting integer hour for both components. Replacing this
        // with wall-clock seconds or normalized midnight subtraction changes
        // when a retained activity is reconsidered.
        var startHour = (int)start;
        var currentHour = (int)current;
        var startMinute = unchecked((sbyte)(int)(((double)start - startHour) * 60));
        var currentMinute = unchecked((sbyte)(int)(((double)current - startHour) * 60));
        return Math.Abs(currentHour - startHour) + currentMinute - startMinute;
    }

    internal static FalloutSandboxActivityProgress Select(FalloutNumericGameSettings settings, byte energy,
        FalloutSandboxActivity activity, float hour, FalloutSandboxPreferences preferences, Func<float> random,
        FalloutFormKey? target = null)
    {
        var (minimum, maximum) = FalloutSandboxActivities.DurationBounds(settings, energy, activity);
        var unit = random();
        if (!float.IsFinite(unit) || unit is < 0 or > 1) throw new InvalidDataException("Sandbox duration draw is invalid.");
        var duration = minimum + (maximum - minimum) * unit;
        var next = new FalloutSandboxPreferences(preferences.Capture());
        next.Selected(activity);
        var result = new FalloutSandboxActivityProgress(activity, hour, duration, next.Capture(), target);
        result.Validate();
        return result;
    }
}
