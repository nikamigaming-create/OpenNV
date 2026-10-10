namespace OpenNV.Runtime.Content;

internal sealed record FalloutMainCachedTailSource(FalloutMainScriptCallerSource Main, string Contract)
{
    private const string Engine = "518c87f58a6c4d9826e9ef8fbb7f4213882fa70822675610d45aea2464502a57";
    private const string Rules = "source-Main-cached-tail/v1;after-second-sample-and-final-cache-store;" +
        "independent-loader-zero-optional-Player-byte;source-request-manager-three-zero-countdown-bytes;" +
        "countdown-first-second-third-short-circuit;greater-than-one-decrement;one-fade-gates-before-real-request;" +
        "two-independent-loader-fields-reset;menu-actor-reset-byte-zero-game-sets-one-menu-consumes-once;" +
        "actual-ordered-actor-registry-native-reset-and-Player-reset;selected-empty-source-object-child;" +
        "source-delay-UInt32-loader-zero-clamp-twenty-public-OS-wait-before-zero-store;" +
        "frame-counter-file-initial-one-wrap;independent-word-zero-store;nonmenu-game-counter-loader-zero-wrap;" +
        "INI-bChangeTimeMultSlowly-before-nonmenu-rate-adjust-before-real-cached-timer-update;" +
        "file-initial-Havok-tau-one;winning-fHavokTauRatio-before-separate-render-clock-child;" +
        "four-source-callback-cache-stores-before-menu-zero-or-scaled-time-times-fAnimationMult;" +
        "remaining-whole-Main-children-independent;actual-failed-prefix-and-cold-without-source-replay";
    private const string Fallout3Rules = "source-FO3-Main-cached-tail/v1;after-second-sample-and-final-cache-store;" +
        "loader-zero-optional-Player-byte;selected-save-request-constructor-two-zero-countdown-bytes;" +
        "first-then-second-priority;greater-than-one-decrement;one-fade-one-two-and-interface-gates-before-real-request;" +
        "first-request-clear-before-child-second-request-clear-after-child;two-independent-loader-fields-reset;" +
        "menu-actor-reset-byte-zero-game-sets-one-menu-consumes-once;ordered-registry-and-Player-reset;" +
        "UInt32-delay-loader-zero-clamp-twenty-OS-wait-before-zero-store;file-initial-frame-counter-one-wrap;" +
        "independent-word-zero-store;nonmenu-game-counter-loader-zero;INI-rate-mode-before-nonmenu-adjust-before-actual-counter-update;" +
        "file-initial-Havok-tau-one-winning-setting-copy-before-separate-renderer-clock-child;" +
        "four-source-callback-cache-stores;menu-zero-otherwise-Float32-animation-multiplier-store;" +
        "remaining-Main-children-independent;entered-failure-and-cold-without-replay";
    internal int RequestCount => Main.HasNewVegasChildren ? 3 : 2;
    internal bool ClearRequestBeforeDispatch(int request) => !Main.HasNewVegasChildren && request == 0;
    internal bool ClearRequestAfterDispatch(int request) => request == 1;
    internal string Identity => FalloutAdvancementRuntimeReceipt.Hash(Main.Identity + "\0" + Contract);
    internal uint InitialFrameCounter => 1;
    internal uint InitialHavokTauBits => 0x3f800000;
    internal static FalloutMainCachedTailSource Read(FalloutMainScriptCallerSource main)
    {
        main.Validate();
        var rules = main.EngineSha256 switch
        {
            Engine => Rules,
            FalloutSourceMainFamily.Fallout3 => Fallout3Rules,
            _ => throw new NotSupportedException("Selected post-sampling Main clock tail is unowned.")
        };
        return new(main, FalloutAdvancementRuntimeReceipt.Hash(rules));
    }
    internal void Validate()
    {
        if (this != Read(Main)) throw new InvalidDataException("Main cached tail changed its actual selected source sequence.");
    }
}
