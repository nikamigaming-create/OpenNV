using System.Security.Cryptography;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutNoActivationSoundSnapshot(FalloutFormKey Sound, string Sha256)
{
    internal void Validate()
    {
        if (Sound.ObjectId == 0 || string.IsNullOrWhiteSpace(Sound.OwnerPlugin) ||
            Sha256 is null || Sha256.Length != 64 || !Sha256.All(Uri.IsHexDigit))
            throw new InvalidDataException("Saved no-activation sound has no valid source identity.");
    }
}

// The selected SOUN is persistent session state. Playback is transient, uses
// the common owned audio owner, and is never reissued merely by restoring it.
internal sealed class FalloutNoActivationSound(FalloutPluginStack records, FalloutScriptSounds sounds)
{
    private FalloutNoActivationSoundSnapshot? _selection;
    private Lazy<string>? _default;
    private FalloutFormKey? _playingSound;
    private long? _voice;
    private long _attempts, _suppressed;
    internal string? LastError { get; private set; }
    internal string? LastDisposition { get; private set; }
    internal object State => new
    {
        selection = _selection,
        defaultBound = _default is not null,
        attempts = _attempts,
        suppressed = _suppressed,
        voice = _voice,
        lastDisposition = LastDisposition,
        error = LastError,
        persistence = "source-SOUN-and-winning-hash;transient-voice-not-replayed-cold",
        unbound = new[] { "retail-precache-and-voice-reuse", "matched-activation-eligibility-and-timing" }
    };

    internal IDisposable BindDefault(Func<string> ownedEditorId)
    {
        ArgumentNullException.ThrowIfNull(ownedEditorId);
        if (_default is not null) throw new InvalidOperationException("No-activation sound default already has an owner.");
        var binding = new Lazy<string>(ownedEditorId); _default = binding;
        return new Scope(() => { if (_default == binding) _default = null; });
    }

    internal void Set(FalloutFormKey sound)
    {
        var selected = Read(sound);
        _selection = selected;
        LastDisposition = "source-override-selected";
    }
    internal void Clear()
    {
        _selection = null;
        LastDisposition = "source-default-reset";
    }
    internal void RejectActivation(FalloutFormKey player)
    {
        try
        {
            _attempts = checked(_attempts + 1);
            var selected = _selection ?? Read(FalloutDialogueTopic.Find(records, "SOUN",
                (_default ?? throw new NotSupportedException("No-activation sound has no owned default association.")).Value).FormKey);
            if (_playingSound == selected.Sound && _voice is { } active && sounds.IsActive(active))
            {
                _suppressed = checked(_suppressed + 1); LastDisposition = "voice-still-active"; return;
            }
            sounds.Play(player, selected.Sound);
            _selection = selected;
            _playingSound = selected.Sound; _voice = sounds.LastRequest!.Id;
            LastDisposition = "source-feedback-requested";
        }
        catch (Exception error) { LastError = error.Message; throw; }
    }

    internal FalloutNoActivationSoundSnapshot? Capture() => _selection;
    internal void Restore(FalloutNoActivationSoundSnapshot? state)
    {
        if (state is not null)
        {
            state.Validate();
            var winning = Read(state.Sound);
            if (!StringComparer.OrdinalIgnoreCase.Equals(state.Sha256, winning.Sha256))
                throw new InvalidDataException("Saved no-activation sound differs from the winning source declaration.");
        }
        _selection = state; _playingSound = null; _voice = null;
    }
    private FalloutNoActivationSoundSnapshot Read(FalloutFormKey sound)
    {
        try
        {
            var record = records.GetEffective(sound);
            if (record.Signature != "SOUN") throw new InvalidDataException("No-activation sound argument is not a SOUN form.");
            _ = FalloutSoundRecordReader.Read(record);
            return new(sound, Convert.ToHexString(SHA256.HashData(record.ReadData())));
        }
        catch (FalloutPluginFormatException error) { throw new InvalidDataException(error.Message, error); }
        catch (KeyNotFoundException error) { throw new InvalidDataException(error.Message, error); }
    }
    private sealed class Scope(Action release) : IDisposable
    {
        private Action? _release = release;
        public void Dispose() { var release = _release; _release = null; release?.Invoke(); }
    }
}
