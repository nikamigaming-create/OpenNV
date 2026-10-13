using System.Reflection;
using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

public partial class NativeNifInstanceAudit
{
    private const string PcmObservationMixerFailure = "First-party PCM observation mixer failure.";

    private async Task ExerciseUnownedPcmObservation()
    {
        var directory = Directory.CreateTempSubdirectory("opennv-pcm-observation-");
        var plugin = Path.Combine(directory.FullName, "BoneEmitter.esm");
        var paused = GetTree().Paused;
        Node3D? actor = null;
        NativeOwnedAnimationSoundPlayer? owner = null;
        NativeOwnedPcmStream? pcm = null;
        AudioStreamPlayer? player = null;
        AudioStreamWav? wav = null;
        FalloutPluginStack? records = null;
        OpenNV.Runtime.World.Cells.FalloutReferenceWorld? world = null;
        try
        {
            File.WriteAllBytes(plugin, BoneEmitterPlugin());
            records = FalloutPluginStack.Load(directory.FullName, ["BoneEmitter.esm"]);
            world = new(records);
            var reference = new FalloutFormKey("BoneEmitter.esm", 2);
            var instance = world.Get(reference);
            actor = BoneEmitterActor(reference, "ObservedSkeleton"); AddChild(actor);
            owner = new(records, null!, actor, .01f, instance.SoundRandom, instance.AnimationSoundEvents);
            actor.AddChild(owner);
            // Explicit Node.Name replaces '@'. Let Godot assign the anonymous
            // name through an actual sibling collision instead.
            actor.AddChild(new Node3D { Name = "Emitter" });
            var emitter = new Node3D { Name = "Emitter" }; actor.AddChild(emitter, forceReadableName: false);
            var emitterPath = actor.GetPathTo(emitter).ToString();
            Require(RuntimeNativeNifSoundEmitters.HasAnonymousPath(emitterPath),
                "PCM fixture did not construct an actual anonymous emitter path: " + emitterPath);
            var sound = FalloutSoundRecordReader.Read(records, new("BoneEmitter.esm", 1));
            wav = BoneEmitterWav(sound.LogicalPath); BoneEmitterStreams(owner).Add(sound.LogicalPath, wav);
            var selection = FalloutAnimationSound.Select(sound, [sound.LogicalPath], instance.SoundRandom, true);
            var generation = instance.AnimationSoundEvents.Begin(records, selection, "Sound:EmitterLoop", false, [sound.LogicalPath]);
            instance.AnimationSoundEvents.BindMedia(generation, wav.GetMeta("opennv_owned_media_sha256").AsString());
            pcm = new(wav, FalloutSoundLoop.Read(sound)); player = new() { Stream = pcm.Stream };
            GetTree().Paused = true;
            BoneEmitterTrack(owner, player, emitter, FalloutSoundLoop.Read(sound), sound.FormKey, generation, pcm);
            BoneEmitterPlay(owner, player);
            Require(player.HasStreamPlayback(), "Unsupported emitter test has no actual PCM playback.");
            var samples = pcm.Capture(); var random = instance.SoundRandom.State;
            Require(records.SoundVoices.PendingFiniteSourceVoices(reference) is null,
                "Anonymous PCM entered the source finite wait: " + emitterPath);
            Require(!owner.CanAwaitFiniteCompletion, "Anonymous PCM entered its owner finite wait: " + emitterPath);
            Require(!owner.CanCaptureSilent, "Anonymous PCM became an owner checkpoint: " + emitterPath);
            Require(!instance.AnimationSoundEvents.CanCapture, "Anonymous PCM became a source history checkpoint: " + emitterPath);
            using (var observation = JsonDocument.Parse(JsonSerializer.Serialize(owner.State)))
            {
                var continuations = observation.RootElement.GetProperty("pcmContinuations");
                var row = continuations.EnumerateArray().Single();
                var result = row.GetProperty("continuation");
                Require(row.GetProperty("generation").GetInt64() == generation && !result.GetProperty("Ready").GetBoolean() &&
                    result.GetProperty("UnownedReason").GetString() == "Anonymous source sound emitter has no typed continuation identity.",
                    "Unsupported PCM observation omitted its exact active source generation or reason.");
            }
            BoneEmitterReject(() => world.Capture());
            BoneEmitterReject(() => records.SoundVoices.PrepareFiniteSaveDrain());
            Require(pcm.Capture() == samples && instance.SoundRandom.State == random && records.SoundVoices.ActiveVoices == 1,
                "Capability observations advanced samples, replayed source random selection or retired the live voice.");
            records.SoundVoices.Stop(sound.FormKey);
            Require(records.SoundVoices.ActiveVoices == 0 && instance.AnimationSoundEvents.CanCapture &&
                instance.AnimationSoundEvents.Capture().Events.Single().End == FalloutAnimationSoundEnd.SourceStopped,
                "Authored Stop did not retire the same active generation after its unsupported continuation.");
            world.Capture();

            // A second real generation independently has an unsupported emitter
            // and a retained mixer fault. Neither observation may mask the fault,
            // advance its sample clock or replay the source selection.
            selection = FalloutAnimationSound.Select(sound, [sound.LogicalPath], instance.SoundRandom, true);
            generation = instance.AnimationSoundEvents.Begin(records, selection, "Sound:EmitterLoop", false, [sound.LogicalPath]);
            instance.AnimationSoundEvents.BindMedia(generation, wav.GetMeta("opennv_owned_media_sha256").AsString());
            pcm = new(wav, FalloutSoundLoop.Read(sound)); player = new() { Stream = pcm.Stream };
            BoneEmitterTrack(owner, player, emitter, FalloutSoundLoop.Read(sound), sound.FormKey, generation, pcm);
            BoneEmitterPlay(owner, player);
            Require(player.HasStreamPlayback(), "Corruption lane has no actual source PCM playback.");
            var playback = PcmObservationPlayback(pcm);
            var clock = PcmObservationClock(playback); random = instance.SoundRandom.State;
            playback.Fail(new InvalidDataException(PcmObservationMixerFailure));
            PcmObservationRejectCorruption(() => _ = owner.State);
            PcmObservationRejectCorruption(() => records.SoundVoices.PendingFiniteSourceVoices(reference));
            PcmObservationRejectCorruption(() => records.SoundVoices.PrepareFiniteSaveDrain());
            PcmObservationRejectCorruption(() => _ = owner.CanAwaitFiniteCompletion);
            BoneEmitterReject(() => world.Capture());
            Require(PcmObservationClock(playback) == clock && instance.SoundRandom.State == random &&
                records.SoundVoices.ActiveVoices == 1 && owner.LastEvent is null &&
                instance.AnimationSoundEvents.Events.Last() is { End: FalloutAnimationSoundEnd.Active, Error: null } active &&
                active.Generation == generation && instance.AnimationSoundEvents.Events.Count == 2,
                "Corruption observations advanced samples, replayed selection or manufactured source completion.");
            records.SoundVoices.Stop(sound.FormKey);
            var faulted = instance.AnimationSoundEvents.Capture();
            Require(records.SoundVoices.ActiveVoices == 0 && faulted.Events.Count == 2 &&
                faulted.Events.Last() is { End: FalloutAnimationSoundEnd.Faulted } fault && fault.Generation == generation &&
                fault.Error == "PCM mixer failed: " + PcmObservationMixerFailure && faulted.Faults?.Count == 1,
                "Source retirement concealed PCM corruption or completed the failed generation.");
            owner.Free(); owner = null;
            actor.Free(); actor = null;
        }
        finally
        {
            GetTree().Paused = paused;
            if (IsInstanceValid(actor)) actor!.Free();
            if (IsInstanceValid(player)) { player!.Stop(); player.Stream = null; player.Free(); }
            if (pcm is not null && IsInstanceValid(pcm.Stream)) pcm.Stream.Dispose();
            if (IsInstanceValid(wav)) wav!.Dispose();
            world?.Dispose(); records?.Dispose();
            File.Delete(plugin); Directory.Delete(directory.FullName);
        }
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        GD.Print("OPENNV_NATIVE_UNOWNED_PCM_OBSERVATION_PASS actualPlayback=true exactGenerationAndReason=true " +
            "checkpointRefused=true finiteDrainRefused=true samplesAndRandomUnchanged=true sourceStop=true " +
            "corruptionPropagated=true faultedRetirement=true sourceReplay=false renderer=NONE");
    }

    private static FalloutPcmPlayback PcmObservationPlayback(NativeOwnedPcmStream pcm)
    {
        var source = typeof(NativeOwnedPcmStream).GetField("_source", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(pcm)!;
        return ((IEnumerable<FalloutPcmPlayback>)source.GetType().GetField("Playbacks", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(source)!).Single();
    }

    private static (double Position, long Loops, bool Releasing, bool StartPending) PcmObservationClock(FalloutPcmPlayback playback)
    {
        T Field<T>(string name) => (T)typeof(FalloutPcmPlayback).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(playback)!;
        return (Field<double>("_position"), Field<long>("_loops"), Field<bool>("_releasing"), Field<bool>("_startPending"));
    }

    private static void PcmObservationRejectCorruption(Action action)
    {
        try { action(); }
        catch (InvalidDataException error) when (error.Message == "PCM mixer failed: " + PcmObservationMixerFailure) { return; }
        throw new InvalidDataException("PCM corruption became an unsupported-emitter capability or save admission.");
    }
}
