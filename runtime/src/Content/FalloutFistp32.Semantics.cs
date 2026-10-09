namespace OpenNV.Runtime.Content;

// Independent bit arithmetic checks the native result. It never supplies a
// product rounding mode or replaces the calling thread's actual instruction.
internal static class FalloutFistp32Semantics
{
    internal static FalloutFistpRounding Rounding(ushort control) => (FalloutFistpRounding)((control >> 10) & 3);
    internal static FalloutFistp32Value Read(uint bits, FalloutFistpRounding rounding)
    {
        if (!Enum.IsDefined(rounding)) throw new InvalidDataException("FISTP has no admitted rounding control.");
        var negative = (bits & 0x80000000u) != 0;
        var exponent = (int)((bits >> 23) & 255);
        var fraction = bits & 0x7fffffu;
        if (exponent == 255 || exponent >= 158 && bits != 0xcf000000u)
            return new(int.MinValue, 1, false, true);
        if (bits == 0xcf000000u) return new(int.MinValue, 0, false, false);
        if (exponent == 0 && fraction == 0) return new(0, 0, false, false);
        var significand = (ulong)(exponent == 0 ? fraction : fraction | 0x800000u);
        var power = exponent == 0 ? -149 : exponent - 150;
        ulong magnitude; var inexact = false;
        if (power >= 0) magnitude = significand << power;
        else
        {
            var shift = -power;
            magnitude = shift >= 64 ? 0 : significand >> shift;
            var remainder = shift >= 64 ? significand : significand & ((1ul << shift) - 1);
            inexact = remainder != 0;
            // A Float32 significand is at most 24 bits. For a larger divisor
            // it is strictly below one half; no oversized host shift is used.
            var half = shift > 24 ? 0ul : 1ul << (shift - 1);
            var increment = inexact && (rounding switch
            {
                FalloutFistpRounding.Down => negative,
                FalloutFistpRounding.Up => !negative,
                FalloutFistpRounding.TowardZero => false,
                FalloutFistpRounding.NearestEven => shift <= 24 && (remainder > half || remainder == half && (magnitude & 1) != 0),
                _ => throw new InvalidDataException("FISTP rounding control changed.")
            });
            if (increment) magnitude++;
        }
        if (magnitude > (negative ? 0x80000000ul : 0x7ffffffful)) return new(int.MinValue, 1, false, true);
        var value = negative ? unchecked(-(int)magnitude) : (int)magnitude;
        // FLD of an original denormal raises DE before FISTP raises PE. Sticky
        // flags already present remain owned by the actual caller environment.
        var exceptions = (ushort)((exponent == 0 ? 2 : 0) | (inexact ? 0x20 : 0));
        return new(value, exceptions, inexact, false);
    }
    internal static FalloutFistp32Value RequireConverted(FalloutFistpObservation observation)
    {
        FalloutFistp32Abi.RequireHeader(observation, observation.Operation, observation.InputBits);
        if (observation.Operation == FalloutFistpOperation.Observe) throw new InvalidDataException("An x87 observation has no native integer instruction.");
        if (observation.Outcome != FalloutFistpOutcome.Converted)
            throw new NotSupportedException("Actual calling-thread FISTP refused its native environment: " + observation.Outcome);
        if ((observation.StatusBefore & 0x80) != 0 || (observation.ControlBefore & 0x3f) != 0x3f ||
            (observation.ControlBefore & 0x300) == 0x100 || NextSlotOccupied(observation.StatusBefore, observation.TagBefore))
            throw new InvalidDataException("Native FISTP claimed conversion across a refused x87 exception/stack policy.");
        RequireArgumentTransport(observation);
        var expected = Read(observation.ArgumentBits, Rounding(observation.ControlBefore));
        if (observation.Value != expected.Integer || observation.ControlBefore != observation.ControlAfter ||
            observation.TagBefore != observation.TagAfter || (observation.StatusBefore & 0x3800) != (observation.StatusAfter & 0x3800) ||
            (observation.StatusAfter & 0x3f) != ((observation.StatusAtConversion & 0x3f) | expected.RaisedExceptions) ||
            (observation.StatusBefore & 0x40) != (observation.StatusAfter & 0x40) || (observation.StatusAfter & 0x80) != 0)
            throw new InvalidDataException("Calling-thread FISTP result/status differs from its exact Float32/control/stack semantics.");
        // C0/C2/C3 are undefined for FISTP. Preserve them as observed data,
        // rather than inventing a condition-bit continuation from C# math.
        return expected;
    }
    private static void RequireArgumentTransport(FalloutFistpObservation observation)
    {
        if (observation.Operation == FalloutFistpOperation.Convert)
        {
            if (observation.ArgumentBits != observation.InputBits || observation.StatusAtConversion != observation.StatusBefore)
                throw new InvalidDataException("Direct FISTP silently added another Float32 transport.");
            return;
        }
        var exponent = (observation.InputBits >> 23) & 255;
        var fraction = observation.InputBits & 0x7fffff;
        var signaling = exponent == 255 && fraction != 0 && (fraction & 0x400000) == 0;
        var denormal = exponent == 0 && fraction != 0;
        var argument = signaling ? observation.InputBits | 0x400000u : observation.InputBits;
        var mandatory = signaling ? 1 : denormal ? 2 : 0;
        // Retain the processor's actual tiny Float32-store UE/PE flags. They
        // are a distinct observed transport prefix, never inferred from the
        // later integer, a default environment or another native thread.
        var allowed = mandatory | (denormal ? 0x30 : 0);
        var before = observation.StatusBefore & 0x3f;
        var at = observation.StatusAtConversion & 0x3f;
        if (observation.Operation != FalloutFistpOperation.SourceArgument || observation.ArgumentBits != argument ||
            (at & before) != before || (at & mandatory) != mandatory || (at & ~(before | allowed)) != 0 ||
            (observation.StatusAtConversion & 0x80) != 0 || (observation.StatusAtConversion & 0x3840) != (observation.StatusBefore & 0x3840))
            throw new InvalidDataException("Actual source Float32 argument transport changed bits, sticky flags or stack ownership.");
    }
    internal static bool NextSlotOccupied(ushort status, byte tag) => (tag & (1 << (((status >> 11) - 1) & 7))) != 0;
    internal static void RequireRefusal(FalloutFistpObservation observation)
    {
        FalloutFistp32Abi.RequireHeader(observation, observation.Operation, observation.InputBits);
        if (observation.Operation == FalloutFistpOperation.Observe) throw new InvalidDataException("An x87 observation was substituted for a refused source integer conversion.");
        var expected = (observation.StatusBefore & 0x80) != 0 ? FalloutFistpOutcome.PendingException :
            (observation.ControlBefore & 0x3f) != 0x3f ? FalloutFistpOutcome.UnmaskedException :
            (observation.ControlBefore & 0x300) == 0x100 ? FalloutFistpOutcome.ReservedPrecision :
            NextSlotOccupied(observation.StatusBefore, observation.TagBefore) ? FalloutFistpOutcome.OccupiedStack : FalloutFistpOutcome.Converted;
        if (expected == FalloutFistpOutcome.Converted || observation.Outcome != expected || observation.ControlBefore != observation.ControlAfter ||
            observation.StatusBefore != observation.StatusAfter || observation.TagBefore != observation.TagAfter || observation.Value != int.MinValue ||
            observation.ArgumentBits != observation.InputBits || observation.StatusAtConversion != observation.StatusBefore)
            throw new InvalidDataException("Native FISTP refusal lost its actual unchanged environment or exact reason.");
    }
}
