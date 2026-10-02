namespace OpenNV.Runtime.Content;

// One transient SOUN instance registry per loaded graph. Source identity and
// reference attachment are separate from the command's caller and media path.
internal sealed class FalloutSoundVoices(FalloutPluginStack records)
{
    private sealed record Voice(long Id, FalloutFormKey Sound, FalloutFormKey? Reference,
        string Owner, Func<bool> Playing, Action Stop);
    private readonly Dictionary<long, Voice> _voices = [];
    private long _nextId, _stopRequests, _stopped;
    internal int ActiveVoices => _voices.Count;
    internal string? Error { get; private set; }
    internal object? LastStop { get; private set; }
    internal object State => new
    {
        stopRequests = _stopRequests,
        stopped = _stopped,
        lastStop = LastStop,
        active = _voices.Values.Select(voice => new
        {
            voice.Id,
            sound = voice.Sound.ToString(),
            reference = voice.Reference?.ToString(),
            voice.Owner,
            playing = voice.Playing(),
        }).ToArray(),
        error = Error,
        persistence = "transient-source-voices;no-cold-replay",
    };

    internal IDisposable Register(FalloutFormKey sound, FalloutFormKey? reference, string owner,
        Func<bool> playing, Action stop)
    {
        sound = ValidateSound(sound);
        if (reference is { } attached) reference = ValidateReference(attached);
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        ArgumentNullException.ThrowIfNull(playing); ArgumentNullException.ThrowIfNull(stop);
        var id = checked(++_nextId);
        _voices.Add(id, new(id, sound, reference, owner, playing, stop));
        return new Scope(() => _voices.Remove(id));
    }

    internal void Stop(FalloutFormKey sound, FalloutFormKey? reference = null)
    {
        if (Error is not null) throw new InvalidOperationException(Error);
        sound = ValidateSound(sound);
        if (reference is { } filter) reference = ValidateReference(filter);
        ++_stopRequests;
        var applied = 0;
        Voice[] matched = [];
        try
        {
            matched = _voices.Values.Where(voice => voice.Sound == sound &&
                (reference is null || voice.Reference == reference)).ToArray();
            foreach (var voice in matched)
            {
                if (!_voices.ContainsKey(voice.Id)) continue;
                voice.Stop();
                _voices.Remove(voice.Id);
                ++applied; ++_stopped;
            }
        }
        catch (Exception error)
        {
            Error = error.Message;
            throw;
        }
        finally
        {
            LastStop = new
            {
                sound = sound.ToString(),
                reference = reference?.ToString(),
                matched = matched.Length,
                applied,
                error = Error
            };
        }
    }

    internal void Retire()
    {
        var voices = _voices.Values.ToArray(); _voices.Clear();
        foreach (var voice in voices) voice.Stop();
    }

    private FalloutFormKey ValidateSound(FalloutFormKey sound)
    {
        var source = records.GetEffective(sound);
        if (source.Signature != "SOUN")
            throw new InvalidDataException("StopSound requires a SOUN form.");
        return source.FormKey;
    }
    private FalloutFormKey ValidateReference(FalloutFormKey reference)
    {
        var source = records.GetEffective(reference);
        if (source.Signature is not ("ACHR" or "ACRE" or "REFR"))
            throw new InvalidDataException("Sound source filter is not an object reference.");
        return source.FormKey;
    }
    private sealed class Scope(Action release) : IDisposable
    {
        private Action? _release = release;
        public void Dispose() { var release = _release; _release = null; release?.Invoke(); }
    }
}
