namespace OpenNV.Runtime.Content;

internal sealed record FalloutActorPackageAssignmentSource(FalloutPackageAssignmentSource Command,
    FalloutReferencePositionSource Reference, string Contract)
{
    internal const byte CreatureKind = 0x2b;
    internal const uint CreatureSkipsMoverMask = 0x00800000;
    internal const uint AnimationAssignmentMask = 0x800;
    internal const ushort ResetMode = 2;
    internal const byte ResetFlagTwo = 2, ResetFlagOne = 1, ResetFlagForty = 0x40;
    internal const byte FollowerExtraType = 0x1d;
    private const string Rules = "source-FO3-Actor-package-assignment/v2;" +
        "actual-nullable-process-before-requested-procedure-reread-and-signed-kind;" +
        "kind-28-27-26-skip-same-raw-Actor-yaw-status-DWORD;" +
        "Current-adjustment-ordered-positive-reread-before-independent-effects;" +
        "nonkind18-Actor-assignment-byte-read-zero-store-before-selected-eligibility;" +
        "Player-selected-eligibility-true-direct-void-callback;" +
        "same-Current-six-field-mutation-real-Actor-Extra-real-GameHour;" +
        "same-winning-mutable-PACK-target-null-constructor-ordered-PTDT-target-object-copy-current-writer-barrier;" +
        "target-zero-buffer-bounded-actual-short-input-omitted-tail-zero-oversized-read-diagnostic-unowned;" +
        "target-absent-and-non03-return-null-kind0-real-reference-kind3-actual-Actor-Extra81;" +
        "fresh-Actor-byte-and-process-direct-Common-animation-not-Player-selector;" +
        "two-source-linked-null-occurrences-equality-preserves-null-cleanup-tail;" +
        "changed-linked-old-effect-cleanup-store-selected-Primary-else-Current-Extra;" +
        "Shared-reset-word2-same-Common-first-word-zero-independent-byte-mask2-mask1-mask40-mask20;" +
        "actual-inline-value-and-link-list-empty-before-shared-loading-cleanup;" +
        "actual-DataHandler-loading-byte-zero-construction-current-writer-independent;" +
        "High-reset-twelve-width-preserving-ordered-stores-after-returned-Shared-reset;" +
        "same-logical-mover-after-returned-package-linked-and-High-children;" +
        "actual-current-Base-creature-bit23-predicate-independent-follower-Extra29;" +
        "distinct-current-process-tier-DWORD-High-ctor-zero-not-class-level;" +
        "source-qualified-primitive-children-failed-prefix-unknown-writer-barriers";

    internal string Identity => FalloutAdvancementRuntimeReceipt.Hash(Command.Identity + "\0" + Reference.Identity + "\0" + Contract);
    internal static FalloutActorPackageAssignmentSource Reviewed(FalloutPackageCommandFieldsSource fields) =>
        new(FalloutPackageAssignmentSource.Reviewed(fields), FalloutReferencePositionSource.Reviewed(fields),
            FalloutAdvancementRuntimeReceipt.Hash(Rules));
    internal void Validate()
    {
        Command.Validate(); Reference.Validate();
        if (Reference.Fields != Command.Fields || this != Reviewed(Command.Fields))
            throw new InvalidDataException("Actor assignment lost its original command, reference fields or selected graph.");
    }
    internal static bool SkipsStatus(sbyte kind) => kind is 28 or 27 or 26;
    internal static bool IsPositiveAdjustment(uint bits) => BitConverter.UInt32BitsToSingle(bits) > 0;
    internal static bool SkipsMover(byte kind, uint? creatureFlags)
    {
        if (kind != CreatureKind) return false;
        return ((creatureFlags ?? throw new NotSupportedException("source-Actor-package-current-creature-flags-unavailable")) &
            CreatureSkipsMoverMask) != 0;
    }
}
