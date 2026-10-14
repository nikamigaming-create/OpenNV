using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutPlayerOpacitySource(FalloutPlayerMovementControlSource Controls, string Contract)
{
    private const string Rules = "source-FO3-Player-teleport-opacity/v1;" +
        "returned-control-bool-merge-before-two-independent-loader-zero-gates;" +
        "High-state-one-fade-positive-zero-target-null-effective-opacity-one-inherited-base-opacity-one;" +
        "state-zero-less-or-unordered-than-one-store-one-opacity-false-otherwise-false;" +
        "nonzero-state-actual-Main-scaled-timer-divides-winning-Player-or-Actor-teleport-GMST;" +
        "states-one-three-add-store-strict-ordered-greater-than-one-clamp-state-zero-before-opacity;" +
        "state-two-positive-subtract-store-strict-negative-clamp-target-clear-before-opacity;" +
        "state-four-Player-fade-one-target-clear-target-child-before-state-zero-true-no-opacity;" +
        "state-four-NPC-subtract-clamp-clear-before-target-and-current-actor-children;" +
        "states-five-six-subtract-store-clamp-before-distinct-reference-enable-disable-children;" +
        "common-opacity-before-current-state-three-through-six-true-other-false;" +
        "Actor-override-independent-global-byte-and-signed-mode-before-extra-data-child;" +
        "ordinary-current-High-base-opacity-times-fade-independent-Player-global-alpha-one;" +
        "Player-opacity-stores-current-High-effective-alpha-before-current-first-person-child;" +
        "scene-alpha-null-bound-and-ordered-zero-radius-return-otherwise-ordered-nullable-children;" +
        "child-geometry-slot-three-shader-invalid-minus-one-skip-other-raw-alpha-store-before-node-recursion;" +
        "actual-NIF-scene-local-world-transform-current-nullable-sphere-and-shader-state;" +
        "renderer-borrows-source-pose-material-and-runtime-shader-alpha-never-producer;" +
        "cold-retains-fields-failed-prefix-and-current-source-graph-fresh-lifetime-no-replay";

    internal const string PlayerFadeSeconds = "fPlayerTeleportFadeSeconds";
    internal const string ActorFadeSeconds = "fActorTeleportFadeSeconds";
    internal const uint PlayerFadeDefaultBits = 0x3a83126f;
    internal const uint ActorFadeDefaultBits = 0x3f000000;
    internal string Identity => FalloutAdvancementRuntimeReceipt.Hash(Controls.Identity + "\0" + Contract);

    internal static FalloutPlayerOpacitySource Reviewed(FalloutPlayerMovementControlSource controls)
    {
        controls.Validate();
        if (!FalloutSourceMainFamily.IsFallout3(controls.Main.EngineSha256))
            throw new NotSupportedException("source-Player-teleport-opacity-source-family-unreviewed");
        return new(controls, FalloutAdvancementRuntimeReceipt.Hash(Rules));
    }
    internal void Validate()
    {
        if (this != Reviewed(Controls)) throw new InvalidDataException("Player opacity lost its selected source contract.");
    }
}
