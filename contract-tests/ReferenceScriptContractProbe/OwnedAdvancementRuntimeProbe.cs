using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

internal static class OwnedAdvancementRuntimeProbe
{
    internal static void Run(string dataRoot, string campaign)
    {
        RuntimeLiveContentSource.Configure(dataRoot, campaign);
        using var content = RuntimeLiveContentSource.Current!;
        using var records = FalloutPluginStack.Load(content.PluginSources);
        using var owner = FalloutAdvancementRuntimeSource.Open(records);
        var rules = FalloutLevelUpRules.Read(records, owner.Receipt);
        rules.Validate();
        Console.WriteLine("OPENNV_OWNED_ADVANCEMENT_SOURCE_PASS " + JsonSerializer.Serialize(new
        {
            engine = owner.Receipt.EngineSha256,
            contract = owner.Receipt.ContractSha256,
            cadence = owner.Receipt.PerkCadence,
            interval = rules.LevelsPerPerk,
            maximumLevel = rules.MaximumLevel,
            taggedMultiplier = rules.TaggedSkillMultiplier,
            skillBase = rules.SkillPointBase,
            intelligenceMultiplier = rules.IntelligenceMultiplier,
            ordinaryGameplay = "unexecuted",
        }));
    }
}
