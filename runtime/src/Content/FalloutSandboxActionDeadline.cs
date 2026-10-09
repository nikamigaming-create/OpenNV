using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutSandboxActionDeadline(string EngineSha256)
{
    internal static FalloutSandboxActionDeadline Read(FalloutAdvancementRuntimeReceipt receipt)
    {
        receipt.Validate();
        if (receipt.EngineSha256 != "518c87f58a6c4d9826e9ef8fbb7f4213882fa70822675610d45aea2464502a57")
            throw new NotSupportedException("Selected Sandbox original game-hour deadline comparator is unowned.");
        return new(receipt.EngineSha256);
    }
    // Preserve the actual independently reduced hour/minute conversion. It is
    // not seconds, TimeScale multiplication, a monotonic frame accumulation or
    // a corrected calendar algorithm. The source has signed-byte intermediate
    // values and uses the saved whole hour for both minute expressions.
    internal float Elapsed(float selectionHour, float currentHour)
    {
        Validate();
        if (!float.IsFinite(selectionHour) || !float.IsFinite(currentHour))
            throw new NotSupportedException("Sandbox deadline has no owned finite source-hour conversion.");
        var oldHour = ByteTruncate(selectionHour);
        var oldMinute = ByteTruncate(((double)selectionHour - oldHour) * 60d);
        var currentWhole = ByteTruncate(currentHour);
        var currentMinute = ByteTruncate(((double)currentHour - oldHour) * 60d);
        var hours = (float)Math.Abs(currentWhole - oldHour);
        return (float)((double)hours + currentMinute - oldMinute);
    }
    internal void Validate()
    {
        if (EngineSha256 != "518c87f58a6c4d9826e9ef8fbb7f4213882fa70822675610d45aea2464502a57")
            throw new InvalidDataException("Sandbox game-hour comparator changed its selected source.");
    }
    private static sbyte ByteTruncate(double value)
    {
        // Do not bound GameHour to one day: the shared source global can hold a
        // carried hour. Only the independently owned signed conversion extent
        // is required before retaining its low byte.
        if (!double.IsFinite(value) || value < -9223372036854775808d || value >= 9223372036854775808d)
            throw new NotSupportedException("Sandbox hour/minute conversion exceeds its signed source extent.");
        return unchecked((sbyte)(long)Math.Truncate(value));
    }
}
