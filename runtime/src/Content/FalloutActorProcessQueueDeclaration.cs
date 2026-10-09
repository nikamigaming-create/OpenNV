using System.Security.Cryptography;
using System.Text;

namespace OpenNV.Runtime.Content;

// The queue map, manager pending list and counted CELL extra are independent
// source owners. A node, process allocation or worker registration is none of them.
internal sealed record FalloutActorProcessQueueDeclaration(string ExecutableSha256, string Contract)
{
    internal static FalloutActorProcessQueueDeclaration ForExecutable(string executable)
    {
        _ = FalloutActorProcessDeclaration.ForExecutable(executable);
        const string rules = "actor-process-queue/v1;loader-constructor-empty-reference-to-owned-queued-reference-map;" +
            "membership-by-exact-reference-key-not-scene;unique-insertion-without-replacement;" +
            "value-retained-by-map-and-exact-consumers;matched-key-and-value-removal;" +
            "CELL-ExtraProcessMiddleLow-type-nine-presence;constructor-count-zero;" +
            "absent-decrease-returns;first-increase-creates-before-current-reference-walk;" +
            "actual-Actor-raw-reference-disabled-bit-11-clear-current-Low-only-reevaluation;" +
            "increase-decrease-uint32-stored-wrap-remove-extra-only-on-stored-zero;" +
            "manager-unique-pending-reference-list;actual-Character-Creature-Player-first-guard-true;" +
            "nullable-process-early-return;current-versus-original-desired-tier;neutral-life-query-codes-1-2-6;" +
            "process-common-byte-bit-four;reference-bit-five;positive-Player-transition-counter;" +
            "reference-bit-seventeen-before-pending-insertion;request-byte-after-guarded-insertion;" +
            "independent-Actor-loader-word-constructor-positive-zero;factory-read-bit-one-only-on-original-reference-type-guard;" +
            "queued-reference-priority-read-byte-request-compares-full-int32-stored-byte;" +
            "explicit-source-3D-null-high-initializer-return-distinct-from-unavailable-cold-provider";
        return new(executable, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(executable + "\0" + rules))).ToLowerInvariant());
    }

    internal void Validate()
    {
        if (this != ForExecutable(ExecutableSha256)) throw new InvalidDataException("Actor process queue declaration drifted.");
    }

    internal static uint StoreCount(uint before, bool increase) => increase ? unchecked(before + 1u) : unchecked(before - 1u);
    internal static bool RejectedLife(int value) => value is 1 or 2 or 6;
    internal const uint DisabledReferenceFlag = 0x800u;
    internal const uint PendingReferenceFlag = 0x20000u;
    internal const uint DeletedReferenceFlag = 0x20u;
    internal const byte CommonRequestFlag = 0x10;
}
