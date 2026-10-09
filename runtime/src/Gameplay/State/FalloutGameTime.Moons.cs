using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutMoonCalendarSource(FalloutFormKey Hour, string HourSha256,
    FalloutFormKey Days, string DaysSha256, string CalendarSha256)
{
    internal void Require(FalloutPluginStack records, FalloutSkyTransferDeclaration sky)
    {
        if (Hour != records.RuntimeFormKey(0x38) || Days != records.RuntimeFormKey(0x39) ||
            FalloutGlobal.Read(records.GetEffective(Hour)).SourceSha256 != HourSha256 ||
            FalloutGlobal.Read(records.GetEffective(Days)).SourceSha256 != DaysSha256)
            throw new InvalidDataException("Moon calendar changed its exact winning GameHour/DaysPassed source globals.");
        var owned = records.OwnedSource ?? throw new NotSupportedException("Moon calendar requires its actual selected executable owner.");
        var executable = owned.FalloutExecutablePath;
        if (FalloutSkyTransferDeclaration.Read(executable) != sky || FalloutCalendar.Read(executable).SourceSha256 != CalendarSha256)
            throw new InvalidDataException("Moon calendar belongs to another selected executable/calendar declaration.");
    }
}
internal sealed record FalloutMoonCalendarSample(FalloutMoonCalendarSource Source, uint HourBits, uint DaysBits);

internal sealed partial class FalloutGameTime
{
    private FalloutMoonCalendarSource? _moonCalendarSource;
    internal FalloutMoonCalendarSample ReadSourceMoonCalendar()
    {
        Validate();
        if (_moonCalendarSource is null)
        {
            var hour = _globals.Sources.Single(row => row.Form == _forms.Hour);
            var days = _globals.Sources.Single(row => row.Form == _forms.DaysPassed);
            _moonCalendarSource = new(hour.Form, hour.SourceSha256, days.Form, days.SourceSha256, _calendar.SourceSha256);
        }
        var source = _moonCalendarSource;
        return new(source, BitConverter.SingleToUInt32Bits(Hour), BitConverter.SingleToUInt32Bits(DaysPassed));
    }
}
