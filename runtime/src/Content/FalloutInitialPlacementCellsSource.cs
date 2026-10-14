namespace OpenNV.Runtime.Content;

internal sealed record FalloutInitialPlacementCellsSource(FalloutReferencePositionSource Reference, string Contract)
{
    private const string Rules = "source-FO3-initial-TES-cell-selection/v1;" +
        "TES-field-constructor-current-CELL-null-grid-constructor-normalizes-unsigned-diameter-minimum-five-odd;" +
        "actual-diameter-squared-CELL-pointer-slots-zero-constructor-and-null-grid-reset-preserve-empty-fields;" +
        "initial-placement-current-TES-CELL-first-else-center-slot-unsigned-diameter-half;" +
        "nonnull-selected-CELL-requires-independent-spatial-transform-null-preserves-supplied-XYZ;" +
        "yaw-borrows-same-existing-controller-shared-zero-vector-Z;" +
        "current-neutral-Actor-life-zero-or-four-current-Common-signed-activity-zero-Actor-yaw-word-outside-four-through-ten;" +
        "selected-NPC-skips-CREA-turn-delta-then-same-raw-reference-RZ-and-mask-two;" +
        "current-TES-CELL-grid-resize-fill-reset-copy-native-writers-retain-independent-barriers;" +
        "failed-prefix-cold-fresh-field-lifetimes-whole-TES-initialization-independent";
    internal string Identity => FalloutAdvancementRuntimeReceipt.Hash(Reference.Identity + "\0" + Contract);
    internal static FalloutInitialPlacementCellsSource Reviewed(FalloutReferencePositionSource reference)
    { reference.Validate(); return new(reference, FalloutAdvancementRuntimeReceipt.Hash(Rules)); }
    internal void Validate()
    { if (this != Reviewed(Reference)) throw new InvalidDataException("Initial placement changed its selected TES field contract."); }
    internal static uint NormalizeGridDiameter(uint value) => value < 5 ? 5 : (value & 1) == 0 ? checked(value + 1) : value;
    internal static FalloutIniValue ReadGridInput(FalloutInstallationSettings settings) =>
        settings.NumericIni.Find(FalloutIniCollection.Main, "uGridsToLoad:General") ??
            throw new NotSupportedException("source-initial-placement-main-grid-diameter-declaration-unbound");
}
