using System.Text.Json;
using OpenNV.Runtime.Content;

internal static class QuestScriptClockProbe
{
    internal static void Run()
    {
        var clock = new FalloutQuestScriptClock(5, 0, 0);
        Require(clock.Interval == 5 && clock.Advance(0.25f) && clock.Elapsed == 0,
            "An already-due invocation accrued another delta or treated authored zero as every-frame.");
        clock.CompleteInvocation();
        Require(!clock.Advance(4.75f) && clock.Remaining == 0.25f && clock.Elapsed == 4.75f,
            "Configured recurrence fired early.");
        Require(clock.Advance(0.75f) && clock.Remaining == -0.5f && clock.Elapsed == 5.5f,
            "An overrun lost its countdown or elapsed time.");
        clock.CompleteInvocation();
        Require(clock.Remaining == 4.5f && clock.Elapsed == 0 && clock.Invocations == 2,
            "The recurrence drifted by replacing the countdown.");

        clock.Advance(0.123f);
        var saved = JsonSerializer.Deserialize<FalloutQuestScriptClockSnapshot>(JsonSerializer.Serialize(clock.Capture()))!;
        var restored = new FalloutQuestScriptClock(5, 0, 0);
        restored.Restore(saved);
        for (var frame = 0; frame < 1000; frame++)
        {
            var due = clock.Advance(0.017f);
            Require(restored.Advance(0.017f) == due, "Cold countdown fired on another frame.");
            if (due) { clock.CompleteInvocation(); restored.CompleteInvocation(); }
            Require(BitConverter.SingleToInt32Bits(clock.Remaining) == BitConverter.SingleToInt32Bits(restored.Remaining) &&
                BitConverter.SingleToInt32Bits(clock.Elapsed) == BitConverter.SingleToInt32Bits(restored.Elapsed) &&
                clock.Invocations == restored.Invocations, "Cold Float32 clock bits diverged.");
        }
        var beforeInvalid = restored.Capture();
        Reject(() => restored.Restore(saved with { Elapsed = float.NaN }));
        Reject(() => restored.Restore(saved with { Remaining = 6 }));
        Require(restored.Capture() == beforeInvalid, "An invalid restoration partially mutated the clock.");

        Require(new FalloutQuestScriptClock(5, 0.125f, 0).Interval == 0.125f &&
            new FalloutQuestScriptClock(5, null, 0).Interval == 5 &&
            new FalloutQuestScriptClock(5, -1, 0).Interval == 5 &&
            new FalloutQuestScriptClock(0, 2, 0).Interval == 0,
            "Authored, default or globally disabled processing selection differs.");
        var overrun = new FalloutQuestScriptClock(1, null, 1);
        Require(overrun.Advance(3.25f), "A long frame did not become due.");
        overrun.CompleteInvocation();
        Require(overrun.Capture() == new FalloutQuestScriptClockSnapshot(-1.25f, 0, 1),
            "A long frame executed a fabricated catch-up loop.");
        Require(overrun.Advance(0.25f) && overrun.Remaining == -1.25f && overrun.Elapsed == 0,
            "An overdue next-frame call consumed delta twice.");
        Console.WriteLine("OPENNV_QUEST_SCRIPT_CLOCK_PASS recurrence=true float32=true overshoot=true coldRestore=true initialPhase=unbound");
    }

    private static void Reject(Action action)
    {
        try { action(); }
        catch (InvalidDataException) { return; }
        throw new Exception("Invalid quest clock state was accepted.");
    }
    private static void Require(bool condition, string error) { if (!condition) throw new Exception(error); }
}
