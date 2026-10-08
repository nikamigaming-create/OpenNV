using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static class AllLedgerFiniteSoundWaitContracts
{
    internal static void Run(FalloutPluginStack records)
    {
        using var world = new FalloutReferenceWorld(records);
        var cell = FalloutCellSceneReader.Read(records, new("Pose.esm", 0x800)); world.LoadCell(cell);
        var actor = world.Get(new("Pose.esm", 0x900)); var nonactor = world.Get(new("Pose.esm", 0x901));
        var sound = new FalloutFormKey("Pose.esm", 0x68);
        var source = FalloutSoundRecordReader.Read(records, sound);
        FalloutFiniteSoundVoice Begin(FalloutReferenceInstance state, ulong native)
        {
            var selected = FalloutAnimationSound.Select(source, [source.LogicalPath], state.SoundRandom, true, true);
            var generation = state.AnimationSoundEvents.Begin(records, selected, "Sound: FixturePartial", true, [source.LogicalPath]);
            state.AnimationSoundEvents.BindMedia(generation, new string('a', 64));
            var entry = state.AnimationSoundEvents.Events.Last();
            return new(native, state.Reference, generation, sound, entry.SoundSha256, entry.Path!, entry.MediaSha256!);
        }
        var first = Begin(actor, 81); var second = Begin(nonactor, 82);
        var firstRandom = actor.SoundRandom.State; var secondRandom = nonactor.SoundRandom.State;
        Require(world.PendingAnimationSoundFiniteVoiceWait() is null, "A source ledger invented a missing native voice.");
        Require(world.AnimationSoundSaveBlocker == "animation-sound-continuation", "Missing native audio admitted the automatic writer.");
        using var actorVoice = records.SoundVoices.Register(sound, actor.Reference, "synthetic-actor-finite", () => true, () => { },
            sourceReference: actor.Reference, finiteWait: () => first);
        Require(world.PendingAnimationSoundFiniteVoiceWait() is null, "A bound actor hid an unbound nonactor generation.");
        Require(world.AnimationSoundSaveBlocker == "animation-sound-continuation", "A bound actor waived another save owner.");
        var eligible = true;
        using var nonactorVoice = records.SoundVoices.Register(sound, null, "synthetic-positional-nonactor-finite", () => true, () => { },
            sourceReference: nonactor.Reference, finiteWait: () => eligible ? second : null);
        var concurrent = Begin(nonactor, 83); secondRandom = nonactor.SoundRandom.State;
        using var concurrentVoice = records.SoundVoices.Register(sound, nonactor.Reference, "synthetic-second-owner-same-ref", () => true, () => { },
            sourceReference: nonactor.Reference, finiteWait: () => concurrent);
        var expected = new[] { first, second, concurrent };
        Require(world.PendingAnimationSoundFiniteVoiceWait()?.SequenceEqual(expected) == true,
            "All-ledger wait lost the actual nonactor source origin or actor generation.");
        Require(world.AnimationSoundSaveBlocker == "source-finite-audio", "Finite native audio admitted capture before Finished.");
        Reject(() => world.Capture());
        world.UnloadCell(cell.Cell.FormKey);
        Require(world.PendingAnimationSoundFiniteVoiceWait()?.SequenceEqual(expected) == true &&
            actor.SoundRandom.State == firstRandom && nonactor.SoundRandom.State == secondRandom,
            "Off-cell read-only audio wait lost retained native binding or consumed source RNG.");
        eligible = false;
        Require(world.PendingAnimationSoundFiniteVoiceWait() is null, "A source loop or missing finite native producer was admitted to wait.");
        Require(world.AnimationSoundSaveBlocker == "animation-sound-continuation", "Retiring a native wait owner waived its source capture refusal.");
        eligible = true;
        actor.AnimationSoundEvents.Complete(first.Generation, FalloutAnimationSoundEnd.NativeFinished); actorVoice.Dispose();
        Require(world.PendingAnimationSoundFiniteVoiceWait()?.SequenceEqual([second, concurrent]) == true,
            "One native completion silently admitted another still-active nonactor.");
        nonactor.AnimationSoundEvents.Complete(second.Generation, FalloutAnimationSoundEnd.NativeFinished); nonactorVoice.Dispose();
        Require(world.PendingAnimationSoundFiniteVoiceWait()?.SequenceEqual([concurrent]) == true,
            "Completing one sound owner waived another generation on the same reference.");
        nonactor.AnimationSoundEvents.Complete(concurrent.Generation, FalloutAnimationSoundEnd.NativeFinished); concurrentVoice.Dispose();
        var snapshot = JsonSerializer.Serialize(world.Capture());
        using var cold = new FalloutReferenceWorld(records);
        cold.Restore(JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(snapshot)!);
        Require(cold.PendingAnimationSoundFiniteVoiceWait() is null && JsonSerializer.Serialize(cold.Capture()) == snapshot,
            "Cold settled history replayed native voices or changed source/RNG state.");
        Require(world.AnimationSoundSaveBlocker is null && cold.AnimationSoundSaveBlocker is null,
            "Genuine completed history failed automatic save admission.");
        var third = Begin(nonactor, 84);
        using var interruptedVoice = records.SoundVoices.Register(sound, null, "synthetic-cancelled", () => false, () => { },
            sourceReference: nonactor.Reference, finiteWait: () => third);
        nonactor.AnimationSoundEvents.Cancel(third.Generation, "Actual source audio node retired.");
        Require(world.PendingAnimationSoundFiniteVoiceWait() is null, "A native binding waived its actual cancelled history.");
        Require(world.AnimationSoundSaveBlocker == "animation-sound-continuation", "Cancelled history admitted the automatic writer.");
        Reject(() => world.Capture());
        using var opaque = new FalloutReferenceWorld(records);
        opaque.Get(nonactor.Reference).AnimationSoundEvents.Fail(null, "Missing actual source emitter.");
        Require(opaque.PendingAnimationSoundFiniteVoiceWait() is null, "Opaque source sound was treated as finite wait.");
        Require(opaque.AnimationSoundSaveBlocker == "animation-sound-continuation", "Opaque history disappeared from save admission.");
        Reject(() => opaque.Capture());
        using var looping = new FalloutReferenceWorld(records);
        var loopActor = looping.Get(actor.Reference);
        var loopSource = FalloutSoundRecordReader.Read(records, new("Pose.esm", 0x6a));
        var loopSelection = FalloutAnimationSound.Select(loopSource, [loopSource.LogicalPath], loopActor.SoundRandom, true, true);
        loopActor.AnimationSoundEvents.Begin(records, loopSelection, "Sound: FixtureLoop", true, [loopSource.LogicalPath]);
        var loopHistory = JsonSerializer.Serialize(loopActor.AnimationSoundEvents.Events);
        var loopRandom = loopActor.SoundRandom.State;
        for (var observation = 0; observation < 3; observation++)
            Require(looping.AnimationSoundSaveBlocker == "animation-sound-continuation", "An unowned source loop admitted automatic capture.");
        Require(JsonSerializer.Serialize(loopActor.AnimationSoundEvents.Events) == loopHistory && loopActor.SoundRandom.State == loopRandom,
            "Automatic save admission changed a looping source generation or RNG.");
        Reject(() => looping.Capture());
        Console.WriteLine("OPENNV_ALL_LEDGER_FINITE_SOUND_WAIT_CONTRACT_PASS actor=true nonactor=true positionalOrigin=true missingNativeRefused=true offCell=true activeRefused=true nativeCompletion=true loopProducerRefused=true cancelledRefused=true opaqueRefused=true coldNoReplay=true rng=true");
        Console.WriteLine("OPENNV_ANIMATION_SOUND_SAVE_ADMISSION_PASS missingNativeRefused=true finiteDeferred=true loopRefused=true cancelledRefused=true opaqueRefused=true completed=true coldNoReplay=true readOnly=true ordinaryDriver=unverified");
    }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException) { return; }
        throw new InvalidDataException("Unsettled source sound entered a complete checkpoint.");
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidDataException(message); }
}
