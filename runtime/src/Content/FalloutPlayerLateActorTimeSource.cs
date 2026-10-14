namespace OpenNV.Runtime.Content;

// Only the selected Actor-time child is admitted by this declaration. It
// neither returns the enclosing Player virtual nor constructs a model.
internal sealed record FalloutPlayerLateActorTimeSource(
    FalloutPlayerFollowingContinuationSource Following, string Contract)
{
    internal const byte InitialMobileState = byte.MaxValue;
    internal const byte InitialSelfPredicate = 1;
    internal const byte InitialDateMismatch = 1;
    internal const byte InitialAiBlocked = 0;
    internal const uint ReferenceNoUpdateFlag = 0x800;
    internal const uint ActorConstructionFlag = 0x200000;
    internal const uint PackedDayMask = 0x1ff;
    internal const string HardwareThreadsSetting = "iNumHWThreads:General";
    private const string Rules = "source-FO3-Player-late-Actor-time/v2;" +
        "selected-following-four-inhibition-branches-and-life-branch-share-tail;" +
        "Actor-time-only-when-desired-model-view-equals-current-model-view;" +
        "raw-reference-flag-eight-hundred-before-actual-Actor-update-byte;" +
        "inherited-Mobile-constructor-packed-word-low-byte-ff;" +
        "Mobile-update-zero-one-three-have-independent-nonempty-children;" +
        "other-current-byte-values-store-ff-and-return;" +
        "actual-Player-singleton-self-predicate-returns-false-before-nonplayer-children;" +
        "returned-predicate-stores-independent-Actor-byte-before-current-Main-scaled-timer;" +
        "Main-model-gated-clock-increments-shared-byte-before-same-manager-ratio-getter;" +
        "ratio-loader-one-over-one-same-manager-kind-field-as-Actor-time;" +
        "clock-multiplies-current-cached-Main-scalar-before-Float32-argument-store;" +
        "selected-Player-virtual-before-sole-returned-shared-byte-decrement;" +
        "positive-remaining-Float32-subtracts-scaled-timer-without-clamp-otherwise-word-zero;" +
        "process-life-false-before-packed-day-low-nine-bits-before-current-calendar-day-signed-byte;" +
        "Low-constructor-packed-day-zero-Low-to-High-copies-that-distinct-word;" +
        "Actor-constructor-date-mismatch-byte-one;" +
        "mismatch-with-zero-byte-reaches-weekday-and-reference-Extra-child-before-byte-one;" +
        "equal-day-stores-zero-mismatch-stores-one;" +
        "High-process-raw-tier-zero-before-actual-Player-signed-travel-counter-positive;" +
        "nonzero-tier-or-positive-travel-reaches-value-and-modifier-update;" +
        "independent-life-false-before-Actor-AI-byte-before-shared-global-AI-byte-before-raw-six;" +
        "shared-global-AI-zero-skips-AI-package-and-date-children;" +
        "global-AI-loader-zero-sole-selected-command-writer-distinct-from-Actor-update-byte;" +
        "independent-life-false-before-death-process-and-timer-children;" +
        "current-signed-hardware-thread-count-one-High-tier-zero-pure-Interface-false-before-values-false;" +
        "values-false-current-Actor-construction-flag-returns-before-kind-fifty;" +
        "actual-Player-singleton-skips-final-nonplayer-spatial-correction;" +
        "ordered-partial-stores-sticky-failure-real-source-scope-bounded-latest-history-cold-no-replay";
    internal string Identity => FalloutAdvancementRuntimeReceipt.Hash(Following.Identity + "\0" + Contract);
    internal static FalloutPlayerLateActorTimeSource Reviewed(FalloutPlayerFollowingContinuationSource following)
    {
        following.Validate(); return new(following, FalloutAdvancementRuntimeReceipt.Hash(Rules));
    }
    internal void Validate()
    {
        if (this != Reviewed(Following)) throw new InvalidDataException("Late Actor-time source declaration changed.");
    }
}
