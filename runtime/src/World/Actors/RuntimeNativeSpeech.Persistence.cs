using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.World.Actors;

internal sealed record FalloutNativeVoiceHistory(FalloutFormKey Speaker, long Generation, long CompletedCommands);
internal sealed record FalloutNativeFinishedSpeechSnapshot(IReadOnlyList<FalloutNativeVoiceHistory> Voices,
    long CompletedCommands, long CompletedPackages, long RequestedPackageEventTopics, long CompletedPackageEventTopics,
    long DisabledCommands, FalloutFormKey? LastDisabledParticipant, FalloutSpeechCompletionHistory? SettledHistory = null,
    FalloutFinishedSpeechFailureSnapshot? Failure = null, FalloutFinishedSpeechFailureSnapshot? LastCompletedFailure = null,
    IReadOnlyList<FalloutFinishedRadioConversationSnapshot>? FinishedRadio = null,
    IReadOnlyList<FalloutActiveRadioVoiceSnapshot>? ActiveRadio = null);

internal partial class RuntimeNativeSpeech
{
    private readonly Dictionary<FalloutFormKey, FalloutNativeVoiceHistory> _restoredVoiceHistory = [];
    private FalloutFinishedSpeechFailureSnapshot? _lastCompletedFailure;
    internal Func<FalloutSpeechCompletionReceipt, bool>? CanResumeSourceCompletion { get; set; }
    internal Action<FalloutSpeechCompletionReceipt>? ResumeSourceCompletion { get; set; }
    private bool NativeVoicesSettled => _deferredRequests.Count == 0 && _npcDialogueParticipants.Count == 0 &&
        _radioConversations.Values.All(radio => !radio.Active) &&
        _channels.Values.All(voice => voice.Info is null && !voice.Player.Playing && voice.PackageCompleted is null &&
            voice.ResponseCompleted is null && voice.NpcExchange is null && voice.Radio is null && voice.PackageEvent is null);
    internal bool CanCaptureFinishedFailure => NativeVoicesSettled && _emptyCompletions.HasCapturableFinishedFailure &&
        Error == _emptyCompletions.Error;
    internal bool CanCaptureFinishedState => NativeVoicesSettled && (!Active || CanCaptureFinishedFailure);

    internal FalloutNativeFinishedSpeechSnapshot CaptureFinishedState()
    {
        if (!CanCaptureFinishedState)
            throw new NotSupportedException("Saving speech requires its active voice, result or opaque callback continuation.");
        return CaptureHistory();
    }

    private FalloutNativeFinishedSpeechSnapshot CaptureHistory()
    {
        var voices = new Dictionary<FalloutFormKey, FalloutNativeVoiceHistory>(_restoredVoiceHistory);
        foreach (var voice in _channels.Values) voices[voice.Reference] = new(voice.Reference, voice.Generation, voice.CompletedCommands);
        return new(voices.Values.OrderBy(value => value.Speaker.ToString(), StringComparer.Ordinal).ToArray(), _completedCommands,
            _completedPackages, _requestedPackageEventTopics, _completedPackageEventTopics, _disabledCommands, _lastDisabledParticipant,
            CanCaptureFinishedFailure ? null : _emptyCompletions.CaptureHistory(),
            CanCaptureFinishedFailure ? _emptyCompletions.CaptureFinishedFailure() : null, _lastCompletedFailure,
            _radioConversations.Where(pair => !pair.Value.Active).OrderBy(pair => pair.Key.ToString(), StringComparer.Ordinal)
                .Select(pair => pair.Value.CaptureFinishedState()).ToArray());
    }

