using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutStandalonePlayerSceneSource(FalloutMainScriptCallerSource Main, string Contract)
{
    private const string Rules = "source-FO3-Main-Player-scene/v1;" +
        "Main-factory-byte-scene-zero-and-independent-hold-zero;" +
        "Player-constructor-view-zero-first-person-null;" +
        "selected-first-person-factory-return-old-owner-release-before-store-before-acquire;" +
        "getter-view-zero-and-nonnull-first-person-returns-directly;" +
        "other-arms-original-TLS-initializer-actor-match-scene-otherwise-loaded-data-scene;" +
        "free-camera-scene-inversion-before-hold-enter-and-argument-nonzero;" +
        "actual-enabled-input-manager-child-byte-before-enter-only-position-height-angles;" +
        "scene-clock-loader-numerator-one-denominator-one-manager-kind-zero;" +
        "byte-increment-before-clock-query-wide-ratio-times-scalar-times-delivered-Float32;" +
        "Float32-store-before-actual-Player-virtual-return-before-byte-decrement;" +
        "unit-kind-not-four-arm-exact-identity-conversion-only;" +
        "nonunit-x87-and-kind-four-child-not-substituted;" +
        "two-loader-zero-view-request-bytes-independent-from-POV;" +
        "Player-constructor-countdown-disabled-five-and-null-scene-reference;" +
        "virtual-releases-shared-owned-child-before-countdown;" +
        "reference-scratch-zero-store-before-Player-query-before-null-reference-test;" +
        "reference-query-force-first-person-field-before-null-and-every-parent-recursion;" +
        "node-reference-virtual-before-parent-walk-player-root-identity-special-base-parent-preference;" +
        "reference-frame-consumer-before-stored-reference-release-remains-independent;" +
        "Actor-scene-reset-four-independent-Float32-minus-one-stores-before-TLS-context-child;" +
        "Player-thread-context-before-pure-current-DirectInput-manager-slot-read-before-Actor-process-virtual;" +
        "getter-retains-actual-current-input-owner-without-polling-and-cold-never-rebinds-old-device;" +
        "actual-Actor-process-class-and-epoch-weapon-entry-before-independent-animation-context-getter;" +
        "MiddleHigh-null-field-constructors-inherited-by-High-Base-virtual-zero;" +
        "equipment-list-never-substitutes-process-entry-and-nonnull-writers-remain-independent;" +
        "Actor-constructor-independent-time-and-word-zero-Player-constructor-cached-word-and-two-scratch-words-zero;" +
        "two-scratch-zero-stores-before-positive-Float32-time-subtracts-current-Main-scaled-timer-otherwise-word-zero;" +
        "time-subtraction-never-clamps-and-does-not-clear-word-until-a-following-nonpositive-or-unordered-test;" +
        "source-reference-list-empty-constructor-resolved-save-load-prepend-duplicates-destructor-remove-first;" +
        "queue-current-reference-3D-before-retention-query-before-node-flag-zero-clear-and-update-one-one-zero;" +
        "retained-node-advances-actual-next-consumed-node-searches-current-list-and-restarts-head;" +
        "current-process-scene-update-virtual-follows-returned-reference-queue;" +
        "non-High-scene-virtual-returns-High-inherited-mask-zero-getter-before-Player-false-predicate-before-independent-mask-clear;" +
        "High-dirty-mask-node-controller-writers-remain-independent-current-cold-never-replays-clear;" +
        "post-process-two-independent-current-third-field-context-getters-MiddleHigh-constructor-null-arms-skip-distinct-nonnull-children;" +
        "Player-independent-timed-action-null-list-and-suppression-zero-constructor;" +
        "current-process-action-condition-before-short-circuit-second-condition-kind-nine-query-or-wildcard-first-removal;" +
        "action-producer-reuses-first-kind-item-prepends-and-retains-time-bits-context-acquire-independent;" +
        "ordered-nonpositive-expiry-excepts-five-and-eight-restarts-head-before-all-survivors-subtract-actual-Main-scaled-timer;" +
        "current-process-post-action-independent-null-context-and-High-value-and-timer-zero-constructor-cells;" +
        "positive-or-unordered-post-action-timer-subtracts-actual-Main-timer-otherwise-context-value-clamp-before-double-timer-transform;" +
        "two-following-current-context-getters-null-first-removes-eight-second-stores-Player-context-byte-zero;" +
        "independent-detection-action-timer-ordered-positive-subtract-or-setting-reset-before-value-zero;" +
        "Player-context-byte-constructor-one-combat-list-word-zero-and-two-timers-minus-one;" +
        "Player-two-post-action-timers-consume-actual-scene-argument-and-retain-overshoot-before-later-minus-one-sentinel;" +
        "actor-distance-timer-explicit-managed-zero-initialization-retail-constructor-unwritten-byte-parity-unaccepted;" +
        "actor-distance-positive-or-unordered-subtracts-actual-scene-argument-otherwise-current-interface-Shell-sort-before-winning-setting-reset;" +
        "current-process-idle-update-lower-class-return-High-current-selected-model-null-return-before-any-animation-or-bound-child;" +
        "idle-Player-model-view-first-person-field-and-current-native-lifetime-independent-from-common-body-and-animation-context;" +
        "idle-fallback-current-thread-loader-null-reference-and-model-cache-before-current-loaded-reference-model-getter;" +
        "High-idle-flag-and-action-and-wait-constructor-fields-independent-model-requery-bound-radius-one-sixty-fourth-winning-distance-setting;" +
        "idle-context-predicates-distance-bypasses-cell-selected-object-and-actor-gates-before-wait-unequal-or-unordered-setting-start-then-reread-reset;" +
        "first-person-animation-independent-returned-factory-two-null-pending-action-constructor-fields;" +
        "active-animation-pending-action-writers-remain-independent-and-unowned;" +
        "bound-read-current-selected-native-root-uses-owned-mesh-and-skin-bone-spheres-with-current-pose;" +
        "Actor-idle-state-predicate-independent-current-package-null-or-not-kind-three-returns-false;" +
        "Actor-idle-class-predicate-independent-process-cache-null-constructor-reads-real-status-value-forty-seven;" +
        "actual-source-status-descriptor-zero-base-no-formula-and-three-independent-mutable-pools;" +
        "Player-status-current-Float32-pool-order-before-ordered-positive-test;" +
        "allocated-status-cache-producers-remain-independent-unowned;" +
        "kind-three-package-action-state-table-remains-independent-and-unowned;" +
        "following-High-process-independent-movement-word-read-before-clear-before-old-mask-tests;" +
        "actual-PlayerMover-constructor-Actor-field-owner-independent-from-physical-controller;" +
        "mover-positive-zero-vector-and-two-zero-flags-and-minus-one-timer;" +
        "Actor-wrapper-independent-null-result-or-current-mover-virtual-flags-repeated-before-current-actor-field-child;" +
        "independent-Actor-movement-branch-byte-constructor-zero-not-physical-controller-or-life-state;" +
        "movement-field-writers-remain-independent-and-unowned;" +
        "current-Actor-physical-field-actual-source-process-factory-before-independent-movement-control-child;" +
        "physical-update-retains-real-living-models-admission-and-utility-prefix-on-return-or-failure;" +
        "following-controls-use-actual-stored-scene-Float32-current-input-and-mover-low-word-masked-cc00;" +
        "unreturned-physical-or-control-child-never-restores-thread-kind-or-decrements-scene-bracket;" +
        "cold-physical-and-control-receipts-remain-history-without-callback-input-or-store-replay;" +
        "idle-wait-otherwise-subtracts-current-Main-scaled-timer-unclamped-independent-from-scene-argument;" +
        "unknown-POV-and-full-virtual-children-retain-entered-prefix;" +
        "current-native-factory-and-source-resource-identity-fresh-cold-rebind";
    internal string Identity => FalloutAdvancementRuntimeReceipt.Hash(Main.Identity + "\0" + Contract);
    internal static FalloutStandalonePlayerSceneSource Read(FalloutMainScriptCallerSource main)
    {
        main.Validate();
        if (!FalloutSourceMainFamily.IsFallout3(main.EngineSha256))
            throw new NotSupportedException("The original standalone Player scene declaration is not this source family.");
        return new(main, FalloutAdvancementRuntimeReceipt.Hash(Rules));
    }
    internal void Validate()
    {
        Main.Validate();
        if (!FalloutSourceMainFamily.IsFallout3(Main.EngineSha256) || Contract != FalloutAdvancementRuntimeReceipt.Hash(Rules))
            throw new InvalidDataException("Standalone Player scene changed its original constructor, selector or transition declaration.");
    }
    internal const uint One = 0x3f800000;
    internal const uint ActorSceneResetBits = 0xbf800000;
    internal static (byte Scene, byte Hold) Toggle(byte before, byte argument) =>
        ((byte)(before == 0 ? 1 : 0), (byte)(before == 0 && argument != 0 ? 1 : 0));
}
