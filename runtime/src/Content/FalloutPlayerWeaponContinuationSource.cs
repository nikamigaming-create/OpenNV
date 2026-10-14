namespace OpenNV.Runtime.Content;

internal sealed record FalloutPlayerWeaponContinuationSource(FalloutPlayerMovementControlSource Controls, string Contract)
{
    internal const int ParalysisActorValue = 47;
    internal const int CachedScalarCount = 11;
    internal const int ParalysisCacheSlot = 6;
    internal const uint StatusCacheMask = 0x20;
    internal const uint UnqueriedScalarBits = 0xbf800000;
    internal string Identity => FalloutAdvancementRuntimeReceipt.Hash(Controls.Identity + "\0" + Contract);
    private const string Rules = "source-FO3-Player-following-weapon/v1;" +
        "High-MiddleHigh-independent-weapon-action-byte-not-initialized-by-process-constructor;" +
        "actual-Player-constructor-writes-zero-through-pure-process-byte-setter;" +
        "false-action-skips-independent-global-weapon-entry-and-signed-word-state-arm;" +
        "true-action-reads-actual-global-before-first-current-nullable-weapon-entry;" +
        "nonnull-entry-requires-current-signed-word-state-two-through-six-before-second-entry-and-form;" +
        "following-signed-activity-byte-is-low-byte-of-independent-constructor-zero-word;" +
        "nonzero-activity-skips-Actor-status-query;" +
        "Actor-status-borrows-current-process-allocated-cached-value-context;" +
        "context-constructs-eleven-minus-one-Float32-cells-and-zero-flags;" +
        "context-retains-two-independent-minus-one-words-two-null-pointers-and-zero-byte-without-padding-claims;" +
        "status-cache-bit-five-miss-queries-current-actor-value-forty-seven;" +
        "returned-Float32-store-before-bit-five-store-before-ordered-greater-than-zero;" +
        "cache-hit-reads-retained-cell-without-pool-query;" +
        "nonnull-weapon-effect-and-observed-unbound-writers-retain-failed-prefixes;" +
        "current-process-class-epoch-source-thread-and-Main-caller-required;no-constructor-or-getter-cold-replay";
    internal static FalloutPlayerWeaponContinuationSource Reviewed(FalloutPlayerMovementControlSource controls)
    {
        controls.Validate(); return new(controls, FalloutAdvancementRuntimeReceipt.Hash(Rules));
    }
    internal void Validate()
    {
        if (Controls.Main.EngineSha256 != FalloutSourceMainFamily.Fallout3 || this != Reviewed(Controls))
            throw new InvalidDataException("Player weapon continuation lost its selected original source contract.");
    }
}
