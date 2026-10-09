using System.Text.Json;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Content;

// These are engine consumer identities. No challenge, plugin, record name or
// outcome selects the implementation. Source settings retain their real owner.
internal sealed record FalloutChallengeEventSource(string EngineSha256, string RuntimeSha256,
    string StatisticSourceSha256, string ContractSha256, FalloutIniValue NoticeSetting,
    string SettingsSha256, ushort CompletionStatistic, uint StatisticEvent)
{
    private const string Contract = "source-CHAL-registration-order;event11-null-form-filters;" +
        "signed-int32-progress-add-and-threshold;original-word-filters;" +
        "immediate-object-script-player-event-list;completion-statistic-call;" +
        "source-notice-setting-HUD-before-interface-cue;raw-runtime-flags;" +
        "recurring-remainder-after-callbacks;attempted-prefix-no-replay";
    internal static string CurrentContractSha256 => FalloutAdvancementRuntimeReceipt.Hash(Contract);
    internal string Identity => FalloutAdvancementRuntimeReceipt.Hash(JsonSerializer.Serialize(this));
    internal bool ShowNotices => NoticeSetting.Number != 0;

    internal static FalloutChallengeEventSource Read(FalloutMiscellaneousStatisticSource statistics,
        FalloutNumericIniSettings settings)
    {
        statistics.Validate(); ArgumentNullException.ThrowIfNull(settings);
        if (statistics.ChallengeEvent != 11)
            throw new NotSupportedException("This selected statistic consumer has no CHAL event11 dispatcher.");
        var setting = settings.Find(FalloutIniCollection.Main, "bShowChallengeUpdates:GamePlay") ??
            throw new NotSupportedException("Challenge notice visibility has no original main INI declaration.");
        var identity = FalloutAdvancementRuntimeReceipt.Hash(JsonSerializer.Serialize(new
        { settings.Source, settings.Layers, setting }));
        var result = new FalloutChallengeEventSource(statistics.EngineSha256, statistics.RuntimeSha256,
            statistics.Identity, CurrentContractSha256, setting, identity, 27, 11);
        result.Require(statistics); return result;
    }

    internal void Validate()
    {
        if (EngineSha256 != "518c87f58a6c4d9826e9ef8fbb7f4213882fa70822675610d45aea2464502a57" ||
            !FalloutAdvancementRuntimeReceipt.Digest(RuntimeSha256) ||
            !FalloutAdvancementRuntimeReceipt.Digest(StatisticSourceSha256) ||
            !FalloutAdvancementRuntimeReceipt.Digest(SettingsSha256) || ContractSha256 != CurrentContractSha256 ||
            CompletionStatistic != 27 || StatisticEvent != 11 || NoticeSetting is null ||
            NoticeSetting.Declaration.Name != "bShowChallengeUpdates:GamePlay" ||
            NoticeSetting.Declaration.Collection != FalloutIniCollection.Main || NoticeSetting.Declaration.Kind != 'b' ||
            NoticeSetting.Number is not (0 or 1) || string.IsNullOrWhiteSpace(NoticeSetting.Origin))
            throw new InvalidDataException("Challenge event source/setting/consumer identity is incomplete.");
    }

    internal void Require(FalloutMiscellaneousStatisticSource statistics)
    {
        Validate(); statistics.Validate();
        if (statistics.Identity != StatisticSourceSha256 || statistics.EngineSha256 != EngineSha256 ||
            statistics.RuntimeSha256 != RuntimeSha256 || statistics.ChallengeEvent != StatisticEvent ||
            CompletionStatistic >= statistics.Rows.Count)
            throw new InvalidDataException("Challenge dispatcher and real statistic constructor differ.");
    }
}
