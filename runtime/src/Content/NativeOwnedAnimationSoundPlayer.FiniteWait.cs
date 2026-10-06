using Godot;

namespace OpenNV.Runtime.Content;

internal sealed partial class NativeOwnedAnimationSoundPlayer
{
    private void PlayVoice(Node node)
    {
        if (node is AudioStreamPlayer3D spatial)
        { spatial.Finished += () => FinishVoice(node); spatial.Play(); }
        else
        { var flat = (AudioStreamPlayer)node; flat.Finished += () => FinishVoice(node); flat.Play(); }
        var voice = _voices[node];
        if (voice.Attachment is null || voice.Generation is not { } generation) return;
        var playback = ReadPlayback(node);
        var stream = node is AudioStreamPlayer3D positioned ? positioned.Stream : ((AudioStreamPlayer)node).Stream;
        if (playback is null || !GodotObject.IsInstanceValid(playback) || stream is null) return;
        var events = _events ?? throw new InvalidDataException("Finite native playback lacks its original source generation.");
        var entry = events.Events.Single(entry => entry.Generation == generation);
        var proof = new FalloutFiniteSoundVoice(_nativeOwner, events.Reference, generation,
            entry.Sound, entry.SoundSha256, entry.Path!, entry.MediaSha256!);
        voice.CompletionWait = new(proof, node.GetInstanceId(), playback.GetInstanceId(), stream.GetInstanceId());
        _ = ReadFiniteVoice(node, voice);
    }

    private static AudioStreamPlayback? ReadPlayback(Node node) => node is AudioStreamPlayer3D spatial
        ? spatial.HasStreamPlayback() ? spatial.GetStreamPlayback() : null
        : ((AudioStreamPlayer)node).HasStreamPlayback() ? ((AudioStreamPlayer)node).GetStreamPlayback() : null;

    private FalloutFiniteSoundVoice? ReadFiniteVoice(Node node, Voice voice)
    {
        if (_lostCaptureAtRetirement || _retiredCaptureDiagnostic is not null ||
            _events?.CanAwaitNativeCompletion != true ||
            _unbound.Except(_events.PartialLanes, StringComparer.Ordinal).Any() ||
            !_voices.TryGetValue(node, out var bound) || !ReferenceEquals(bound, voice) ||
            voice.Generation is not { } generation || voice.Loop.Mode != FalloutSoundLoopMode.None || voice.Releasing ||
            voice.CompletionWait is null || !GodotObject.IsInstanceValid(node) || !node.IsInsideTree() || !node.CanProcess() ||
            (voice.Attachment is { } attachment ? !attachment.Alive :
                !GodotObject.IsInstanceValid(voice.Emitter) || !voice.Emitter.IsInsideTree())) return null;
        var entry = _events.Events.SingleOrDefault(entry => entry.Generation == generation);
        if (entry is not { End: FalloutAnimationSoundEnd.Active, Error: null }) return null;
        var playing = node is AudioStreamPlayer3D spatial ? spatial.Playing : ((AudioStreamPlayer)node).Playing;
        var paused = node is AudioStreamPlayer3D spatiallyPaused ? spatiallyPaused.StreamPaused : ((AudioStreamPlayer)node).StreamPaused;
        var stream = node is AudioStreamPlayer3D positioned ? positioned.Stream : ((AudioStreamPlayer)node).Stream;
        var playback = ReadPlayback(node);
        if (paused || playback is null || !GodotObject.IsInstanceValid(playback) ||
            stream is null || !GodotObject.IsInstanceValid(stream) ||
            !double.IsFinite(stream.GetLength()) || stream.GetLength() <= 0 ||
            stream is AudioStreamWav { LoopMode: not AudioStreamWav.LoopModeEnum.Disabled } ||
            !stream.GetMeta("opennv_owned_media_sha256", "").AsString().Equals(entry.MediaSha256, StringComparison.OrdinalIgnoreCase)) return null;
        var proof = new FalloutFiniteSoundVoice(_nativeOwner, _events.Reference, generation,
            entry.Sound, entry.SoundSha256, entry.Path!, entry.MediaSha256!);
        return voice.CompletionWait.Observe(proof, node.GetInstanceId(), playback.GetInstanceId(), stream.GetInstanceId(),
            playing, node is AudioStreamPlayer3D ? Engine.GetPhysicsFrames() : Engine.GetProcessFrames(), Time.GetTicksMsec());
    }

    // This owner-scoped observation remains useful for procedure admission.
    // The global registry composes individual receipts across all sound owners.
    private IReadOnlyList<FalloutFiniteSoundVoice>? PendingOwnFiniteVoices
    {
        get
        {
            if (_events?.CanAwaitNativeCompletion != true || _voices.Count == 0 ||
                _spatial.Keys.Any(node => !_voices.ContainsKey(node))) return null;
            var voices = new List<FalloutFiniteSoundVoice>();
            foreach (var (node, voice) in _voices)
            {
                if (ReadFiniteVoice(node, voice) is not { } proof) return null;
                voices.Add(proof);
            }
            return voices.AsReadOnly();
        }
    }

    internal IReadOnlyList<FalloutFiniteSoundVoice>? PendingFiniteVoices =>
        _events?.Events.Count(entry => entry.End == FalloutAnimationSoundEnd.Active) == _voices.Count
            ? PendingOwnFiniteVoices : null;

    // Empty local voices do not mean completion. This only allows a parent to
    // prepare a nonaudio copy when the entire shared ledger still has proven
    // finite media; its source/native registry must prove every active generation.
    internal bool CanAwaitFiniteCompletion => _events?.CanAwaitNativeCompletion == true &&
        !_lostCaptureAtRetirement && _retiredCaptureDiagnostic is null &&
        !_unbound.Except(_events.PartialLanes, StringComparer.Ordinal).Any() &&
        !_spatial.Keys.Any(node => !_voices.ContainsKey(node)) &&
        (_voices.Count == 0 || PendingOwnFiniteVoices?.Count == _voices.Count);
}
