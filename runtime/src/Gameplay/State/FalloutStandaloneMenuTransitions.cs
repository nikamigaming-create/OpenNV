namespace OpenNV.Runtime.Gameplay.State;

// The selected source uses these SSE operand orders, not Math.Clamp. This
// helper describes Float32 value transport; it does not admit an unowned
// calling-thread status/control mode, physical frame or allocator/TLS scope.
internal static class FalloutStandaloneMenuTransitions
{
    internal static uint Duration(uint supplied)
    {
        var value = BitConverter.UInt32BitsToSingle(supplied);
        return BitConverter.SingleToUInt32Bits(Max(value, 0));
    }
    internal static (uint Elapsed, uint Progress, bool Remove) Advance(uint elapsed, uint duration,
        uint delta, uint kind, uint? divisor)
    {
        var added = BitConverter.UInt32BitsToSingle(delta);
        if (kind == 4)
        {
            if (divisor is not { } scale)
                throw new NotSupportedException("Original menu kind4 update omitted its independent Float32 divisor.");
            added /= BitConverter.UInt32BitsToSingle(scale);
        }
        else if (divisor is not null)
            throw new InvalidDataException("Normal source menu update invented the kind4 divisor arm.");
        var stored = BitConverter.UInt32BitsToSingle(elapsed) + added;
        var ratio = stored / BitConverter.UInt32BitsToSingle(duration);
        var progress = Max(0, Min(1, ratio));
        return (BitConverter.SingleToUInt32Bits(stored), BitConverter.SingleToUInt32Bits(progress),
            !float.IsNaN(progress) && progress == 1);
    }
    private static float Min(float first, float second) => float.IsNaN(first) || float.IsNaN(second) || first == second
        ? second : first < second ? first : second;
    private static float Max(float first, float second) => float.IsNaN(first) || float.IsNaN(second) || first == second
        ? second : first > second ? first : second;
}
