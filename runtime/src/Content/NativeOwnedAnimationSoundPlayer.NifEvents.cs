using Godot;
using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.Content;

internal sealed partial class NativeOwnedAnimationSoundPlayer
{
    private sealed record Voice(Node3D Emitter, bool FollowEmitter, FalloutSoundLoop Loop, AudioStream? OwnedStream, Action? Completed, long? Generation)
    {
        internal NativeOwnedPcmStream? Pcm { get; init; }
        internal bool Releasing { get; set; }
        internal IDisposable? Registration { get; set; }
        internal NativeOwnedFiniteSoundHost.Attachment? Attachment { get; set; }
        internal FalloutFiniteSoundCompletionWait? CompletionWait { get; set; }
        internal NativeOwnedFiniteSoundSaveDrain? SaveDrain { get; set; }
    }
    private readonly Dictionary<Node, Voice> _voices = [];
    private readonly Dictionary<string, Node3D> _emitters = new(StringComparer.Ordinal);

    private string DispatchNifEvent(string text)
    {
        try
        {
            const string soundPrefix = "Sound:";
            const string stopPrefix = "Enum: StopSounds";
            if (text.StartsWith(soundPrefix, StringComparison.OrdinalIgnoreCase))
            {
                var payload = text[soundPrefix.Length..].Trim();
                var separator = payload.IndexOfAny([' ', '\t']);
                var editorId = separator < 0 ? payload : payload[..separator];
                var source = FalloutSoundRecordReader.Read(_records, FalloutSoundRecordReader.Find(_records, editorId).FormKey);
                var selected = ResolveEmitter(separator < 0 ? "" : payload[(separator + 1)..].Trim(), source.IsLooping);
                return Dispatch(soundPrefix + editorId, selected.Emitter, ownsLoopStop: true, followEmitter: selected.FollowEmitter);
            }
            if (text.Equals(stopPrefix, StringComparison.OrdinalIgnoreCase) ||
                text.StartsWith(stopPrefix + " ", StringComparison.OrdinalIgnoreCase))
            {
                var emitter = ResolveEmitter(text[stopPrefix.Length..].Trim()).Emitter;
                var count = StopVoices(emitter);
                LastEvent = new { ordinal = ++_eventCount, textKey = text, disposition = "source-stop-sounds", voices = count };
                ObserveEvent();
                return "source-stop-sounds";
            }
            return "unbound-runtime-event";
        }
        catch (Exception error) when (error is IOException or InvalidDataException or NotSupportedException)
        {
            _events?.Fail(null, error.Message, text);
            _unbound.Add(text + ":" + error.Message);
            LastEvent = new { ordinal = ++_eventCount, textKey = text, disposition = "unbound-source-sound", error = error.Message };
            ObserveEvent();
            return "unbound-source-sound";
        }
    }

    internal (Node3D Emitter, bool FollowEmitter) ResolveEmitter(string name, bool sourceLoop = false)
        => RuntimeNativeNifSoundEmitters.Resolve(_actor, name, sourceLoop, _emitters);

    private void TrackVoice(Node voice, Node3D emitter, FalloutSoundLoop loop, FalloutFormKey sound, Action? completed, long? generation, bool followEmitter,
        NativeOwnedPcmStream? pcm = null)
    {
        var stream = voice is AudioStreamPlayer3D spatial ? spatial.Stream : ((AudioStreamPlayer)voice).Stream;
        var state = new Voice(emitter, followEmitter, loop, loop.Mode == FalloutSoundLoopMode.None ? null : stream, completed, generation) { Pcm = pcm };
        var entry = generation is { } boundGeneration ? _events!.Events.Single(value => value.Generation == boundGeneration) : null;
        var retain = loop.Mode == FalloutSoundLoopMode.None && entry is { End: FalloutAnimationSoundEnd.Active, Played: true } &&
            stream is not null && double.IsFinite(stream.GetLength()) && stream.GetLength() > 0 &&
            stream.GetMeta("opennv_owned_media_sha256", "").AsString().Equals(entry.MediaSha256, StringComparison.OrdinalIgnoreCase);
        try
        {
            if (!followEmitter && !retain)
                throw new NotSupportedException("Positional-only source sound lacks its retained finite source generation.");
            if (retain)
                state.Attachment = NativeOwnedFiniteSoundHost.Attach(_records, _events!, generation!.Value, voice, emitter, loop,
                    () => { StopVoice(voice); FinishVoice(voice, FalloutAnimationSoundEnd.Cancelled); },
                    () => { if (voice is AudioStreamPlayer3D spatialVoice) ApplyListener(spatialVoice, _spatial[spatialVoice]); }, followEmitter);
            else if (voice is AudioStreamPlayer3D) emitter.AddChild(voice);
            else AddChild(voice);
        }
        catch { if (voice is AudioStreamPlayer3D spatialVoice) _spatial.Remove(spatialVoice); voice.Free(); throw; }
        var reference = voice is AudioStreamPlayer3D && followEmitter ? NativeOwnedSoundVoice.Reference(_records, emitter) : null;
        state.Registration = _records.SoundVoices.Register(sound, reference, "source-animation-or-response",
            () => GodotObject.IsInstanceValid(voice) && (voice is AudioStreamPlayer3D positioned ? positioned.Playing : ((AudioStreamPlayer)voice).Playing),
            () => { StopVoice(voice); FinishVoice(voice, FalloutAnimationSoundEnd.SourceStopped); },
            () => { StopVoice(voice); FinishVoice(voice, FalloutAnimationSoundEnd.Cancelled); },
            _events?.Reference, () => ReadFiniteVoice(voice, state), () => state.CompletionWait?.State,
            () => PrepareFiniteSaveDrain(voice, state),
            pcm is null ? null : () => CapturePcmVoice(voice, state).Samples is not null);
        _voices.Add(voice, state);
        // Unexpected retirement of the actual audio node remains cancellation.
        // Retiring only its finite source attachment does not emit this signal.
        voice.TreeExiting += () =>
        {
            if (_voices.TryGetValue(voice, out var active))
            {
                _lostCaptureAtRetirement = true;
                if (active.Generation is { } id) _events!.Cancel(id, "Emitter retired before source completion.");
                _retiredCaptureDiagnostic ??= ReadCaptureDiagnostic(true);
            }
            ForgetVoice(voice);
            DisposeRetiredStreams();
        };
        voice.SetMeta("opennv_sound_emitter", emitter.GetPath().ToString());
        voice.SetMeta("opennv_sound_loop_mode", loop.Mode.ToString());
    }

