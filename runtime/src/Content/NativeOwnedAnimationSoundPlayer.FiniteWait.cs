using Godot;

namespace OpenNV.Runtime.Content;

internal sealed partial class NativeOwnedAnimationSoundPlayer
{
    private FalloutFiniteSoundVoice? ReadFiniteVoice(Node node, Voice voice)
    {
        if (_lostCaptureAtRetirement || _retiredCaptureDiagnostic is not null ||
            _events?.CanAwaitNativeCompletion != true ||
            _unbound.Except(_events.PartialLanes, StringComparer.Ordinal).Any() ||
            !_voices.TryGetValue(node, out var bound) || !ReferenceEquals(bound, voice) ||
            voice.Generation is not { } generation || voice.Loop.Mode != FalloutSoundLoopMode.None || voice.Releasing ||
            !GodotObject.IsInstanceValid(node) || !node.IsInsideTree() ||
            (voice.Attachment is { } attachment ? !attachment.Alive :
                !GodotObject.IsInstanceValid(voice.Emitter) || !voice.Emitter.IsInsideTree())) return null;
        var entry = _events.Events.SingleOrDefault(entry => entry.Generation == generation);
        if (entry is not { End: FalloutAnimationSoundEnd.Active, Error: null }) return null;
        var playing = node is AudioStreamPlayer3D spatial ? spatial.Playing : ((AudioStreamPlayer)node).Playing;
        var stream = node is AudioStreamPlayer3D positioned ? positioned.Stream : ((AudioStreamPlayer)node).Stream;
        if (!playing || stream is null || !GodotObject.IsInstanceValid(stream) ||
            !double.IsFinite(stream.GetLength()) || stream.GetLength() <= 0 ||
            stream is AudioStreamWav { LoopMode: not AudioStreamWav.LoopModeEnum.Disabled } ||
            !stream.GetMeta("opennv_owned_media_sha256", "").AsString().Equals(entry.MediaSha256, StringComparison.OrdinalIgnoreCase)) return null;
        var proof = new FalloutFiniteSoundVoice(_nativeOwner, _events.Reference, generation,
            entry.Sound, entry.SoundSha256, entry.Path!, entry.MediaSha256!);
        proof.Validate(); return proof;
    }

    // This owner-scoped observation remains useful for procedure admission.
    // The global registry composes individual receipts across all sound owners.
    internal IReadOnlyList<FalloutFiniteSoundVoice>? PendingFiniteVoices
    {
        get
        {
            if (_events?.CanAwaitNativeCompletion != true || _voices.Count == 0 ||
                _events.Events.Count(entry => entry.End == FalloutAnimationSoundEnd.Active) != _voices.Count ||
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
}
