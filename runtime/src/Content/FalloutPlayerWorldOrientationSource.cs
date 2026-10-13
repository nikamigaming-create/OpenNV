using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutPlayerWorldOrientationSource(FalloutPlayerMovementControlSource Controls, string Contract)
{
    private const string Rules = "source-FO3-world-projection-reference-orientation/v1;" +
        "actual-specialized-World-scenegraph-base-camera-scene-children-before-FOV;" +
        "logical-culling-plane-set-and-base-BSSceneGraph-constructor-camera-identity-frustum-viewport-matrix-bound;" +
        "ordinary-camera-cyclic-local-rotation-minimum-near-one-far-near-ratio-before-forced-FOV;" +
        "base-constructor-far-min-plus-max-minus-min-times-fraction-minus-zero;" +
        "Float32-half-radians-double-tangent-Float32-store-target-height-over-width-scale-three-quarters;" +
        "frustum-near-ratio-minimum-ordered-clamps-transform-before-perspective-matrix-before-zero-bound;" +
        "forced-setter-camera-detached-then-attach-and-update-before-root-unit-bound-flags-return;" +
        "derived-SceneGraph-world-process-far-getter-replaces-base-BSSceneGraph-table-only-after-base-constructor-return;" +
        "ordinary-false-mode-null-supplied-camera-forces-effective-Main-fDefaultFOV;" +
        "returned-root-global-publication-replaces-releases-acquires-only-if-different;" +
        "supplied-camera-constructor-current-root-FOV-unwritten;" +
        "Player-constructor-copies-effective-fDefaultFOV-before-world-setter-false-null-false;" +
        "world-setter-owned-camera-stores-on-force-or-not-ordered-equal-before-projection-children;" +
        "ordered-equal-no-ratio-returns-without-projection;supplied-camera-never-stores-root-FOV;" +
        "console-Player-and-world-refresh-ingress-share-retained-setter-failed-prefix;" +
        "default-world-FOV-control-denominator-is-independent;" +
        "reference-constructor-positive-zero-X-Y-Z-independent-from-Player-offset;" +
        "current-pitch-direct-X-base-yaw-Z-character-NPC-bit23-adds-Y-normalizes;" +
        "Player-yaw-adds-independent-current-offset-normalizes;" +
        "placement-commits-distinct-captured-source-X-Y-Z-never-scene-basis;" +
        "raw-axis-setters-store-before-change-mask-two-raw-Z-character-normalizes-before-change;" +
        "yaw-delta-Float32-before-action-zero-four-and-current-process-admission-writer;" +
        "pitch-delta-Float32-before-independent-action-limits-reference-setter;" +
        "cold-bits-prefix-fresh-world-process-no-constructor-placement-setter-callback-replay";

    internal const string ConstructorFovName = "fDefaultFOV:Display";
    internal const uint ConstructorFovDefaultBits = 0x42960000;
    internal string Identity => FalloutAdvancementRuntimeReceipt.Hash(Controls.Identity + "\0" + Contract);

    internal static FalloutPlayerWorldOrientationSource Reviewed(FalloutPlayerMovementControlSource controls)
    {
        controls.Validate();
        return new(controls, FalloutAdvancementRuntimeReceipt.Hash(Rules));
    }
    internal void Validate()
    {
        if (this != Reviewed(Controls)) throw new InvalidDataException("World/reference orientation changed its reviewed source association.");
    }
    internal FalloutIniValue ReadConstructorFov(FalloutNumericIniSettings settings, string stack)
    {
        Validate(); ArgumentNullException.ThrowIfNull(settings);
        var value = settings.Find(FalloutIniCollection.Main, ConstructorFovName);
        if (!settings.Source.StartsWith(stack + ":", StringComparison.Ordinal) ||
            !settings.Source.EndsWith(":" + Controls.Main.EngineSha256, StringComparison.Ordinal) ||
            value is not { Declaration: { Name: ConstructorFovName, Collection: FalloutIniCollection.Main, Payload: ConstructorFovDefaultBits }, Number: { } number } ||
            !double.IsFinite(number) || (double)(float)number != number || string.IsNullOrWhiteSpace(value.Origin))
            throw new InvalidDataException("World constructor FOV lost its exact Main descriptor or effective source setting.");
        return value;
    }
}
