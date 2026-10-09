using System.Security.Cryptography;
using System.Text;
namespace OpenNV.Runtime.Content;

// Reduced from the selected constructor, public OS counter import and its
// independent update/pause methods. No address or native layout is an input.
internal sealed record FalloutSourceFrameTimer(string EngineSha256, string ContractSha256,
    double MillisecondsToSeconds, uint MinimumFrameMilliseconds, uint MaximumFrameMilliseconds,
    float InitialRate, float InitialRateAdjustment)
{
    private const string ReviewedExecutable = "518c87f58a6c4d9826e9ef8fbb7f4213882fa70822675610d45aea2464502a57";
    internal static bool Supports(FalloutAdvancementRuntimeReceipt receipt)
    {
        receipt.Validate();
        return receipt.EngineSha256 == ReviewedExecutable;
    }

    private const string Contract = "source-cached-timer/v1;uint32-tick-origin;constructor-cached-zero;" +
        "nested-byte-pause-wrap;resume-zero-returns-without-counter-read;cached-modulo-subtraction;separate-unscaled-and-scaled-float32-delta;" +
        "fixed-float32-millisecond-fraction;truncation;minimum-wait;maximum-delta;" +
        "rate-target-adjustment;raw-counter-not-game-hour;current-process-cold-rebase";

    internal static FalloutSourceFrameTimer Read(FalloutAdvancementRuntimeReceipt receipt)
    {
        receipt.Validate();
        if (!Supports(receipt))
            throw new NotSupportedException("Selected executable cached timer constructor/writers have not been reduced.");
        return new(receipt.EngineSha256, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Contract))).ToLowerInvariant(),
            (double)0.001f, 10, 166, 1f, 0.1f);
    }

    internal void Validate()
    {
        if (EngineSha256 != ReviewedExecutable ||
            ContractSha256 != Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Contract))).ToLowerInvariant() ||
            MillisecondsToSeconds != (double)0.001f || MinimumFrameMilliseconds != 10 ||
            MaximumFrameMilliseconds != 166 || InitialRate != 1 || InitialRateAdjustment != 0.1f)
            throw new InvalidDataException("Cached timer source constructor/arithmetic identity is invalid.");
    }
}
