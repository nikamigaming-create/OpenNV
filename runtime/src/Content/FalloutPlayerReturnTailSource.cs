namespace OpenNV.Runtime.Content;

internal sealed record FalloutPlayerReturnTailSource(FalloutPlayerActorMovementSource Movement,
    FalloutStandalonePlayerSceneSource Scene, string Contract)
{
    internal const string DeathDelayName = "fPlayerDeathReloadTime";
    internal const string PairDelayName = "fExplodeLimbRemovalDelay";
    internal const string PairVatsDelayName = "fExplodeLimbRemovalDelayVATS";
    internal const uint InitialDeathTimerBits = 0xbf800000;
    internal const byte AnimationNodeExtraKind = 0x29;
    internal const int AnimationGroupBuckets = 101;
    internal const ushort NoAnimationGroup = 0xff;
    private const string Rules = "source-FO3-Player-return-tail/v1;" +
        "same-returned-mover-before-winning-death-delay-before-current-raw-Life;" +
        "positive-delay-Life-neither-one-nor-two-overwrites-shared-death-timer-minus-one;" +
        "positive-death-reads-current-timer-before-owned-menu-child-and-subtract;" +
        "nonpositive-delay-Life-two-independent-once-byte-and-dialog-effects;" +
        "selected-Player-animation-byte-increment-before-same-clock-ratio-and-cached-Main-delta;" +
        "fresh-Common-animation-before-independent-direct-first-AnimData-before-fresh-Common-pair-queue;" +
        "actual-MiddleHigh-inline-pair-head-and-next-null-constructor-inherited-by-High;" +
        "High-selected-clock-kind-and-winning-delay-read-before-empty-inline-list-return;" +
        "actual-reference-list-type-forty-one-payload-getter-before-Player-view-root-swap;" +
        "null-Extra-payload-returns-without-querying-Player-view-models;" +
        "SAME-outer-Scene-byte-nested-increment-and-decrement-only-after-all-mandatory-children-return;" +
        "selected-Player-three-D-loader-constructs-first-AnimData-before-third-load-and-model-bind;" +
        "entered-load-invalidates-pointer-until-actual-factory-store-never-logical-graph-null;" +
        "AnimData-real-constructor-null-root-pending-lists-pair-slots-and-owned-group-map;" +
        "context-update-tests-pending-then-groups-and-actor-before-independent-root-null-return;" +
        "two-actual-view-toggles-prepare-once-and-two-separate-status-animation-frames;" +
        "camera-cell-attachment-resource-water-inhibition-input-children-before-thread-kind-restore;" +
        "thread-restore-before-source-virtual-return-before-outer-scene-byte-decrement;" +
        "same-authorities-current-writers-failed-prefixes-and-fresh-cold-publications-no-replay";
    internal string Identity => FalloutAdvancementRuntimeReceipt.Hash(Movement.Identity + "\0" + Scene.Identity + "\0" + Contract);
    internal static FalloutPlayerReturnTailSource Reviewed(FalloutPlayerActorMovementSource movement,
        FalloutStandalonePlayerSceneSource scene)
    {
        movement.Validate(); scene.Validate();
        if (movement.Mover.Controller.Fields.Controls.Main != scene.Main)
            throw new InvalidDataException("Player tail changed its actual movement and Scene source family.");
        return new(movement, scene, FalloutAdvancementRuntimeReceipt.Hash(Rules));
    }
    internal void Validate()
    {
        Movement.Validate(); Scene.Validate();
        if (this != Reviewed(Movement, Scene))
            throw new InvalidDataException("Player return tail changed its selected source declaration.");
    }
}
