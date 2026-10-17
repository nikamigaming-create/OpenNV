using Godot;

namespace OpenNV.Runtime.Content;

/// <summary>One script-selected MUSC voice, using the normal owned-media loader.</summary>
internal sealed partial class NativeOwnedMusicPlayer(FalloutPluginStack records) : Node
{
    private FalloutMusicType? _source;
    private AudioStreamPlayer? _voice;
    private AudioStream? _stream;
    private string? _mediaHash;
    private double _duration, _position;
    private bool _active;
    internal FalloutFormKey? SourceForm => _source?.Form;
    internal bool Playing => _active && _voice?.Playing == true;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        if (_active) _voice!.Play((float)_position);
    }

    internal void Play(FalloutFormKey form) => Install(FalloutMusicType.Read(records.GetEffective(form)), null);

    internal void Restore(FalloutMusicPlaybackSnapshot snapshot) =>
        Install(FalloutMusicType.ValidateSnapshot(records, snapshot), snapshot);

    internal FalloutMusicPlaybackSnapshot? Capture()
    {
        if (_source is null) return null;
        var active = _active;
        var position = _position;
        if (_active && IsInsideTree())
        {
            // The audio thread can finish before the main-thread Finished signal.
            // Read the cursor first, then test the voice so a removed stream's
            // zero cursor cannot restart a completed stinger on cold load.
            position = _voice!.GetPlaybackPosition();
            active = _voice.Playing || _voice.StreamPaused;
            if (!active) position = _duration;
        }
        var snapshot = new FalloutMusicPlaybackSnapshot(_source.Form, _source.RecordSha256, _source.Path,
            _mediaHash, _duration, position, active);
        _ = FalloutMusicType.ValidateSnapshot(records, snapshot);
        return snapshot;
    }

    private void Install(FalloutMusicType source, FalloutMusicPlaybackSnapshot? snapshot)
    {
        AudioStream? stream = null;
        AudioStreamPlayer? voice = null;
        string? mediaHash = null;
        var duration = 0d;
        var active = source.Path is not null && (snapshot?.Playing ?? true);
        try
        {
            if (source.Path is { } path)
            {
                stream = NativeOwnedMediaLoader.LoadAudio(path);
                mediaHash = stream.GetMeta("opennv_owned_media_sha256").AsString();
                duration = stream.GetLength();
                if (!double.IsFinite(duration) || duration <= 0 ||
                    snapshot is not null && (!string.Equals(snapshot.MediaSha256, mediaHash, StringComparison.OrdinalIgnoreCase) || snapshot.DurationSeconds != duration))
                    throw new InvalidDataException("Saved music media or duration differs from the owned stream.");
                switch (stream)
                {
                    case AudioStreamMP3 mp3: mp3.Loop = source.Loop; break;
                    case AudioStreamOggVorbis ogg: ogg.Loop = source.Loop; break;
                    case AudioStreamWav wav:
                        wav.LoopMode = source.Loop ? AudioStreamWav.LoopModeEnum.Forward : AudioStreamWav.LoopModeEnum.Disabled;
                        if (source.Loop) { wav.LoopBegin = 0; wav.LoopEnd = checked((int)Math.Round(duration * wav.MixRate)); }
                        break;
                    default: throw new NotSupportedException("Music decoder returned an unowned stream type.");
                }
                if (active)
                {
                    voice = new AudioStreamPlayer
                    {
                        Name = "ScriptMusicVoice",
                        ProcessMode = ProcessModeEnum.Always,
                        Stream = stream,
                        VolumeDb = source.GainDb
                    };
                    voice.AddToGroup("opennv_music");
                    voice.SetMeta("opennv_music_form_key", source.Form.ToString());
                    var selectedVoice = voice;
                    voice.Finished += () =>
                    {
                        if (!ReferenceEquals(_voice, selectedVoice)) return;
                        _active = false; _position = _duration;
                        RetireVoice();
                        GD.Print($"OPENNV_SCRIPT_MUSIC_FINISHED music={source.Form}");
                    };
                    AddChild(voice);
                }
            }
            // Reading, decoding and snapshot validation precede replacement.
            RetireVoice();
            _source = source; _mediaHash = mediaHash; _duration = duration;
            _position = snapshot?.PositionSeconds ?? 0; _active = active;
            _voice = voice; _stream = active ? stream : null;
            if (!active) stream?.Dispose();
            stream = null; voice = null;
            if (_active && IsInsideTree()) _voice!.Play((float)_position);
            GD.Print($"OPENNV_SCRIPT_MUSIC_PLAY music={source.Form} path={source.Path} gainDb={source.GainDb:R} " +
                $"loop={source.Loop} playing={_active} position={_position:R} restored={snapshot is not null} parity=unverified");
        }
        catch
        {
            if (voice is not null) { voice.Stream = null; voice.Free(); }
            stream?.Dispose();
            throw;
        }
    }

    private void RetireVoice()
    {
        if (_voice is not null)
        {
            _voice.Stop(); _voice.Stream = null; _voice.QueueFree(); _voice = null;
        }
        _stream?.Dispose(); _stream = null;
    }

    public override void _ExitTree()
    {
        RetireVoice(); _source = null; _active = false;
    }
}
