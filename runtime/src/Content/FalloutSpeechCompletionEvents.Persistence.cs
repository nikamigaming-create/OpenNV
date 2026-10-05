namespace OpenNV.Runtime.Content;

internal sealed record FalloutFinishedSpeechSourceIdentity(string InfoSha256,
    FalloutFormKey? SpeakerScript, string? SpeakerScriptSha256)
{
    internal void Validate()
    {
        static bool Hash(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
        if (!Hash(InfoSha256) || (SpeakerScript is null ? SpeakerScriptSha256 is not null : !Hash(SpeakerScriptSha256)))
            throw new InvalidDataException("Finished speech lost its source INFO or speaker-script identity.");
    }
}

internal sealed record FalloutFinishedSpeechReceipt(FalloutFormKey Speaker,
    IReadOnlyList<FalloutFormKey> Topics, FalloutFormKey Info, long Generation,
    FalloutFinishedSpeechSourceIdentity Source)
{
    internal FalloutSpeechCompletionReceipt Completion() => new(Speaker, Topics.ToHashSet(), Info, Generation);
    internal FalloutFinishedSpeechReceipt Copy() => this with { Topics = Topics.ToArray() };
}
internal sealed record FalloutCompletedVoiceGeneration(FalloutFormKey Speaker, long Generation);
internal sealed record FalloutFinishedSpeechFailureSnapshot(FalloutFinishedSpeechReceipt Receipt,
    string Error, string OriginalError, long Requests, long CompletedTopics, long CompletedPackages,
    IReadOnlyList<FalloutCompletedVoiceGeneration> CompletedVoices);
internal sealed record FalloutSpeechCompletionHistory(long Requests, long CompletedTopics, long CompletedPackages,
    IReadOnlyList<FalloutCompletedVoiceGeneration> CompletedVoices);
internal sealed record FalloutSpeechSpeakerDomain(int Bound, Func<FalloutFormKey, bool> Contains);

internal sealed partial class FalloutSpeechCompletionEvents
{
    private FalloutFinishedSpeechSourceIdentity? _finishedEffects;
    private string? _finishedOriginalError;
    internal bool HasCapturableFinishedFailure => Error is not null && !_draining &&
        _pending.Count == 0 && _dispatching is { Receipt.Info: not null, Packages.Count: 0 } &&
        _finishedEffects is not null && _finishedOriginalError is not null;
    internal FalloutFinishedSpeechReceipt? FinishedFailureReceipt => HasCapturableFinishedFailure ?
        new(_dispatching!.Speaker, _dispatching.Receipt!.Topics.ToArray(), _dispatching.Receipt.Info!.Value,
            _dispatching.Receipt.Generation, _finishedEffects!) : null;

    internal void CompleteFinished(FalloutSpeechCompletionReceipt receipt, FalloutFinishedSpeechSourceIdentity source,
        Action commitFinishedEffects, Action<FalloutSpeechCompletionReceipt> dispatch, Action? settled = null)
    {
        source.Validate();
        if (Error is not null) throw new InvalidOperationException(Error);
        if (_draining) throw new InvalidOperationException("Speech completion dispatch cannot be recursive.");
        if (receipt.Info is null || receipt.Generation <= _completedVoices.GetValueOrDefault(receipt.Speaker))
            throw new InvalidOperationException("Finished speech has no new voice completion generation.");
        var requests = checked(_requests + 1);
        _draining = true;
        _dispatching = new(receipt.Speaker, new(receipt.Topics), []) { Receipt = receipt };
        _requests = requests;
        try
        {
            // Publish this marker only after actual audio and INFO end results
            // have retired. Failure within those effects stays uncapturable.
            commitFinishedEffects();
            _finishedEffects = source;
            dispatch(receipt);
            CommitFinishedSource(receipt);
        }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException or InvalidOperationException or
            KeyNotFoundException or OverflowException or FileNotFoundException)
        {
            Error = error.Message;
            if (_finishedEffects is not null) _finishedOriginalError ??= error.Message;
            throw;
        }
        finally { _draining = false; }
        // Stage notification follows source commitment. Its own opaque failure
        // cannot recreate a source receipt or replay a completed prefix.
        settled?.Invoke();
    }
    private void CommitFinishedSource(FalloutSpeechCompletionReceipt receipt)
    {
        var topics = checked(_completedTopics + receipt.Topics.Count);
        _completedVoices[receipt.Speaker] = receipt.Generation;
        _completedTopics = topics; _dispatching = null;
        Error = null; _finishedEffects = null; _finishedOriginalError = null;
    }
    internal FalloutFinishedSpeechFailureSnapshot CaptureFinishedFailure()
    {
        var receipt = FinishedFailureReceipt ?? throw new NotSupportedException("Speech does not own a fully ended source-completion failure.");
        return new(receipt.Copy(), Error!, _finishedOriginalError!, _requests, _completedTopics, _completedPackages, Generations());
    }
    private FalloutCompletedVoiceGeneration[] Generations() => _completedVoices.OrderBy(pair => pair.Key.ToString(), StringComparer.Ordinal)
        .Select(pair => new FalloutCompletedVoiceGeneration(pair.Key, pair.Value)).ToArray();
    internal FalloutSpeechCompletionHistory CaptureHistory()
    {
        if (Active || Error is not null || _draining) throw new NotSupportedException("Pending speech completion is not settled history.");
        return new(_requests, _completedTopics, _completedPackages, Generations());
    }
    private static Dictionary<FalloutFormKey, long> ValidateHistory(long requests, long topics, long packages,
        IReadOnlyList<FalloutCompletedVoiceGeneration> voices, FalloutSpeechSpeakerDomain sourceSpeakers)
    {
        if (requests < 0 || topics < 0 || packages < 0 || voices is null || sourceSpeakers.Bound < 0 || voices.Count > sourceSpeakers.Bound ||
            topics > requests || packages > requests || voices.Count > topics)
            throw new InvalidDataException("Saved speech completion history is outside its source domain.");
        var result = new Dictionary<FalloutFormKey, long>();
        foreach (var value in voices)
            if (value is null || value.Generation <= 0 || !sourceSpeakers.Contains(value.Speaker) || !result.TryAdd(value.Speaker, value.Generation))
                throw new InvalidDataException("Saved voice generation is duplicated or unbound.");
        return result;
    }
    private void RequireEmpty()
    {
        if (_draining || Active || Error is not null || _finishedEffects is not null || _requests != 0 || _completedVoices.Count != 0)
            throw new InvalidOperationException("Speech owner is not empty for restoration.");
    }
    private void RestoreHistoryCore(long requests, long topics, long packages, Dictionary<FalloutFormKey, long> generations)
    {
        foreach (var (speaker, generation) in generations) _completedVoices.Add(speaker, generation);
        _requests = requests; _completedTopics = topics; _completedPackages = packages;
    }
    internal void RestoreHistory(FalloutSpeechCompletionHistory snapshot, FalloutSpeechSpeakerDomain sourceSpeakers)
    {
        RequireEmpty();
        var generations = ValidateHistory(snapshot.Requests, snapshot.CompletedTopics, snapshot.CompletedPackages, snapshot.CompletedVoices, sourceSpeakers);
        RestoreHistoryCore(snapshot.Requests, snapshot.CompletedTopics, snapshot.CompletedPackages, generations);
    }
    internal void RestoreFinishedFailure(FalloutFinishedSpeechFailureSnapshot snapshot,
        FalloutSpeechSpeakerDomain sourceSpeakers, Action<FalloutFinishedSpeechReceipt> requireWinningSource)
    {
        RequireEmpty();
        if (snapshot is null || snapshot.Receipt is null || snapshot.Receipt.Source is null || snapshot.Receipt.Topics is not { Count: 1 } ||
            snapshot.Receipt.Generation <= 0 || !sourceSpeakers.Contains(snapshot.Receipt.Speaker) || snapshot.Requests <= 0 ||
            string.IsNullOrWhiteSpace(snapshot.Error) || string.IsNullOrWhiteSpace(snapshot.OriginalError))
            throw new InvalidDataException("Saved finished speech has an invalid ended receipt.");
        var generations = ValidateHistory(snapshot.Requests, snapshot.CompletedTopics, snapshot.CompletedPackages, snapshot.CompletedVoices, sourceSpeakers);
        snapshot.Receipt.Source.Validate();
        requireWinningSource(snapshot.Receipt);
        if (generations.GetValueOrDefault(snapshot.Receipt.Speaker) >= snapshot.Receipt.Generation)
            throw new InvalidDataException("Saved failed speech was already committed.");
        var receipt = snapshot.Receipt.Completion();
        RestoreHistoryCore(snapshot.Requests, snapshot.CompletedTopics, snapshot.CompletedPackages, generations);
        _dispatching = new(receipt.Speaker, new(receipt.Topics), []) { Receipt = receipt };
        _finishedEffects = snapshot.Receipt.Source; _finishedOriginalError = snapshot.OriginalError; Error = snapshot.Error;
    }
    internal void ResumeFinishedFailure(Action<FalloutSpeechCompletionReceipt> resumeSource,
        Action<FalloutSpeechCompletionReceipt>? settled = null)
    {
        if (!HasCapturableFinishedFailure) throw new NotSupportedException("Speech has no owned finished source continuation.");
        var receipt = _dispatching!.Receipt!;
        _draining = true;
        try { resumeSource(receipt); CommitFinishedSource(receipt); }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException or InvalidOperationException or
            KeyNotFoundException or OverflowException or FileNotFoundException)
        { Error = error.Message; throw; }
        finally { _draining = false; }
        settled?.Invoke(receipt);
    }
}
