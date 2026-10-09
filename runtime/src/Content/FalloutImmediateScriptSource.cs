using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Content;

// Build selection owns the implementation-neutral interpreter and Main-field
// contracts; neither a challenge's name nor a menu's presence selects them.
internal sealed record FalloutImmediateScriptSource(string EngineSha256, string RuntimeSha256, string ContractSha256)
{
    private const string Contract = "source-loader-zero-Main-cached-byte;two-ordered-Main-interface-channel1-samples;" +
        "actual-native-pointer-and-increasing-byte-and-ordered-opacity-equal-one;" +
        "GameMode-menu-short-circuit-then-cached-byte-negation-double-zero-or-one;" +
        "shared-synchronous-interpreter-top-level-cache-nested-fresh-owner-restore-parent;" +
        "source-run-float32-seconds;immediate-object-script-player-fresh-event-list-seconds-zero;" +
        "filtered-block-has-no-compiled-execution-receipt;actual-entered-cursor-retirement-only";
    internal string Identity => FalloutAdvancementRuntimeReceipt.Hash(EngineSha256 + "\0" + RuntimeSha256 + "\0" + ContractSha256);
    internal static FalloutImmediateScriptSource Read(FalloutAdvancementRuntimeReceipt runtime)
    {
        runtime.Validate();
        var result = new FalloutImmediateScriptSource(runtime.EngineSha256, runtime.SourceSha256,
            FalloutAdvancementRuntimeReceipt.Hash(Contract));
        result.Validate(); return result;
    }
    internal static FalloutImmediateScriptSource Read(FalloutChallengeEventSource challenges)
    {
        challenges.Validate();
        var source = new FalloutImmediateScriptSource(challenges.EngineSha256, challenges.RuntimeSha256,
            FalloutAdvancementRuntimeReceipt.Hash(Contract));
        source.Validate(); return source;
    }
    internal void Validate()
    {
        if (EngineSha256 != "518c87f58a6c4d9826e9ef8fbb7f4213882fa70822675610d45aea2464502a57")
            throw new NotSupportedException("Selected engine has no reviewed immediate challenge interpreter/Main scalar owner.");
        if (!FalloutAdvancementRuntimeReceipt.Digest(RuntimeSha256) ||
            ContractSha256 != FalloutAdvancementRuntimeReceipt.Hash(Contract))
            throw new InvalidDataException("Immediate script source contract/runtime identity differs from its selected owner.");
    }
}