    internal static void ValidateFinishedState(FalloutPluginStack records, FalloutReferenceWorld world,
        FalloutNativeFinishedSpeechSnapshot snapshot)
    {
        var speakers = FalloutFinishedSpeechSourceBinding.Speakers(records);
        if (snapshot.Voices is null || snapshot.Voices.Count > speakers.Bound || snapshot.CompletedCommands < 0 ||
            snapshot.CompletedPackages < 0 || snapshot.RequestedPackageEventTopics < 0 || snapshot.CompletedPackageEventTopics < 0 ||
            snapshot.CompletedPackageEventTopics > snapshot.RequestedPackageEventTopics || snapshot.DisabledCommands < 0 ||
            snapshot.LastDisabledParticipant is { } disabled && !speakers.Contains(disabled) ||
            (snapshot.Failure is null) == (snapshot.SettledHistory is null))
            throw new InvalidDataException("Saved native speech has absent or conflicting finite history.");
        var voices = new Dictionary<FalloutFormKey, FalloutNativeVoiceHistory>();
        foreach (var voice in snapshot.Voices)
            if (voice is null || !speakers.Contains(voice.Speaker) || voice.Generation < 0 || voice.Generation == long.MaxValue || voice.CompletedCommands < 0 ||
                voice.CompletedCommands > voice.Generation || !voices.TryAdd(voice.Speaker, voice))
                throw new InvalidDataException("Saved native voice generation is duplicated or outside its source owner.");
        if (snapshot.FinishedRadio is { } radios)
        {
            if (radios.Count > speakers.Bound) throw new InvalidDataException("Saved radio history exceeds its source speakers.");
            var stations = new HashSet<FalloutFormKey>();
            foreach (var radio in radios)
            {
                if (radio is null) throw new InvalidDataException("Saved radio history is absent.");
                FalloutRadioConversation.ValidateFinishedState(records, radio);
                if (!stations.Add(radio.Station.Reference) || !speakers.Contains(radio.Station.Reference) ||
                    radio.CompletedLines > 0 && (!voices.TryGetValue(radio.Station.Reference, out var voice) || radio.CompletedLines > voice.Generation))
                    throw new InvalidDataException("Saved radio completion exceeds its original native voice history.");
            }
        }
        var owner = new FalloutSpeechCompletionEvents();
        void Require(FalloutFinishedSpeechReceipt receipt) => FalloutFinishedSpeechSourceBinding.Require(records, world.Retained(receipt.Speaker), receipt);
        if (snapshot.Failure is { } failure)
        {
            owner.RestoreFinishedFailure(failure, speakers, Require);
            if (!voices.TryGetValue(failure.Receipt.Speaker, out var voice) || voice.Generation != failure.Receipt.Generation)
                throw new InvalidDataException("Saved ended receipt differs from its retired native voice generation.");
        }
        else owner.RestoreHistory(snapshot.SettledHistory!, speakers);
        var history = snapshot.Failure?.CompletedVoices ?? snapshot.SettledHistory!.CompletedVoices;
        foreach (var completed in history)
            if (!voices.TryGetValue(completed.Speaker, out var voice) || completed.Generation > voice.Generation)
                throw new InvalidDataException("Saved completion history exceeds the source voice clock.");
        if (snapshot.LastCompletedFailure is { } last)
        {
            new FalloutSpeechCompletionEvents().RestoreFinishedFailure(last, speakers, Require);
            if (!history.Any(value => value.Speaker == last.Receipt.Speaker && value.Generation >= last.Receipt.Generation))
                throw new InvalidDataException("Saved recovered speech lacks its once-only committed generation.");
        }
        ValidateActiveRadio(records, world, snapshot, voices);
    }

    internal void RestoreFinishedState(FalloutNativeFinishedSpeechSnapshot? snapshot)
    {
        if (snapshot is null) return;
        if (snapshot.ActiveRadio is { Count: > 0 })
            throw new InvalidDataException("Active radio requires the complete native speech restoration owner.");
        if (_channels.Count != 0 || Active || _restoredVoiceHistory.Count != 0 || _radioConversations.Count != 0)
            throw new InvalidOperationException("Native finished speech restoration needs a fresh inactive owner.");
        var world = _references ?? throw new InvalidOperationException("Saved speech has no shared world.");
        ValidateFinishedState(_stack, world, snapshot);
        if (snapshot.FinishedRadio is { Count: > 0 } && _quests is null)
            throw new InvalidOperationException("Saved radio has no quest state.");
        var speakers = FalloutFinishedSpeechSourceBinding.Speakers(_stack);
        if (snapshot.Failure is { } failure)
            _emptyCompletions.RestoreFinishedFailure(failure, speakers,
                receipt => FalloutFinishedSpeechSourceBinding.Require(_stack, world.Retained(receipt.Speaker), receipt));
        else _emptyCompletions.RestoreHistory(snapshot.SettledHistory!, speakers);
        foreach (var radio in snapshot.FinishedRadio ?? [])
        {
            var conversation = new FalloutRadioConversation(_stack, world,
                _quests ?? throw new InvalidOperationException("Saved radio has no quest state."), EvaluateRadioCondition, _questStage, _said, _dialogueRandom);
            conversation.RestoreFinishedState(radio);
            _radioConversations.Add(radio.Station.Reference, conversation);
        }
        foreach (var voice in snapshot.Voices) _restoredVoiceHistory.Add(voice.Speaker, voice);
        _completedCommands = snapshot.CompletedCommands; _completedPackages = snapshot.CompletedPackages;
        _requestedPackageEventTopics = snapshot.RequestedPackageEventTopics; _completedPackageEventTopics = snapshot.CompletedPackageEventTopics;
        _disabledCommands = snapshot.DisabledCommands; _lastDisabledParticipant = snapshot.LastDisabledParticipant;
        _lastCompletedFailure = snapshot.LastCompletedFailure; Error = snapshot.Failure?.Error;
    }

    private void ResumeFinishedSource()
    {
        var failure = _emptyCompletions.CaptureFinishedFailure();
        try
        {
            _emptyCompletions.ResumeFinishedFailure(receipt =>
                (ResumeSourceCompletion ?? throw new NotSupportedException("Finished source continuation has no persistent owner."))(receipt), receipt =>
            {
                // Audio/results are already committed. Only the completion
                // counter and its settled notification follow the source suffix.
                Error = null; _lastCompletedFailure = failure;
                var voice = Channel(receipt.Speaker);
                ++voice.CompletedCommands; ++_completedCommands;
                InfoCompleted?.Invoke(receipt.Info!.Value);
            });
        }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException or InvalidOperationException or
            KeyNotFoundException or OverflowException or FileNotFoundException)
        { Fail(error); }
    }
}
