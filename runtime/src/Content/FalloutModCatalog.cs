namespace OpenNV.Runtime.Content;

internal sealed record FalloutModDefinition(string Id, string Title, IReadOnlyList<string> PluginChoices,
    IReadOnlyList<FalloutInstallationSetting>? Settings = null);

/// <summary>Launcher identities and authored entry filenames, not a runtime capability list.</summary>
internal static class FalloutModCatalog
{
    internal static IReadOnlyList<FalloutModDefinition> All { get; } =
    [
        new("jam", "JAM", ["JustAssortedMods.esp"]),
        // TTW's authored installation requirements. These configure the selected
        // source profile; they never edit the player's installation or INIs.
        new("ttw", "Tale of Two Wastelands", ["TaleOfTwoWastelands.esm"],
            [new("General", "SCharGenQuest", "001FFFF8"), new("General", "SIntroMovie", "")]),
        new("yup", "YUP", ["YUP - Base Game + All DLC.esm"]),
        new("jsawyer", "JSawyer Ultimate", ["JSawyer Ultimate.esp"]),
        new("uncut-wasteland", "Uncut Wasteland", ["Uncut Wasteland.esp"]),
        new("living-desert", "The Living Desert", ["TLD_Travelers.esm"]),
        new("nmc", "NMC Texture Pack", []),
        new("eve", "EVE", ["EVE FNV - ALL DLC.esp", "EVE FNV - NO GRA.esp", "EVE FNV - NO DLC.esp"]),
        new("nevada-skies", "Nevada Skies", ["NevadaSkies.esp"]),
        new("bounties", "New Vegas Bounties I", ["NewVegasBounties.esp"]),
    ];

    internal static FalloutModDefinition Get(string id) => All.FirstOrDefault(value => value.Id == id)
        ?? throw new ArgumentException($"Unknown mod: {id}.", nameof(id));
}
