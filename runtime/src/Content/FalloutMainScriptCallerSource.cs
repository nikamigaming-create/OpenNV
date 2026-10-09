using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Content;

// These declarations describe the selected original Main segment. They do not
// execute its executable, select content or identify a rendering frame.
internal sealed record FalloutMainScriptCallerSource(string EngineSha256, string RuntimeSha256,
    string ImmediateSource, string ContractSha256)
{
    private const string Contract = "source-Main-script-caller/v1;clock-prelude-noop;OS-Tab-then-Alt-high-bit-short-circuit;" +
        "source-prologue-before-interface-predicates;menu-gate-short-circuit-GUI-mode-two;" +
        "three-other-independent-interface-cache-fields-loader-zero;actual-query-before-each-cache-store;" +
        "first-interface-predicate-before-first-channel-one-sample;final-interface-predicate-after-sample;" +
        "foreign-active-menu-and-kind-three-prelude;Player-child-before-SteamAPI_RunCallbacks-before-timed-context-child;" +
        "timed-context-argument-not-menu-gate-and-not-independent-Main-hold;" +
        "refresh-menu-and-first-interface-predicate-before-second-channel-one-sample;final-predicate-after-second;" +
        "separate-context-clock-loader-zero-wide-add-Float32-store-no-guard;" +
        "genuine-ordered-child-return-required;scope-return-does-not-return-unowned-whole-Main;" +
        "failed-entered-prefix-not-replayed;current-source-process-and-native-delivery-lease-independent";
    internal string Identity => FalloutAdvancementRuntimeReceipt.Hash(EngineSha256 + "\0" + RuntimeSha256 +
        "\0" + ImmediateSource + "\0" + ContractSha256);
    internal uint InitialContextTimeBits => 0;
    internal bool HasNewVegasChildren => FalloutSourceMainFamily.HasNewVegasChildren(EngineSha256);
    internal static FalloutMainScriptCallerSource Read(FalloutImmediateScriptSource source)
    {
        source.Validate();
        return new(source.EngineSha256, source.RuntimeSha256, source.Identity,
            FalloutAdvancementRuntimeReceipt.Hash(FalloutSourceMainFamily.MainRules(source.EngineSha256, Contract)));
    }
    internal void Validate()
    {
        var selectedRules = FalloutSourceMainFamily.MainRules(EngineSha256, Contract);
        if (!FalloutAdvancementRuntimeReceipt.Digest(RuntimeSha256) || !FalloutAdvancementRuntimeReceipt.Digest(ImmediateSource) ||
            ContractSha256 != FalloutAdvancementRuntimeReceipt.Hash(selectedRules))
            throw new InvalidDataException("Source Main script caller changed its selected runtime or neutral declaration.");
        var immediate = new FalloutImmediateScriptSource(EngineSha256, RuntimeSha256, FalloutImmediateScriptSource.ContractForEngine(EngineSha256));
        immediate.Validate();
        if (ImmediateSource != immediate.Identity) throw new InvalidDataException("Main caller changed its real shared interpreter/cache declaration.");
    }
}
