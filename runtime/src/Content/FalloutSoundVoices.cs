namespace OpenNV.Runtime.Content;

// One transient SOUN instance registry per loaded graph. Source identity and
// reference attachment are separate from the command's caller and media path.
internal sealed class FalloutSoundVoices(FalloutPluginStack records)
{
    private sealed record Voice(long Id, FalloutFormKey Sound, FalloutFormKey? Reference,
        string Owner, Func<bool> Playing, Action Stop, Action? Retire,
        FalloutFormKey? SourceReference, Func<OpenNV.Runtime.Content.FalloutFiniteSoundVoice?>? FiniteWait,
        Func<object?>? FiniteWaitState);
    private readonly Dictionary<long, Voice> _voices = [];
    private long _nextId, _stopRequests, _stopped;
    private readonly List<Action> _retirementObservers = [];
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
            finiteCompletion = voice.FiniteWaitState?.Invoke(),
        }).ToArray(),
        error = Error,
        persistence = "transient-source-voices;no-cold-replay",
    };

    internal IDisposable Register(FalloutFormKey sound, FalloutFormKey? reference, string owner,
        Func<bool> playing, Action stop, Action? retire = null, FalloutFormKey? sourceReference = null,
        Func<OpenNV.Runtime.Content.FalloutFiniteSoundVoice?>? finiteWait = null, Func<object?>? finiteWaitState = null)
    {
        sound = ValidateSound(sound);
        if (reference is { } attached) reference = ValidateReference(attached);
        if (sourceReference is { } origin) sourceReference = ValidateReference(origin);
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        ArgumentNullException.ThrowIfNull(playing); ArgumentNullException.ThrowIfNull(stop);
        var id = checked(++_nextId);
        _voices.Add(id, new(id, sound, reference, owner, playing, stop, retire, sourceReference, finiteWait, finiteWaitState));
        return new Scope(() => _voices.Remove(id));
    }

    internal IReadOnlyList<OpenNV.Runtime.Content.FalloutFiniteSoundVoice>? PendingFiniteSourceVoices(FalloutFormKey reference)
    {
        reference = ValidateReference(reference);
        var bound = _voices.Values.Where(voice => voice.SourceReference == reference).ToArray();
        if (bound.Length == 0 || Error is not null) return null;
        var result = new List<OpenNV.Runtime.Content.FalloutFiniteSoundVoice>();
        foreach (var voice in bound)
        {
            if (voice.FiniteWait?.Invoke() is not { } proof) return null;
            proof.Validate();
            if (proof.Reference != reference || proof.Sound != voice.Sound)
                throw new InvalidDataException("Native finite wait belongs to another source voice.");
            result.Add(proof);
        }
        if (result.DistinctBy(proof => proof.Generation).Count() != result.Count)
            throw new InvalidDataException("Native finite wait repeats a source generation.");
        return result.OrderBy(proof => proof.Generation).ToArray();
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

    internal IDisposable BindRetirement(Action retire)
    {
        ArgumentNullException.ThrowIfNull(retire);
        _retirementObservers.Add(retire);
        return new Scope(() => _retirementObservers.Remove(retire));
    }

    internal void Retire()
    {
        var voices = _voices.Values.ToArray(); _voices.Clear();
        foreach (var voice in voices) (voice.Retire ?? voice.Stop)();
        foreach (var retire in _retirementObservers.ToArray()) retire();
        _retirementObservers.Clear();
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
