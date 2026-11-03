using Godot;
using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.Content;

internal sealed partial class NativeOwnedAnimationSoundPlayer
{
    private Exception? _pcmRestoreFailure;
    private bool _referencePcmRestored;
    internal void RequirePcmRestored()
    {
        if (SourcePosePending && !_sourcePcmPublished)
            throw new InvalidOperationException("Player PCM restoration has not reached source pose publication.");
        if (_referenceLoopOwner && !_referencePcmRestored)
            throw new InvalidOperationException("Placed-reference PCM restoration has not reached its parent publication boundary.");
        if (_pcmRestoreFailure is { } failure) throw new InvalidDataException("Saved audio could not be restored.", failure);
    }
    private FalloutAnimationSoundPlaybackDiagnostic ObservePcmVoice(Node node, Voice voice) =>
        FalloutAnimationSoundEvents.ObservePlayback(() => CapturePcmVoice(node, voice));

    private FalloutAnimationSoundPlaybackSnapshot CapturePcmVoice(Node node, Voice voice)
    {
        if (!_voices.TryGetValue(node, out var current) || !ReferenceEquals(current, voice) ||
            voice.Pcm is null || voice.Completed is not null || !GodotObject.IsInstanceValid(node) || !node.IsInsideTree() ||
            !GodotObject.IsInstanceValid(voice.Emitter) || !voice.Emitter.IsInsideTree())
            throw new NotSupportedException("PCM continuation lost its native voice or source emitter.");
        var stream = node is AudioStreamPlayer3D spatial ? spatial.Stream : ((AudioStreamPlayer)node).Stream;
        if (stream != voice.Pcm.Stream || ReadPlayback(node) is null)
            throw new NotSupportedException("PCM continuation lost its original native playback.");
        var sourceRoot = voice.SourceScene?.Root ?? _actor;
        var path = sourceRoot.GetPathTo(voice.Emitter).ToString();
        var bone = RuntimeNativeNifSoundEmitters.CaptureBone(sourceRoot, voice.Emitter, _events?.Identity.Source);
        if (bone is not null) _events!.RequireEnabledSourceEmitter();
        else if (RuntimeNativeNifSoundEmitters.HasAnonymousPath(path))
            throw new NotSupportedException("Anonymous source sound emitter has no typed continuation identity.");
        return new(voice.Pcm.Capture(), path, voice.FollowEmitter, bone, voice.SourceScene?.Identity);
    }

    public override void _Ready()
    {
        if (!_referenceLoopOwner && !_sourcePoseBoundary) RestoreOwnedPcm();
    }
    internal void RestoreReferencePcm()
    {
        if (!_referenceLoopOwner) throw new InvalidOperationException("PCM parent restoration has no placed-reference producer.");
        if (_referencePcmRestored) return;
        _referencePcmRestored = true;
        RestoreOwnedPcm();
    }
    private void RestoreOwnedPcm()
    {
        try { RestorePcmVoices(); }
        catch (Exception error) { _pcmRestoreFailure = error; GD.PushError($"OPENNV_PCM_RESTORE_FAILED {error}"); }
    }

    private void RestorePcmVoices()
    {
        foreach (var entry in _events?.ActiveEvents ?? [])
        {
            // A still-live PCM owner already supplies the exact sample clock;
            // do not create a second voice from its earlier saved snapshot.
            if (_events!.HasLivePlayback(entry.Generation)) continue;
            if (entry.Playback is not { } saved)
            {
                // A finite voice may outlive its streamed-out emitter in the
                // source-graph audio host. A new presentation owner must not
                // restart that same generation as a cold PCM continuation.
                // Admit it only with the live native/source/media receipt;
                // an absent or ambiguous receipt still fails closed.
                var live = _records.SoundVoices.PendingFiniteSourceVoices(_events!.Identity);
                if (live is not null && live.Count(proof => proof.Generation == entry.Generation &&
                    proof.Reference == _events.Identity && proof.Sound == entry.Sound &&
                    proof.SoundSha256 == entry.SoundSha256 && proof.Path == entry.Path &&
                    proof.MediaSha256 == entry.MediaSha256) == 1)
                    continue;
                throw new InvalidDataException($"Active sound has no stored PCM continuation: reference={_events!.Identity} " +
                    $"sound={entry.Sound} generation={entry.Generation} liveCaptureBound={_events.HasPlaybackBinding(entry.Generation)}.");
            }
            var source = FalloutSoundRecordReader.Read(_records, entry.Sound) with { LogicalPath = entry.LogicalPath };
            NativeOwnedSoundSourceScene? sourceScene = null;
            if (_sourceScenes is not null)
            {
                if (saved.SourceScene is not { } sceneIdentity)
                    throw new NotSupportedException("Player PCM continuation has no retained source-scene identity.");
                sourceScene = _sourceScenes(sceneIdentity);
                if (sourceScene.Identity != sceneIdentity)
                    throw new InvalidDataException("PCM resolver changed its retained source-scene identity.");
            }
            else if (saved.SourceScene is not null)
                throw new NotSupportedException("PCM source-scene continuation has no matching presentation owner.");
            sourceScene?.RequirePublishedPose?.Invoke();
            var selected = FalloutAnimationSound.Select(source, entry.Variants, new(entry.RandomBefore), true, entry.StereoOutput);
            var sourceRoot = sourceScene?.Root ?? _actor;
            Node3D emitter;
            if (saved.SourceBone is { } bone)
            {
                _events!.RequireEnabledSourceEmitter();
                emitter = RuntimeNativeNifSoundEmitters.RestoreBone(sourceRoot, _events.Reference, bone, sourceScene is null ? _emitters : SceneEmitterCache(sourceScene));
            }
            else
            {
                if (RuntimeNativeNifSoundEmitters.HasAnonymousPath(saved.EmitterPath))
                    throw new NotSupportedException("Legacy anonymous sound emitter has no provable source continuation.");
                emitter = sourceRoot.GetNodeOrNull<Node3D>(saved.EmitterPath) ??
                    throw new InvalidDataException("Cold PCM sound has no original source emitter.");
                RuntimeNativeNifSoundEmitters.RequireNoAnimationObject(sourceRoot, emitter);
            }
            if (!_streams.TryGetValue(entry.Path!, out var media))
            {
                media = NativeOwnedMediaLoader.LoadAudio(entry.Path!); _streams.Add(entry.Path!, media);
            }
            var pcm = new NativeOwnedPcmStream(media, saved.Samples.Loop, saved.Samples);
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
            if (sourceScene is not null) _emitterScenes[emitter] = sourceScene;
            TrackVoice(node, emitter, saved.Samples.Loop, entry.Sound, null, entry.Generation, saved.FollowEmitter, pcm);
            _voices[node].Releasing = saved.Samples.Releasing;
            if (node is AudioStreamPlayer3D positioned) ApplyListener(positioned, selected);
            PlayVoice(node);
        }
    }
}
