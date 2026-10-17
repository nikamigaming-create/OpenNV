namespace OpenNV.Runtime.Content;

internal sealed record FalloutPlayerThreadContextSource(FalloutMainScriptCallerSource Main, string Contract,
    uint InitialKind, byte InitialInitialized, uint PlayerKind, uint InitialModel = 0, uint InitialReference = 0, uint InitialCollisionMode = 0)
{
    private const string Rules = "source-FO3-Player-thread-context/v4;" +
        "current-source-calling-thread-loaded-kind-fifty-nine-and-initialized-zero;" +
        "on-demand-callback-reason-two-initialized-byte-store-before-empty-initializer-range;" +
        "first-initializer-before-previous-kind-read-second-initializer-before-kind-store;" +
        "Player-kind-fifty-two-before-distinct-current-DirectInput-manager-query;" +
        "kind-scope-remains-entered-until-returned-Player-body-before-epilogue;" +
        "epilogue-same-current-thread-index-initializer-and-prior-kind-restore-before-virtual-return;" +
        "successive-source-invocations-reenter-initialized-thread-with-fresh-receipts;" +
        "Menu-transition-allocation-same-on-demand-thread-kind-owner-initializes-before-save-and-enters-thirteen;" +
        "returned-Menu-allocation-restores-actual-captured-prior-kind-and-failed-child-never-invents-restoration;" +
        "Menu-transition-control-mode-four-is-independent-from-current-thread-kind;" +
        "loaded-current-thread-model-and-reference-cache-pointers-null-independent-from-kind-and-common-body;" +
        "Player-model-current-thread-cache-reference-comparison-before-loaded-reference-model-fallback;" +
        "model-cache-writers-remain-independent-and-cold-retains-history-without-replay;" +
        "independent-current-thread-collision-update-mode-starts-at-original-loader-zero-and-is-read-in-the-actual-model-load;" +
        "other-TLS-fields-and-native-module-image-are-independent-unowned-projections;" +
        "cold-state-retains-history-without-native-thread-or-scope-promotion";
    internal string Identity => FalloutAdvancementRuntimeReceipt.Hash(Main.Identity + "\0" + Contract);
    internal static FalloutPlayerThreadContextSource Reviewed(FalloutMainScriptCallerSource main)
    {
        main.Validate();
        if (!FalloutSourceMainFamily.IsFallout3(main.EngineSha256))
            throw new NotSupportedException("Player current-thread context source is unreviewed.");
        return new(main, FalloutAdvancementRuntimeReceipt.Hash(Rules), 59, 0, 52);
    }
    internal void Validate()
    {
        if (this != Reviewed(Main)) throw new InvalidDataException("Current-thread context changed its source loader, initializer or scope declaration.");
    }
}
