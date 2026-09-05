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

/// <summary>Simulation-owned calendar/time advance. Presentation only reads this owner.</summary>
internal sealed class FalloutGameTime
{
    private readonly FalloutGlobalState _globals;
    private readonly FalloutGameTimeBindings _forms;
    private readonly FalloutCalendar _calendar;
    private float _previousHour;
    private bool _reconcileDaysPassed;
    internal float Hour => _globals.Get(_forms.Hour);
    internal float TimeScale => _globals.Get(_forms.TimeScale);
    internal float DaysPassed => _globals.Get(_forms.DaysPassed);

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
        var increment = (float)((double)TimeScale * seconds / 3600.0);
        var hour = Hour + increment;
        if (!float.IsFinite(hour)) throw new InvalidDataException("Game-time advance overflowed.");
        var daysPassed = DaysPassed;
        if (_reconcileDaysPassed || hour > _previousHour + 1.0)
            daysPassed = (float)(MathF.Truncate(daysPassed) + hour / 24.0);
        var year = _globals.Get(_forms.Year);
        var month = _globals.Get(_forms.Month);
        var day = _globals.Get(_forms.Day);
        if (hour > 24)
        {
            var monthLength = _calendar.MonthDays[(int)month];
            while (hour > 24)
            {
                var nextHour = hour - 24;
                var nextDay = day + 1;
                if (nextHour >= hour || nextDay <= day)
                    throw new InvalidDataException("Calendar advance exceeds Float32 clock/day resolution.");
                hour = nextHour; day = nextDay;
            }
            if (day > monthLength)
            {
                day -= monthLength; month += 1;
                if (month >= 12) { month -= 12; year += 1; }
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
