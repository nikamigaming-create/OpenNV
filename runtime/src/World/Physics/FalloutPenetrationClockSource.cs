using System.Runtime.CompilerServices;

namespace OpenNV.Runtime.World.Physics;

// This is the reduced clock/conversion contract for the selected executable.
// Public counter APIs supply current ticks; executable data never supplies them.
internal sealed class FalloutPenetrationClockSource
{
    internal const float MillisecondsPerSecond = 1000f;
    private const double UnsignedWordRange = 4294967296d;
    private const int Float32ExponentBias = 127, Float32FractionBits = 23;
    private const uint Float32FractionMask = 0x007fffff, Float32ImplicitBit = 0x00800000;
    private const int Float32MaximumExponent = 0xff, SignedDurationMagnitudeBits = 63;

    private FalloutPenetrationClockSource(string engineSha256) => EngineSha256 = engineSha256;

    internal string EngineSha256 { get; }

    internal static FalloutPenetrationClockSource ForExecutable(string engineSha256)
    {
        if (!string.Equals(engineSha256, FalloutPhysicalConstructorInputs.ReviewedEngineSha256, StringComparison.Ordinal))
            throw new NotSupportedException("Selected engine has no reviewed penetration performance-counter contract.");
        return new(engineSha256);
    }

    internal uint TicksPerMillisecondBits(long frequency)
    {
        // Signed high word, unsigned low word, Double conversion, Float32
        // store, then Float32 division. Do not divide in Double or skip stores.
        var high = (int)(frequency >> 32);
        var low = unchecked((int)frequency);
        var lowValue = low < 0 ? AddDouble(low, UnsignedWordRange) : low;
        var signedFrequency = AddDouble(MultiplyDouble(high, UnsignedWordRange), lowValue);
        var storedFrequency = StoreFloat32(signedFrequency);
        return BitConverter.SingleToUInt32Bits(DivideFloat32(storedFrequency, MillisecondsPerSecond));
    }

    internal long ConvertDurationTicks(uint bits)
    {
        var exponentField = (int)((bits >> Float32FractionBits) & Float32MaximumExponent);
        var fraction = bits & Float32FractionMask;
        var negative = (bits & 0x80000000) != 0;
        var exponent = exponentField - Float32ExponentBias;
        if (exponentField == Float32MaximumExponent || exponent > SignedDurationMagnitudeBits ||
            (exponent == SignedDurationMagnitudeBits && (!negative || fraction != 0)))
            throw new NotSupportedException("source-penetration-duration-invalid-floating-exception-producer-unowned");
        if (exponent < 0) return 0;
        if (exponent == SignedDurationMagnitudeBits) return long.MinValue;
        var significand = (ulong)(Float32ImplicitBit | fraction);
        var magnitude = exponent < Float32FractionBits ?
            significand >> (Float32FractionBits - exponent) : significand << (exponent - Float32FractionBits);
        var signed = (long)magnitude;
        return negative ? -signed : signed;
    }

    // Finite destination values are known. This owner does not claim the source
    // CPU dispatch, sticky floating flags or a canonical register-image lane.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static double AddDouble(double left, double right) => left + right;
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static double MultiplyDouble(double left, double right) => left * right;
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static float StoreFloat32(double value) => (float)value;
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static float DivideFloat32(float left, float right) => left / right;
}
