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
        Require(overrun.Advance(0.25f) && overrun.Remaining == -1.25f && overrun.Elapsed == 0.25f,
            "An overdue next-frame call lost elapsed time or subtracted countdown debt twice.");
        overrun.CompleteInvocation();
        Require(overrun.Capture() == new FalloutQuestScriptClockSnapshot(-0.25f, 0, 2),
            "An overdue invocation changed its recurrence order.");
        Require(new[] { 0L, 1, 2, 7, 14, 255, 256 }.Select(index => FalloutQuestScriptInitialization.Phase(5, index))
            .SequenceEqual([5f, 2.5f, 1.25f, 4.375f, 2.1875f, 4.98046875f, 5f]),
            "Source initialization fractions or counter-byte wrap differ.");
        FastIntervals();
        ModalAndCold();
        var failedAdvance = new FalloutQuestScriptClock(1, null, 0);
        failedAdvance.Restore(new(-1, float.MaxValue, 1));
        var beforeOverflow = failedAdvance.Capture();
        Reject(() => failedAdvance.Advance(float.MaxValue));
        Require(failedAdvance.Capture() == beforeOverflow, "An overdue elapsed overflow partially mutated the clock.");
        Console.WriteLine("OPENNV_QUEST_SCRIPT_CLOCK_PASS recurrence=true float32=true overshoot=true coldRestore=true phaseFractions=true " +
            "fastElapsed=true oncePerFrame=true modalSuppression=true overdueCold=true overflowAtomic=true");
    }

    private static void FastIntervals()
    {
        // Binary fractions make the independent total exact. Script dispatch
        // remains once per caller frame, even when many intervals become due.
        foreach (var interval in new[] { 1f / 128, 1f / 32, 1f / 8 })
        {
            var clock = new FalloutQuestScriptClock(1, interval, 0);
            Require(clock.Advance(0), "The initial dispatch was not due.");
            clock.CompleteInvocation();
            double frameTotal = 0, observed = 0;
            var calls = 0;
            for (var frame = 0; frame < 192; frame++)
            {
                var seconds = new[] { 1f / 64, 1f / 32, 1f / 16, 1f / 4 }[frame % 4];
                frameTotal += seconds;
                if (!clock.Advance(seconds)) continue;
                observed += clock.Elapsed;
                ++calls;
                clock.CompleteInvocation();
                Require(observed + clock.Elapsed == frameTotal,
                    "The source elapsed denominator lost a frame at a fast/overrun boundary.");
            }
            Require(calls <= 192 && clock.Invocations == calls + 1 &&
                observed + clock.Elapsed == frameTotal,
                "The recurrence fabricated catch-up calls or lost elapsed time between invocations.");
        }
    }

    private static void ModalAndCold()
    {
        const string source = "begin GameMode\nset ticks to ticks + 1\nif timer > 0\n" +
            "set timer to timer - GetSecondsPassed\nelseif completed == 0\nset completed to 1\nReached\nendif\nend\n" +
            "begin MenuMode\nset menus to menus + 1\nend";
        var game = FalloutGameModeProgram.Read(source);
        var menu = FalloutGameModeProgram.Read(source, "MenuMode");
        var warm = new FalloutQuestScriptClock(1, 1f / 128, 0);
        var values = new Dictionary<string, double> { ["ticks"] = 0, ["timer"] = 0.125, ["completed"] = 0, ["menus"] = 0 };
        var reached = 0;
        void Tick(FalloutQuestScriptClock clock, Dictionary<string, double> locals, float seconds, bool modal, Action result)
        {
            if (!clock.Advance(seconds)) return;
            (modal ? menu : game).Execute(name => locals[name], (name, value) => locals[name] = value,
                (name, _) => { Require(name == "Reached", "An unrelated result was dispatched."); result(); },
                name => name == "GetSecondsPassed" ? new([], _ => clock.Elapsed) : null);
            clock.CompleteInvocation();
        }
        Tick(warm, values, 0, false, () => ++reached);
        Tick(warm, values, 0.03125f, false, () => ++reached);
        Tick(warm, values, 0.03125f, false, () => ++reached);
        var timerBeforeModal = values["timer"];
        for (var frame = 0; frame < 8; frame++) Tick(warm, values, 0.0625f, true, () => ++reached);
        Require(values["timer"] == timerBeforeModal && values["ticks"] == 3 && values["menus"] == 8 && reached == 0,
            "Modal recurrence executed GameMode or charged paused time to its timer.");
        var saved = JsonSerializer.Deserialize<FalloutQuestScriptClockSnapshot>(JsonSerializer.Serialize(warm.Capture()))!;
        Require(saved.Remaining < 0 && saved.Elapsed == 0, "The fixture did not reach carried overdue debt.");
        var cold = new FalloutQuestScriptClock(1, 1f / 128, 0);
        cold.Restore(saved);
        var restoredValues = new Dictionary<string, double>(values);
        var coldReached = 0;
        for (var frame = 0; frame < 7; frame++)
        {
            Tick(warm, values, 0.03125f, false, () => ++reached);
            Tick(cold, restoredValues, 0.03125f, false, () => ++coldReached);
            Require(warm.Capture().HasSameBits(cold.Capture()) && values.OrderBy(pair => pair.Key).SequenceEqual(restoredValues.OrderBy(pair => pair.Key)),
                "A cold overdue timer changed the resumed frame, values or recurrence bits.");
            if (frame == 1) Require(values["timer"] == 0 && reached == 0,
                "The timer reached its result before the authored following invocation.");
            if (frame == 2) Require(reached == 1 && coldReached == 1,
                "The source result did not follow the completed timer exactly once.");
        }
        Require(reached == 1 && coldReached == 1, "Later overdue frames duplicated a source result.");
    }

    private static void Reject(Action action)
    {
        try { action(); }
        catch (InvalidDataException) { return; }
        throw new Exception("Invalid quest clock state was accepted.");
    }
    private static void Require(bool condition, string error) { if (!condition) throw new Exception(error); }
}
