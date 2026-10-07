using System.Security.Cryptography;

namespace OpenNV.Runtime.Content;

// One transient SOUN instance registry per loaded graph. Source identity and
// reference attachment are separate from the command's caller and media path.
internal sealed class FalloutSoundVoices(FalloutPluginStack records)
{
    private sealed record Voice(long Id, FalloutFormKey Sound, FalloutFormKey? Reference,
        string Owner, Func<bool> Playing, Action Stop, Action? Retire,
        FalloutFormKey? SourceReference, Func<OpenNV.Runtime.Content.FalloutFiniteSoundVoice?>? FiniteWait,
        Func<object?>? FiniteWaitState, Func<IFalloutFiniteSoundSaveDrainLease>? PrepareSaveDrain, Func<bool>? CheckpointReady);
    private readonly Dictionary<long, Voice> _voices = [];
    private long _nextId, _stopRequests, _stopped;
    private readonly List<Action> _retirementObservers = [];
    private FalloutFiniteSoundSaveDrain? _saveDrain;
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
        Func<OpenNV.Runtime.Content.FalloutFiniteSoundVoice?>? finiteWait = null, Func<object?>? finiteWaitState = null,
        Func<IFalloutFiniteSoundSaveDrainLease>? prepareSaveDrain = null, Func<bool>? checkpointReady = null)
    {
        sound = ValidateSound(sound);
        if (reference is { } attached) reference = ValidateReference(attached);
        if (sourceReference is { } origin) sourceReference = ValidateReference(origin);
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        ArgumentNullException.ThrowIfNull(playing); ArgumentNullException.ThrowIfNull(stop);
        var id = checked(++_nextId);
        _voices.Add(id, new(id, sound, reference, owner, playing, stop, retire, sourceReference, finiteWait, finiteWaitState, prepareSaveDrain, checkpointReady));
        return new Scope(() => _voices.Remove(id));
    }

    internal FalloutFiniteSoundSaveDrain PrepareFiniteSaveDrain()
    {
        if (_saveDrain is not null) throw new InvalidOperationException("The source graph already has a finite save-drain lease.");
        if (Error is not null) throw new NotSupportedException("Source audio failure: " + Error);
        var generation = _nextId;
        var bound = _voices.Values.ToArray();
        var leases = new List<IFalloutFiniteSoundSaveDrainLease>();
        try
        {
            foreach (var voice in bound)
            {
                if (voice.CheckpointReady?.Invoke() == true) continue;
                if (voice.SourceReference is null || voice.PrepareSaveDrain is null)
                    throw new NotSupportedException($"Active audio has no proven finite save-drain owner: registration={voice.Id} owner={voice.Owner} sound={voice.Sound}.");
                var lease = voice.PrepareSaveDrain();
                leases.Add(lease);
                lease.Voice.Validate();
                var source = records.GetEffective(lease.Voice.Sound);
                if (lease.Voice.Reference != voice.SourceReference || lease.Voice.Sound != voice.Sound ||
                    voice.FiniteWait?.Invoke() != lease.Voice || source.Signature != "SOUN" ||
                    !Convert.ToHexString(SHA256.HashData(source.ReadData())).Equals(lease.Voice.SoundSha256, StringComparison.OrdinalIgnoreCase) ||
                    FalloutSoundLoop.Read(FalloutSoundRecordReader.Read(source)).Mode != FalloutSoundLoopMode.None)
                    throw new InvalidDataException("Prepared native audio differs from its exact registered source voice.");
            }
            if (leases.DistinctBy(lease => (lease.Voice.Reference, lease.Voice.Generation)).Count() != leases.Count)
                throw new InvalidDataException("Finite save-drain leases repeat a source generation.");
            var drain = new FalloutFiniteSoundSaveDrain(leases.AsReadOnly(), () =>
            {
                if (Error is not null || _nextId != generation || bound.Any(voice => voice.CheckpointReady is not null && !voice.CheckpointReady()) ||
                    _voices.Keys.Except(bound.Select(voice => voice.Id)).Any())
                    throw new FalloutFiniteSoundSaveDrainInvalidatedException("The source/native audio registry changed during save preparation.");
            }, () => _saveDrain = null);
            _saveDrain = drain;
            return drain;
        }
        catch (Exception failure)
        {
            List<Exception> errors = [failure];
            foreach (var lease in leases.AsEnumerable().Reverse())
            {
                try { lease.Dispose(); }
                catch (Exception cleanup) { errors.Add(cleanup); }
            }
            if (errors.Count != 1) throw new AggregateException("Finite save-drain preparation and cleanup failed.", errors);
            throw;
        }
    }

    internal IReadOnlyList<OpenNV.Runtime.Content.FalloutFiniteSoundVoice>? PendingFiniteSourceVoices(FalloutFormKey reference)
    {
        reference = ValidateReference(reference);
        var bound = _voices.Values.Where(voice => voice.SourceReference == reference).ToArray();
        if (bound.Length == 0 || Error is not null) return null;
        var result = new List<OpenNV.Runtime.Content.FalloutFiniteSoundVoice>();
        foreach (var voice in bound)
        {
            if (voice.CheckpointReady?.Invoke() == true) continue;
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
