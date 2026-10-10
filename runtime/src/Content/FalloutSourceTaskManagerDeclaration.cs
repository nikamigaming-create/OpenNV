using System.Security.Cryptography;
using System.Text;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutSourceTaskManagerDeclaration(string Executable, string Contract, int BucketCount, int IteratorSlots)
{
    internal static FalloutSourceTaskManagerDeclaration Read(FalloutMainPlayerCellSource player)
    {
        player.Validate();
        if (!FalloutSourceMainFamily.IsFallout3(player.Main.EngineSha256))
            throw new NotSupportedException("Selected original task-manager construction/reset declaration is unowned.");
        const string rules = "source-task-manager-reset/v1;actual-world-constructor;thirty-seven-zero-buckets;" +
            "zero-map-count;two-iterator-slots;first-current-thread-slot-one;" +
            "key-low-uint32-unsigned-modulo-bucket-count;bucket-ascending-current-entry-pin;" +
            "state-zero-one-five-compare-exchange-to-six;cancel-virtual-prior-state-null-child;" +
            "state-one-extra-reference-scheduler-key-index-remove-conditional-counters-before-cancel;" +
            "state-two-requery;state-three-four-yield-zero-until-change-and-requery;" +
            "above-five-no-cancel;iterator-reference-release-after-each-child;" +
            "actual-source-task-cohort-and-factory-ownership-required;" +
            "current-thread-registration-not-cold-handle;failed-prefix-and-still-owned-references-retained";
        return new(player.Main.EngineSha256, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            player.Contract + "\0" + rules))).ToLowerInvariant(), 37, 2);
    }

    internal void Validate(FalloutMainPlayerCellSource player)
    {
        if (this != Read(player)) throw new InvalidDataException("Source task-manager declaration changed its selected original transport.");
    }

    internal int Bucket(ulong key) => (int)(unchecked((uint)key) % (uint)BucketCount);
}
