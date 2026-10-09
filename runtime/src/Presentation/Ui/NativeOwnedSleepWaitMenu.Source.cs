using System.Globalization;
using System.Security.Cryptography;
using System.Xml.Linq;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Presentation.Ui;

internal sealed class NativeSleepWaitMenuSource
{
    internal NativeOwnedMenuTree Tiles { get; }
    internal FalloutSleepWaitSource Source { get; }
    internal string Identity { get; }
    private readonly FalloutPluginStack _records;
    private readonly Func<FalloutGameTimeStamp, string> _calendarCaption;
    private readonly Func<FalloutGameTimeStamp> _time;

    internal NativeSleepWaitMenuSource(FalloutPluginStack records, FalloutSleepWaitSource source,
        Func<FalloutGameTimeStamp> time, Func<FalloutGameTimeStamp, string> sourceCalendarCaption,
        FalloutUiComponentStore? scriptUi = null)
    {
        source.Validate(); Source = source; _records = records; _time = time;
        _calendarCaption = sourceCalendarCaption ?? throw new NotSupportedException("Rest view has no owned date/time formatting consumer.");
        var content = records.OwnedSource ?? throw new NotSupportedException("Rest view has no selected owned resource source.");
        if (!ReferenceEquals(content, RuntimeLiveContentSource.Current) ||
            !content.TryRead(FalloutSleepWaitSource.MenuPath, null, out var bytes, out var identity))
            throw new FileNotFoundException("Rest view's selected menu/resource source is absent.", FalloutSleepWaitSource.MenuPath);
        if (Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() != source.MenuSha256)
            throw new InvalidDataException("Rest menu changed after the source request was admitted.");
        Identity = identity;
        var menu = FalloutMenuXml.Expand(content, FalloutMenuXml.Parse(bytes)).Elements("menu").Single();
        Tiles = new(menu, name => FalloutGameSettingStrings.Read(records, name), scriptUi);
        foreach (var (name, expected) in new[] { ("SWM_HowManyText", 0), ("SWM_Scrollbar", 1), ("SWM_HoursChosen", 2),
            ("SWM_CurrentTime", 3), ("SWM_WaitButton", 4), ("SWM_CancelButton", 5) })
            if (Tiles.Number(Named(name), "id") != expected)
                throw new NotSupportedException("Rest source tile has another engine input identity.");
        var slider = Named("SWM_Scrollbar");
        Tiles.Bind(slider, "_SetInCode", 1); Tiles.Bind(slider, "_current_value", 0);
        if (Tiles.Number(slider, "_number_of_items") != source.MaximumMenuHours ||
            Tiles.Number(slider, "_number_of_visible_items") != 1 || Tiles.Number(slider, "_step_size") != 1)
            throw new InvalidDataException("Rest prepared slider differs from its C# allocation source.");
        _ = CalendarCaption();
    }
    internal XElement Named(string name) => Tiles.Root.DescendantsAndSelf().Single(tile =>
        string.Equals((string?)tile.Attribute("name"), name, StringComparison.OrdinalIgnoreCase));
    internal string Action(FalloutRestKind kind) => FalloutGameSettingStrings.Read(_records,
        kind == FalloutRestKind.Sleep ? "sSleep" : "sWait");
    internal string Question(FalloutRestKind kind)
    {
        var format = FalloutGameSettingStrings.Read(_records, "sHowManyWait");
        var at = format.IndexOf("%s", StringComparison.Ordinal);
        if (at < 0 || format[(at + 2)..].Contains('%') || format[..at].Contains('%'))
            throw new NotSupportedException("Rest question requires another source string formatter.");
        return format[..at] + Action(kind) + format[(at + 2)..];
    }
    internal string Hours(int hours) => hours.ToString(CultureInfo.InvariantCulture) + " " +
        FalloutGameSettingStrings.Read(_records, hours == 1 ? "sHour" : "sHours");
    internal string CalendarCaption()
    {
        var value = _calendarCaption(_time());
        return string.IsNullOrWhiteSpace(value) ? throw new InvalidDataException("Rest source date formatter returned no caption.") : value;
    }
}
