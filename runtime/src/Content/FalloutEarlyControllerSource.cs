namespace OpenNV.Runtime.Content;

internal sealed record FalloutEarlyControllerSource(FalloutPackageCommandFieldsSource Fields,
    FalloutPlayerThreadContextSource Thread, string Contract)
{
    internal const uint PhysicsKind = 16, InitialSlopeBits = 0x423c0000, DegreesToRadiansBits = 0x3c8efa35;
    internal const uint MaximumSlopeBits = 0x3fc90fdb, ActorPropertyKey = 1000, ProxyPropertyKey = 0x1300;
    internal const uint PositiveOneBits = 0x3f800000, PositiveZeroBits = 0, StrengthBits = 0x42c80000, MaximumBoundBits = 0x7f7fffee;
    internal const uint HelperConditionalFlag = 0x100000, HelperAlwaysFlag = 0x200000, ResetFlag = 0x80;
    internal const uint PostActionContextFlag = 0x20000;
    internal const uint ArrayEmptyFlag = 0x80000000, ArrayCapacityMask = 0x3fffffff, ArrayRetainedFlag = 0x40000000;
    internal const int ContactIterations = 4, UserPlanes = 10, ManagerSlots = 11;
    internal const uint State2ScalarBits = 0x44fa0000, ContextScalarABits = 0x441c4000, ContextScalarBBits = 0x41a00000;
    internal const byte InitialState = 0;
    internal const ushort PlayerCollisionGroup = 9;
    internal static World.Physics.FalloutPhysicalVector3 InitialStandardCenter => default;
    internal const byte InitialHelperFlag = 0;
    internal const string NumericalBoundary = "source-default-control-constructor-Float32-fields-qualified;general-double-control-and-canonical-byte-parity-unqualified";
    private const string Rules = "source-FO3-early-detached-character-controller/v3;" +
        "actual-Player-High-process-constructor-before-global-Player-and-Main;" +
        "Common-Runtime-and-current-thread-process-lineages-independent;" +
        "inherited-reference-final-Base-model-and-ParentCELL-null-stores-after-intermediate-Base-preservation;" +
        "reference-scale-positive-one-actual-current-null-Base-returns-reference-scale-before-later-winning-Base-assignment;" +
        "actual-current-thread-model-cache-and-current-reference-fallback-independent-from-renderer;" +
        "creation-info-ordered-stores-group-allocation-and-real-shared-manager-retention;" +
        "manager-eleven-slots-real-state-registrations-zero-five-one-two-three-four-six;" +
        "state-context-current-and-previous-zero-constructor-without-state-update;" +
        "actual-shared-slope-forty-seven-Float32-angle-cosine-sine-and-step-division-multiplication-stores;" +
        "proxy-distinct-Float32-half-pi-cosine-and-up-normalization-before-derived-Z-up;" +
        "same-thread-initializer-prior-kind-and-kind-sixteen-return-only-restoration;" +
        "real-capsule-and-rounded-convex-shared-cache-before-actual-detached-phantom-and-proxy;" +
        "source-null-requested-world-never-default-native-space;" +
        "four-real-proxy-arrays-retained-storage-capacity-and-source-empty-flags;" +
        "phantom-properties-retain-actual-proxy-and-Actor-owners;" +
        "returned-controller-retained-through-actual-Common-pointer-setter;" +
        "three-independent-body-body-object-wrapper-constructor-null-fields-before-selected-initialization;" +
        "separate-inherited-base-proxy-publication-and-proxy-back-reference-preserve-those-wrapper-fields;" +
        "selected-creation-info-cleanup-preserves-independent-body-object-fields;" +
        "Main-repeated-current-context-getters-first-body-second-body-equality-before-second-body-reread;" +
        "independent-object-null-conversion-still-reads-forced-first-person-before-null-input-return;" +
        "later-post-action-repeated-current-context-reads-first-body-before-null-reference-conversion-and-action-eight-removal;" +
        "independent-second-post-action-context-reads-same-current-controller-flags-bit-seventeen-before-Player-context-byte-store;" +
        "post-action-flags-read-borrows-real-constructor-helper-and-position-reset-flag-stores-with-distinct-source-scope;" +
        "body-object-current-writer-boundaries-retained-independently-through-cold-state-without-replay;" +
        "Player-later-selector-one-thousand-and-upper-filter-nine-preserves-lower-thirty;" +
        "XYZ-reset-offset-current-rotation-translation-velocity-four-zero-scalar-zero-conditional-flags-array-count;" +
        "position-reset-preserves-current-body-body-object-wrapper-fields;" +
        "unwritten-translation-W-explicit-and-source-nonnull-world-AABB-broadphase-independent;" +
        "failure-prefix-native-lifetime-and-retirement-retry-retained;" +
        "canonical-hull-trig-bytes-contacts-solver-world-admission-and-unreached-state-transitions-independent";
    internal string Identity => FalloutAdvancementRuntimeReceipt.Hash(Fields.Identity + "\0" + Thread.Identity + "\0" + Contract);
    internal static FalloutEarlyControllerSource Reviewed(FalloutPackageCommandFieldsSource fields, FalloutPlayerThreadContextSource thread)
    {
        fields.Validate(); thread.Validate();
        if (fields.Controls.Main != thread.Main || fields.Controls.Main.EngineSha256 != World.Physics.FalloutPhysicalConstructorInputs.ReviewedEngineSha256)
            throw new InvalidDataException("Early controller changed its selected source/current-thread declarations.");
        return new(fields, thread, FalloutAdvancementRuntimeReceipt.Hash(Rules));
    }
    internal void Validate()
    { if (this != Reviewed(Fields, Thread)) throw new InvalidDataException("Early controller changed its actual source contract."); }
}
