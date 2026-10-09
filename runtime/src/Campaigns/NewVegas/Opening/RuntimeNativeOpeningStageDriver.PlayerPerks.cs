using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Campaigns.NewVegas.Opening;

internal partial class RuntimeNativeOpeningStageDriver
{
    internal int PlayerPerkRank(FalloutFormKey perk) =>
        _scripts.References!.PerkRank(_pluginStack.RuntimeFormKey(0x14), perk);

    internal void AcquirePlayerPerkRank(FalloutFormKey perk, int rank) =>
        _scripts.References!.SetPerkRank(_pluginStack.RuntimeFormKey(0x14), perk, rank);

    private void ExecutePlayerPerkQuestStage(FalloutPerkQuestStage effect)
    {
        if (effect.Stage > short.MaxValue)
            throw new NotSupportedException("Perk quest stage requires the original unsigned stage owner.");
        RequestSourceStage(effect.Quest, (short)effect.Stage);
    }
}
