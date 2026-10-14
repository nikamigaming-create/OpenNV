namespace OpenNV.Runtime.Content;

internal sealed record FalloutPackageCommandFieldsSource(FalloutPlayerMovementControlSource Controls, string Contract)
{
    internal const uint ReferenceChangedMask = 0x800;
    internal const byte FirstPersonView = 0;
    private const string Rules = "source-FO3-package-command-reference-fields/v1;" +
        "actual-Actor-constructor-raw-XYZ-and-rotation-positive-zero-ParentCELL-null;" +
        "Extra-list-constructor-head-null-presence-bits-zero-adjusted-position-byte-zero;" +
        "current-position-distinct-from-pending-MoveTo-adjusted-position-children-independent;" +
        "Script-Extra-constructor-null-event-list-separate-script-and-event-list-publication;" +
        "package-event-mark-existing-match-empty-and-first-wildcard-rows-OR-no-allocation;" +
        "saved-position-worldspace-else-cell-XYZ-new-row-rotation-Z-existing-row-preserves-rotation;" +
        "saved-position-stores-before-independent-reference-change-800-consumer;" +
        "Player-animation-override-then-first-context-view-then-current-Common-animation-context;" +
        "Player-animation-constructor-null-independent-of-whole-physical-return;" +
        "deferred-startup-empty-configured-CELL-then-empty-grid-X-before-world-and-CELL-publication;" +
        "animation-view-borrows-current-scene-owner-independent-of-Common-process;" +
        "animation-context-distinct-from-model-root-TLS-and-renderer-absence;" +
        "constructor-preceding-writers-explicit-admission-failed-prefix-cold-no-replay";
    internal string Identity => FalloutAdvancementRuntimeReceipt.Hash(Controls.Identity + "\0" + Contract);
    internal static FalloutPackageCommandFieldsSource Reviewed(FalloutPlayerMovementControlSource controls)
    {
        controls.Validate(); return new(controls, FalloutAdvancementRuntimeReceipt.Hash(Rules));
    }
    internal void Validate()
    {
        if (Controls.Main.EngineSha256 != FalloutSourceMainFamily.Fallout3 || this != Reviewed(Controls))
            throw new InvalidDataException("Package command fields lost their selected original source.");
    }
}
