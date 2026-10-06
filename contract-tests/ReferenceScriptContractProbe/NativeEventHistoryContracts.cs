using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

internal static class NativeEventHistoryContracts
{
    internal static void Run(FalloutPluginStack records)
    {
        FiniteSoundCompletionWaitContracts.Run(records);
        FiniteSoundRegistryContracts.Run(records);
        AllLedgerFiniteSoundWaitContracts.Run(records);
        var caller = new FalloutFormKey("Pose.esm", 0x900);
        var sound = new FalloutFormKey("Pose.esm", 0x68);
        var idle = new FalloutFormKey("Pose.esm", 0x69);
        using var world = new FalloutReferenceWorld(records);
        var state = world.Get(caller);
        var source = FalloutSoundRecordReader.Read(records, sound);
        var selected = FalloutAnimationSound.Select(source, [source.LogicalPath], state.SoundRandom, true, true);
        var events = state.AnimationSoundEvents;
        var generation = events.Begin(records, selected, "Sound: FixturePartial", true, [source.LogicalPath]);
        Reject(() => world.Capture());
        events.BindMedia(generation, new string('a', 64));
        Require(!events.CanCapture, "Media binding was mistaken for native completion.");
        Reject(() => events.Complete(generation + 1, FalloutAnimationSoundEnd.NativeFinished));
        Require(events.Complete(generation, FalloutAnimationSoundEnd.NativeFinished) &&
            !events.Complete(generation, FalloutAnimationSoundEnd.NativeFinished), "Completion was not exactly once for its source generation.");
        Require(events.PartialLanes.Count() == 2 && events.CanCapture,
            "Completed partial audio erased its divergence or remained an unfinished voice.");
        var faults = state.HitReactionFaults;
        var attempt = faults.BeginAttempt(); var random = state.HitReactionRandom.State;
        var binding = FalloutHitReactionFaults.Source(records.GetEffective(idle));
        faults.Record(new(attempt, 0, 0, binding, 0, 143, "Synthetic current procedure is unbound.",
            random, random, [binding], []), records);
        var saved = JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(world.Capture()))!;
        using var cold = new FalloutReferenceWorld(records); cold.Restore(saved);
        Require(JsonSerializer.Serialize(cold.Capture()) == JsonSerializer.Serialize(saved) &&
            cold.Get(caller).CurrentHitReactionError == "Synthetic current procedure is unbound." &&
            cold.Get(caller).SoundRandom.State == state.SoundRandom.State && cold.Get(caller).HitReactionRandom.State == random,
            "Cold native history replayed source selection, erased faults or changed random state.");
        var before = JsonSerializer.Serialize(cold.Capture());
        var original = saved.Single(value => value.Reference == caller);
        var sounds = original.AnimationSoundEvents!;
        foreach (var invalid in new[]
        {
            sounds with { Reference = new("Pose.esm", 0x902) },
            sounds with { Events = [sounds.Events[0] with { End = FalloutAnimationSoundEnd.Active }] },
            sounds with { Events = [sounds.Events[0] with { End = FalloutAnimationSoundEnd.Cancelled }] },
            sounds with { Events = [sounds.Events[0] with { SoundSha256 = new string('0', 64) }] },
            sounds with { Events = [sounds.Events[0] with { PartialLanes = [] }] },
            sounds with { Events = [sounds.Events[0] with { MediaSha256 = "missing" }] }
        })
        {
            using var rejected = new FalloutReferenceWorld(records);
            Reject(() => rejected.Restore(saved.Select(value => value.Reference == caller ? value with { AnimationSoundEvents = invalid } : value).ToArray()));
            Require(rejected.InstanceCount == 0 && JsonSerializer.Serialize(cold.Capture()) == before,
                "Invalid sound history partially published a world or changed the cold source history.");
        }
        var stopped = original.HitReactionFaults!;
        using var rejectedRead = new FalloutReferenceWorld(records);
        Reject(() => rejectedRead.Restore(saved.Select(value => value.Reference == caller ? value with
        { HitReactionFaults = stopped with { Faults = [stopped.Faults[0] with { Function = 77 }] } } : value).ToArray()));
        Require(rejectedRead.InstanceCount == 0, "Invalid failed read partially published a cold world.");
        Require(JsonSerializer.Serialize(cold.Capture()) == before, "Invalid failed read partially replaced source history.");
        var cancelled = new FalloutAnimationSoundEvents(caller);
        var cancelGeneration = cancelled.Begin(records, selected, "Sound: FixturePartial", true, [source.LogicalPath]);
        cancelled.BindMedia(cancelGeneration, new string('a', 64)); cancelled.Cancel(cancelGeneration, "Emitter retired.");
        Reject(() => cancelled.Capture());
        Reject(() => cancelled.Complete(cancelGeneration, FalloutAnimationSoundEnd.NativeFinished));
        var stoppedVoice = new FalloutAnimationSoundEvents(caller);
        var stopGeneration = stoppedVoice.Begin(records, selected, "Sound: FixturePartial", true, [source.LogicalPath]);
        stoppedVoice.BindMedia(stopGeneration, new string('a', 64)); stoppedVoice.Complete(stopGeneration, FalloutAnimationSoundEnd.SourceStopped);
        Require(stoppedVoice.Capture().Events[0].End == FalloutAnimationSoundEnd.SourceStopped, "Authored stop lost its distinct source receipt.");
        var failed = new FalloutAnimationSoundEvents(caller); failed.Fail(null, "Missing source emitter."); Reject(() => failed.Capture());
        state.HitReactionFaultCaptureBlocker = "Opaque post-selection pose failure.";
        Reject(() => world.Capture());
        Console.WriteLine("OPENNV_NATIVE_EVENT_HISTORY_CONTRACT_PASS activeRefused=true missingReceiptRefused=true finishedOnce=true partialRetained=true cancelledRefused=true sourceStop=true sourceHash=true coldNoReplay=true rng=true failedRead=true invalidAtomic=true opaqueRefused=true parity=unverified");
    }

    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException) { return; }
        throw new InvalidDataException("Unowned or malformed native history was admitted.");
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
}
