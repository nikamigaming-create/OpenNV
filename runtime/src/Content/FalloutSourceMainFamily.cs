using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Content;

// Selected instruction transports are independent of campaign names, CHAL
// presence, UI readiness and installed data. Only the independently reviewed
// original identities choose these neutral declarations.
internal static class FalloutSourceMainFamily
{
    internal const string NewVegas = "518c87f58a6c4d9826e9ef8fbb7f4213882fa70822675610d45aea2464502a57";
    internal const string Fallout3 = "c3f97c2255fa041a851c17cf372d69aaadd8694e2dc4230ba556001bbfbd2f3e";
    internal static bool IsFallout3(string executable) => executable == Fallout3;
    internal static bool HasNewVegasChildren(string executable) => executable switch
    {
        NewVegas => true,
        Fallout3 => false,
        _ => throw new NotSupportedException("Selected original Main/Player family is unowned.")
    };
    internal static string MainFieldsContract(string executable, string newVegas) => executable switch
    {
        NewVegas => newVegas,
        Fallout3 => FalloutAdvancementRuntimeReceipt.Hash("source-FO3-Main-cache-only/v1;loader-zero;" +
            "two-ordered-channel-one-samples;nonnull-native-pointer-increasing-and-ordered-Float32-equal-one;" +
            "source-child-ordinal-and-current-Main-lease;failed-prefix-retained;" +
            "immediate-interpreter-and-CHAL-not-admitted-by-this-declaration"),
        _ => throw new NotSupportedException("Selected original Main cached-field transport is unowned.")
    };
    internal static string MainRules(string executable, string newVegas) => executable switch
    {
        NewVegas => newVegas,
        Fallout3 => "source-FO3-Main-script-segment/v1;OS-Tab-then-Alt-high-bit-short-circuit;" +
            "lazy-source-singleton-then-actual-empty-virtual-Prologue-return;" +
            "MenuGate-short-circuit-GUI-kind-two;independent-first-predicate-before-channel-one-sample;" +
            "independent-final-predicate-after-sample;foreign-active-menu-and-byte-kind-three-prelude;" +
            "Player-before-timed-contexts-with-no-intervening-Steam-child;" +
            "timer-argument-not-cached-menu-and-not-independent-Main-hold;" +
            "refreshed-menu-first-channel-one-final-in-source-order;context-clock-loader-zero-SSE-Float32-add-store;" +
            "real-child-return-required;this-segment-does-not-return-whole-Main;failed-prefix-not-replayed;" +
            "current-source-process-and-actual-native-thread-delivery-independent",
        _ => throw new NotSupportedException("Selected original Main segment is unowned.")
    };
    internal static string PlayerRules(string executable, string newVegas) => executable switch
    {
        NewVegas => newVegas,
        Fallout3 => "source-FO3-Main-Player/v1;one-constructor-null-pending-slot;" +
            "old-allocation-release-before-new-store-without-overlap-assertion;" +
            "independent-TES-two-byte-immediate-consume-query;" +
            "TaskManager-reset-before-owned-child-release-before-reference-WRLD-CELL-target-selection;" +
            "nonnull-callback-before-furniture-no-outer-scene-scalar-store;" +
            "reference-travel-skips-callback-and-furniture;empty-still-reaches-furniture;" +
            "deferred-destruction-before-payload-free-before-null;manager-word-bit-two-only-before-whole-byte-one-store;" +
            "cached-menu-and-independent-interface-kind-two-held-child;" +
            "independent-Main-scene-mode-before-selected-Player-scene-read-and-virtual-update;" +
            "raw-parent-CELL-and-position;interior-short-circuit-before-rounding;" +
            "SSE-Float32-stack-copy-direct-FLD-FISTP-X-then-Y-and-signed-shift-twelve;" +
            "target-phase-three-or-six-skips-world-load;world-bracket-set-CELL-attach-world-bracket-clear;" +
            "no-Player-movement-bracket-or-source-root-store;optional-tree-child;" +
            "genuine-return-publication-retirement-distinct;failed-prefix-and-cold-epochs-retained",
        _ => throw new NotSupportedException("Selected original Player dispatch is unowned.")
    };
    internal static string PendingRules(string executable, string newVegas) => executable switch
    {
        NewVegas => newVegas,
        Fallout3 => "source-FO3-Player-pending-tail/v1;constructor-null-child-zero-tail-byte;" +
            "actual-TaskManager-reset-is-distinct-from-NV-exterior-thirty-seven-bucket-map;" +
            "release-old-owned-refcount-before-null;target-reference-then-worldspace-then-CELL;" +
            "no-outer-position-third-cell-to-High-controller-scalar-store;" +
            "nonnull-declared-callback-and-context-before-furniture;reference-arm-skips-both;" +
            "empty-target-reaches-furniture;winning-FURN-first-enabled-unoccupied-marker;" +
            "actual-native-marker-and-physical-publication-required;" +
            "original-TLS-deferred-manager-before-free-and-null;" +
            "post-null-manager-word-mask-two-with-no-second-Player-bit-test;whole-Player-byte-store-one;" +
            "failed-child-ownership-and-fresh-process-cold-binding-retained",
        _ => throw new NotSupportedException("Selected original pending consumer is unowned.")
    };
    internal static string RawRules(string executable, string newVegas) => executable switch
    {
        NewVegas => newVegas,
        Fallout3 => "source-FO3-Player-pending-factories/v1;actual-fifty-two-byte-allocation;" +
            "literal-null-borrowed-target-callback-context-furniture-and-final-word;" +
            "literal-positive-zero-position-rotation-and-zero-argument;reference-WRLD-CELL-priority;" +
            "MoveToMarker-captured-target-position-plus-offset-in-source-Float32-add-store;" +
            "rotation-first-and-third-overwrite-middle-keeps-constructor-zero;" +
            "actual-target-parent-WRLD-otherwise-interior-CELL;argument-one;callback-null;furniture-target;" +
            "actual-Player-neutral-life-one-two-six-early-return;" +
            "door-directed-XTEL-all-six-vector-cells;interior-CELL-otherwise-WRLD;argument-zero;" +
            "nonnull-source-door-handler-with-source-door-context;furniture-retains-null;" +
            "one-slot-old-free-before-new-store;independent-TES-immediate-consume;" +
            "no-late-target-reread;exact-winners-and-current-request-source;" +
            "Sky-and-task-TLS-consumer-admission-independent",
        _ => throw new NotSupportedException("Selected original Player allocation writer is unowned.")
    };
    internal static FalloutFistpOperation GridOperation(string executable) => executable switch
    {
        NewVegas => FalloutFistpOperation.SourceArgument,
        Fallout3 => FalloutFistpOperation.Convert,
        _ => throw new NotSupportedException("Selected original grid argument transport is unowned.")
    };
    internal static bool PendingFinalFlagRequired(string executable, uint manager, byte flags)
    {
        var newVegas = HasNewVegasChildren(executable);
        return (manager & 2u) == 0 && (!newVegas || (flags & 2) == 0);
    }
    internal static byte PendingFinalFlagValue(string executable, byte before) =>
        HasNewVegasChildren(executable) ? (byte)(before | 1) : (byte)1;
    internal static float ContextClockValue(string executable, float before, float delta) =>
        HasNewVegasChildren(executable) ? (float)((double)before + delta) : before + delta;
}
