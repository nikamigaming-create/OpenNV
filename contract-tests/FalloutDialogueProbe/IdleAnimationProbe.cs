using OpenNV.Runtime.Content;
using System.Text.Json;

internal static class IdleAnimationProbe
{
    internal static void Run()
    {
        var source = FalloutIdleAnimationData.Read([7, 2, 6, 101, 120, 0, 0, 0]);
        Require(source.ReplayDelaySeconds == 120 && source.LoopMinimum == 2 && source.LoopMaximum == 6,
            "IDLE loop bounds or UInt16 replay delay were decoded from the wrong bytes.");
        Require(FalloutIdleAnimationData.Read([7, 1, 1, 0, 4, 1]).ReplayDelaySeconds == 260,
            "The legacy IDLE delay lost its upper byte.");
        Require(source.SelectAdditionalLoops(bound => { Require(bound == 4, "Loop range is not upper-exclusive."); return 0; }) == 1 &&
            source.SelectAdditionalLoops(_ => 3) == 4, "IDLE selection counted initial playback as an extra repeat.");
        Require(Enumerable.Range(0, 256).Where(value => source.AdmitsAdditionalLoops((byte)value)).SequenceEqual([1, 2, 3, 4]),
            "Cold IDLE admission disagrees with the source upper-exclusive selection range.");
        uint NoRandom(uint _) => throw new InvalidOperationException("Constant or disabled IDLE loops consumed random state.");
        Require((source with { LoopMinimum = 0 }).SelectAdditionalLoops(NoRandom) == 0 &&
            (source with { LoopMaximum = 0 }).SelectAdditionalLoops(NoRandom) == 0 &&
            (source with { LoopMinimum = 8, LoopMaximum = 3 }).SelectAdditionalLoops(NoRandom) == 2 &&
            (source with { LoopMinimum = 4, LoopMaximum = 4 }).SelectAdditionalLoops(NoRandom) == 3 &&
            (source with { LoopMinimum = 255 }).SelectAdditionalLoops(NoRandom) == 255,
            "A zero, fixed, reversed or infinite IDLE loop bound lost its source behavior.");

        var visits = new List<FalloutIdleAnimationInterval>();
        var clock = new FalloutIdleAnimationPlayback(2, 9, 2, 2, [(3, "StartLoop"), (7, "EndLoop")], 2);
        Require(clock.Advance(3, visits.Add) == 0 && clock.SourceSeconds == 4 && clock.CompletedRepeats == 1,
            "An inner repeat replayed the intro or dropped the time after EndLoop.");
        Require(visits.Count == 2 && visits[0] == new FalloutIdleAnimationInterval(2, 7, true) &&
            visits[1] == new FalloutIdleAnimationInterval(3, 4, true), "Text-key traversal lost the repeat boundary.");
        clock.Advance(2.5, visits.Add);
        Require(clock.SourceSeconds == 5 && clock.CompletedRepeats == 2 && clock.AdditionalLoops == 0,
            "Finite extra repeats did not decrement at their authored boundary.");
        Require(clock.Advance(3) == 1 && clock.Complete && clock.SourceSeconds == 9,
            "The finite outro did not release exactly its unused simulation time.");
        var finished = new FalloutIdleAnimationPlayback(2, 9, 2, 2, [(3, "StartLoop"), (7, "EndLoop")], 2);
        finished.Restore(clock.Capture());
        Require(finished.Capture() == clock.Capture() && finished.ElapsedSeconds == 7.5 && finished.Advance(4) == 4,
            "Cold finite completion lost its source outro or unconsumed time.");

        var forever = new FalloutIdleAnimationPlayback(0, 9, 1, 2, [(1, "startloop"), (5, "EndLoop")], 255);
        forever.Advance(25);
        Require(forever.SourceSeconds == 1 && forever.CompletedRepeats == 6 && forever.AdditionalLoops == 255 && !forever.Complete,
            "The source infinite sentinel was treated as a finite repeat count.");
        ColdIntervals(forever, () => new(0, 9, 1, 2, [(1, "startloop"), (5, "EndLoop")], 255), 7.75);
        Require(forever.Endless && forever.ElapsedSeconds == 32.75, "Forever playback replayed its intro or lost elapsed time.");
        var continuous = new FalloutIdleAnimationPlayback(1, 5, 1, 0, [], 0);
        continuous.Advance(10);
        Require(continuous.SourceSeconds == 3 && !continuous.Complete, "A source cycling sequence stopped unexpectedly.");
        ColdIntervals(continuous, () => new(1, 5, 1, 0, [], 0), 6);
        var finite = new FalloutIdleAnimationPlayback(2, 9, 2, 2, [(3, "StartLoop"), (7, "EndLoop")], 2);
        finite.Advance(3);
        ColdIntervals(finite, () => new(2, 9, 2, 2, [(3, "StartLoop"), (7, "EndLoop")], 2), 2.5);
        var beforeInvalid = finite.Capture();
        foreach (var invalid in new[]
        {
            beforeInvalid with { SourceSeconds = double.NaN }, beforeInvalid with { SourceSeconds = 1 },
            beforeInvalid with { SourceSeconds = 10 }, beforeInvalid with { CompletedRepeats = -1 },
            beforeInvalid with { CompletedRepeats = 3 }, beforeInvalid with { RemainingAdditionalLoops = 1 },
            beforeInvalid with { SelectedAdditionalLoops = 3 }, beforeInvalid with { IncludeStart = true },
            beforeInvalid with { Complete = true }
        })
        {
            Reject(() => finite.Restore(invalid));
            Require(finite.Capture() == beforeInvalid, "Rejected cold phase changed a live IDLE clock.");
        }
        var foreverSaved = forever.Capture();
        Reject(() => forever.Restore(foreverSaved with { RemainingAdditionalLoops = 254 }));
        Reject(() => forever.Restore(foreverSaved with { SourceSeconds = 6 }));
        Reject(() => continuous.Restore(continuous.Capture() with { Complete = true }));
        Reject(() => continuous.Restore(continuous.Capture() with { SourceSeconds = 5 }));
        var rejected = false;
        try { _ = new FalloutIdleAnimationPlayback(0, 9, 1, 2, [(1, "StartLoop")], 1); }
        catch (InvalidDataException) { rejected = true; }
        Require(rejected, "An incomplete source repeat interval was invented.");

        var idle = new FalloutFormKey("Synthetic.esm", 0x210);
        var other = new FalloutFormKey("Synthetic.esm", 0x211);
        var replay = new FalloutIdleReplayState();
        replay.Started(idle, 12);
        replay.Advance(5);
        Require(!replay.CanSelect(idle) && replay.CanSelect(other) && replay.Remaining[idle] == 7,
            "Replay cooldown was not actor- and IDLE-specific from successful start.");
        // Animation cancellation does not erase the actor's admission state.
        replay.Advance(6.75f);
        Require(!replay.CanSelect(idle), "An interrupted idle became eligible before its cooldown ended.");
        replay.Advance(0.25f);
        Require(replay.CanSelect(idle), "Replay eligibility did not resume when its source delay elapsed.");
    }

    private static void ColdIntervals(FalloutIdleAnimationPlayback live, Func<FalloutIdleAnimationPlayback> create, double delta)
    {
        var saved = JsonSerializer.Deserialize<FalloutIdleAnimationPlaybackSnapshot>(JsonSerializer.Serialize(live.Capture()))!;
        var cold = create();
        cold.Restore(saved);
        Require(cold.Capture() == saved && cold.ElapsedSeconds == live.ElapsedSeconds,
            "Cold restoration changed the selected repetitions, phase or boundary admission.");
        var liveIntervals = new List<FalloutIdleAnimationInterval>();
        var coldIntervals = new List<FalloutIdleAnimationInterval>();
        Require(live.Advance(delta, liveIntervals.Add) == cold.Advance(delta, coldIntervals.Add) &&
            live.Capture() == cold.Capture() && liveIntervals.SequenceEqual(coldIntervals),
            "Cold advancement replayed past keys or changed future repeat-boundary intervals.");
    }

    private static void Reject(Action action)
    {
        try { action(); } catch (InvalidDataException) { return; }
        throw new InvalidOperationException("Invalid source playback phase was accepted.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
