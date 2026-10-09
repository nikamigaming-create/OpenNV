using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutGameTimeBindings(FalloutFormKey Year, FalloutFormKey Month, FalloutFormKey Day,
    FalloutFormKey Hour, FalloutFormKey DaysPassed, FalloutFormKey TimeScale)
{
    // These are the engine's reserved global identities, resolved through the
    // active masters and their winning overrides, for every supported cell.
    internal static FalloutGameTimeBindings Read(FalloutPluginStack records) => new(records.RuntimeFormKey(0x35),
        records.RuntimeFormKey(0x36), records.RuntimeFormKey(0x37), records.RuntimeFormKey(0x38),
        records.RuntimeFormKey(0x39), records.RuntimeFormKey(0x3a));
}
internal sealed record FalloutGameTimeSnapshot(float PreviousHour, bool ReconcileDaysPassed, string CalendarSha256);
internal readonly record struct FalloutScheduleTime(int Month, int Date, int Weekday, float Hour);

/// <summary>Simulation-owned calendar/time advance. Presentation only reads this owner.</summary>
internal sealed partial class FalloutGameTime
{
    private readonly FalloutGlobalState _globals;
    private readonly FalloutGameTimeBindings _forms;
    private readonly FalloutCalendar _calendar;
    private float _previousHour;
    private bool _reconcileDaysPassed;
    internal float Hour => _globals.Get(_forms.Hour);
    internal int Year => DatePart(_globals.Get(_forms.Year));
    internal float TimeScale => _globals.Get(_forms.TimeScale);
    internal float DaysPassed => _globals.Get(_forms.DaysPassed);
    internal int CalendarDays => _calendar.MonthDays.Sum(value => value);
    internal bool CarryAtDayBoundary => _calendar.CarryAtDayBoundary;

    internal FalloutScheduleTime ScheduleTime(int daysBefore = 0)
    {
        Validate();
        if (daysBefore < 0) throw new ArgumentOutOfRangeException(nameof(daysBefore));
        var carry = checked((int)MathF.Floor(Hour / 24));
        var offset = carry - daysBefore;
        var month = checked((int)_globals.Get(_forms.Month));
        var date = checked((int)_globals.Get(_forms.Day));
        var dayOfYear = _calendar.MonthDays.Take(month).Sum(value => value) + date - 1;
        dayOfYear = (int)(((long)dayOfYear + offset) % CalendarDays + CalendarDays) % CalendarDays;
        month = 0;
        while (dayOfYear >= _calendar.MonthDays[month]) dayOfYear -= _calendar.MonthDays[month++];
        var weekday = (int)((Math.Floor(DaysPassed) - daysBefore) % 7 + 7) % 7;
        return new(month, dayOfYear + 1, weekday, Hour % 24);
    }

    internal FalloutGameTime(FalloutGlobalState globals, FalloutGameTimeBindings forms, FalloutCalendar calendar)
    {
        _globals = globals; _forms = forms; _calendar = calendar;
        Validate();
    }
    internal void InitializeNewGame()
    {
        _globals.Set(_forms.DaysPassed, (float)(DaysPassed + Hour / 24.0));
        _previousHour = Hour; _reconcileDaysPassed = true;
    }
    internal static float SourceHour(double value)
    {
        var hour = (float)value;
        if (!double.IsFinite(value) || !float.IsFinite(hour) || hour < 0)
            throw new InvalidDataException("SetGameHour requires a finite non-negative source Float32 hour.");
        return hour;
    }
    internal void SetHour(float hour)
    {
        hour = SourceHour(hour);
        Validate();
        _globals.Set(_forms.Hour, Hour <= hour ? hour : 24.0f + hour);
    }
    internal float GetDaysPassed(int year = 2281, int month = 10, int day = 13)
    {
        Validate();
        if (month is < 1 or > 12)
            throw new InvalidDataException("GetGameDaysPassed requires a one-based month within the source calendar.");
        var currentYear = DatePart(_globals.Get(_forms.Year));
        var currentMonth = (int)_globals.Get(_forms.Month);
        var currentDay = DatePart(_globals.Get(_forms.Day));
        var startMonth = month - 1;
        long days = (long)currentDay - day;
        // The extension retains an offset across forward month/year boundaries,
        // and does not subtract future years or months. Do not replace its query
        // with Gregorian date arithmetic or the separate GameDaysPassed global.
        if (currentYear > year || startMonth < currentMonth)
            days += (currentYear > year ? ((long)currentYear - year) * CalendarDays : 0) +
                _calendar.MonthDays.Take(currentMonth).Sum(value => value) -
                _calendar.MonthDays.Take(startMonth).Sum(value => value) - 1;
        if (days is < int.MinValue or > int.MaxValue)
            throw new NotSupportedException("GetGameDaysPassed exceeds its source signed 32-bit date span.");
        return (float)days + Hour * (1.0f / 24.0f);
    }
    private static int DatePart(float value) => value >= int.MinValue && (double)value <= int.MaxValue
        ? (int)value : throw new NotSupportedException("Game date has no source signed 32-bit query representation.");

