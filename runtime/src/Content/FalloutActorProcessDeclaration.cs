using System.Security.Cryptography;
using System.Text;
using System.Numerics;

namespace OpenNV.Runtime.Content;

// The selected original owns process construction, cohort operations and the
// timer consumer. These declarations contain no original code or addresses.
internal sealed record FalloutActorProcessDeclaration(string ExecutableSha256,
    FalloutDetectionScalarKind Arithmetic, bool PlayerScoreIncludesEquality, string Contract)
{
    internal static FalloutActorProcessDeclaration Read(string executable)
    {
        using var input = new FileStream(executable, FileMode.Open, FileAccess.Read, FileShare.Read);
        return ForExecutable(Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant());
    }

    internal static FalloutActorProcessDeclaration ForExecutable(string sha256)
    {
        var perception = FalloutActorPerceptionDeclaration.ForExecutable(sha256);
        var includesEquality = perception.Scalar == FalloutDetectionScalarKind.NewVegas;
        var neutral = sha256 + "\0actor-process/v1;actor-initial-low;constructor-registration-caller-boolean-and-mode;player-high-outside-cohort;" +
            "segmented-four-tier-array;recursive-boundary-displacement;swap-last-removal;" +
            "lookup-from-tier-start-through-low-end;cursor-wrap-at-source-segment;" +
            "full-high-walk-current-end;high-timer-initial-positive-zero;compute-only-strict-negative;" +
            "negative-life-query-codes-1-2-3-6-remove-player-target-before-decrement;" +
            "category-one-positive-skips-compute;shared-unsigned-random;maximum-random-window-five;" +
            "producer-clears-both-pending-directions-before-early-guards;timer-reset-after-complete-producer;" +
            "factory-copy-before-old-retirement-before-new-publication;" +
            (includesEquality ? "x87-count-times-delta-stored-once;score-ge-threshold" :
                "sse-count-stored-before-multiply;score-gt-threshold");
        return new(sha256, perception.Scalar, includesEquality,
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(neutral))).ToLowerInvariant());
    }

    internal void Validate()
    {
        if (this != ForExecutable(ExecutableSha256)) throw new InvalidDataException("Actor process declaration drifted.");
    }

    internal double MaximumPlayerProducerDistance => 8192d;

    internal static bool TimerNeedsProducer(float current)
    {
        if (!float.IsFinite(current)) throw new InvalidDataException("Actor detection timer is non-finite.");
        return current < 0f;
    }

    internal static float DecrementTimer(float current, float delta)
    {
        if (!float.IsFinite(current) || !float.IsFinite(delta) || delta < 0)
            throw new InvalidDataException("Actor detection countdown has no finite source inputs.");
        var next = (float)((double)current - delta);
        return float.IsFinite(next) ? next : throw new InvalidDataException("Detection timer decrement overflowed.");
    }

    internal float RandomWindow(uint sourceHighEnd, float frameDelta)
    {
        if (!float.IsFinite(frameDelta) || frameDelta < 0)
            throw new InvalidDataException("Original actor update has no finite frame interval.");
        var scaled = Arithmetic == FalloutDetectionScalarKind.NewVegas ?
            MultiplyUnsignedStored(sourceHighEnd, frameDelta, 0) : (float)((float)sourceHighEnd * frameDelta);
        return Math.Min(scaled, 5f);
    }

    internal float CadenceDraw(uint rawSharedWord, float window)
    {
        if (!float.IsFinite(window) || window < 0 || window > 5f)
            throw new InvalidDataException("Detection cadence has no original capped window.");
        return Arithmetic == FalloutDetectionScalarKind.NewVegas ?
            (float)(MultiplyUnsignedStored(rawSharedWord, window, -32) + 0f) :
            (float)((float)((float)((window - 0f) * (float)rawSharedWord) * (1f / 4294967296f)) + 0f);
    }

    internal bool PlayerScoreAdmitted(int signedScore, float sourceThreshold)
    {
        if (!float.IsFinite(sourceThreshold)) throw new InvalidDataException("Player detection threshold is non-finite.");
        return Arithmetic == FalloutDetectionScalarKind.NewVegas ? (double)signedScore >= sourceThreshold :
            (float)signedScore > sourceThreshold;
    }

    // uint32 * float32 needs as many as 56 significant bits. The original
    // x87 operation retains that exact product before its one Float32 store;
    // evaluating through double can round twice at a final halfway value.
    private static float MultiplyUnsignedStored(uint word, float source, int power)
    {
        var bits = unchecked((uint)BitConverter.SingleToInt32Bits(source));
        var encodedExponent = (int)((bits >> 23) & 255);
        var significand = (bits & 0x7fffff) | (encodedExponent == 0 ? 0u : 0x800000u);
        var integer = (ulong)word * significand;
        if (integer == 0) return BitConverter.Int32BitsToSingle(unchecked((int)(bits & 0x80000000)));
        var scale = (encodedExponent == 0 ? -149 : encodedExponent - 150) + power;
        var length = 64 - BitOperations.LeadingZeroCount(integer);
        var exponent = length - 1 + scale;
        if (exponent < -126)
            return BitConverter.Int32BitsToSingle(unchecked((int)RoundStored(integer, -(scale + 149))));
        var rounded = RoundStored(integer, length - 24);
        if (rounded == 0x1000000) { rounded >>= 1; exponent++; }
        if (exponent > 127) return float.PositiveInfinity;
        var encoded = ((uint)(exponent + 127) << 23) | (uint)(rounded & 0x7fffff);
        return BitConverter.Int32BitsToSingle(unchecked((int)encoded));
    }
    private static ulong RoundStored(ulong integer, int shift)
    {
        if (shift <= 0) return integer << -shift;
        if (shift >= 64) return 0;
        var value = integer >> shift;
        var remainder = integer & ((1UL << shift) - 1);
        var half = 1UL << (shift - 1);
        return value + (remainder > half || remainder == half && (value & 1) != 0 ? 1UL : 0UL);
    }
}
