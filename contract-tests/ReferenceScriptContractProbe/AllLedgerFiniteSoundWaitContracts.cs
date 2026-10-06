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
        using var actorVoice = records.SoundVoices.Register(sound, actor.Reference, "synthetic-actor-finite", () => true, () => { },
            sourceReference: actor.Reference, finiteWait: () => first);
        Require(world.PendingAnimationSoundFiniteVoiceWait() is null, "A bound actor hid an unbound nonactor generation.");
        var eligible = true;
        using var nonactorVoice = records.SoundVoices.Register(sound, null, "synthetic-positional-nonactor-finite", () => true, () => { },
            sourceReference: nonactor.Reference, finiteWait: () => eligible ? second : null);
        var concurrent = Begin(nonactor, 83); secondRandom = nonactor.SoundRandom.State;
        using var concurrentVoice = records.SoundVoices.Register(sound, nonactor.Reference, "synthetic-second-owner-same-ref", () => true, () => { },
            sourceReference: nonactor.Reference, finiteWait: () => concurrent);
        var expected = new[] { first, second, concurrent };
        Require(world.PendingAnimationSoundFiniteVoiceWait()?.SequenceEqual(expected) == true,
            "All-ledger wait lost the actual nonactor source origin or actor generation.");
        Reject(() => world.Capture());
        world.UnloadCell(cell.Cell.FormKey);
        Require(world.PendingAnimationSoundFiniteVoiceWait()?.SequenceEqual(expected) == true &&
            actor.SoundRandom.State == firstRandom && nonactor.SoundRandom.State == secondRandom,
            "Off-cell read-only audio wait lost retained native binding or consumed source RNG.");
        eligible = false;
        Require(world.PendingAnimationSoundFiniteVoiceWait() is null, "A source loop or missing finite native producer was admitted to wait.");
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
        var third = Begin(nonactor, 84);
        using var interruptedVoice = records.SoundVoices.Register(sound, null, "synthetic-cancelled", () => false, () => { },
            sourceReference: nonactor.Reference, finiteWait: () => third);
        nonactor.AnimationSoundEvents.Cancel(third.Generation, "Actual source audio node retired.");
        Require(world.PendingAnimationSoundFiniteVoiceWait() is null, "A native binding waived its actual cancelled history.");
        Reject(() => world.Capture());
        using var opaque = new FalloutReferenceWorld(records);
        opaque.Get(nonactor.Reference).AnimationSoundEvents.Fail(null, "Missing actual source emitter.");
        Require(opaque.PendingAnimationSoundFiniteVoiceWait() is null, "Opaque source sound was treated as finite wait.");
        Console.WriteLine("OPENNV_ALL_LEDGER_FINITE_SOUND_WAIT_CONTRACT_PASS actor=true nonactor=true positionalOrigin=true missingNativeRefused=true offCell=true activeRefused=true nativeCompletion=true loopProducerRefused=true cancelledRefused=true opaqueRefused=true coldNoReplay=true rng=true");
    }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException) { return; }
        throw new InvalidDataException("Unsettled source sound entered a complete checkpoint.");
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidDataException(message); }
}