    internal FalloutGameTimeSnapshot Capture() => new(_previousHour, _reconcileDaysPassed, _calendar.SourceSha256);
    internal void Restore(FalloutGameTimeSnapshot snapshot)
    {
        if (!float.IsFinite(snapshot.PreviousHour) || snapshot.CalendarSha256 != _calendar.SourceSha256)
            throw new InvalidDataException("Saved game time has an invalid clock or another calendar declaration.");
        Validate();
        _previousHour = snapshot.PreviousHour; _reconcileDaysPassed = snapshot.ReconcileDaysPassed;
    }

    internal void AdvanceSimulation(float seconds)
    {
        if (!float.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        Validate();
        var increment = _calendar.SinglePrecisionProduct ? TimeScale * seconds / 3600.0f :
            (float)((double)TimeScale * seconds / 3600.0);
        var hour = Hour + increment;
        if (!float.IsFinite(hour)) throw new InvalidDataException("Game-time advance overflowed.");
        var daysPassed = DaysPassed;
        if (_reconcileDaysPassed || hour > _previousHour + 1.0)
            daysPassed = (float)(MathF.Truncate(daysPassed) + hour / 24.0);
        var year = _globals.Get(_forms.Year);
        var month = _globals.Get(_forms.Month);
        var day = _globals.Get(_forms.Day);
        if (_calendar.CarryAtDayBoundary ? hour >= 24 : hour > 24)
        {
            while (_calendar.CarryAtDayBoundary ? hour >= 24 : hour > 24)
            {
                var nextHour = hour - 24;
                var nextDay = day + 1;
                if (nextHour >= hour || nextDay <= day)
                    throw new InvalidDataException("Calendar advance exceeds Float32 clock/day resolution.");
                hour = nextHour; day = nextDay;
            }
            while (day > _calendar.MonthDays[(int)month])
            {
                day -= _calendar.MonthDays[(int)month]; month += 1;
                if (month >= 12)
                {
                    var nextYear = year + 1;
                    if (!float.IsFinite(nextYear) || nextYear <= year)
                        throw new InvalidDataException("Calendar advance exceeds Float32 year resolution.");
                    month -= 12; year = nextYear;
                }
            }
        }
        daysPassed = (float)(daysPassed + increment / 24.0);
        if (!float.IsFinite(daysPassed)) throw new InvalidDataException("Game-time day counter overflowed.");
        _globals.Set(_forms.Year, year); _globals.Set(_forms.Month, month); _globals.Set(_forms.Day, day);
        _globals.Set(_forms.DaysPassed, daysPassed); _globals.Set(_forms.Hour, hour);
        _previousHour = hour; _reconcileDaysPassed = false;
    }

    private void Validate()
    {
        var month = _globals.Get(_forms.Month);
        if (_calendar.MonthDays.Count != 12 || _calendar.MonthDays.Any(value => value == 0) ||
            month != MathF.Truncate(month) || month < 0 || month >= 12 || TimeScale < 0 || Hour < 0 ||
            _globals.Get(_forms.Day) < 1 || _globals.Get(_forms.Year) < 0 || DaysPassed < 0)
            throw new NotSupportedException("Game time has an unbound calendar/global state.");
    }
}
