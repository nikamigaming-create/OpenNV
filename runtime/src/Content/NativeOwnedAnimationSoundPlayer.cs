using System.Text;
using Godot;

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
    internal object? LastEvent { get; private set; }
    internal object State => new { eventCount = _eventCount, activeSpatial = _spatial.Count, last = LastEvent };

    internal NativeOwnedAnimationSoundPlayer(FalloutPluginStack records, RuntimeLiveContentSource content,
        Node3D actor, float unitsToMetres, FalloutSoundRandomState random)
    {
        if (!float.IsFinite(unitsToMetres) || unitsToMetres <= 0) throw new ArgumentOutOfRangeException(nameof(unitsToMetres));
        _records = records; _content = content; _actor = actor; _unitsToMetres = unitsToMetres; _random = random;
        Name = "OwnedAnimationSounds";
    }

    internal string Dispatch(string textKey)
    {
        LastEvent = null;
        var editorId = FalloutAnimationSound.EditorId(textKey);
        if (editorId is null) return "unbound-runtime-event";
        try
        {
            if (!_descriptors.TryGetValue(editorId, out var entry))
            {
                var rows = _records.EffectiveRecords("SOUN").Where(record => record.ReadSubrecords()
                    .Any(row => row.Signature == "EDID" && Encoding.ASCII.GetString(row.Data.Span).TrimEnd('\0')
                        .Equals(editorId, StringComparison.OrdinalIgnoreCase))).ToArray();
                if (rows.Length != 1) throw new InvalidDataException($"KF sound {editorId} has {rows.Length} winning SOUN owners.");
                var source = FalloutSoundRecordReader.Read(rows[0]);
                entry = (source, FalloutAnimationSound.Variants(source, source.HasExactFile ? [] : _content.ResourcePathsUnder(source.LogicalPath)));
                _descriptors.Add(editorId, entry);
            }
            var selected = FalloutAnimationSound.Select(entry.Source, entry.Variants, _random);
            var disposition = selected.Play ? selected.Unbound.Count == 0 ? "source-sound-playing" : "source-sound-dry-playing-partial" : "source-sound-chance-skipped";
            AudioStream? stream = null;
            if (selected.Play)
            {
                if (!_streams.TryGetValue(selected.Path!, out stream))
                    _streams.Add(selected.Path!, stream = NativeOwnedMediaLoader.LoadAudio(selected.Path!));
                if (selected.Source.IsTwoDimensional)
                {
                    var voice = new AudioStreamPlayer { Stream = stream, PitchScale = selected.PitchScale, VolumeDb = selected.GainDb };
                    AddChild(voice); voice.Finished += voice.QueueFree; voice.Play();
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
                    AddChild(voice); _spatial.Add(voice, selected);
                    voice.Finished += () => { _spatial.Remove(voice); voice.QueueFree(); };
                    ApplyListener(voice, selected); voice.Play();
                }
            }
            LastEvent = new
            {
                ordinal = ++_eventCount,
                textKey,
                disposition,
                selected,
                asset = stream?.GetMeta("opennv_owned_media_source").AsString(),
                sha256 = stream?.GetMeta("opennv_owned_media_sha256").AsString(),
                randomOwner = "authoritative-actor-stream-retail-sequence-unmatched"
            };
            return disposition;
        }
        catch (Exception error) when (error is IOException or NotSupportedException)
        {
            LastEvent = new { ordinal = ++_eventCount, textKey, disposition = "unbound-source-sound", error = error.Message };
            return "unbound-source-sound";
        }
    }

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
