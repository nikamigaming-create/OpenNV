namespace OpenNV.Runtime.Content;

internal sealed record FalloutPlayerFollowingContinuationSource(FalloutPlayerMovementControlSource Controls, string Contract)
{
    internal const uint PackageUnsetTimeBits = 0xbf800000;
    internal const int ReadyItemControl = 7;
    private const string Rules = "source-FO3-Player-following-package-prefix/v2;" +
        "movement-and-following-borrow-one-Player-inhibition-owner;" +
        "constructor-third-literal-zero-fourth-completed-loop-zero-second-zero-callback-first-zero-callback;" +
        "selected-Player-Actor-inhibition-callback-void-ignores-its-argument;" +
        "first-constructor-callback-argument-unwritten-sibling-never-fabricated;" +
        "ordered-first-second-third-fourth-current-bytes-first-nonzero-selects-unowned-late-tail;" +
        "current-existing-Actor-life-predicate-false-accepts-one-two-six-true-accepts-one-two;" +
        "life-true-selects-same-unowned-late-tail;" +
        "Base-current-package-and-MiddleHigh-primary-package-own-independent-complete-six-word-states;" +
        "package-context-extra-null-null-zero-index-minus-one-time-minus-one-flags-zero;" +
        "Low-common-copy-moves-only-package-context-index-time-and-clears-only-those-source-fields;" +
        "current-then-independent-current-process-primary-getters-null-skips-each-clear;" +
        "nonnull-current-clear-and-primary-clear-callback-remain-independent-unowned;" +
        "actual-package-assignment-ingress-invalidates-unowned-slots-before-semantic-state-writes;" +
        "Player-constructor-timer-byte-zero-skips-scalar-without-reading-constructing-or-writing-it;" +
        "nonnull-timer-writers-and-delta-and-threshold-remain-independent;" +
        "Ready-Item-control-seven-rising-then-zero-held-then-zero-falling-Int32-count-queries;" +
        "all-three-zero-selects-independent-global-stores-positive-rising-or-held-selects-disabled-word-child;" +
        "positive-falling-skips-global-stores-and-selects-following-frame-services;" +
        "failed-prefix-current-owner-thread-source-epoch-Main-invocation-and-cold-history-no-replay";

    internal string Identity => FalloutAdvancementRuntimeReceipt.Hash(Controls.Identity + "\0" + Contract);
    internal static FalloutPlayerFollowingContinuationSource Reviewed(FalloutPlayerMovementControlSource controls)
    {
        controls.Validate(); return new(controls, FalloutAdvancementRuntimeReceipt.Hash(Rules));
    }
    internal void Validate()
    {
        if (Controls.Main.EngineSha256 != FalloutSourceMainFamily.Fallout3 || this != Reviewed(Controls))
            throw new InvalidDataException("Following Player fields lost their selected original source contract.");
    }
}
