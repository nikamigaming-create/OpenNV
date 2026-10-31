using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

internal static class IdleTransitionContracts
{
    internal static void Run()
    {
        OrderedGroups();
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
        AutomaticIdleContracts();
        CreatureSoundInheritanceContracts.Run();
        Console.WriteLine("OPENNV_IDLE_TRANSITION_CONTRACT_PASS origin=true heldCursor=true cold=true groupMarkers=true sharedSoundGate=true native=false");
    }

    private static void AutomaticIdleContracts()
    {
        var folder = Path.Combine(Path.GetTempPath(), "opennv-automatic-idle-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            static byte[] Join(params byte[][] values) => values.SelectMany(value => value).ToArray();
            static byte[] Field(string type, byte[] bytes) => Join(System.Text.Encoding.ASCII.GetBytes(type),
                BitConverter.GetBytes(checked((ushort)bytes.Length)), bytes);
            static byte[] Record(string type, uint form, params byte[][] fields)
            {
                var data = Join(fields); var header = new byte[24];
                System.Text.Encoding.ASCII.GetBytes(type).CopyTo(header, 0);
                BitConverter.GetBytes(data.Length).CopyTo(header, 4); BitConverter.GetBytes(form).CopyTo(header, 12);
                return Join(header, data);
            }
            static byte[] Idle(uint id, uint parent, uint previous, string model, bool chance, byte group = 7)
            {
                var condition = new byte[28]; condition[0] = 0x80;
                BitConverter.GetBytes(66f).CopyTo(condition, 4); BitConverter.GetBytes((ushort)77).CopyTo(condition, 8);
                return Record("IDLE", id, Field("MODL", System.Text.Encoding.ASCII.GetBytes(model + "\0")),
                    Field("ANAM", Join(BitConverter.GetBytes(parent), BitConverter.GetBytes(previous))),
                    Field("DATA", [group, 0, 0, 0, 20, 0, 0, 0]), chance ? Field("CTDA", condition) : []);
            }
            var header = new byte[12]; BitConverter.GetBytes(1.34f).CopyTo(header, 0);
            File.WriteAllBytes(Path.Combine(folder, "Automatic.esm"), Join(Record("TES4", 0, Field("HEDR", header)),
                Idle(13, 0, 0, "creatures/Any/IdleAnims/Manual.kf", false, 0x47),
                Idle(10, 0, 0, "creatures/Any/IdleAnims", false),
                Idle(11, 10, 0, "creatures/Any/IdleAnims/First.kf", true),
                Idle(12, 10, 11, "creatures/Any/IdleAnims/Second.kf", true)));
            using var records = FalloutPluginStack.Load(folder, ["Automatic.esm"]);
            var state = new FalloutAmbientIdleState();
            Require(!state.Advance(1, 3, true) && state.WaitSeconds == 0 &&
                !state.Advance(.25f, 3, true) && state.WaitSeconds < 0,
                "Automatic idle lost fresh one-second or strict-negative admission.");
            var held = state.WaitSeconds;
            Require(!state.Advance(100, 3, false) && state.WaitSeconds == held,
                "Blocked automatic admission advanced its countdown.");
            Require(state.Advance(4, 3, true) && state.WaitSeconds == 3,
                "Automatic attempt failed to reset its source wait.");
            state.Started(records.GetEffective(new("Automatic.esm", 11)), 20);
            state.Replay.Advance(5);
            var saved = JsonSerializer.Deserialize<FalloutAmbientIdleSnapshot>(JsonSerializer.Serialize(state.Capture()))!;
            var cold = new FalloutAmbientIdleState(); cold.Restore(saved, records);
            Require(cold.WaitSeconds == 3 && !cold.Replay.CanSelect(new("Automatic.esm", 11)),
                "Cold automatic countdown or actual replay identity was lost.");
            var tree = new FalloutAmbientIdleTree(records, "meshes/creatures/Any/skeleton.nif");
            var draws = 0;
            var selected = tree.Select(_ => { draws++; return 10; }, cold.Replay.CanSelect);
            Require(selected?.FormKey == new FalloutFormKey("Automatic.esm", 12) && draws == 2,
                "Automatic source order, conditions-before-replay or chance draw count changed.");
            var disabled = new FalloutAmbientIdleState();
            Require(!disabled.Advance(2, 0, true) && !disabled.Advance(100, 0, true),
                "Zero source wait admitted an automatic idle.");
            Console.WriteLine("OPENNV_AUTOMATIC_IDLE_CONTRACT_PASS sourceTree=true countdown=true coldReplay=true randomOrder=true native=false");
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    private static void OrderedGroups()
    {
        (float Time, string Value)[] sparse = [(0, "start"), (.5f, "StartLoop"), (1.25f, "StartLoop"), (2, "end")];
        var timing = FalloutIdleGroupTiming.Read("SpecialIdle_Sparse", sparse)!;
        Require(timing == new FalloutIdleGroupTiming(0, .5f, 0, 0),
            "Unmatched duplicate or missing group marker invented a replacement interval.");
        var prefix = FalloutIdleGroupTiming.Read("SpecialIdle_Prefix",
            [(0, "start"), (.5f, "StartLoop"), (1.5f, "EndLoop"), (1.75f, "EndLoop"), (2, "end")]);
        Require(prefix == new FalloutIdleGroupTiming(0, .5f, 1.5f, 1.75f),
            "Ordered End-prefix selection overwrote the consumed source marker.");
        Require(FalloutIdleGroupTiming.Read("SpecialIdle_Whole", [(0, "start"), (2, "end")]) ==
            new FalloutIdleGroupTiming(0, 0, 2, 2), "Unwritten loop markers lost source group normalization.");
        FalloutIdleAnimationPlayback Create(byte repeats) => new(0, 2, 1, 2, sparse, repeats, "SpecialIdle_Sparse");
        var finite = Create(0);
        finite.DispatchHeldBoundary(_ => { });
        Require(!finite.Complete, "Held snapshot ran the Animating group consumer.");
        finite.Advance(.125);
        Require(finite.Complete && finite.SourceSeconds == .125f,
            "A zero compiled End was replaced with the KF endpoint.");
        finite.Advance(.125, processGroup: false);
        Require(finite.Complete && finite.SourceSeconds == .25f,
            "Group retirement incorrectly froze its still-active outgoing source clock.");
        var repeating = Create(1);
        var events = new List<FalloutIdleAnimationInterval>();
        repeating.Advance(.125, events.Add);
        Require(!repeating.Complete && repeating.SourceSeconds == .625f && repeating.CompletedRepeats == 1 &&
            repeating.AdditionalLoops == 0 && events.Single() == new FalloutIdleAnimationInterval(.5f, .625f, true),
            "Signed repeat interval or its adjusted event endpoints were discarded.");
        var saved = JsonSerializer.Deserialize<FalloutIdleAnimationPlaybackSnapshot>(JsonSerializer.Serialize(repeating.Capture()))!;
        var restored = Create(1); restored.Restore(saved);
        repeating.Advance(.125); restored.Advance(.125);
        Require(repeating.Capture() == restored.Capture() && restored.Complete,
            "Cold group continuation changed a consumed repeat or marker cursor.");
        Reject(() => Create(1).Restore(saved with { GroupMarkerState = null }));
        Reject(() => Create(1).Restore(saved with { SourceSeconds = .25 }));
        FalloutIdleAnimationPlayback Clamped() => new(0, 2, 1, 2,
            [(0, "start"), (2, "end")], 0, "SpecialIdle_Clamped");
        var clamped = Clamped();
        clamped.Advance(1.875);
        events.Clear(); clamped.Advance(.25, events.Add);
        Require(clamped.Complete && clamped.Capture().SourceSeconds == 2.125 && clamped.SourceSeconds == 2 &&
            events.Single() == new FalloutIdleAnimationInterval(1.875f, 2, false),
            "Raw group overshoot leaked into pose/event samples or was lost from continuation.");
        var coldClamped = Clamped(); coldClamped.Restore(clamped.Capture());
        events.Clear(); coldClamped.Advance(.125, events.Add, processGroup: false);
        clamped.Advance(.125, processGroup: false);
        Require(coldClamped.Capture() == clamped.Capture() && coldClamped.Capture().SourceSeconds == 2.25 &&
            events.Single() == new FalloutIdleAnimationInterval(2, 2, false),
            "Outgoing/cold clamped sampling reused raw time or replayed endpoint events.");
        var sought = new FalloutIdleAnimationPlayback(0, 2, 1, 2,
            [(0, "start"), (1.875f, "StartLoop"), (1.9f, "StartLoop"), (2, "end")], 1, "SpecialIdle_Seek");
        events.Clear(); sought.Advance(.25, events.Add);
        Require(sought.Capture().SourceSeconds == 2.125 && sought.SourceSeconds == 2 && !sought.Complete &&
            events.Single() == new FalloutIdleAnimationInterval(1.875f, 2, true),
            "A signed group seek failed to resolve its destination with the source clamp policy.");
        var fading = Create(1);
        fading.Advance(.125, processGroup: false);
        Require(!fading.Complete && fading.AdditionalLoops == 1 && fading.SourceSeconds == .125f,
            "An ineligible easing sequence consumed group transitions.");
    }

    private static void Require(bool value, string error) { if (!value) throw new InvalidOperationException(error); }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException) { return; }
        throw new InvalidOperationException("Invalid transition admission was accepted.");
    }
}
