namespace OpenNV.Runtime.Content;

internal sealed record FalloutPlayerActorMovementSource(FalloutPlayerMoverDeltaSource Mover,
    FalloutInitialPlacementCellsSource Cells, FalloutPlayerWorldOrientationSource Orientation, string Contract)
{
    internal const byte CharacterFormKind = 0x3b;
    internal const uint ControllerDisabledMovementFlag = 1u << 27;
    internal const uint ControllerPoseFlag = 1u << 11;
    internal const uint ControllerCorrectionFlag = 1u << 5;
    internal const uint ControllerTurnFlag = 1;
    internal const uint ControllerFallFlag = 1u << 7;
    internal const uint ControllerTimedMask = (1u << 25) | (1u << 26);
    internal const uint PositionCorrectionDistanceSquaredBits = 0x4c800000;
    internal const uint GroundCorrectionHeightBits = 0x41f00000;
    internal const uint ExceptionalAngleBits = 0x7f7fffff;
    internal const string SkipFramesName = "iHavokSkipFrameCountTEST:Havok";
    internal const string WarpDistanceName = "fCharControllerWarpDistSqr:HAVOK";
    private const string Rules = "source-FO3-Player-Actor-movement/v1;" +
        "nonnull-reference-data-enters-selected-Actor-body-independent-from-model;" +
        "same-Common-controller-before-Character-current-form-kind-predicate-and-raw-XYZ;" +
        "Float32-input-NaN-diagnostic-then-raw-angles-exceptional-reset-through-real-setter;" +
        "typed-current-Main-Havok-skip-frame-INI-before-same-Player-disable-word-bit-zero;" +
        "same-Actor-yaw-selection-word-before-actual-current-World-camera-world-translation;" +
        "same-position-mode-raw-XYZ-before-life-argument-false-life-equals-one-and-activity-predicates;" +
        "distance-query-before-Player-singleton-bypass-of-nonplayer-streaming;" +
        "same-controller-bit-twenty-seven-source-store-before-life-true-query;" +
        "disabled-arm-actual-state-two-predicate-before-independent-phase-word-two-child;" +
        "constructor-phase-zero-returns-without-delta-speed-or-solver;" +
        "ordinary-arm-enters-real-controller-delta-not-replacement-authority;" +
        "proxy-rotation-offset-and-current-translation-read-in-source-units;" +
        "paired-Float32-distance-then-fresh-raw-position-reset-on-large-correction;" +
        "independent-current-TES-CELL-before-height-query;" +
        "same-controller-pending-head-tail-before-final-simple-reference-position-mask-two;" +
        "bounded-current-call-failure-prefix-cold-no-original-child-replay";
    internal string Identity => FalloutAdvancementRuntimeReceipt.Hash(Mover.Identity + "\0" + Cells.Identity + "\0" + Orientation.Identity + "\0" + Contract);
    internal static FalloutPlayerActorMovementSource Reviewed(FalloutPlayerMoverDeltaSource mover,
        FalloutInitialPlacementCellsSource cells, FalloutPlayerWorldOrientationSource orientation)
    {
        mover.Validate(); cells.Validate(); orientation.Validate();
        if (cells.Reference.Fields != mover.Controller.Fields || orientation.Controls != mover.Controller.Fields.Controls)
            throw new InvalidDataException("Actor movement changed its existing reference/TES/orientation source owners.");
        return new(mover, cells, orientation, FalloutAdvancementRuntimeReceipt.Hash(Rules));
    }
    internal void Validate()
    { if (this != Reviewed(Mover, Cells, Orientation)) throw new InvalidDataException("Actor movement changed its selected source contract."); }
    internal static bool UsesOtherFormMovement(byte currentKind) => currentKind is >= 0x3d and <= 0x40;
}
