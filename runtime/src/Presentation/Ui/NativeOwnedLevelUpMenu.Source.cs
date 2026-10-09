using System.Globalization;
using System.Security.Cryptography;
using System.Xml.Linq;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Presentation.Ui;

// All source/resource work precedes native Control allocation. This prepared
// tree is one request's view, not another skill, rank or completion owner.
internal sealed class NativeLevelUpMenuSource
{
    internal const string LogicalPath = "menus/levelup_menu.xml";
    internal const uint MenuId = 0x403;
    internal NativeOwnedMenuTree Tiles { get; }
    internal FalloutLevelUpMenuCatalogue Catalogue { get; }
    internal string Identity { get; }
    internal string Sha256 { get; }
    private readonly FalloutPluginStack _records;

    internal NativeLevelUpMenuSource(FalloutPluginStack records, FalloutLevelUpMenuCatalogue catalogue,
        FalloutLevelUpMenuSession session, FalloutUiComponentStore? scriptUi)
    {
        catalogue.RequireSession(session);
        var source = RuntimeLiveContentSource.Current ?? throw new InvalidOperationException("Native level-up has no selected owned UI source.");
        if (records.OwnedSource is { } selected && !ReferenceEquals(source, selected))
            throw new InvalidDataException("Level-up XML and record catalog belong to different selected installations.");
        if (!source.TryRead(LogicalPath, null, out var bytes, out var identity))
            throw new FileNotFoundException("The selected level-up menu XML is absent.", LogicalPath);
        Identity = identity; Sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        _records = records; Catalogue = catalogue;
        var menu = FalloutMenuXml.Expand(source, FalloutMenuXml.Parse(bytes)).Elements("menu").Single();
        var declared = menu.Element("class")?.Value.Trim() ?? throw new InvalidDataException("Level-up menu has no source class.");
        var id = declared.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? uint.Parse(declared[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture)
            : uint.Parse(declared, NumberStyles.Integer, CultureInfo.InvariantCulture);
        if (id != MenuId) throw new NotSupportedException("The selected menu class is not the level-up input contract.");
        Tiles = new(menu, name => FalloutGameSettingStrings.Read(records, name), scriptUi);
        foreach (var name in new[] { "LUM_SkillList", "LUM_PerkList", "LUM_Headline_Title", "LUM_SelectionText",
            "LUM_SelectionIcon", "LUM_PointCounter", "LUM_ResetButton", "LUM_ContinueButton", "LUM_BackButton",
            "LUM_SkillTemplate", "LUM_PerkTemplate" }) _ = Named(name);
        foreach (var (name, expected) in new[] { ("LUM_ResetButton", 6), ("LUM_ContinueButton", 7), ("LUM_BackButton", 8) })
            if (Tiles.Number(Named(name), "id") != expected)
                throw new NotSupportedException("The selected level-up action identity requires a different input owner.");
        foreach (var (name, expected) in new[] { ("LUM_Template_LeftArrow", 13), ("LUM_Template_RightArrow", 14) })
            if (Tiles.Number(Named(name), "id") != expected)
                throw new NotSupportedException("The selected level-up arrow identity requires a different input owner.");
        var skillRow = Named("LUM_SkillTemplate").Elements().Single();
        if (Tiles.Number(skillRow, "_MinValue") != 0 || Tiles.Number(skillRow, "_MaxValue") != 100)
            throw new NotSupportedException("The selected skill row limits require a different admitted input contract.");
        // These executable-declared GMST identities are read through winning
        // overrides. No English fallback or copied XML/text enters the view.
        _ = Title(session.Level); _ = Counter(FalloutLevelUpPage.Skills, session.Budget);
        _ = Counter(FalloutLevelUpPage.Perks, 1);
    }
    internal XElement Named(string name) => Tiles.Root.DescendantsAndSelf().Single(tile =>
        string.Equals((string?)tile.Attribute("name"), name, StringComparison.OrdinalIgnoreCase));
    internal uint RuntimeFormId(FalloutFormKey form) => _records.RuntimeFormId(form);
    internal string Title(int level) => Format("sLevelUpTitleText", level, optionalArgument: true);
    internal string Counter(FalloutLevelUpPage page, int remaining) => Format(page == FalloutLevelUpPage.Skills
        ? remaining == 1 ? "sLevelUpSkillCounter" : "sLevelUpSkillCounterPl"
        : remaining == 1 ? "sLevelUpPerkCounter" : "sLevelUpPerkCounterPl", remaining);
    private string Format(string setting, int value, bool optionalArgument = false)
    {
        var text = FalloutGameSettingStrings.Read(_records, setting);
        if (optionalArgument && !text.Contains('%')) return text;
        return FalloutTagSkillMenuSelection.FormatCounts(text, value);
    }
}
