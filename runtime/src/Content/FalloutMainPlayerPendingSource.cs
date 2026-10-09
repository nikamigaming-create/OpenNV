using System.Security.Cryptography;
using System.Text;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutMainPlayerPendingSource(FalloutMainPlayerCellSource Player, string Contract)
{
    private const string Rules = "source-Player-pending-tail/v2;Player-constructor-null-owned-child-and-zero-tail-flags;" +
        "world-prelude-traverses-actual-ExteriorCellLoaderTask-map-and-calls-distinct-TaskManager-cancel;" +
        "map-key-low-signed-sixteen-X-shift-sixteen-or-low-unsigned-sixteen-Y;thirty-seven-buckets-key-unsigned-modulo-new-head-iteration-bucket-ascending;" +
        "source-exterior-task-base-constructor-argument-zero-request-state-zero-request-byte-zero-cancellation-zero-argument-not-destruction;" +
        "payload-retains-all-three-reference-CELL-worldspace-cells-and-selects-reference-then-worldspace-then-CELL;" +
        "same-owned-child-is-noop;old-refcount-release-before-new-pointer-store-before-new-acquire;" +
        "High-process-owned-character-controller-from-actual-source-physics-body-factory;" +
        "pending-scalar-is-third-position-Float32-cell-and-store-preserves-bits;" +
        "nonnull-callback-invoked-with-declared-context-before-furniture;" +
        "reference-travel-skips-scalar-callback-furniture;empty-target-assertion-still-reaches-furniture;" +
        "furniture-requires-winning-FURN;first-enabled-unoccupied-of-thirty;pending-selection-ignores-reserved-mask;" +
        "source-marker-native-geometry-before-actual-physical-furniture-publication;" +
        "deferred-manager-before-payload-release-before-null-store;" +
        "post-null-distinct-manager-word-mask-two-short-circuit-before-Player-byte-mask-two;" +
        "final-child-sets-Player-byte-mask-one-preserving-other-bits;" +
        "actual-child-lease-failure-and-new-process-handoff-no-native-pointer-promotion";

    internal uint PackCellKey(int x, int y) => unchecked(((uint)(ushort)x << 16) | (ushort)y);
    internal static FalloutMainPlayerPendingSource Read(FalloutMainPlayerCellSource player)
    {
        player.Validate();
        return new(player, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(player.Contract + "\0" + FalloutSourceMainFamily.PendingRules(player.Main.EngineSha256, Rules)))).ToLowerInvariant());
    }
    internal void Validate()
    {
        if (this != Read(Player)) throw new InvalidDataException("Player pending consumer changed its selected source declaration.");
    }
}
