using Godot;

namespace OpenNV.Runtime.Content;

internal sealed partial class NativeOwnedAnimationSoundPlayer
{
    private Exception? _pcmRestoreFailure;
    internal void RequirePcmRestored()
    {
        if (_pcmRestoreFailure is { } failure) throw new InvalidDataException("Saved audio could not be restored.", failure);
    }
    private FalloutAnimationSoundPlaybackSnapshot CapturePcmVoice(Node node, Voice voice)
    {
        if (!_voices.TryGetValue(node, out var current) || !ReferenceEquals(current, voice) ||
            voice.Pcm is null || voice.Completed is not null || !GodotObject.IsInstanceValid(node) || !node.IsInsideTree() ||
            !GodotObject.IsInstanceValid(voice.Emitter) || !voice.Emitter.IsInsideTree())
            throw new NotSupportedException("PCM continuation lost its native voice or source emitter.");
        var stream = node is AudioStreamPlayer3D spatial ? spatial.Stream : ((AudioStreamPlayer)node).Stream;
        if (stream != voice.Pcm.Stream || ReadPlayback(node) is null)
            throw new NotSupportedException("PCM continuation lost its original native playback.");
        return new(voice.Pcm.Capture(), _actor.GetPathTo(voice.Emitter).ToString(), voice.FollowEmitter);
    }

    public override void _Ready()
    {
        try { RestorePcmVoices(); }
        catch (Exception error) { _pcmRestoreFailure = error; GD.PushError($"OPENNV_PCM_RESTORE_FAILED {error}"); }
    }

    private void RestorePcmVoices()
    {
        foreach (var entry in _events?.Events.Where(entry => entry.End == FalloutAnimationSoundEnd.Active) ?? [])
        {
            if (entry.Playback is not { } saved) throw new InvalidDataException("Cold active sound has no PCM continuation.");
            var source = FalloutSoundRecordReader.Read(_records, entry.Sound) with { LogicalPath = entry.LogicalPath };
            var selected = FalloutAnimationSound.Select(source, entry.Variants, new(entry.RandomBefore), true, entry.StereoOutput);
            var emitter = _actor.GetNodeOrNull<Node3D>(saved.EmitterPath) ??
                throw new InvalidDataException("Cold PCM sound has no original source emitter.");
            if (!_streams.TryGetValue(entry.Path!, out var media))
            {
                media = NativeOwnedMediaLoader.LoadAudio(entry.Path!); _streams.Add(entry.Path!, media);
            }
            if (media is not AudioStreamWav wav) throw new InvalidDataException("Cold PCM sound has no decoded WAV.");
            var pcm = new NativeOwnedPcmStream(wav, saved.Samples.Loop, saved.Samples);
            Node node;
            if (source.IsTwoDimensional)
                node = new AudioStreamPlayer { Stream = pcm.Stream, PitchScale = selected.PitchScale, VolumeDb = selected.GainDb };
            else
            {
                var spatial = new AudioStreamPlayer3D
                {
                    Stream = pcm.Stream,
                    PitchScale = selected.PitchScale,
                    AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.Disabled,
                    MaxDistance = source.MaximumDistanceGameUnits * _unitsToMetres,
                    AreaMask = 0
                };
                node = spatial; _spatial.Add(spatial, selected);
            }
            TrackVoice(node, emitter, saved.Samples.Loop, entry.Sound, null, entry.Generation, saved.FollowEmitter, pcm);
            _voices[node].Releasing = saved.Samples.Releasing;
            if (node is AudioStreamPlayer3D positioned) ApplyListener(positioned, selected);
            PlayVoice(node);
        }
    }
}
