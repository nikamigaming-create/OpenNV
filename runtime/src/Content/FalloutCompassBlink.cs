namespace OpenNV.Runtime.Content;

internal sealed record FalloutCompassBlinkSettings(int MinimumDistance, int ThresholdDistance, int MaximumDistance,
    int FastestRate, int ThresholdRate, int SlowestRate, int ShortestPause, int ThresholdPause, int LongestPause)
{
    internal int MinimumSquared => unchecked(MinimumDistance * MinimumDistance);
    internal int ThresholdSquared => unchecked(ThresholdDistance * ThresholdDistance);
    internal int MaximumSquared => unchecked(MaximumDistance * MaximumDistance);
    internal static FalloutCompassBlinkSettings Read(FalloutPluginStack records)
    {
        int Setting(string suffix)
        {
            var name = "iHUDCompassIconBlink" + suffix;
            records.NumericSettings.RequireRetainedConsumer(name, nameof(FalloutCompassBlinkSettings));
            return unchecked((int)records.NumericSettings.IntegerBits(name));
        }
        var value = new FalloutCompassBlinkSettings(Setting("MinDist"), Setting("ThresholdDist"), Setting("MaxDist"),
            Setting("FastestBlinkTime"), Setting("ThresholdBlinkTime"), Setting("SlowestBlinkTime"),
            Setting("ShortestPause"), Setting("ThresholdPause"), Setting("LongestPause"));
        value.Validate(); return value;
    }
    internal void Validate()
    {
        if (MinimumDistance < 0 || ThresholdDistance < 0 || MaximumDistance < 0 || MinimumSquared < 0 ||
            MinimumSquared >= ThresholdSquared || ThresholdSquared >= MaximumSquared ||
            FastestRate <= 0 || ThresholdRate <= 0 || SlowestRate <= 0 || ShortestPause < 0 || ThresholdPause < 0 || LongestPause < 0)
            throw new NotSupportedException("Compass blink settings have unbound distance/rate/pause arithmetic.");
    }
    internal (float Rate, float Pause) At(float squaredDistance)
    {
        Validate();
        if (!float.IsFinite(squaredDistance) || squaredDistance < 0) throw new InvalidDataException("Compass blink distance is invalid.");
        var distance = Math.Clamp(squaredDistance, MinimumSquared, MaximumSquared);
        float Map(int from, int to, int firstDistance, int lastDistance) =>
            (float)(from + ((double)distance - firstDistance) / (lastDistance - (double)firstDistance) * (to - (double)from));
        return distance < ThresholdSquared
            ? (Map(ThresholdRate, FastestRate, ThresholdSquared, MinimumSquared), Map(ThresholdPause, ShortestPause, ThresholdSquared, MinimumSquared))
            : (Map(SlowestRate, ThresholdRate, MaximumSquared, ThresholdSquared), Map(LongestPause, ThresholdPause, MaximumSquared, ThresholdSquared));
    }
}

// Presentation state belongs to a retained icon, not its target actor's AI.
internal sealed class FalloutCompassBlinkState(float alpha, int phase)
{
    internal float Alpha { get; private set; } = alpha;
    internal int Phase { get; private set; } = phase;
    internal void SetAlpha(float value)
    {
        if (!float.IsFinite(value) || value < 0) throw new InvalidDataException("Compass presentation alpha is invalid.");
        Alpha = value;
    }
    internal float PauseDeadlineMilliseconds { get; private set; }
    internal void Advance(FalloutCompassBlinkSettings settings, float distanceSquared, float maximumAlpha,
        float baseAlpha, float seconds, uint milliseconds)
    {
        if (!float.IsFinite(Alpha) || Phase is not (0 or 1 or 3) || !float.IsFinite(maximumAlpha) || maximumAlpha < 0 ||
            !float.IsFinite(baseAlpha) || baseAlpha < 0 || !float.IsFinite(seconds) || seconds < 0)
            throw new InvalidDataException("Compass blink has no finite retained presentation state.");
        var (rate, pause) = settings.At(distanceSquared);
        Alpha = Phase switch
        {
            0 => baseAlpha == 0 ? 0 : (float)(Alpha + (double)seconds * rate * maximumAlpha / baseAlpha),
            1 => baseAlpha == 0 ? 0 : (float)(Alpha - (double)seconds * rate * maximumAlpha / baseAlpha),
            3 => baseAlpha,
            _ => throw new InvalidDataException("Compass blink phase is unbound.")
        };
        if (Alpha > maximumAlpha) { Alpha = maximumAlpha; Phase = 3; return; }
        if (Alpha < 0)
        {
            Alpha = 0; Phase = 0; PauseDeadlineMilliseconds = (float)((double)milliseconds + pause); return;
        }
        if (milliseconds > (double)PauseDeadlineMilliseconds && Phase == 3) Phase = 1;
    }
}
