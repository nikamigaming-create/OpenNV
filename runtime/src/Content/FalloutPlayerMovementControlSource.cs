namespace OpenNV.Runtime.Content;

internal sealed record FalloutPlayerMovementControlSource(FalloutMainScriptCallerSource Main, string Contract)
{
    internal const uint MoverMask = 0xcc00;
    private const string Rules = "source-FO3-Player-movement-control/v2;" +
        "independent-Actor-physical-utility-before-movement-control-child;" +
        "actual-scene-Float32-and-zero-byte-and-masked-mover-word-and-zero-byte-arguments;" +
        "independent-source-mode-zero-selects-ordinary-control-arm;" +
        "four-Player-constructor-zero-inhibition-bytes-ordered-first-second-third-fourth;" +
        "nonnull-current-source-input-owner-before-two-independent-global-control-cache-zero-stores;" +
        "joystick-availability-byte-before-independent-suppression-byte;" +
        "mouse-arm-reads-current-cache-axes-one-X-two-Y-three-wheel-without-polling;" +
        "source-input-axis-outside-one-through-three-returns-zero;" +
        "Prefs-typed-invert-Y-byte-before-independent-Player-constructor-zero-disable-word;" +
        "Int32-negate-Y-then-disable-bit-two-suppresses-X-Y-wheel;" +
        "nonzero-wheel-view-services-remain-explicit-before-world-projection;" +
        "Main-typed-default-world-FOV-scaling-compares-actual-world-root-current-Float32;" +
        "actual-Player-yaw-getter-adds-independent-constructor-zero-offset-to-source-reference-yaw;" +
        "character-NPC-base-flag-bit-twenty-three-adds-source-roll-before-normalization;" +
        "Float32-period-negative-add-once-then-signed-Float64-remainder-never-positive-modulo;" +
        "independent-current-reference-pitch-raw-Float32-getter-before-source-camera-context;" +
        "loader-zero-camera-context-and-independent-controller-context-byte-before-actual-orientation-writers;" +
        "all-current-source-axis-values-zero-returns-false-only-after-independent-FOV-yaw-pitch-getters;" +
        "source-flag-mask-argument-bits-one-four-eight-sixteen-two-thirty-two-sixty-four;" +
        "FO3-omitted-disable-arguments-first-three-only-enable-all-seven;" +
        "full-flag-byte-store-then-clear-high-bit-unless-full-disable-before-input-type-and-conditional-effects;" +
        "actual-observed-command-invalidates-current-disable-word-until-genuine-writer-owned-never-semantic-replay;" +
        "unknown-writers-modes-joystick-wheel-and-world-reference-getters-remain-separate-refusals;" +
        "failed-prefix-cold-history-never-replays-input-or-cache-stores;" +
        "Boolean-child-return-and-later-Player-services-are-required-before-thread-exit-and-scene-decrement";

    internal string Identity => FalloutAdvancementRuntimeReceipt.Hash(Main.Identity + "\0" + Contract);
    internal static FalloutPlayerMovementControlSource Reviewed(FalloutMainScriptCallerSource main)
    {
        main.Validate();
        if (!FalloutSourceMainFamily.IsFallout3(main.EngineSha256))
            throw new NotSupportedException("Player movement/control source is unreviewed.");
        return new(main, FalloutAdvancementRuntimeReceipt.Hash(Rules));
    }
    internal void Validate()
    {
        if (this != Reviewed(Main))
            throw new InvalidDataException("Player movement/control declaration changed its source child, arguments or producers.");
    }
}
