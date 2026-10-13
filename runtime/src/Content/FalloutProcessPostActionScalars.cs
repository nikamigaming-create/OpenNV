namespace OpenNV.Runtime.Content;

// These independent High-process cells are constructed before its first
// update. The timer gates the nullable context consumer, not Player's action
// list, scene clock, or the separate detection-action timer.
internal sealed record FalloutProcessPostActionScalars(uint ValueBits, uint TimerBits, long Updates)
{
    internal static FalloutProcessPostActionScalars Constructed => new(0, 0, 0);
    internal bool SubtractsTimer => !(BitConverter.UInt32BitsToSingle(TimerBits) <= 0);
    internal FalloutProcessPostActionScalars Subtract(float scaledTimer) => this with
    {
        TimerBits = BitConverter.SingleToUInt32Bits((float)((double)BitConverter.UInt32BitsToSingle(TimerBits) - scaledTimer)),
        Updates = checked(Updates + 1)
    };
    internal FalloutProcessPostActionScalars Publish(float contextValue)
    {
        // SSE max/min use their second operand for unordered/equal values.
        var lower = contextValue < 0 ? 0f : contextValue;
        var value = lower > 100 ? 100f : lower;
        return new(BitConverter.SingleToUInt32Bits(value),
            BitConverter.SingleToUInt32Bits((float)(value * .01d + 3d)), checked(Updates + 1));
    }
}

internal sealed record FalloutProcessDetectionActionScalars(uint ValueBits, uint TimerBits, long Updates)
{
    internal static FalloutProcessDetectionActionScalars Constructed => new(0, 0, 0);
    internal bool SubtractsTimer => BitConverter.UInt32BitsToSingle(TimerBits) > 0;
    internal FalloutProcessDetectionActionScalars Subtract(float scaledTimer) => this with
    {
        TimerBits = BitConverter.SingleToUInt32Bits(BitConverter.UInt32BitsToSingle(TimerBits) - scaledTimer),
        Updates = checked(Updates + 1)
    };
    internal FalloutProcessDetectionActionScalars Reset(float sourceTimer) =>
        new(0, BitConverter.SingleToUInt32Bits(sourceTimer), checked(Updates + 1));
}

internal sealed record FalloutPlayerPostActionFields(byte ContextFlag, uint CombatListFlag,
    uint FirstTimerBits, uint SecondTimerBits)
{
    internal static FalloutPlayerPostActionFields Constructed => new(1, 0, 0xbf800000, 0xbf800000);
    internal FalloutPlayerPostActionFields UpdateTimers(float sceneSeconds) => this with
    {
        FirstTimerBits = Update(FirstTimerBits, sceneSeconds),
        SecondTimerBits = Update(SecondTimerBits, sceneSeconds)
    };
    private static uint Update(uint bits, float seconds) => BitConverter.UInt32BitsToSingle(bits) > 0
        ? BitConverter.SingleToUInt32Bits(BitConverter.UInt32BitsToSingle(bits) - seconds) : 0xbf800000;
}
