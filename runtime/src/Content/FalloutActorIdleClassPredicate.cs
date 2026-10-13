namespace OpenNV.Runtime.Content;

internal sealed record FalloutActorIdleClassValue(uint ValueBits, bool Cached);
internal static class FalloutActorIdleClassPredicate
{
    internal static (bool Result, FalloutActorIdleClassValue Value) Read(Func<float> currentValue,
        FalloutActorIdleClassValue? cached = null)
    {
        ArgumentNullException.ThrowIfNull(currentValue);
        var value = cached ?? new FalloutActorIdleClassValue(BitConverter.SingleToUInt32Bits(currentValue()), false);
        if (cached is { Cached: false }) throw new InvalidDataException("Idle actor cache has no valid source value.");
        return (BitConverter.UInt32BitsToSingle(value.ValueBits) > 0, value);
    }
}
