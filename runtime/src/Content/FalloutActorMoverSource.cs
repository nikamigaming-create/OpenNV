namespace OpenNV.Runtime.Content;

internal sealed record FalloutActorMoverSource(FalloutPlayerMovementControlSource Controls, string Contract)
{
    internal const uint InitialInheritedFlags = 0x101;
    internal const uint InitialPlayerTimerBits = 0xbf800000;
    private const string Rules = "source-FO3-ActorMover-PlayerMover-fields/v1;" +
        "actual-Player-constructor-retires-old-mover-constructs-PlayerMover-publishes-same-field;" +
        "PlayerMover-inherits-actual-ActorMover-constructor-and-independent-default-vectors;" +
        "inherited-flags-101-and-inherited-reset-byte-in-zero-word-distinct-from-Player-movement-word;" +
        "Player-derived-default-vector-flags-zero-timer-minus-one-secondary-word-zero;" +
        "Current-and-Primary-package-setters-join-linked-reference-null-then-High-reset-before-mover;" +
        "actual-Actor-byte-nonzero-skips-base-predicate-and-mover-reset;" +
        "base-predicate-nonzero-skips-reset-zero-reads-same-Actor-mover-field;" +
        "reset-copies-default-XY-together-default-Z-then-independent-byte-zero;" +
        "current-getters-borrow-same-mutable-owner-source-thread-and-lifetime;" +
        "observed-unowned-writers-invalidate-specific-cells-cold-stores-never-replayed;" +
        "logical-mover-is-independent-of-physical-controller-phantom-shape-and-renderer";

    internal string Identity => FalloutAdvancementRuntimeReceipt.Hash(Controls.Identity + "\0" + Contract);
    internal static FalloutActorMoverSource Reviewed(FalloutPlayerMovementControlSource controls)
    {
        controls.Validate(); return new(controls, FalloutAdvancementRuntimeReceipt.Hash(Rules));
    }
    internal void Validate()
    {
        if (Controls.Main.EngineSha256 != FalloutSourceMainFamily.Fallout3 || this != Reviewed(Controls))
            throw new InvalidDataException("Actor mover changed its source constructor, field lanes or package reset child.");
    }
}
