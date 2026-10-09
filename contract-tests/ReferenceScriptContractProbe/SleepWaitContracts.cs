using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

internal static class SleepWaitContracts
{
    internal static void Run()
    {
        MenuCadenceAndCold(); SignedPlayerCadence(); DistinctMenuFlag(); HourFaultPrefix(); CompletionAndCancelFaults();
        RestrictionOrder(); CalendarBoundary();
        Console.WriteLine("OPENNV_SLEEP_WAIT_CONTRACT_PASS authored=true menuCadence=true signedPlayerCadence=true " +
            "coldNoOfflineTime=true prefixIOException=true completionAndCancel=true unownedRefused=true " +
            "calendarBoundary=true native=unexecuted originalEffects=unexecuted");
    }

    private sealed class Fixture
    {
        internal readonly FalloutGlobalState Globals;
        internal readonly FalloutGameTime Clock;
        internal readonly FalloutSleepWait Owner;
        internal bool Sleeping;
        internal float WorldSeconds;
        internal int Preludes, Effects, Completions, Closes;
        internal bool FailEffects, FailCompletion, FailFlag;
        internal Fixture(bool carry = true, float hour = 12, FalloutSleepWaitSnapshot? restore = null,
            FalloutGlobalStateSnapshot? globals = null, FalloutGameTimeSnapshot? clock = null)
        {
            var keys = Enumerable.Range(0, 6).Select(index => new FalloutFormKey("Authored.esm", (uint)(0x35 + index))).ToArray();
            float[] values = [2280, 3, 4, hour, 600.25f, 30];
            Globals = new(keys.Select((key, index) => new FalloutGlobal(key, "AuthoredTime" + index,
                (byte)'f', values[index], "authored-global-" + index)));
            Clock = new(Globals, new(keys[0], keys[1], keys[2], keys[3], keys[4], keys[5]),
                new([31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31], new('a', 64), carry, !carry));
            Clock.InitializeNewGame();
            if (globals is not null) Globals.Restore(globals);
            if (clock is not null) Clock.Restore(clock);
            Sleeping = restore?.Sleeping ?? false;
            var source = new FalloutSleepWaitSource(carry ?
                "518c87f58a6c4d9826e9ef8fbb7f4213882fa70822675610d45aea2464502a57" :
                "c3f97c2255fa041a851c17cf372d69aaadd8694e2dc4230ba556001bbfbd2f3e",
                new('1', 64), new('2', 64), FalloutSleepWaitSource.CurrentContractSha256, carry, carry, 24);
            Owner = new(source, Clock, new(
                (_, _) => new(FalloutRestFactState.Satisfied, "authored-restriction-producer"),
                _ => { },
                (flag, write) => { write(); Sleeping = flag; if (FailFlag) throw new IOException("authored-flag-prefix"); },
                _ => Preludes++, seconds => WorldSeconds += seconds,
                _ => { Effects++; if (FailEffects) throw new IOException("authored-hour-prefix"); },
                _ => { Completions++; if (FailCompletion) throw new IOException("authored-completion-prefix"); },
                (_, _) => Closes++, () => Sleeping)
                { AfterMenuPlayerHours = _ => { }, BeforeCancelPlayerHours = _ => { } }, restore);
        }
        internal Fixture Cold() => new(Owner.Source.CarryAtDayBoundary, restore: RoundTrip(Owner.Capture()),
            globals: RoundTrip(Globals.Capture()), clock: RoundTrip(Clock.Capture()));
        internal void Menu(FalloutRestKind kind, int hours)
        { Owner.Open(new(kind, FalloutRestOrigin.SourceCommand)); Owner.Publish(Owner.RequestOrdinal); Owner.Select(hours); Owner.Begin(); }
    }
    private static T RoundTrip<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;
    private static void MenuCadenceAndCold()
    {
        var warm = new Fixture(); warm.Menu(FalloutRestKind.Wait, 2);
        Require(!warm.Owner.AdvanceCountdown(.625f) && warm.Owner.CommittedHours == 0, "Menu advanced before its actual one-second threshold.");
        var cold = warm.Cold();
        Require(cold.Clock.Stamp().HasSameBits(warm.Clock.Stamp()) && cold.Preludes == 0 && cold.Effects == 0,
            "Cold creation consumed an offline hour or replayed an effect.");
        cold.Owner.Publish(cold.Owner.RequestOrdinal);
        foreach (var fixture in new[] { warm, cold })
        {
            Require(fixture.Owner.AdvanceCountdown(.375f) && fixture.Owner.CommittedHours == 1, "Fractional countdown was lost.");
            Require(fixture.Owner.AdvanceCountdown(7.25f) && fixture.Owner.CommittedHours == 2 && fixture.Owner.Countdown == 0,
                "Large native delta invented catch-up hours.");
            Require(fixture.Owner.Phase == FalloutRestPhase.Completed && fixture.Owner.MenuPending && fixture.Closes == 0 && fixture.Completions == 0,
                "Wait completion performed sleep effects or closed before its actual next UI check.");
            fixture.Owner.RetireCompletedMenu();
            Require(fixture.Closes == 1 && !fixture.Owner.NeedsMenuPublication, "Actual completed menu did not retire exactly once.");
        }
        Require(warm.Clock.Stamp().HasSameBits(cold.Clock.Stamp()), "Cold menu changed calendar bits.");
    }
    private static void SignedPlayerCadence()
    {
        foreach (var hours in new[] { 0, -3, 2 })
        {
            var fixture = new Fixture(); fixture.Owner.SetScriptHours(hours);
            Require(fixture.Sleeping && fixture.Owner.RemainingHours == hours && !fixture.Owner.Published, "Signed source write did not commit its independent flag.");
            Require(fixture.Owner.AdvancePlayerUpdate(1) && fixture.Owner.CommittedHours == 1,
                "Script hours waited for a menu timer or refused an original nonpositive count.");
            var clock = fixture.Clock.Stamp(); Reject(() => fixture.Owner.AdvancePlayerUpdate(1));
            Require(fixture.Clock.Stamp().HasSameBits(clock), "Duplicate actual player frame consumed another hour.");
            if (hours == 2)
            {
                var cold = fixture.Cold();
                Require(cold.Owner.AdvancePlayerUpdate(1) && !cold.Sleeping && cold.Owner.CommittedHours == 2 && cold.Completions == 1,
                    "Cold player update lost its new process epoch or replayed the previous hour.");
            }
            else Require(!fixture.Sleeping && fixture.Owner.RemainingHours == unchecked(hours - 1) && fixture.Completions == 1,
                "Source nonpositive counter failed to retain its real post-hour signed prefix.");
        }
        var wrapped = new Fixture(); wrapped.Owner.SetScriptHours(int.MinValue); wrapped.Owner.AdvancePlayerUpdate(1);
        Require(wrapped.Owner.RemainingHours == int.MaxValue && wrapped.Sleeping, "Source int32 counter did not preserve its unchecked arithmetic.");
    }
    private static void DistinctMenuFlag()
    {
        var fixture = new Fixture(); fixture.Menu(FalloutRestKind.Wait, 1);
        Require(!fixture.Sleeping, "Ordinary wait invented independent IsPCSleeping.");
        fixture.Owner.SetScriptHours(1); fixture.Owner.AdvanceCountdown(1);
        Require(fixture.Completions == 1 && !fixture.Sleeping, "A real script flag was substituted by the menu's displayed kind.");
        fixture.Owner.SetScriptHours(2);
        Require(fixture.Owner.Phase == FalloutRestPhase.Running && fixture.Owner.MenuPending && fixture.Sleeping,
            "Counter replacement before native close lost the live menu counting owner.");
        fixture.Owner.Cancel();
        Require(fixture.Completions == 1 && fixture.Closes == 1 && fixture.Owner.RemainingHours == 0 && !fixture.Sleeping,
            "Cancellation fabricated sleep completion or retained the source flag.");
    }
    private static void HourFaultPrefix()
    {
        var fixture = new Fixture { FailEffects = true }; fixture.Owner.SetScriptHours(2);
        Reject(() => fixture.Owner.AdvancePlayerUpdate(1));
        var snapshot = RoundTrip(fixture.Owner.Capture());
        Require(snapshot.Failure?.Step == FalloutRestStep.HourEffects && snapshot.LastHour is
            { PreludeCommitted: true, WorldSecondsCommitted: true, CalendarCommitted: true, EffectsCommitted: false, Finished: false } &&
            snapshot.RemainingHours == 2 && snapshot.CommittedHours == 0 && fixture.WorldSeconds == 120 && fixture.Effects == 1 && fixture.Clock.Hour == 13,
            "IOException discarded or completed the actual committed hour prefix.");
        var cold = fixture.Cold(); Reject(() => cold.Owner.AdvancePlayerUpdate(1));
        Require(cold.Effects == 0 && cold.Preludes == 0 && cold.Clock.Stamp().HasSameBits(fixture.Clock.Stamp()), "Cold replayed a failed effect prefix.");
        cold.Owner.ReportNativeFailure(FalloutRestStep.Retirement, new IOException("authored-cleanup"));
        Require(cold.Owner.Capture().NativeFailures.Count == 1 && cold.Owner.Capture().Failure == snapshot.Failure,
            "Secondary native cleanup erased the actual first attempted operation.");
    }
    private static void CompletionAndCancelFaults()
    {
        var completion = new Fixture { FailCompletion = true }; completion.Owner.SetScriptHours(1);
        Reject(() => completion.Owner.AdvancePlayerUpdate(1));
        var state = completion.Owner.Capture();
        Require(state.Failure?.Step == FalloutRestStep.Completion && state.CommittedHours == 1 && state.RemainingHours == 0 &&
            state.Sleeping && completion.Completions == 1, "Failed completion lost the actual finished-hour prefix.");
        var cold = completion.Cold(); Reject(() => cold.Owner.AdvancePlayerUpdate(1));
        Require(cold.Completions == 0, "Cold replayed completed side effects after IOException.");
        var cancel = new Fixture(); cancel.Menu(FalloutRestKind.Sleep, 3); cancel.FailFlag = true;
        Reject(cancel.Owner.Cancel); var prefix = cancel.Owner.Capture();
        Require(prefix.Failure?.Step == FalloutRestStep.Cancellation && !prefix.Sleeping && prefix.RemainingHours == 0 && cancel.Completions == 0,
            "Cancellation failure discarded its actually committed flag/counter or manufactured completion.");
        Reject(cancel.Cold().Owner.Cancel);
    }
    private static void RestrictionOrder()
    {
        var observed = new List<FalloutRestFact>();
        var request = new FalloutRestRequest(FalloutRestKind.Wait, FalloutRestOrigin.PlayerControl);
        var result = FalloutSleepWaitAdmission.Inspect(request, (_, fact) =>
        {
            observed.Add(fact);
            return fact == FalloutRestFact.NoHostileActorsInSourceRadius ?
                new(FalloutRestFactState.Unowned, "authored-source-radius", "producer absent") :
                new(FalloutRestFactState.Satisfied, "authored-source-predicate");
        });
        Reject(result.Require);
        Require(!result.Admitted && result.Failure == FalloutRestFact.NoHostileActorsInSourceRadius &&
            observed.SequenceEqual(new[] { FalloutRestFact.NativeMenuFactoryReady, FalloutRestFact.PlayerAlive,
                FalloutRestFact.PlayerNotTrespassing, FalloutRestFact.NoAlarm, FalloutRestFact.PlayerNotUnderwater,
                FalloutRestFact.NoHostileActorsInSourceRadius }), "Absent hostile-radius owner silently admitted or evaluated later effects.");
    }
    private static void CalendarBoundary()
    {
        var carried = new Fixture(hour: 23); carried.Owner.SetScriptHours(1); carried.Owner.AdvancePlayerUpdate(1);
        var retained = new Fixture(carry: false, hour: 23); retained.Owner.SetScriptHours(1); retained.Owner.AdvancePlayerUpdate(1);
        Require(carried.Clock.Hour == 0 && carried.Clock.Stamp().Day == 5 && retained.Clock.Hour == 24 && retained.Clock.Stamp().Day == 4,
            "Selected exact-midnight calendar rules were flattened.");
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is InvalidOperationException or InvalidDataException or NotSupportedException or IOException) { return; }
        throw new InvalidDataException("Sleep/wait contract accepted a required refusal.");
    }
}
