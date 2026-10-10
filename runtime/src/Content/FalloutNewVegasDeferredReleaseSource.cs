using System.Security.Cryptography;
using System.Text;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutNewVegasDeferredReleaseSource(string Executable, string Contract,
    byte InitialCallerEnabled, uint InitialSkipMask, ushort ArrayGrowth, ushort FormGrowth, uint EmbeddedCapacity)
{
    internal static FalloutNewVegasDeferredReleaseSource Read(FalloutMainPlayerCellSource player)
    {
        player.Validate();
        if (!player.Main.HasNewVegasChildren)
            throw new NotSupportedException("New Vegas deferred release requires its independently selected source caller.");
        const string rules = "newvegas-deferred-release/v1;original-compiler-TLS-initial-byte-one;module-busy-and-mask-zero;" +
            "six-primary-and-five-secondary-actual-empty-array-constructors;array-growth-150;form-growth-5000;embedded-capacity-70-end-zero;" +
            "recursive-owner-and-count-zero-lock-constructors;pending-true-uses-try-lock;busy-and-failed-try-lock-return;" +
            "same-caller-byte-save-clear-restore;independent-raw-and-allocation-lock-queries;" +
            "ordered-skip-bits-16-8-4-2-1-32;restored-nonzero-caller-byte-transfers-secondary-after-primary;" +
            "unbound-object-enqueue-and-memory-thread-writers-refused;fresh-cold-lock-and-caller-constructors";
        return new(player.Main.EngineSha256, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            player.Contract + "\0" + rules))).ToLowerInvariant(), 1, 0, 150, 5000, 70);
    }

    internal void Validate(FalloutMainPlayerCellSource player)
    {
        if (this != Read(player))
            throw new InvalidDataException("New Vegas deferred release changed its reviewed caller/constructor source.");
    }
}
