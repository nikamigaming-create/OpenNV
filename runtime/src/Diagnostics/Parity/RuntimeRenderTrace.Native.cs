using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.World.Actors;

namespace OpenNV.Runtime.Diagnostics.Parity;

internal sealed partial class RuntimeRenderTrace
{
    private void NativeNodeState(Node node, IDictionary<string, object?> properties)
    {
        if (node is RuntimeNativeSpeech speech)
        {
            properties["sourceSpeech"] = speech.State;
            foreach (var gap in speech.Unbound) _missing.Add($"{speech.GetPath()}:speech:{gap}");
            if (speech.Active) _missing.Add($"{speech.GetPath()}:speech:spatial-mixing-emotion-head-motion-and-creature-face-unverified");
            if (speech.Error is { } speechFailure) _missing.Add($"{speech.GetPath()}:speech:{speechFailure}");
        }
        if (node is RuntimeNativeCreature creature)
        {
            properties["sourceCreature"] = creature.Observation;
            foreach (var lane in creature.Unbound) _missing.Add($"{creature.GetPath()}:creature:{lane}");
            if (creature.Error is { } failure) _missing.Add($"{creature.GetPath()}:creature:{failure}");
        }
        if (node is MultiMeshInstance3D instance && instance.Multimesh is { } multimesh)
        {
            properties["sourceModel"] = SourceModel(node);
            properties["multimesh"] = ResourceState(multimesh);
            properties["materialOverride"] = instance.MaterialOverride is { } material ? ResourceState(material) : null;
            properties["layers"] = instance.Layers;
            properties["submissionEvidence"] = "scene-instance-buffer;GPU-execution-unobserved";
        }
        if (node is RuntimeNifControllerPlayer controller)
        {
            properties["sourceController"] = controller.Observation;
            foreach (var key in controller.UnboundTextKeys) _missing.Add($"{controller.GetPath()}:unbound-source-event:{key}");
        }
        if (node is RuntimeNifParticleSystem particles)
            properties["sourceParticles"] = particles.Observation;
        if (node is NativeOwnedAnimationSoundPlayer sounds)
        {
            properties["animationSound"] = sounds.State;
            var stack = _stack() ?? throw new InvalidOperationException("Sound trace has no winning record stack.");
            properties["soundRecords"] = sounds.Sources.Select(sound =>
            {
                var record = stack.GetEffective(sound.FormKey);
                return RecordState(record, record.ReadData());
            }).ToArray();
            foreach (var lane in sounds.Unbound) _missing.Add($"{sounds.GetPath()}:audio:{lane}");
        }
        if (node is AudioStreamPlayer voice)
            properties["voice"] = VoiceState(voice.Stream, voice.Playing, voice.StreamPaused,
                voice.GetPlaybackPosition(), voice.PitchScale, voice.VolumeDb, voice.Bus.ToString(), voice);
        if (node is AudioStreamPlayer3D spatial)
            properties["voice"] = VoiceState(spatial.Stream, spatial.Playing, spatial.StreamPaused,
                spatial.GetPlaybackPosition(), spatial.PitchScale, spatial.VolumeDb, spatial.Bus.ToString(), spatial);
        if (node is AudioStreamPlayer2D canvas)
            properties["voice"] = VoiceState(canvas.Stream, canvas.Playing, canvas.StreamPaused,
                canvas.GetPlaybackPosition(), canvas.PitchScale, canvas.VolumeDb, canvas.Bus.ToString(), canvas);
    }

    private object VoiceState(AudioStream? stream, bool playing, bool paused, double seconds,
        float pitch, float gainDb, string bus, Node voice) => new
        {
            stream = stream is null ? null : ResourceState(stream),
            playing,
            paused,
            positionSeconds = Value(seconds),
            pitch = Value(pitch),
            gainDb = Value(gainDb),
            bus,
            storageProperties = Properties(voice),
            observation = "voice-state-at-scene-walk;start-stop-events-between-observations-unobserved",
        };

    private object AudioBusState() => Enumerable.Range(0, AudioServer.BusCount).Select(index => new
    {
        index,
        name = AudioServer.GetBusName(index).ToString(),
        send = AudioServer.GetBusSend(index).ToString(),
        volumeDb = Value(AudioServer.GetBusVolumeDb(index)),
        muted = AudioServer.IsBusMute(index),
        solo = AudioServer.IsBusSolo(index),
        bypassEffects = AudioServer.IsBusBypassingEffects(index),
        effects = Enumerable.Range(0, AudioServer.GetBusEffectCount(index))
            .Where(slot => AudioServer.GetBusEffect(index, slot) is not AudioEffectCapture)
            .Select(slot => new
            {
                slot,
                enabled = AudioServer.IsBusEffectEnabled(index, slot),
                resource = ResourceState(AudioServer.GetBusEffect(index, slot))
            }).ToArray(),
    }).ToArray();
}
