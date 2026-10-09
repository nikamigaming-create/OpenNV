using System.Security.Cryptography;
using System.Text;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutMainPlayerCellSource(FalloutMainScriptCallerSource Main, string Contract)
{
    private const string Rules = "source-Main-Player-CELL/v1;constructor-null-pending-destination;" +
        "one-pending-slot-not-FIFO;replacement-assertion-before-old-payload-release-and-new-store;" +
        "source-world-two-byte-query-after-pending-store-before-optional-immediate-consumption;" +
        "pending-world-prelude-before-owned-child-release-before-target-arm;" +
        "reference-travel-distinct-from-cell-or-world-position-transfer;" +
        "Float32-vectors-rotation-and-scene-scalar;optional-source-callback-and-furniture-arm;" +
        "deferred-destruction-before-pending-payload-release-and-null-store;" +
        "pending-post-flag-queries-after-null-store-before-Player-early-return;" +
        "cached-menu-short-circuit-and-distinct-GUI-kind-two-query-before-held-child;" +
        "independent-Main-scene-mode-and-hold-bytes;selected-Player-scene-update-before-parent-CELL;" +
        "parent-CELL-null-or-interior-or-position-contained-returns;" +
        "position-Float32-FISTP-current-rounding-signed-shift-twelve;" +
        "target-source-CELL-phase-three-or-six-skips-world-load;" +
        "world-bracket-before-Player-bracket-before-target-CELL-attach-before-root-store;" +
        "Player-bracket-clear-before-world-bracket-clear-before-optional-tree-child;" +
        "genuine-child-return-and-native-publication-retirement-distinct;" +
        "failed-entered-prefix-never-replayed;current-source-process-handoff-no-native-identity-promotion";

    internal static FalloutMainPlayerCellSource Read(FalloutMainScriptCallerSource main)
    {
        main.Validate();
        return new(main, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            main.Identity + "\0" + Rules))).ToLowerInvariant());
    }
    internal void Validate()
    {
        if (this != Read(Main)) throw new InvalidDataException("Main Player/CELL source declaration drifted.");
    }
}
