using System.Buffers.Binary;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutPackageSchedule(int Month, int Weekday, int Date, int Hour, int Duration)
{
    internal static FalloutPackageSchedule Read(FalloutPluginRecord package)
    {
        var fields = package.ReadSubrecords().Where(field => field.Signature == "PSDT").ToArray();
        if (fields.Length != 1 || fields[0].Data.Length != 8)
            throw new InvalidDataException($"Package {package.FormKey} schedule has an invalid extent.");
        var bytes = fields[0].Data.Span;
        var value = new FalloutPackageSchedule(unchecked((sbyte)bytes[0]), unchecked((sbyte)bytes[1]),
            bytes[2], unchecked((sbyte)bytes[3]), BinaryPrimitives.ReadInt32LittleEndian(bytes[4..]));
        if (value.Month is < -1 or > 11 || value.Weekday is < -1 or > 10 || value.Date > 31 ||
            value.Hour is < -1 or > 23 || value.Duration < 0)
            throw new InvalidDataException($"Package {package.FormKey} schedule has invalid calendar fields.");
        return value;
    }

    internal bool IsActive(FalloutGameTime? clock)
    {
        if (Month == -1 && Weekday == -1 && Date == 0 && Hour == -1) return true;
        if (clock is null) throw new NotSupportedException("Scheduled package has no simulation clock.");
        var now = clock.ScheduleTime();
        if (Hour == -1) return MatchesDate(now);
        var duration = Math.Max(1, Duration); // The source schedule selects whole hour blocks.
        var elapsed = now.Hour - Hour;
        var daysBefore = elapsed < 0 ? 1 : 0;
        if (elapsed < 0) elapsed += 24;
        // A fixed calendar plus weekday repeats within seven calendar years.
        // This bounds long source durations without discarding valid windows.
        var limit = checked(clock.CalendarDays * 7);
        for (var day = 0; day < limit && elapsed < duration; day++, elapsed += 24)
            if (MatchesDate(clock.ScheduleTime(daysBefore + day))) return true;
        return false;
    }

    private bool MatchesDate(FalloutScheduleTime time) => (Month == -1 || Month == time.Month) &&
        (Date == 0 || Date == time.Date) && Weekday switch
        {
            -1 => true,
            <= 6 => Weekday == time.Weekday,
            7 => time.Weekday is >= 1 and <= 5,
            8 => time.Weekday is 0 or 6,
            9 => time.Weekday is 1 or 3 or 5,
            10 => time.Weekday is 2 or 4,
            _ => false,
        };
}
