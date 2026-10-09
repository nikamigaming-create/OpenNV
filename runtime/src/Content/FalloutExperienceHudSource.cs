using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutExperienceHudDeclaration(string ExecutableSha256, string Contract,
    float MeterFadeSeconds, float MeterSweepSeconds, float ThresholdSweepSeconds,
    float LevelTextFadeSeconds, uint LevelTextHoldMilliseconds, uint LevelTextNextTimestampOffset,
    int MeterInsetX, int MeterInsetY, string AmountFormat, string LevelFormat,
    string OpacitySetting, string GainSound, string LevelSound)
{
    internal void Validate()
    {
        if (ExecutableSha256.Length != 64 || Contract.Length != 64 ||
            !float.IsFinite(MeterFadeSeconds) || MeterFadeSeconds <= 0 ||
            !float.IsFinite(MeterSweepSeconds) || MeterSweepSeconds <= 0 ||
            !float.IsFinite(ThresholdSweepSeconds) || ThresholdSweepSeconds <= 0 ||
            !float.IsFinite(LevelTextFadeSeconds) || LevelTextFadeSeconds <= 0 || LevelTextHoldMilliseconds == 0 ||
            LevelTextNextTimestampOffset == 0 || MeterInsetX <= 0 || MeterInsetY <= 0 ||
            AmountFormat != "+%i" || LevelFormat != "%i" || string.IsNullOrWhiteSpace(OpacitySetting) ||
            string.IsNullOrWhiteSpace(GainSound) || string.IsNullOrWhiteSpace(LevelSound))
            throw new InvalidDataException("Experience HUD source declaration is incomplete.");
    }
}

internal sealed class FalloutExperienceHudSource
{
    internal const string MenuPath = "menus/main/hud_main_menu.xml";
    internal static readonly string[] Tiles =
        ["XPMeter", "XPBracket", "XPPointer", "XPAmount", "XPLabel", "XPLastLevel", "XPNextLevel", "XPLevelUp"];
    internal FalloutExperienceHudDeclaration Declaration { get; }
    internal string Identity { get; }
    internal string MenuSha256 { get; }
    internal string Contract { get; }
    internal string StatsLabel { get; }
    internal string LevelUpLabel { get; }
    internal XElement Menu { get; }

    internal FalloutExperienceHudSource(FalloutPluginStack records, RuntimeLiveContentSource source)
    {
        if (!ReferenceEquals(records.OwnedSource, source))
            throw new InvalidDataException("Experience HUD records differ from the selected source.");
        if (!source.TryRead(MenuPath, null, out var bytes, out var identity))
            throw new FileNotFoundException("Experience HUD source menu is missing.", MenuPath);
        Identity = identity; MenuSha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        Declaration = FalloutExecutableStringTable.ReadExperienceHud(source.FalloutExecutablePath);
        var expanded = FalloutMenuXml.Expand(source, FalloutMenuXml.Read(source, MenuPath));
        var root = expanded.Elements("menu").Single();
        Menu = new XElement(root.Name, root.Attributes(),
            root.Elements().Where(value => value.Attribute("name") is null && value.Name != "template").Select(value => new XElement(value)));
        Menu.Add(new XElement(root.Elements().Single(value => (string?)value.Attribute("name") == Tiles[0])));
        foreach (var name in Tiles)
        {
            var found = Menu.DescendantsAndSelf().Where(value => (string?)value.Attribute("name") == name).ToArray();
            if (found.Length != 1) throw new NotSupportedException($"Experience HUD tile {name} is absent or ambiguous.");
        }
        if (Tile("XPMeter").Name != "rect" || Tile("XPBracket").Name != "image" || Tile("XPPointer").Name != "image" ||
            Tiles.Skip(3).Any(name => Tile(name).Name != "text"))
            throw new NotSupportedException("Experience HUD source tile roles changed.");
        var settings = FalloutInstallationSettings.Read(source);
        var opacity = settings.Number(Declaration.OpacitySetting);
        if (!float.IsFinite(opacity) || opacity is < 0 or > 1)
            throw new InvalidDataException("Experience HUD opacity is outside its source interval.");
        StatsLabel = FalloutGameSettingStrings.Read(records, "sStatsXP");
        LevelUpLabel = FalloutGameSettingStrings.Read(records, "sLevelUp");
        var labels = new[] { StatsLabel, LevelUpLabel };
        Contract = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source.StackId + "\0" + Declaration.Contract + "\0" +
            MenuSha256 + "\0" + Menu.ToString(SaveOptions.DisableFormatting) + "\0" +
            opacity.ToString("R", CultureInfo.InvariantCulture) + "\0" + string.Join('\0', labels)))).ToLowerInvariant();
    }

    internal XElement Tile(string name) => Menu.DescendantsAndSelf().Single(value => (string?)value.Attribute("name") == name);
    internal static string Integer(string format, int value, int bufferBytes)
    {
        if (format is not ("%i" or "+%i") || bufferBytes <= 1)
            throw new NotSupportedException("Experience HUD integer formatting is unbound.");
        var result = (format == "+%i" ? "+" : "") + value.ToString(CultureInfo.InvariantCulture);
        if (result.Length >= bufferBytes)
            throw new NotSupportedException("XP source integer exceeds its original buffer; its CRT truncation/status consumer is unowned.");
        return result;
    }
}
