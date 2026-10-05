using Godot;

namespace OpenNV.Runtime.Content;

internal sealed partial class NativeOwnedAnimationSoundPlayer
{
    private sealed record Voice(Node3D Emitter, FalloutSoundLoop Loop, AudioStream? OwnedStream, Action? Completed)
    {
        internal bool Releasing { get; set; }
        internal IDisposable? Registration { get; set; }
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
                var emitter = ResolveEmitter(separator < 0 ? "" : payload[(separator + 1)..].Trim());
                return Dispatch(soundPrefix + editorId, emitter, ownsLoopStop: true);
            }
            if (text.Equals(stopPrefix, StringComparison.OrdinalIgnoreCase) ||
                text.StartsWith(stopPrefix + " ", StringComparison.OrdinalIgnoreCase))
            {
                var emitter = ResolveEmitter(text[stopPrefix.Length..].Trim());
                var count = StopVoices(emitter);
                LastEvent = new { ordinal = ++_eventCount, textKey = text, disposition = "source-stop-sounds", voices = count };
                ObserveEvent();
                return "source-stop-sounds";
            }
            return "unbound-runtime-event";
        }
        catch (Exception error) when (error is IOException or InvalidDataException or NotSupportedException)
        {
            _unbound.Add(text + ":" + error.Message);
            LastEvent = new { ordinal = ++_eventCount, textKey = text, disposition = "unbound-source-sound", error = error.Message };
            ObserveEvent();
            return "unbound-source-sound";
        }
    }

    private Node3D ResolveEmitter(string name)
    {
        if (name.Length == 0) return this;
        if (_emitters.TryGetValue(name, out var cached) && GodotObject.IsInstanceValid(cached)) return cached;
        var nodes = _actor.FindChildren("*", "", true, false).OfType<Node3D>()
            .Where(node => node.GetMeta("opennv_nif_source_name", "").AsString() == name).ToArray();
        if (nodes.Length != 1) throw new NotSupportedException($"Source sound emitter {name} has {nodes.Length} native node owners.");
        return _emitters[name] = nodes[0];
    }

    private void TrackVoice(Node voice, Node3D emitter, FalloutSoundLoop loop, FalloutFormKey sound, Action? completed)
    {
        var stream = voice is AudioStreamPlayer3D spatial ? spatial.Stream : ((AudioStreamPlayer)voice).Stream;
        var state = new Voice(emitter, loop, loop.Mode == FalloutSoundLoopMode.None ? null : stream, completed);
        var reference = voice is AudioStreamPlayer3D ? NativeOwnedSoundVoice.Reference(_records, emitter) : null;
        state.Registration = _records.SoundVoices.Register(sound, reference, "source-animation-or-response",
            () => GodotObject.IsInstanceValid(voice) && (voice is AudioStreamPlayer3D positioned ? positioned.Playing : ((AudioStreamPlayer)voice).Playing),
            () => { StopVoice(voice); FinishVoice(voice); });
        _voices.Add(voice, state);
        // An effect or weapon can leave the tree before its sound completes.
        // Finished is not emitted when its emitter frees the child voice.
        voice.TreeExiting += () => ForgetVoice(voice);
        voice.SetMeta("opennv_sound_emitter", emitter.GetPath().ToString());
        voice.SetMeta("opennv_sound_loop_mode", loop.Mode.ToString());
    }

    private int StopVoices(Node3D emitter)
    {
        var stopped = 0;
        foreach (var (node, voice) in _voices.ToArray())
        {
            if (voice.Emitter != emitter || voice.Releasing) continue;
            stopped++;
            if (voice.Loop.Mode is FalloutSoundLoopMode.EnvelopeFast or FalloutSoundLoopMode.EnvelopeSlow)
            {
                var stream = (AudioStreamWav)voice.OwnedStream!;
                stream.LoopMode = AudioStreamWav.LoopModeEnum.Disabled;
                voice.Releasing = true;
                node.SetMeta("opennv_sound_envelope_releasing", true);
                if (voice.Loop.ReleasePosition(checked((uint)stream.MixRate)) is { } position)
                {
                    if (node is AudioStreamPlayer3D spatial) spatial.Play((float)position);
                    else ((AudioStreamPlayer)node).Play((float)position);
                }
            }
            else
            {
                StopVoice(node);
                FinishVoice(node);
            }
        }
        return stopped;
    }

    private static void StopVoice(Node node)
    {
        if (node is AudioStreamPlayer3D spatial) spatial.Stop();
        else ((AudioStreamPlayer)node).Stop();
    }

    private void FinishVoice(Node node, bool completed = true)
    {
        var completion = completed && _voices.TryGetValue(node, out var voice) ? voice.Completed : null;
        ForgetVoice(node);
        node.QueueFree();
        completion?.Invoke();
    }

    private void ForgetVoice(Node node)
    {
        if (node is AudioStreamPlayer3D spatialVoice) _spatial.Remove(spatialVoice);
        if (!_voices.Remove(node, out var voice)) return;
        voice.Registration?.Dispose();
        if (voice.OwnedStream is { } stream)
        {
            if (node is AudioStreamPlayer3D spatial) spatial.Stream = null;
            else ((AudioStreamPlayer)node).Stream = null;
            stream.Dispose();
        }
    }

    public override void _ExitTree()
    {
        _lostCaptureAtRetirement |= _spatial.Count != 0 || _voices.Count != 0 || _unbound.Count != 0;
        foreach (var node in _voices.Keys.ToArray())
        {
            StopVoice(node); FinishVoice(node, completed: false);
        }
        _spatial.Clear(); _emitters.Clear();
        foreach (var stream in _streams.Values) stream.Dispose();
        _streams.Clear();
    }
}
