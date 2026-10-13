namespace OpenNV.Runtime.Content;

// Requested camera mode and the selected model's view byte are independent.
// This declaration describes the flag setter's source fields and ordering;
// it does not provide the following live camera-transform child.
internal sealed record FalloutPlayerPointOfViewSource(FalloutPlayerMovementControlSource Controls, string Contract)
{
    private const string Rules = "source-FO3-Player-POV-disable/v1;" +
        "actual-Player-constructor-null-first-person-field-shared-with-model-and-forced-reference-getters;" +
        "constructor-four-byte-clear-contains-independent-model-view-and-requested-camera-mode;" +
        "actual-Player-constructor-positive-zero-shared-camera-Float32-field;" +
        "flag-setter-reads-current-first-person-field-before-unconditional-requested-camera-mode-zero-store;" +
        "null-current-first-person-stores-shared-camera-positive-zero-before-return;" +
        "nonnull-current-first-person-enters-current-camera-transform-child-with-false-argument-after-byte-store;" +
        "model-view-byte-never-aliased-or-written-by-null-arm;" +
        "current-source-factory-and-thread-before-field-read-cold-retains-stores-without-replay";
    internal string Identity => FalloutAdvancementRuntimeReceipt.Hash(Controls.Identity + "\0" + Contract);
    internal static FalloutPlayerPointOfViewSource Reviewed(FalloutPlayerMovementControlSource controls)
    { controls.Validate(); return new(controls, FalloutAdvancementRuntimeReceipt.Hash(Rules)); }
    internal void Validate()
    { if (this != Reviewed(Controls)) throw new InvalidDataException("Player POV changed its actual constructor, distinct fields or ordered consumers."); }
}
