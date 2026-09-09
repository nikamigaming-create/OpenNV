using Godot;

namespace OpenNV.Runtime.Content;

internal sealed partial class NativeOwnedAnimationSoundPlayer
{
    private sealed record Voice(Node3D Emitter, FalloutSoundLoop Loop, AudioStream? OwnedStream)
    {
        internal bool Releasing { get; set; }
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

    private static AudioStream CreateLoopStream(AudioStream source, FalloutSoundLoop loop)
    {
        if (source is not AudioStreamWav wav)
            throw new NotSupportedException("Source loop/envelope playback requires a decoded WAV sample clock.");
        var frames = Math.Round(wav.GetLength() * wav.MixRate);
        if (loop.Start >= frames || loop.End > frames)
            throw new InvalidDataException("SOUN loop region exceeds the decoded owned WAV.");
        // The decoder cache remains immutable. Every envelope owns its loop
        // switch so releasing one voice cannot release another instance.
        var result = (AudioStreamWav)wav.Duplicate();
        result.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
        result.LoopBegin = checked((int)loop.Start);
        result.LoopEnd = loop.End == 0 ? checked((int)frames) : checked((int)loop.End);
        return result;
    }

    private void TrackVoice(Node voice, Node3D emitter, FalloutSoundLoop loop)
    {
        var stream = voice is AudioStreamPlayer3D spatial ? spatial.Stream : ((AudioStreamPlayer)voice).Stream;
        _voices.Add(voice, new(emitter, loop, loop.Mode == FalloutSoundLoopMode.None ? null : stream));
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
                if (node is AudioStreamPlayer3D spatial) { spatial.Stop(); _spatial.Remove(spatial); }
                else ((AudioStreamPlayer)node).Stop();
                FinishVoice(node);
            }
        }
        return stopped;
    }

    private void FinishVoice(Node node)
    {
        if (!_voices.Remove(node, out var voice)) return;
        if (voice.OwnedStream is { } stream)
        {
            if (node is AudioStreamPlayer3D spatial) spatial.Stream = null;
            else ((AudioStreamPlayer)node).Stream = null;
            stream.Dispose();
        }
        node.QueueFree();
    }

    public override void _ExitTree()
    {
        foreach (var node in _voices.Keys.ToArray())
        {
            if (node is AudioStreamPlayer3D spatial) spatial.Stop();
            else ((AudioStreamPlayer)node).Stop();
            FinishVoice(node);
        }
        _spatial.Clear(); _emitters.Clear();
        foreach (var stream in _streams.Values) stream.Dispose();
        _streams.Clear();
    }
}
