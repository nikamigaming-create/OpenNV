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
    internal string Identity => FalloutAdvancementRuntimeReceipt.Hash(Main.Identity + "\0" + Contract);
    internal uint InitialFrameCounter => 1;
    internal uint InitialHavokTauBits => 0x3f800000;
    internal static FalloutMainCachedTailSource Read(FalloutMainScriptCallerSource main)
    {
        main.Validate();
        if (main.EngineSha256 != Engine) throw new NotSupportedException("Selected post-sampling Main clock tail is unowned.");
        return new(main, FalloutAdvancementRuntimeReceipt.Hash(Rules));
    }
    internal void Validate()
    {
        if (this != Read(Main)) throw new InvalidDataException("Main cached tail changed its actual selected source sequence.");
    }
}
