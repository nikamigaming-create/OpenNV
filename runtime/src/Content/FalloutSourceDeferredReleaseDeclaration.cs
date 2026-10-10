using System.Security.Cryptography;
using System.Text;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutSourceDeferredReleaseDeclaration(string Executable, string Contract,
    byte InitialEnabled, ushort ReferenceGrowth, ushort RawGrowth, ushort FormGrowth)
{
    internal static FalloutSourceDeferredReleaseDeclaration Read(FalloutMainPlayerCellSource player)
    {
        player.Validate();
        if (!FalloutSourceMainFamily.IsFallout3(player.Main.EngineSha256))
            throw new NotSupportedException("Selected deferred-release caller TLS, skip-mask and try-lock producers are unowned.");
        const string rules = "source-deferred-release/v1;module-initial-data-enable-one;five-empty-UInt16-arrays-and-empty-UInt32-buffer;" +
            "raw-and-reference-growth-fifty;form-growth-one-thousand;embedded-buffer-initial-four-double-through-1024-then-add-1024;" +
            "actual-current-owner-recursive-lock;save-enable-clear-before-first-child-restore-after-last-child;" +
            "reference-A-before-raw-A-before-raw-B-before-reference-B-before-self-removing-form-before-embedded-reference;" +
            "raw-batches-capture-end-before-destruct-free-then-clear-all-slots;reference-end-requeried-after-each-release;" +
            "self-removing-front-virtual-delete-true-must-remove-its-own-entry;embedded-reference-release-before-buffer-clear;" +
            "raw-distinct-preparation-before-enabled-and-Main-forced-bit-two;duplicate-raw-before-preparation;" +
            "reference-and-embedded-disabled-no-acquire;raw-and-form-disabled-immediate-child;" +
            "reference-temporary-acquire-before-array-slot-acquire-before-temporary-release;" +
            "smart-reference-template-TLS-construction-is-independent;" +
            "failed-opaque-child-keeps-potentially-owned-allocation-reference-and-entered-prefix;" +
            "current-source-factory-and-native-owner-required;fresh-cold-process-no-pointer-or-return-promotion";
        return new(player.Main.EngineSha256, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            player.Contract + "\0" + rules))).ToLowerInvariant(), 1, 50, 50, 1000);
    }

    internal void Validate(FalloutMainPlayerCellSource player)
    {
        if (this != Read(player))
            throw new InvalidDataException("Deferred-release declaration changed its actual selected source transport.");
    }
}
