using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

internal static class IdleTransitionContracts
{
    internal static void Run()
    {
        var admission = new FalloutIdleTransition.Admission(0x47, false, false, false, false, false);
        var metadata = new FalloutIdleTransition.Metadata(1, 0, 0);
        var settings = new FalloutIdleTransition.Settings(0, 1);
        var transition = FalloutIdleTransition.CreateSnapshot(admission, metadata, null, settings);
        Require(transition.Slot.Index == 7 && !transition.Started && transition.Factors.IncomingWeight == 0,
            "Source execution slot or pre-update origin was lost.");
        var before = JsonSerializer.Deserialize<FalloutIdleTransition.Snapshot>(JsonSerializer.Serialize(transition.Capture()))!;
        transition = FalloutIdleTransition.Restore(before, admission, metadata, null, settings);
        Require(transition.Advance(.5) && transition.Started && transition.ElapsedSeconds == 0 && !transition.Complete,
            "First animation update incorrectly advanced the finite blend.");
        var latched = transition.Capture();
        transition = FalloutIdleTransition.Restore(latched, admission, metadata, null, settings);
        transition.Advance(.01);
        Require(transition.ElapsedSeconds == .01 && transition.Factors.IncomingWeight > 0 &&
            transition.Factors.SnapshotEase == 1 && transition.Factors.IncomingEase == 1,
            "Restored origin relatched, or sequence weights were confused with ordinary ease factors.");
        Require(transition.Advance(1) && transition.Complete && !transition.Advance(.01),
            "Completion update leaked overshoot into the source clip or replayed the transition.");
        Reject(() => FalloutIdleTransition.Restore(before with { ElapsedSeconds = .001 }, admission, metadata, null, settings));
        Reject(() => FalloutIdleTransition.CreateSnapshot(admission with { HasSynchronization = true }, metadata, null, settings));
        Reject(() => FalloutIdleTransition.CreateSnapshot(admission, new(0, 0, 0), null, settings));

        var clock = new FalloutIdleAnimationPlayback(0, 2, 1, 2, [(0, "start"), (.001f, "Enum: Auxiliary1"), (2, "end")], 0);
        var events = new List<FalloutIdleAnimationInterval>();
        clock.DispatchHeldBoundary(events.Add); clock.DispatchHeldBoundary(events.Add);
        Require(events.Count == 1 && events[0] == new FalloutIdleAnimationInterval(0, 0, true) && clock.SourceSeconds == 0,
            "Held phase dispatched a positive-time key or repeated its start boundary.");
        var restoredClock = new FalloutIdleAnimationPlayback(0, 2, 1, 2, [(0, "start"), (2, "end")], 0);
        restoredClock.Restore(clock.Capture()); restoredClock.DispatchHeldBoundary(events.Add);
        Require(events.Count == 1, "Cold held cursor replayed its consumed boundary.");

        FalloutNifTextKey[] keys = [new(0, "start"), new(.001f, "Enum: Auxiliary1"), new(.4f, "Attach"), new(1, "Blend:1"), new(2, "end")];
        var compiled = FalloutIdleGroupTextKeys.Read("SpecialIdle_ArbitrarySuffix", keys);
        Require(compiled.Kind(2, 0) == FalloutIdleGroupKeyKind.NonmatchingGroupMarker && compiled.Blend.Blend == 1 &&
            compiled.Kind(3, 0) == FalloutIdleGroupKeyKind.TransitionMetadata,
            "Stored-name group compilation lost its intermediate marker or load-time metadata.");
        Require(FalloutIdleGroupTextKeys.Read("Equip_ArbitrarySuffix", keys).Kind(2, 0) == FalloutIdleGroupKeyKind.TimedAction &&
            FalloutIdleGroupTextKeys.Read("SpecialIdle_Other", [new(0, "Attach"), new(1, "start")]).Kind(0, 0) == FalloutIdleGroupKeyKind.TimedAction,
            "Foreign-group or pre-start Attach was globally ignored.");

        var creature = new FalloutFormKey("Fixture.esm", 1); var sound = new FalloutFormKey("Fixture.esm", 2);
        var draws = 0;
        uint Draw(uint bound) { Require(bound == 100, "Sound gate changed the shared draw domain."); draws++; return 20; }
        Require(new FalloutCreatureAnimationSounds.Declaration(creature, 14, null, 35).Select(Draw).Kind ==
            FalloutCreatureAnimationSounds.SelectionKind.DeclaredNull && draws == 0, "Null sound consumed process randomness.");
        Require(new FalloutCreatureAnimationSounds.Declaration(creature, 14, sound, 20).Select(Draw).Kind ==
            FalloutCreatureAnimationSounds.SelectionKind.ChanceMiss && draws == 1, "Chance equality was accepted or not drawn.");
        Require(new FalloutCreatureAnimationSounds.Declaration(creature, 14, sound, 100).Select(Draw).Kind ==
            FalloutCreatureAnimationSounds.SelectionKind.Selected && draws == 2, "Certain nonnull sound bypassed its shared draw.");
        Require(new FalloutCreatureAnimationSounds.Declaration(creature, 14, sound, 0).Select(Draw).Kind ==
            FalloutCreatureAnimationSounds.SelectionKind.ChanceMiss && draws == 3, "Zero-chance nonnull sound bypassed its shared draw.");
        Console.WriteLine("OPENNV_IDLE_TRANSITION_CONTRACT_PASS origin=true heldCursor=true cold=true groupMarkers=true sharedSoundGate=true native=false");
    }

    private static void Require(bool value, string error) { if (!value) throw new InvalidOperationException(error); }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException) { return; }
        throw new InvalidOperationException("Invalid transition admission was accepted.");
    }
}
