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
    private readonly FalloutAnimationSoundEvents? _events;
    private readonly Dictionary<string, (FalloutSoundRecord Source, IReadOnlyList<string> Variants, long Revision)> _descriptors = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, AudioStream> _streams = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<AudioStreamPlayer3D, FalloutAnimationSoundSelection> _spatial = [];
    private long _eventCount;
    private readonly SortedSet<string> _unbound = new(StringComparer.Ordinal);
    private bool _lostCaptureAtRetirement;
    private bool _retired;
    private readonly ulong _nativeOwner;
    internal IReadOnlyList<Node> ActiveNativeVoices => _voices.Keys.ToArray();
    private FalloutAnimationSoundCaptureDiagnostic? _retiredCaptureDiagnostic;
    internal FalloutAnimationSoundCaptureDiagnostic CaptureDiagnostic => _retiredCaptureDiagnostic ?? ReadCaptureDiagnostic(_retired);

    private FalloutAnimationSoundCaptureDiagnostic ReadCaptureDiagnostic(bool retired) => new(CanCaptureSilent, retired,
        _lostCaptureAtRetirement, _spatial.Count, _voices.Count, _eventCount, Array.AsReadOnly(_unbound.ToArray()));
    private bool HasUnreceiptedFault => _events is null ? _unbound.Count != 0 :
        !_events.CanCapture || _unbound.Except(_events.OwnedLanes, StringComparer.Ordinal).Any();
    internal bool CanCaptureSilent => !_lostCaptureAtRetirement &&
        _voices.Values.All(voice => voice.Pcm is not null && voice.Completed is null) && !HasUnreceiptedFault;
    internal IReadOnlyCollection<string> Unbound => _unbound;
    internal IEnumerable<FalloutSoundRecord> Sources => _descriptors.Values.Select(entry => entry.Source);
    internal static Action<object>? SoundObserver { get; set; }
    internal object? LastEvent { get; private set; }
    internal object State => new
    {
        eventCount = _eventCount,
        ownerRetired = _retired,
        activeSpatial = _spatial.Count,
        activeVoices = _voices.Count,
        unbound = _unbound.ToArray(),
        last = LastEvent,
        sourceEvents = _events?.Events,
        finiteLifetime = _voices.Values.Where(voice => voice.Attachment is not null).Select(voice => new
        {
            generation = voice.Generation,
            nativeEmitter = voice.Attachment!.EmitterNativeOwner,
            emitter = voice.Attachment.EmitterPath,
            followsEmitter = voice.Attachment.FollowEmitter,
            retired = voice.Attachment.EmitterRetired,
            lastRealPosition = new[] { voice.Attachment.LastRealPosition.X, voice.Attachment.LastRealPosition.Y, voice.Attachment.LastRealPosition.Z }
        }).ToArray()
    };

    internal NativeOwnedAnimationSoundPlayer(FalloutPluginStack records, RuntimeLiveContentSource content,
        Node3D actor, float unitsToMetres, FalloutSoundRandomState random, FalloutAnimationSoundEvents? events = null)
    {
        if (!float.IsFinite(unitsToMetres) || unitsToMetres <= 0) throw new ArgumentOutOfRangeException(nameof(unitsToMetres));
        _records = records; _content = content; _actor = actor; _unitsToMetres = unitsToMetres; _random = random; _events = events;
        _nativeOwner = actor.GetInstanceId();
        if (_events is not null)
        {
            _events.ValidateMedia(content);
            _unbound.UnionWith(_events.OwnedLanes);
        }
        Name = "OwnedAnimationSounds";
    }

    internal string Dispatch(string textKey) => Dispatch(textKey, this, ownsLoopStop: true);

    internal string DispatchSound(FalloutFormKey form, Node3D? emitter = null, Action? completed = null) =>
        Dispatch("SOUN:" + form, emitter ?? this, ownsLoopStop: true, form, completed);

    private string Dispatch(string textKey, Node3D emitter, bool ownsLoopStop, FalloutFormKey? form = null, Action? completed = null, bool followEmitter = true)
    {
        LastEvent = null;
        long? generation = null;
        try
        {
            var editorId = form?.ToString() ?? FalloutAnimationSound.EditorId(textKey);
            if (editorId is null) return "unbound-runtime-event";
            if (!_descriptors.TryGetValue(editorId, out var entry) || entry.Revision != _records.SoundPaths.Revision(entry.Source.FormKey))
            {
                var key = form ?? FalloutSoundRecordReader.Find(_records, editorId).FormKey;
                var path = _records.SoundPaths.Read(key);
                var source = FalloutSoundRecordReader.Read(_records, key);
                entry = (source, FalloutAnimationSound.Variants(source, source.HasExactFile ? [] : _content.ResourcePathsUnder(source.LogicalPath)), path.Revision);
                _descriptors[editorId] = entry;
            }
            var stereoOutput = AudioServer.GetSpeakerMode() == AudioServer.SpeakerMode.ModeStereo;
            var selected = FalloutAnimationSound.Select(entry.Source, entry.Variants, _random, ownsLoopStop, stereoOutput);
            generation = _events?.Begin(_records, selected, textKey, stereoOutput, entry.Variants);
            foreach (var lane in selected.Unbound) _unbound.Add(selected.Source.FormKey + ":" + lane);
            var disposition = selected.Play ? selected.Unbound.Count == 0 ? "source-sound-playing" : "source-sound-dry-playing-partial" : "source-sound-chance-skipped";
            AudioStream? stream = null;
            NativeOwnedPcmStream? pcm = null;
            if (selected.Play)
            {
                if (!_streams.TryGetValue(selected.Path!, out stream))
                {
                    stream = NativeOwnedMediaLoader.LoadAudio(selected.Path!);
                    if (stream is AudioStreamWav wav) wav.LoopMode = AudioStreamWav.LoopModeEnum.Disabled;
                    _streams.Add(selected.Path!, stream);
                }
                if (generation is { } mediaGeneration)
                    _events!.BindMedia(mediaGeneration, stream!.GetMeta("opennv_owned_media_sha256").AsString());
                var loop = FalloutSoundLoop.Read(selected.Source);
                if (loop.Mode != FalloutSoundLoopMode.None)
                {
                    if (stream is not AudioStreamWav wav) throw new NotSupportedException("Source PCM loops require decoded owned WAV samples.");
                    pcm = new(wav, loop); stream = pcm.Stream;
                }
                if (selected.Source.IsTwoDimensional)
                {
                    var voice = new AudioStreamPlayer { Stream = stream, PitchScale = selected.PitchScale, VolumeDb = selected.GainDb };
                    TrackVoice(voice, emitter, loop, selected.Source.FormKey, completed, generation, followEmitter, pcm);
                    PlayVoice(voice);
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
                    _spatial.Add(voice, selected);
                    TrackVoice(voice, emitter, loop, selected.Source.FormKey, completed, generation, followEmitter, pcm);
                    ApplyListener(voice, selected); PlayVoice(voice);
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
            if (!selected.Play) completed?.Invoke();
            return disposition;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or NotSupportedException)
        {
            _events?.Fail(generation, error.Message, textKey);
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
        foreach (var (voice, selected) in _spatial.Where(pair => _voices[pair.Key].Attachment is null)) ApplyListener(voice, selected);
    }

    private void ApplyListener(AudioStreamPlayer3D voice, FalloutAnimationSoundSelection selected)
    {
        var listener = voice.GetViewport().GetCamera3D();
        // No listener is not zero distance: keep the unresolved lane silent.
        voice.VolumeDb = listener is null ? float.NegativeInfinity : selected.GainDb +
            selected.Source.AttenuationDbAtDistanceGameUnits(voice.GlobalPosition.DistanceTo(listener.GlobalPosition) / _unitsToMetres);
        voice.SetMeta("opennv_sound_listener", listener?.GetPath().ToString() ?? "unbound");
    }
}
