using OpenNV.Runtime.Content;

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

        var forever = new FalloutIdleAnimationPlayback(0, 9, 1, 2, [(1, "startloop"), (5, "EndLoop")], 255);
        forever.Advance(25);
        Require(forever.SourceSeconds == 1 && forever.CompletedRepeats == 6 && forever.AdditionalLoops == 255 && !forever.Complete,
            "The source infinite sentinel was treated as a finite repeat count.");
        var continuous = new FalloutIdleAnimationPlayback(1, 5, 1, 0, [], 0);
        continuous.Advance(10);
        Require(continuous.SourceSeconds == 3 && !continuous.Complete, "A source cycling sequence stopped unexpectedly.");
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

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
