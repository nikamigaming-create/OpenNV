namespace OpenNV.Runtime.Content;

internal sealed record FalloutPlayerMoverDeltaSource(FalloutEarlyControllerSource Controller, string Contract)
{
    internal const uint DirectPositionFlag = 0x10;
    internal const uint DirectionMask = 0xf, TurningMask = 0xc, ForwardMask = 3;
    internal const uint MotionMask = 0x3f;
    internal const byte CellWaterFlag = 2;
    internal const uint ControllerTransientMask = 0xc0;
    internal const uint UninitializedWater = 0xff7fffff, UninitializedScalar = 0xbf800000;
    private const string Rules = "source-FO3-PlayerMover-late-delta/v1;" +
        "same-PlayerMover-current-frame-default-XY-Z-return-before-delta-virtual;" +
        "actual-stored-Float32-scene-argument-and-actual-Player-current-Common-epoch-lease-thread;" +
        "fresh-direct-Common-animation-before-current-view-before-optional-direct-first-animation;" +
        "null-Common-selects-first-in-view-zero-independent-of-first-person-KF-or-native-model;" +
        "movement-override-activity-nonzero-predicate-before-fresh-activity-six-or-life-false-or-current-special-motion-byte;" +
        "override-nonnull-animation-extraction-and-rotation-have-independent-real-producers;" +
        "fresh-current-controller-before-its-conditional-motion-suppression-fields;" +
        "mover-direction-and-current-reference-flag-before-independent-turning-arm;" +
        "direct-position-flag-adds-source-XYZ-vector-and-enters-real-Actor-position-setter;" +
        "otherwise-real-Actor-movement-child-before-current-ParentCELL-and-exterior-water;" +
        "Actor-movement-menu-predicate-before-current-controller-before-raw-reference-data-null;" +
        "reference-data-pointer-independent-from-node-pointer-TLS-node-selection-and-Player-scene-reference;" +
        "reference-ctor-clears-data-lazy-load-allocates-real-data-before-model-factory;" +
        "data-null-and-data-nonnull-model-null-are-different-Actor-movement-arms;" +
        "lazy-data-fields-word-zero-word-zero-current-CELL-water-or-sentinel-minus-one-word-zero;" +
        "model-setter-releases-old-stores-supplied-then-retains-same-real-node-owner;" +
        "null-model-setter-releases-both-model-children-and-frees-data-before-pointer-null;" +
        "detached-controller-delta-clears-transient-flags-then-empty-pending-queue-and-body-object-cleanup;" +
        "controller-scalar-divides-requested-speed-by-current-shared-Player-speed-cache-before-world-getter;" +
        "loaded-cache-uses-real-SpeedMult-ACBS-word-limb-GMST-formulas-and-separately-truncated-ignore-limb-pools;" +
        "actual-current-world-null-or-ordered-small-delta-returns-after-committed-controller-prefix;" +
        "nonnull-world-state-contact-integration-and-Actor-model-camera-position-tail-remain-independent;" +
        "failed-prefix-and-cold-history-never-replay-original-constructors-frame-or-model-setters;" +
        "current-unowned-writers-invalidate-exact-fields-not-missing-renderer-or-unrelated-byte-parity";
    internal string Identity => FalloutAdvancementRuntimeReceipt.Hash(Controller.Identity + "\0" + Contract);
    internal static FalloutPlayerMoverDeltaSource Reviewed(FalloutEarlyControllerSource controller)
    { controller.Validate(); return new(controller, FalloutAdvancementRuntimeReceipt.Hash(Rules)); }
    internal void Validate()
    { if (this != Reviewed(Controller)) throw new InvalidDataException("Player mover delta changed its selected source field or caller contract."); }
}
