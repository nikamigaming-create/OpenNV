using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static class ManualSaveFiniteVoiceContracts
{
    internal static void Run(FalloutPluginStack records)
    {
        var reference = new FalloutFormKey("Pose.esm", 0x900);
        var other = new FalloutFormKey("Pose.esm", 0x902);
        var sound = new FalloutFormKey("Pose.esm", 0x68);
        using var world = new FalloutReferenceWorld(records);
        var state = world.Get(reference);
        var source = FalloutSoundRecordReader.Read(records, sound);
        var selected = FalloutAnimationSound.Select(source, [source.LogicalPath], state.SoundRandom, true, true);
        var events = state.AnimationSoundEvents;
        var generation = events.Begin(records, selected, "Sound: FixturePartial", true, [source.LogicalPath]);
        Require(!events.CanAwaitNativeCompletion, "Unbound media became a proven finite wait.");
        events.BindMedia(generation, new string('a', 64));
        Require(events.CanAwaitNativeCompletion && !events.CanCapture, "Live media was admitted to capture or refused a read-only wait.");
        var entry = events.Events.Single();
        var proof = new FalloutFiniteSoundVoice(15, reference, generation, sound, entry.SoundSha256, entry.Path!, entry.MediaSha256!);
        state.ProcedureCaptureBlocker = "synthetic stopped package binding";
        state.CanCapturePackageBindingFailure = () => false;
        Require(world.PendingProcedureFiniteVoiceWait() is null, "Absent native binding was assumed finite.");
        state.PendingPackageBindingFiniteVoices = () => [proof];
        Require(world.PendingProcedureFiniteVoiceWait()?.Single() == proof && !state.PackageBindingFailureCaptureReady,
            "Finite wait changed capture readiness or lost its exact generation.");
        Reject(() => world.Capture());
        var stopped = world.Get(other); stopped.ProcedureCaptureBlocker = "unowned independent pose";
        Require(world.PendingProcedureFiniteVoiceWait() is null, "One known voice waived a second unowned capture owner.");
        stopped.ProcedureCaptureBlocker = null;
        foreach (var invalid in new[] { proof with { Reference = other }, proof with { SoundSha256 = new string('0', 64) },
            proof with { Generation = generation + 1 }, proof with { MediaSha256 = new string('b', 64) } })
        {
            state.PendingPackageBindingFiniteVoices = () => [invalid];
            Reject(() => world.PendingProcedureFiniteVoiceWait());
        }
        state.PendingPackageBindingFiniteVoices = () => [proof];
        events.Complete(generation, FalloutAnimationSoundEnd.NativeFinished);
        Require(events.CanCapture && !events.CanAwaitNativeCompletion && world.PendingProcedureFiniteVoiceWait() is null,
            "A finished generation remained an active wait.");
        state.ProcedureCaptureBlocker = null;
        var snapshot = world.Capture();
        using var cold = new FalloutReferenceWorld(records); cold.Restore(snapshot);
        Require(cold.Get(reference).PendingPackageBindingFiniteVoices is null &&
            JsonSerializer.Serialize(cold.Capture()) == JsonSerializer.Serialize(snapshot),
            "Cold history created live voice proof or replayed an ended source generation.");
        foreach (var cancelled in new[] { true, false })
        {
            var failed = new FalloutAnimationSoundEvents(reference);
            var id = failed.Begin(records, selected, "Sound: FixturePartial", true, [source.LogicalPath]);
            failed.BindMedia(id, new string('a', 64));
            if (cancelled) failed.Cancel(id, "Actual emitter retirement."); else failed.Fail(id, "Actual source failure.");
            Require(!failed.CanAwaitNativeCompletion && !failed.CanCapture, "A cancelled/failed generation became a transient wait.");
        }
        var opaque = new FalloutAnimationSoundEvents(reference); opaque.Fail(null, "Missing authored emitter.");
        Require(!opaque.CanAwaitNativeCompletion, "Opaque source failure became a finite wait.");
        Console.WriteLine("OPENNV_MANUAL_SAVE_FINITE_VOICE_CONTRACT_PASS exactLiveGeneration=true missingBindingRefused=true independentRefused=true sourceHash=true activeCaptureRefused=true cancelledFailedRefused=true coldNoLiveProof=true campaignAndParity=unverified");
    }

    private static void Reject(Action action)
    {
        try { action(); } catch (Exception error) when (error is InvalidDataException or NotSupportedException) { return; }
        throw new InvalidDataException("Invalid finite wait/capture was admitted.");
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidDataException(message); }
}
