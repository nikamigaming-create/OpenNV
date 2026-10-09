using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static class FiniteSoundCompletionWaitContracts
{
    internal static void Run(FalloutPluginStack records)
    {
        var reference = new FalloutFormKey("Pose.esm", 0x901);
        var sound = new FalloutFormKey("Pose.esm", 0x68);
        using var world = new FalloutReferenceWorld(records);
        var state = world.Get(reference);
        var source = FalloutSoundRecordReader.Read(records, sound);
        var selected = FalloutAnimationSound.Select(source, [source.LogicalPath], state.SoundRandom, true, true);
        var ledger = state.AnimationSoundEvents;
        var generation = ledger.Begin(records, selected, "Sound: FixturePartial", true, [source.LogicalPath]);
        ledger.BindMedia(generation, new string('a', 64));
        var entry = ledger.Events.Single();
        var proof = new FalloutFiniteSoundVoice(81, reference, generation, sound, entry.SoundSha256, entry.Path!, entry.MediaSha256!);
        var history = JsonSerializer.Serialize(ledger.Events); var random = state.SoundRandom.State;
        FalloutFiniteSoundCompletionWait Started()
        {
            var value = new FalloutFiniteSoundCompletionWait(proof, 82, 83, 84);
            Require(value.Observe(proof, 82, 83, 84, true, 10, 100) == proof, "Actual native playing was refused.");
            return value;
        }
        var unknown = new FalloutFiniteSoundCompletionWait(proof, 82, 83, 84);
        Require(unknown.Observe(proof, 82, 83, 84, false, 10, 100) is null,
            "A never-observed native playback became completion wait.");
        var wait = Started(); var playing = true; ulong phase = 10, milliseconds = 100;
        using var registration = records.SoundVoices.Register(sound, reference, "synthetic-native-completion-edge", () => playing,
            () => throw new InvalidDataException("Finite waiting fabricated source Stop."),
            sourceReference: reference, finiteWait: () => wait.Observe(proof, 82, 83, 84, playing, phase, milliseconds),
            finiteWaitState: () => wait.State);
        var manual = new RuntimeManualSaveRequests(); var session = Guid.NewGuid(); const string compatibility = "fixture";
        ulong queuePhase = 20;
        var continuePath = Path.Combine(Path.GetTempPath(), "opennv-finite-source-" + Guid.NewGuid().ToString("N"), "continue.json");
        world.ScriptManualSaves.BindSelection(new(compatibility, continuePath,
            id => Path.Combine(continuePath + RuntimeSaveSlotCatalog.SlotDirectorySuffix, id.ToString("N") + ".json")), () => queuePhase);
        manual.Bind(world.ScriptManualSaves, session, compatibility);
        manual.Request(session, compatibility, queuePhase, site: new(session, 1, records.RuntimeFormKey(0x14), state.Cell));
        playing = false; phase = 11; milliseconds = 200;
        Require(world.PendingAnimationSoundFiniteVoiceWait()?.Single() == proof && wait.Phase == "awaiting-native-finished",
            "Native inactivity dropped its still-registered exact finite generation.");
        queuePhase = 21;
        manual.Drain(session, compatibility, queuePhase,
            () => new(RuntimeManualSaveAdmissionKind.FiniteSourceAudio, Voices: world.PendingAnimationSoundFiniteVoiceWait()),
            _ => throw new InvalidDataException("Pre-Finished native audio reached the writer."));
        Require(manual.Pending && manual.Receipt!.DeferredVoices?.Single() == proof &&
            manual.Receipt!.AwaitedVoices?.Single() == proof, "Manual completion wait lost original receipt identity.");
        for (var read = 0; read < 8; read++)
            Require(world.PendingAnimationSoundFiniteVoiceWait()?.Single() == proof, "Repeated reads consumed a native phase budget.");
        Reject(() => world.Capture());
        phase += FalloutFiniteSoundCompletionWait.MaximumNativePhases + 1;
        Require(world.PendingAnimationSoundFiniteVoiceWait() is null && wait.Error is not null,
            "Missing native Finished remained an unbounded eligible wait.");
        queuePhase = 22;
        manual.Drain(session, compatibility, queuePhase,
            () => new(RuntimeManualSaveAdmissionKind.Refused, "animation-sound-continuation"),
            _ => throw new InvalidDataException("Expired native wait reached the writer."));
        Require(manual.Receipt is { Disposition: "failed", Error: "animation-sound-continuation" } &&
            manual.Receipt!.AwaitedVoices?.Single() == proof && ledger.Events.Single().End == FalloutAnimationSoundEnd.Active &&
            JsonSerializer.Serialize(ledger.Events) == history && state.SoundRandom.State == random,
            "Timeout changed history/RNG, fabricated completion or erased its failed manual request.");
        Reject(() => world.Capture());
        var failedReceipt = JsonSerializer.Serialize(manual.Receipt);

        var deadline = Started();
        Require(deadline.Observe(proof, 82, 83, 84, false, 11, 200) == proof &&
            deadline.Observe(proof, 82, 83, 84, false, 11, 200 + FalloutFiniteSoundCompletionWait.MaximumMilliseconds + 1) is null,
            "A stalled native phase hid the completion timeout.");
        foreach (var changed in new[]
        {
            proof with { NativeOwner = 91 }, proof with { Generation = generation + 1 },
            proof with { Reference = new("Pose.esm", 0x900) }, proof with { Sound = new("Pose.esm", 0x6a) },
            proof with { SoundSha256 = new string('b', 64) }, proof with { MediaSha256 = new string('c', 64) },
            proof with { Path = "sound\\fixture\\other.wav" }
        })
            Require(Started().Observe(changed, 82, 83, 84, false, 11, 200) is null, "Source/media/generation drift entered completion wait.");
        foreach (var binding in new[] { (92UL, 83UL, 84UL), (82UL, 93UL, 84UL), (82UL, 83UL, 94UL), (0UL, 83UL, 84UL) })
            Require(Started().Observe(proof, binding.Item1, binding.Item2, binding.Item3, false, 11, 200) is null,
                "Replacement or missing native playback entered completion wait.");
        var resumed = Started();
        Require(resumed.Observe(proof, 82, 83, 84, false, 11, 200) == proof &&
            resumed.Observe(proof, 82, 83, 84, true, 12, 300) is null, "Unreceipted resumed playback was assumed finished.");
        var regression = Started();
        Require(regression.Observe(proof, 82, 83, 84, false, 11, 200) == proof &&
            regression.Observe(proof, 82, 83, 84, false, 10, 200) is null, "A regressed native phase entered completion wait.");

        // The contract's genuine completion is independent of the expired
        // request; it cannot retry that request or alter its failure history.
        ledger.Complete(generation, FalloutAnimationSoundEnd.NativeFinished); registration.Dispose();
        queuePhase = 23;
        Require(!manual.Drain(session, compatibility, queuePhase,
            () => throw new InvalidDataException("Failed manual request replayed admission."),
            _ => throw new InvalidDataException("Failed manual request replayed writer.")) &&
            JsonSerializer.Serialize(manual.Receipt) == failedReceipt, "Late Finished rewrote or retried the failed request.");
        Reject(() => world.Capture());
        Require(world.ScriptManualSaves.Order.Requests.Single().Disposition == RuntimeSaveRequestDisposition.Failed &&
            records.SoundVoices.ActiveVoices == 0 && state.SoundRandom.State == random &&
            ledger.Events.Single().End == FalloutAnimationSoundEnd.NativeFinished,
            "Late native completion erased the joined queue failure, retained a voice or replayed selection.");
        Console.WriteLine("OPENNV_FINITE_SOUND_COMPLETION_WAIT_CONTRACT_PASS preFinished=true exactPlayback=true missingStartupRefused=true " +
            "phaseTimeout=true stalledClockTimeout=true driftRefused=true resumedRefused=true activeCaptureRefused=true " +
            "failedRequestImmutable=true joinedFailureCaptureRefused=true rng=true nativeOrderingAndOwnedAttribution=separate");
    }

    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException) { return; }
        throw new InvalidDataException("Still-active native audio entered a complete checkpoint.");
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidDataException(message); }
}
