using System.Security.Cryptography;
using System.Text;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutPlayerRawTransferSource(FalloutMainPlayerPendingSource Pending, string Contract)
{
    private const string Rules = "source-Player-raw-transfer/v1;constructor-borrowed-world-CELL-reference-callback-context-furniture-null;" +
        "constructor-position-and-rotation-positive-zero;constructor-argument-zero;" +
        "target-selection-reference-then-worldspace-then-CELL;" +
        "MoveTo-source-current-position-at-command-before-offset-Float32-store;" +
        "MoveTo-rotation-X-and-Z-write-only-Y-retains-source-constructor-zero;" +
        "MoveTo-exterior-parent-worldspace-otherwise-interior-parent-CELL;argument-one;callback-null;furniture-target;" +
        "door-exact-directed-XTEL-position-and-all-rotation;interior-CELL-otherwise-worldspace;argument-zero;" +
        "door-nonnull-source-handler-and-door-reference-context;constructor-furniture-null;" +
        "source-Player-neutral-life-one-two-six-MoveTo-early-return;" +
        "world-transfer-original-FISTP-shift-twelve-before-current-CELL-consumer;" +
        "nonnull-argument-calls-distinct-Sky-reset-consumer;" +
        "no-late-target-reread-or-callback-null-inference;source-winner-and-current-request-retained";

    internal static FalloutPlayerRawTransferSource Read(FalloutMainPlayerPendingSource pending)
    {
        pending.Validate();
        return new(pending, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            pending.Contract + "\0" + FalloutSourceMainFamily.RawRules(pending.Player.Main.EngineSha256, Rules)))).ToLowerInvariant());
    }

    internal void Validate()
    {
        if (this != Read(Pending)) throw new InvalidDataException("Player raw transfer changed its selected factory declaration.");
    }
}
