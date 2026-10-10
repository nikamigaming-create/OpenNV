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
        return receipt.EngineSha256 is ReviewedExecutable or FalloutSourceMainFamily.Fallout3;
    }

    private const string Contract = "source-cached-timer/v1;uint32-tick-origin;constructor-cached-zero;" +
        "nested-byte-pause-wrap;resume-zero-returns-without-counter-read;cached-modulo-subtraction;separate-unscaled-and-scaled-float32-delta;" +
        "fixed-float32-millisecond-fraction;truncation;minimum-wait;maximum-delta;" +
        "rate-target-adjustment;raw-counter-not-game-hour;current-process-cold-rebase";

    private const string Fallout3Contract = "source-FO3-cached-timer/v1;selected-Main-single-constructor;uint32-tick-origin;constructor-cached-zero;" +
        "nested-byte-pause-wrap;resume-zero-returns-without-counter-read;cached-modulo-subtraction;" +
        "SSE-Float32-arithmetic-at-each-store;fixed-nonzero-Float32-millisecond-fraction;unsigned-truncation;" +
        "minimum-ten-wait;maximum-one-hundred-sixty-six-delta;current-target-rate-and-tenth-adjustment;" +
        "INI-rate-mode-before-nonmenu-adjust-before-counter-update;raw-counter-not-game-hour;current-process-cold-rebase";
    internal bool Float32Intermediates => EngineSha256 == FalloutSourceMainFamily.Fallout3;
    internal static bool SameFloat(float left, float right) =>
        BitConverter.SingleToUInt32Bits(left) == BitConverter.SingleToUInt32Bits(right);
    internal float Multiply(float left, float right) => Float32Intermediates ? left * right : (float)((double)left * right);
    internal float Divide(float left, float right) => Float32Intermediates ? left / right : (float)(left / (double)right);
    internal float Add(float left, float right) => Float32Intermediates ? left + right : (float)((double)left + right);
    internal float Milliseconds(uint value) => Float32Intermediates ? value * (float)MillisecondsToSeconds : (float)(value * MillisecondsToSeconds);
    internal float Milliseconds(float value) => Float32Intermediates ? value * (float)MillisecondsToSeconds : (float)(value * MillisecondsToSeconds);
    internal float Adjust(float value, bool increase)
    {
        if (!Float32Intermediates)
            return (float)((increase ? 1d + InitialRateAdjustment : 1d - InitialRateAdjustment) * value);
        var factor = increase ? 1f + InitialRateAdjustment : 1f - InitialRateAdjustment;
        return factor * value;
    }
    private static string ContractFor(string image) => image switch
    {
        ReviewedExecutable => Contract,
        FalloutSourceMainFamily.Fallout3 => Fallout3Contract,
        _ => throw new NotSupportedException("Selected executable cached timer constructor/writers have not been reduced.")
    };

    internal static FalloutSourceFrameTimer Read(FalloutAdvancementRuntimeReceipt receipt)
    {
        receipt.Validate();
        if (!Supports(receipt))
            throw new NotSupportedException("Selected executable cached timer constructor/writers have not been reduced.");
        return new(receipt.EngineSha256, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ContractFor(receipt.EngineSha256)))).ToLowerInvariant(),
            (double)0.001f, 10, 166, 1f, 0.1f);
    }

    internal void Validate()
    {
        if (ContractSha256 != Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ContractFor(EngineSha256)))).ToLowerInvariant() ||
            MillisecondsToSeconds != (double)0.001f || MinimumFrameMilliseconds != 10 ||
            MaximumFrameMilliseconds != 166 || InitialRate != 1 || InitialRateAdjustment != 0.1f)
            throw new InvalidDataException("Cached timer source constructor/arithmetic identity is invalid.");
    }
}
