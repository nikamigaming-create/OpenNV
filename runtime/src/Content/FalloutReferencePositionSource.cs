namespace OpenNV.Runtime.Content;

internal sealed record FalloutReferencePositionSource(FalloutPackageCommandFieldsSource Fields, string Contract)
{
    internal const uint PositionChangeMask = 2, SavedPositionChangeMask = 0x800;
    internal const uint InitialScale = 0x3f800000, InitialChangeManagerFlags = 1;
    internal const int ChangeBucketCount = 5039;
    private const string Rules = "source-FO3-reference-position-and-change-map/v1;" +
        "reference-raw-XYZ-positive-zero-ParentCELL-null-scale-positive-one-Extra-list-empty;" +
        "Actor-adjusted-position-byte-zero-distinct-from-current-PlayerMover-movement-word;" +
        "Actor-raw-yaw-selection-word-zero-distinct-from-existing-neutral-life-and-Common-activity;" +
        "current-mode-zero-returns-raw-XYZ-nonzero-requires-independent-bounds-and-scratch-ring;" +
        "form-file-persistent-predicate-before-pure-BaseForm-kind-selector;" +
        "NPC-kind-false-skips-Worldspace-detach-attach-before-paired-XY-then-Z-stores;" +
        "mandatory-mask-two-before-current-controller-reset-and-independent-model-node-translation;" +
        "first-startup-XYZ-2048-2048-zero-distinct-from-configured-2048-2048-128;" +
        "empty-configured-CELL-then-X-preserves-supplied-vector-current-null-CELL-grid-arm;" +
        "pending-transfer-never-supplies-current-reference-position-or-ParentCELL;" +
        "ParentCELL-before-selected-persistent-Extra-twelve-before-Worldspace;" +
        "SavedPosition-Extra-eighteen-construction-publication-before-mask-eight-hundred;" +
        "reference-flags-borrowed-from-existing-Script-initializer-thread-flags-from-actual-thread-owner;" +
        "manager-flags-one-constructor-and-explicit-startup-clear-before-configured-placement;" +
        "thread-bit-zero-manager-bit-zero-force-reference-fourteen-five-selectors;" +
        "shared-5039-zero-bucket-change-map-unsigned-ID-modulo-equality-and-head-insertion;" +
        "new-entry-flags-zero-data-null-existing-nonnull-data-skips-mask-OR;" +
        "failed-source-prefix-keeps-actual-stores-cold-joins-fresh-owners-without-replay";
    internal string Identity => FalloutAdvancementRuntimeReceipt.Hash(Fields.Identity + "\0" + Contract);
    internal static FalloutReferencePositionSource Reviewed(FalloutPackageCommandFieldsSource fields)
    { fields.Validate(); return new(fields, FalloutAdvancementRuntimeReceipt.Hash(Rules)); }
    internal void Validate()
    { if (this != Reviewed(Fields)) throw new InvalidDataException("Reference position changed its selected original source contract."); }
    internal static bool UsesWorldspacePositionList(byte kind) =>
        kind is 0x0d or 0x15 or 0x1b or 0x1c or 0x20 or 0x21 or 0x25 or 0x26 or 0x27 or 0x2c or 0x2d;
}
