using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

internal static class SoundCaptureDiagnosticContracts
{
    internal static void Run(FalloutPluginStack records)
    {
        var caller = new FalloutFormKey("Pose.esm", 0x900);
        var cell = new FalloutFormKey("Pose.esm", 0x800);
        var sound = new FalloutFormKey("Pose.esm", 0x68);
        using var world = new FalloutReferenceWorld(records);
        world.LoadCell(FalloutCellSceneReader.Read(records, cell));
        var state = world.Get(caller);
        Require(state.AnimationSoundCaptureDiagnostic is null && world.PendingAnimationSoundCaptureCount == 0,
            "Observation created an absent sound history.");
        var source = FalloutSoundRecordReader.Read(records, sound);
        var selected = FalloutAnimationSound.Select(source, [source.LogicalPath], state.SoundRandom, true, true);
        var events = state.AnimationSoundEvents;
        var generation = events.Begin(records, selected, "Sound: FixturePartial", true, [source.LogicalPath]);
        var before = JsonSerializer.Serialize(events.Events);
        var random = state.SoundRandom.State;
        var count = world.InstanceCount;
        for (var repetition = 0; repetition < 3; repetition++)
        {
            var diagnostic = state.AnimationSoundCaptureDiagnostic!;
            var blocker = diagnostic.Unsettled.Single();
            Require(!diagnostic.Ready && diagnostic.OpaqueError is null && diagnostic.Reference == caller &&
                blocker.Generation == generation && blocker.Sound == sound && blocker.End == FalloutAnimationSoundEnd.Active &&
                blocker.Error is null && FalloutAnimationSoundEventsSnapshot.Hash(blocker.SoundSha256) &&
                world.PendingAnimationSoundCaptureCount == 1,
                "The exact source Active refusal disappeared or acquired an invented opaque error.");
            _ = world.PendingAnimationSoundCaptures;
        }
        Require(state.SoundRandom.State == random && world.InstanceCount == count && JsonSerializer.Serialize(events.Events) == before,
            "Capture diagnostics consumed source state, randomness or history.");
        Reject(world, caller, generation, sound, FalloutAnimationSoundEnd.Active);
        world.UnloadCell(cell);
        using (var json = JsonDocument.Parse(JsonSerializer.Serialize(world.PendingAnimationSoundCaptures)))
            Require(json.RootElement[0].GetProperty("resident").GetBoolean() == false &&
                json.RootElement[0].GetProperty("history").GetProperty("Unsettled")[0].GetProperty("Generation").GetInt64() == generation,
                "Unloaded sound history lost its blocking reference or generation.");
        events.BindMedia(generation, new string('a', 64));
        events.Complete(generation, FalloutAnimationSoundEnd.NativeFinished);
        var finished = state.AnimationSoundCaptureDiagnostic!;
        Require(world.PendingAnimationSoundCaptureCount == 0 && finished.Ready &&
            finished.Unsettled.Count == 0 && world.Capture().Count > 0,
            "A genuine Finished receipt remained an unfinished-history diagnostic.");

        using var cancelled = new FalloutReferenceWorld(records);
        var cancelledState = cancelled.Get(caller);
        var cancelledEvents = cancelledState.AnimationSoundEvents;
        var cancellation = cancelledEvents.Begin(records, selected, "Sound: FixturePartial", true, [source.LogicalPath]);
        cancelledEvents.BindMedia(cancellation, new string('a', 64));
        cancelledEvents.Cancel(cancellation, "Actual emitter retirement.");
        Require(cancelledState.AnimationSoundCaptureDiagnostic!.Unsettled.Single() is
            { End: FalloutAnimationSoundEnd.Cancelled, Error: "Actual emitter retirement." },
            "Cancellation diagnostics erased the actual reason or converted it to completion.");
        Reject(cancelled, caller, cancellation, sound, FalloutAnimationSoundEnd.Cancelled);
        var unloaded = new FalloutAnimationSoundEvents(caller);
        var unloadGeneration = unloaded.Begin(records, selected, "Sound: FixturePartial", true, [source.LogicalPath]);
        unloaded.BindMedia(unloadGeneration, new string('a', 64));
        unloaded.Complete(unloadGeneration, FalloutAnimationSoundEnd.SourceUnloaded);
        var coldUnloaded = new FalloutAnimationSoundEvents(caller);
        coldUnloaded.Restore(unloaded.Capture(), records);
        Require(coldUnloaded.CaptureDiagnostic.Ready && coldUnloaded.Events.Single().End == FalloutAnimationSoundEnd.SourceUnloaded,
            "Committed source unload lost its terminal audio state during cold capture.");
        var opaque = new FalloutAnimationSoundEvents(caller);
        opaque.Fail(null, "Missing source owner.");
        Require(!opaque.CaptureDiagnostic.Ready && opaque.CaptureDiagnostic.Unsettled.Count == 0 &&
            opaque.CaptureDiagnostic.OpaqueError == "Missing source owner.", "Opaque source failure lost its independent cause.");
        Console.WriteLine("OPENNV_SOUND_CAPTURE_DIAGNOSTIC_PASS owner=true generation=true sourceHash=true activeRefused=true " +
            "cancelledRefused=true opaque=true unloaded=true readOnly=true finished=true noSaveSchema=true parity=unverified");
    }

    private static void Reject(FalloutReferenceWorld world, FalloutFormKey reference, long generation,
        FalloutFormKey sound, FalloutAnimationSoundEnd end)
    {
        try { world.Capture(); }
        catch (NotSupportedException error) when (error.Message.Contains(reference.ToString(), StringComparison.Ordinal) &&
            error.Message.Contains("generation=" + generation, StringComparison.Ordinal) &&
            error.Message.Contains("sound=" + sound, StringComparison.Ordinal) &&
            error.Message.Contains("end=" + end, StringComparison.Ordinal)) { return; }
        throw new InvalidDataException("Capture waived an unfinished sound or lost its exact refusal identity.");
    }

    private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
}