    private int StopVoices(Node3D emitter)
    {
        var stopped = 0;
        foreach (var (node, voice) in _voices.ToArray())
        {
            if (!voice.FollowEmitter || voice.Emitter != emitter || voice.Releasing) continue;
            stopped++;
            if (voice.Loop.Mode is FalloutSoundLoopMode.EnvelopeFast or FalloutSoundLoopMode.EnvelopeSlow)
            {
                voice.Pcm!.ReleaseEnvelope();
                voice.Releasing = true;
                node.SetMeta("opennv_sound_envelope_releasing", true);
            }
            else
            {
                StopVoice(node);
                FinishVoice(node, FalloutAnimationSoundEnd.SourceStopped);
            }
        }
        return stopped;
    }

    private static void StopVoice(Node node)
    {
        if (node is AudioStreamPlayer3D spatial) spatial.Stop();
        else ((AudioStreamPlayer)node).Stop();
    }

    private void FinishVoice(Node node, FalloutAnimationSoundEnd end = FalloutAnimationSoundEnd.NativeFinished)
    {
        if (!_voices.TryGetValue(node, out var voice)) return;
        var completed = end is FalloutAnimationSoundEnd.NativeFinished or FalloutAnimationSoundEnd.SourceStopped;
        if (voice.Generation is { } generation)
        {
            if (end == FalloutAnimationSoundEnd.NativeFinished) voice.SaveDrain?.NativeFinished();
            if (completed) _events!.Complete(generation, end);
            else
            {
                _lostCaptureAtRetirement = true;
                _events!.Cancel(generation, "Native sound graph or voice retired before source completion.");
            }
        }
        var completion = completed ? voice.Completed : null;
        ForgetVoice(node);
        node.QueueFree();
        DisposeRetiredStreams();
        completion?.Invoke();
    }

    private void ForgetVoice(Node node)
    {
        if (node is AudioStreamPlayer3D spatialVoice) _spatial.Remove(spatialVoice);
        if (!_voices.Remove(node, out var voice)) return;
        voice.Registration?.Dispose();
        voice.Attachment?.Dispose();
        if (voice.OwnedStream is { } stream)
        {
            if (node is AudioStreamPlayer3D spatial) spatial.Stream = null;
            else ((AudioStreamPlayer)node).Stream = null;
            stream.Dispose();
        }
    }

    private void DisposeRetiredStreams()
    {
        if (!_retired || _voices.Count != 0) return;
        _emitters.Clear();
        foreach (var stream in _streams.Values) stream.Dispose();
        _streams.Clear();
    }

    public override void _ExitTree()
    {
        _retired = true;
        // Only a proven finite, already-playing source generation has an
        // independent native node. Loops and other retirements still refuse.
        foreach (var (node, voice) in _voices.ToArray())
        {
            if (voice.Attachment?.Alive == true) continue;
            StopVoice(node); FinishVoice(node, FalloutAnimationSoundEnd.Cancelled);
        }
        if (_lostCaptureAtRetirement) _retiredCaptureDiagnostic ??= ReadCaptureDiagnostic(true);
        DisposeRetiredStreams();
    }
}
