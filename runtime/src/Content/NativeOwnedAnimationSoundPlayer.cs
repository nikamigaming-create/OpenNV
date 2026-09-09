using Godot;
using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.Content;

/// <summary>Consumes ordered KF events without owning the actor's animation clock.</summary>
internal sealed partial class NativeOwnedAnimationSoundPlayer : Node3D
{
    private readonly FalloutPluginStack _records;
    private readonly RuntimeLiveContentSource _content;
    private readonly Node3D _actor;
    private readonly float _unitsToMetres;
    private readonly FalloutSoundRandomState _random;
    private readonly Dictionary<string, (FalloutSoundRecord Source, IReadOnlyList<string> Variants)> _descriptors = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, AudioStream> _streams = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<AudioStreamPlayer3D, FalloutAnimationSoundSelection> _spatial = [];
    private long _eventCount;
    private readonly SortedSet<string> _unbound = new(StringComparer.Ordinal);
    internal IReadOnlyCollection<string> Unbound => _unbound;
    internal IEnumerable<FalloutSoundRecord> Sources => _descriptors.Values.Select(entry => entry.Source);
    internal static Action<object>? SoundObserver { get; set; }
    internal object? LastEvent { get; private set; }
    internal object State => new
    {
        eventCount = _eventCount,
        activeSpatial = _spatial.Count,
        activeVoices = _voices.Count,
        unbound = _unbound.ToArray(),
        last = LastEvent
    };

    internal NativeOwnedAnimationSoundPlayer(FalloutPluginStack records, RuntimeLiveContentSource content,
        Node3D actor, float unitsToMetres, FalloutSoundRandomState random)
    {
        if (!float.IsFinite(unitsToMetres) || unitsToMetres <= 0) throw new ArgumentOutOfRangeException(nameof(unitsToMetres));
        _records = records; _content = content; _actor = actor; _unitsToMetres = unitsToMetres; _random = random;
        Name = "OwnedAnimationSounds";
    }

    internal string Dispatch(string textKey) => Dispatch(textKey, this, ownsLoopStop: false);

    internal string DispatchSound(FalloutFormKey form, Node3D? emitter = null) => Dispatch("SOUN:" + form, emitter ?? this, ownsLoopStop: false, form);

    private string Dispatch(string textKey, Node3D emitter, bool ownsLoopStop, FalloutFormKey? form = null)
    {
        LastEvent = null;
        try
        {
            var editorId = form?.ToString() ?? FalloutAnimationSound.EditorId(textKey);
            if (editorId is null) return "unbound-runtime-event";
            if (!_descriptors.TryGetValue(editorId, out var entry))
            {
                var source = FalloutSoundRecordReader.Read(form is { } key ? _records.GetEffective(key) : FalloutSoundRecordReader.Find(_records, editorId));
                entry = (source, FalloutAnimationSound.Variants(source, source.HasExactFile ? [] : _content.ResourcePathsUnder(source.LogicalPath)));
                _descriptors.Add(editorId, entry);
            }
            var selected = FalloutAnimationSound.Select(entry.Source, entry.Variants, _random, ownsLoopStop,
                stereoOutput: AudioServer.GetSpeakerMode() == AudioServer.SpeakerMode.ModeStereo);
            foreach (var lane in selected.Unbound) _unbound.Add(selected.Source.FormKey + ":" + lane);
            var disposition = selected.Play ? selected.Unbound.Count == 0 ? "source-sound-playing" : "source-sound-dry-playing-partial" : "source-sound-chance-skipped";
            AudioStream? stream = null;
            if (selected.Play)
            {
                if (!_streams.TryGetValue(selected.Path!, out stream))
                {
                    stream = NativeOwnedMediaLoader.LoadAudio(selected.Path!);
                    if (stream is AudioStreamWav wav) wav.LoopMode = AudioStreamWav.LoopModeEnum.Disabled;
                    _streams.Add(selected.Path!, stream);
                }
                var loop = FalloutSoundLoop.Read(selected.Source);
                if (loop.Mode != FalloutSoundLoopMode.None) stream = CreateLoopStream(stream, loop);
                if (selected.Source.IsTwoDimensional)
                {
                    var voice = new AudioStreamPlayer { Stream = stream, PitchScale = selected.PitchScale, VolumeDb = selected.GainDb };
                    AddChild(voice); TrackVoice(voice, emitter, loop);
                    voice.Finished += () => FinishVoice(voice); voice.Play();
                }
                else
                {
                    var voice = new AudioStreamPlayer3D
                    {
                        Stream = stream,
                        PitchScale = selected.PitchScale,
                        AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.Disabled,
                        MaxDistance = selected.Source.MaximumDistanceGameUnits * _unitsToMetres,
                        AreaMask = 0
                    };
                    emitter.AddChild(voice); _spatial.Add(voice, selected); TrackVoice(voice, emitter, loop);
                    voice.Finished += () => { _spatial.Remove(voice); FinishVoice(voice); };
                    ApplyListener(voice, selected); voice.Play();
                }
            }
            LastEvent = new
            {
                ordinal = ++_eventCount,
                textKey,
                disposition,
                selected,
                outputSpeakerMode = AudioServer.GetSpeakerMode().ToString(),
                asset = stream?.GetMeta("opennv_owned_media_source").AsString(),
                sha256 = stream?.GetMeta("opennv_owned_media_sha256").AsString(),
                randomOwner = "authoritative-source-owner-stream-retail-sequence-unmatched"
            };
            ObserveEvent();
            return disposition;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or NotSupportedException)
        {
            _unbound.Add(textKey + ":" + error.Message);
            LastEvent = new { ordinal = ++_eventCount, textKey, disposition = "unbound-source-sound", error = error.Message };
            ObserveEvent();
            return "unbound-source-sound";
        }
    }

    internal string Dispatch(FalloutNifTextKeyEvent key)
    {
        var dispositions = new List<string>();
        foreach (var value in key.Text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Select(value => value.Trim()))
        {
            var structural = value.Equals("start", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("end", StringComparison.OrdinalIgnoreCase);
            dispositions.Add(structural ? "source-sequence-boundary" : DispatchNifEvent(value));
        }
        return string.Join(';', dispositions);
    }

    private void ObserveEvent() => SoundObserver?.Invoke(new
    {
        owner = _actor.GetMeta("opennv_reference_form_key", "unbound").AsString(),
        observation = LastEvent
    });

    public override void _Process(double delta)
    {
        foreach (var (voice, selected) in _spatial) ApplyListener(voice, selected);
    }

    private void ApplyListener(AudioStreamPlayer3D voice, FalloutAnimationSoundSelection selected)
    {
        var listener = _actor.GetViewport().GetCamera3D();
        // No listener is not zero distance: keep the unresolved lane silent.
        voice.VolumeDb = listener is null ? float.NegativeInfinity : selected.GainDb +
            selected.Source.AttenuationDbAtDistanceGameUnits(voice.GlobalPosition.DistanceTo(listener.GlobalPosition) / _unitsToMetres);
        voice.SetMeta("opennv_sound_listener", listener?.GetPath().ToString() ?? "unbound");
    }
}
