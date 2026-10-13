using OpenNV.Runtime.Content;

internal static partial class SourceMainScriptCallerContracts
{
    private static void RunPlayerPostActionArithmetic()
    {
        var constructed = FalloutProcessPostActionScalars.Constructed;
        Require(!constructed.SubtractsTimer && constructed.Publish(0) == new FalloutProcessPostActionScalars(0, 0x40400000, 1),
            "Null post-action context lost its independent value/timer stores.");
        foreach (var (input, value, timer) in new[] { (-5f, 0f, 3f), (50f, 50f, 3.5f), (125f, 100f, 4f) })
        {
            var result = constructed.Publish(input);
            Require(BitConverter.UInt32BitsToSingle(result.ValueBits) == value && BitConverter.UInt32BitsToSingle(result.TimerBits) == timer,
                "Post-action value clamp or double-to-Float32 timer transform changed.");
        }
        var negativeZero = constructed.Publish(BitConverter.UInt32BitsToSingle(0x80000000));
        Require(negativeZero.ValueBits == 0x80000000, "SSE max/min lost the equal second operand's negative zero.");
        var unordered = constructed.Publish(BitConverter.UInt32BitsToSingle(0x7fc00012));
        Require(float.IsNaN(BitConverter.UInt32BitsToSingle(unordered.ValueBits)) && unordered.SubtractsTimer &&
            float.IsNaN(BitConverter.UInt32BitsToSingle(unordered.Subtract(1).TimerBits)),
            "Post-action unordered timer took the context arm or discarded its unordered value.");
        var overshot = constructed.Publish(0).Subtract(4);
        Require(overshot.TimerBits == 0xbf800000 && !overshot.SubtractsTimer && overshot.ValueBits == 0,
            "Post-action timer clamped overshoot or mutated its independent value.");
        var detection = FalloutProcessDetectionActionScalars.Constructed.Reset(1.2f);
        Require(detection.SubtractsTimer && detection.Subtract(2).TimerBits == BitConverter.SingleToUInt32Bits(1.2f - 2f),
            "Detection action conflated its Game setting with the post-action timer or clamped subtraction.");
        Require(!(detection with { TimerBits = 0x7fc00012 }).SubtractsTimer,
            "Ordered detection-action comparison reused the post-action unordered timer branch.");
        var fields = new FalloutPlayerPostActionFields(1, 0, 0x3e800000, 0x7fc00012);
        var after = fields.UpdateTimers(.5f);
        Require(after == fields with { FirstTimerBits = 0xbe800000, SecondTimerBits = 0xbf800000 } &&
            after.UpdateTimers(.5f).FirstTimerBits == 0xbf800000,
            "Player scene timers conflated the Main timer, clamped overshoot or retained an unordered expired value.");
    }
}
