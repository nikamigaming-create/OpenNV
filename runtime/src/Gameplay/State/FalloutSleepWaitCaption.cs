using System.Globalization;
using System.Text;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed class FalloutSleepWaitCaption(FalloutPluginStack records, FalloutSleepWaitSource source,
    FalloutSleepWaitCaptionDeclaration declaration)
{
    private static readonly string[] DaySettings = ["sDaySunday", "sDayMonday", "sDayTuesday", "sDayWednesday",
        "sDayThursday", "sDayFriday", "sDaySaturday"];
    internal string Format(FalloutGameTimeStamp time)
    {
        time.Validate();
        if (declaration.EngineSha256 != source.EngineSha256 || !FalloutPlayerPhysicalSource.Digest(declaration.SourceSha256))
            throw new InvalidDataException("Rest caption and request belong to different original source consumers.");
        if (time.Year > int.MaxValue || time.DaysPassed >= uint.MaxValue || time.Hour >= int.MaxValue)
            throw new NotSupportedException("Rest caption exceeds its original integer operand extent.");
        var hour = (int)time.Hour;
        var fractional = time.Hour - hour;
        var minutes = declaration.Float32Minutes ? (int)(fractional * 60f) : (int)((double)fractional * 60);
        if (time.Hour is >= 0 and < 1) hour = 12;
        else if (time.Hour >= 13) hour -= 12;
        var ampm = FalloutGameSettingStrings.Read(records, time.Hour < 12 ? "sAMTime" : "sPMTime");
        var day = FalloutGameSettingStrings.Read(records, DaySettings[(uint)time.DaysPassed % 7]);
        var date = Print(declaration.DateFormat, (int)time.Month + 1, (int)time.Day, (int)time.Year % 100);
        return Print(declaration.CaptionFormat, day, date, hour, minutes, ampm);
    }
    private static string Print(string format, params object[] arguments)
    {
        var result = new StringBuilder(); var operand = 0;
        for (var at = 0; at < format.Length; ++at)
        {
            if (format[at] != '%') { result.Append(format[at]); continue; }
            var width = 0;
            if (++at >= format.Length) throw new InvalidDataException("Rest source format is truncated.");
            if (format[at] == '0')
            {
                while (++at < format.Length && char.IsAsciiDigit(format[at])) width = checked(width * 10 + format[at] - '0');
            }
            if (at >= format.Length || operand >= arguments.Length) throw new InvalidDataException("Rest source format has no complete operand.");
            var value = arguments[operand++];
            result.Append(format[at] switch
            {
                's' when value is string text && width == 0 => text,
                'd' when value is int number => number.ToString(width == 0 ? "D" : "D" + width.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture),
                _ => throw new NotSupportedException("Rest source format has an unowned typed operand."),
            });
        }
        if (operand != arguments.Length) throw new InvalidDataException("Rest source format did not consume its exact operands.");
        return result.ToString();
    }
}
