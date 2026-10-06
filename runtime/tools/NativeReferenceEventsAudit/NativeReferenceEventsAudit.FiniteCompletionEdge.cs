using System.Diagnostics;
using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

public partial class NativeReferenceEventsAudit
{
    private async Task<object> ProveFiniteCompletionEdge(FalloutPluginStack records, FalloutReferenceWorld world,
        IReadOnlyList<Node> voices, string compatibility)
    {
        var expected = world.PendingAnimationSoundFiniteVoiceWait()?.ToArray() ??
            throw new InvalidDataException("Completion-edge fixture lacks its original finite native generations.");
        Require(expected.Length > 0 && expected.Length == voices.Count,
            "Completion-edge fixture lacks its original finite native generations.");
        var requests = new RuntimeManualSaveRequests(); var session = Guid.NewGuid();
        requests.Request(session, compatibility, Engine.GetProcessFrames());
        if (voices.Any(voice => voice is AudioStreamPlayer3D))
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        for (var attempt = 0; Engine.GetProcessFrames() <= requests.Receipt!.RequestedPhase && attempt < 4; attempt++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Require(Engine.GetProcessFrames() > requests.Receipt!.RequestedPhase &&
            voices.All(voice => GodotObject.IsInstanceValid(voice) && voice.IsInsideTree() && NativeFinitePlaying(voice)),
            "Finite native playback did not reach an observable later playing phase.");
        var history = expected.Select(proof => world.Get(proof.Reference).AnimationSoundEvents.Events
            .Single(entry => entry.Generation == proof.Generation).Copy()).ToArray();
        var random = expected.DistinctBy(proof => proof.Reference).ToDictionary(proof => proof.Reference,
            proof => world.Get(proof.Reference).SoundRandom.State);

        // Keep the main thread before Finished while the real mixer reaches EOS.
        // The timer only refuses this isolated component; it supplies no event.
        var timer = Stopwatch.StartNew();
        while (voices.All(NativeFinitePlaying) && timer.Elapsed.TotalSeconds < 30)
            Thread.Sleep(1);
        Require(voices.Any(voice => !NativeFinitePlaying(voice)) &&
            JsonSerializer.Serialize(expected.Select(proof => world.Get(proof.Reference).AnimationSoundEvents.Events
                .Single(entry => entry.Generation == proof.Generation))) == JsonSerializer.Serialize(history),
            "Actual native inactivity was not observed before its matching source Finished.");
        var pending = world.PendingAnimationSoundFiniteVoiceWait();
        Require(pending is not null && pending.SequenceEqual(expected),
            "Pre-Finished native inactivity dropped or replaced the original source/native/media receipts.");
        var window = records.SoundVoices.State;
        var writes = 0;
        requests.Drain(session, compatibility, Engine.GetProcessFrames(),
            () => new(RuntimeManualSaveAdmissionKind.FiniteSourceAudio, Voices: pending),
            _ => throw new InvalidDataException("Pre-Finished native audio reached the complete writer."));
        Require(requests.Pending && requests.Receipt!.DeferredVoices?.SequenceEqual(expected) == true &&
            requests.Receipt!.AwaitedVoices?.SequenceEqual(expected) == true, "Native completion window failed the ordinary manual wait.");
        Reject(() => world.Capture());

        timer.Restart();
        while (world.PendingAnimationSoundCaptureCount != 0 && timer.Elapsed.TotalSeconds < 90)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Require(world.PendingAnimationSoundCaptureCount == 0 &&
            expected.All(proof => world.Get(proof.Reference).AnimationSoundEvents.Events
                .Single(entry => entry.Generation == proof.Generation).End == FalloutAnimationSoundEnd.NativeFinished),
            "Original finite native playback did not deliver genuine Finished after the observed window.");
        for (var index = 0; index < expected.Length; index++)
        {
            var proof = expected[index];
            var ended = world.Get(proof.Reference).AnimationSoundEvents.Events.Single(entry => entry.Generation == proof.Generation);
            Require(JsonSerializer.Serialize(ended with { End = FalloutAnimationSoundEnd.Active }) == JsonSerializer.Serialize(history[index]),
                "Native Finished changed original source, media, generation or partial history.");
        }
        Require(random.All(pair => world.Get(pair.Key).SoundRandom.State == pair.Value) && records.SoundVoices.ActiveVoices == 0,
            "Native completion wait replayed selection or retained a transient native registration.");
        var snapshot = JsonSerializer.Serialize(world.Capture());
        using var cold = new FalloutReferenceWorld(records);
        cold.Restore(JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(snapshot)!);
        Require(JsonSerializer.Serialize(cold.Capture()) == snapshot && cold.PendingAnimationSoundFiniteVoiceWait() is null,
            "Cold completed history recreated a native completion wait or changed the source world.");
        var directory = Path.Combine("tmp", "opennv-native-finite-completion-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            Require(requests.Drain(session, compatibility, Engine.GetProcessFrames(),
                () => new(RuntimeManualSaveAdmissionKind.Ready), id =>
                {
                    writes++;
                    var output = Path.Combine(directory, id.ToString("N") + ".json");
                    File.WriteAllText(output, JsonSerializer.Serialize(world.Capture()));
                    return new(id.ToString("N"), output, "disposable-reference-world-only", null, null, null, DateTime.UtcNow);
                }) && requests.Receipt!.Disposition == "completed" && writes == 1,
                "Genuine Finished did not permit exactly one later complete component capture.");
            requests.Drain(session, compatibility, Engine.GetProcessFrames(),
                () => throw new InvalidDataException("Completed native request replayed admission."),
                _ => throw new InvalidDataException("Completed native request replayed writer."));
            return new
            {
                nativeWindow = window,
                manual = requests.Receipt,
                writes,
                genuineFinished = true,
                activeCaptureRefused = true,
                unchangedHistory = true,
                coldNoReplay = true,
                recording = false,
                boundary = "isolated-native-completion-window-and-reference-world-capture;ordinary-F5-attribution/campaign/retail-independent"
            };
        }
        finally { Directory.Delete(directory, true); }
    }

    private static bool NativeFinitePlaying(Node voice) => voice is AudioStreamPlayer3D spatial
        ? spatial.Playing : ((AudioStreamPlayer)voice).Playing;

    private static AudioStreamPlayback? NativeFinitePlayback(Node voice) => voice is AudioStreamPlayer3D spatial
        ? spatial.HasStreamPlayback() ? spatial.GetStreamPlayback() : null
        : ((AudioStreamPlayer)voice).HasStreamPlayback() ? ((AudioStreamPlayer)voice).GetStreamPlayback() : null;
}
