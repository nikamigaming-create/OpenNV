namespace OpenNV.Runtime.Content;

internal sealed record FalloutReferenceFollowerSource(FalloutReferencePositionSource Reference, string Contract)
{
    internal const byte ExtraKind = FalloutActorPackageAssignmentSource.FollowerExtraType;
    private const string Rules = "source-FO3-reference-follower-Extra-membership/v1;" +
        "same-actual-reference-Extra-list-source-constructor-null-head-and-zero-presence;" +
        "type-twenty-nine-membership-predicate-before-thread-cache-and-list-lookup;" +
        "independent-current-lane-source-prefix-and-type-specific-writer-preservation;" +
        "Extra-126-116-Script-13-persistent-18-and-saved-24-preserve-follower-membership;" +
        "unknown-membership-copy-load-native-or-Script-callback-invalidates-before-read;" +
        "absent-returns-present-null-payload-returns-nonnull-ordered-Actor-payload-independent;" +
        "cold-retained-membership-and-writer-history-fresh-reference-binding-no-replay";
    internal string Identity => FalloutAdvancementRuntimeReceipt.Hash(Reference.Identity + "\0" + Contract);
    internal static FalloutReferenceFollowerSource Reviewed(FalloutReferencePositionSource reference)
    { reference.Validate(); return new(reference, FalloutAdvancementRuntimeReceipt.Hash(Rules)); }
    internal void Validate(FalloutReferencePositionSource reference)
    {
        reference.Validate(); Reference.Validate();
        if (Reference != reference || this != Reviewed(reference))
            throw new InvalidDataException("Follower membership changed its actual reference source or selected lookup.");
    }
}
