namespace OpenNV.Runtime.Content;

internal sealed record FalloutPlayerPostAnimationSource(FalloutPlayerReturnTailSource Tail, string Contract)
{
    internal const int PreparationGroupSlot = 4;
    internal const byte FirstTimedPreparationGroup = 0x4a, SecondTimedPreparationGroup = 0x4d;
    private const string Rules = "source-FO3-Player-post-animation/v1;" +
        "one-SAME-Scene-view-field-for-outer-load-temporary-one-exact-prior-restore-and-two-frame-toggles;" +
        "initial-view-nonzero-prepares-before-fresh-view-read-and-toggle-zero-prepares-after-toggle;" +
        "prepare-current-life-false-before-signed-activity-before-current-Player-view-before-selected-AnimData;" +
        "selected-override-before-current-view-first-nonnull-otherwise-current-Common;" +
        "controller-getter-before-independent-context-process-controller-null-return;" +
        "source-process-prerequisite-child-has-no-body-effects-on-either-arm;" +
        "selected-current-slot-four-sequence-and-group-before-independent-direct-first-same-slot;" +
        "current-null-slot-and-untimed-group-skips-only-qualified-sequence-transition-children;" +
        "actual-action-and-group-selection-remain-required-after-preparation-prefix;" +
        "status-cache-before-status-early-return-each-view-has-distinct-AnimData-frame-bracket;" +
        "camera-false-CELL-attach-true-false-independent-resource-offer-water-false-inhibition-read-water-store-branch;" +
        "current-input-life-values-before-real-thread-kind-restore-return-and-outer-bracket-leave";
    internal string Identity => FalloutAdvancementRuntimeReceipt.Hash(Tail.Identity + "\0" + Contract);
    internal static FalloutPlayerPostAnimationSource Reviewed(FalloutPlayerReturnTailSource tail)
    { tail.Validate(); return new(tail, FalloutAdvancementRuntimeReceipt.Hash(Rules)); }
    internal void Validate()
    {
        Tail.Validate();
        if (this != Reviewed(Tail)) throw new InvalidDataException("Player post-animation source declaration changed.");
    }
}
