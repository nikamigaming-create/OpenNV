namespace OpenNV.Runtime.Content;

// This is the selected prefix before the late Actor-time child. It does not
// admit a returned Player body or supply a current field from constructor data.
internal sealed record FalloutPlayerLateFramePrefixSource(FalloutPlayerFollowingContinuationSource Following, string Contract)
{
    internal const byte InitialCameraViewRequest = 0;
    internal const ushort ViewDisabledMask = 0x10;
    internal const uint InhibitionThresholdBits = 0x41700000;
    internal const uint InitialInhibitionTimerBits = 0;
    private const string Rules = "source-FO3-Player-late-frame-prefix/v1;" +
        "selected-following-shared-late-target-before-Actor-time;" +
        "constructor-packed-four-byte-zero-current-view-middle-request-desired-view-independent;" +
        "request-before-current-view-before-disabled-control-bit-sixteen-before-optional-view-child;" +
        "same-PlayerMover-selected-Flags-setter-consumes-caller-local-low-word;" +
        "control-inout-word-masked-current-mover-cc00-distinct-from-Boolean-merge-word;" +
        "shared-inhibition-Float32-loader-zero-is-independent-from-POV-camera-scalar;" +
        "complete-selected-direct-symbol-inventory-two-reads-two-add-stores-three-zero-stores-in-same-late-body;" +
        "current-scalar-fifteen-comparison-before-any-second-or-first-inhibition-read;" +
        "over-fifteen-has-independent-raw-state-children;" +
        "other-arm-second-zero-before-first-zero-directly-reaches-view-comparison-without-scalar-store;" +
        "desired-current-view-equality-alone-enters-selected-Actor-time;" +
        "source-qualification-never-closes-unowned-current-field-writers-or-later-body";
    internal string Identity => FalloutAdvancementRuntimeReceipt.Hash(Following.Identity + "\0" + Contract);
    internal static FalloutPlayerLateFramePrefixSource Reviewed(FalloutPlayerFollowingContinuationSource following)
    { following.Validate(); return new(following, FalloutAdvancementRuntimeReceipt.Hash(Rules)); }
    internal void Validate()
    { if (this != Reviewed(Following)) throw new InvalidDataException("Late Player prefix changed its actual source field/branch associations."); }
}
