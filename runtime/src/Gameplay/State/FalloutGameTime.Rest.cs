namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutGameTimeStamp(float Year, float Month, float Day, float Hour, float DaysPassed,
    float TimeScale, string CalendarSha256)
{
    internal void Validate()
    {
        if (new[] { Year, Month, Day, Hour, DaysPassed, TimeScale }.Any(value => !float.IsFinite(value)) ||
            Year < 0 || Month is < 0 or >= 12 || Month != MathF.Truncate(Month) || Day < 1 ||
            Hour < 0 || DaysPassed < 0 || TimeScale < 0 || !Content.FalloutPlayerPhysicalSource.Digest(CalendarSha256))
            throw new InvalidDataException("Saved rest calendar stamp is invalid.");
    }
    internal bool HasSameBits(FalloutGameTimeStamp other) => CalendarSha256 == other.CalendarSha256 &&
        new[] { Year, Month, Day, Hour, DaysPassed, TimeScale }.Select(BitConverter.SingleToInt32Bits)
        .SequenceEqual(new[] { other.Year, other.Month, other.Day, other.Hour, other.DaysPassed, other.TimeScale }
            .Select(BitConverter.SingleToInt32Bits));
}
internal sealed partial class FalloutGameTime
{
    internal FalloutGameTimeStamp Stamp()
    {
        Validate(); var result = new FalloutGameTimeStamp(_globals.Get(_forms.Year), _globals.Get(_forms.Month),
            _globals.Get(_forms.Day), Hour, DaysPassed, TimeScale, _calendar.SourceSha256);
        result.Validate(); return result;
    }
    internal float RestHourSimulationSeconds()
    {
        Validate();
        var seconds = (float)(3600.0 / TimeScale);
        if (!float.IsFinite(seconds) || seconds <= 0)
            throw new NotSupportedException("Rest cannot advance one source hour with the current TimeScale.");
        return seconds;
    }
}
