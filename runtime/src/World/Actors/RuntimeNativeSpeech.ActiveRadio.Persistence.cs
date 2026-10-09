using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.World.Actors;

internal sealed record FalloutActiveRadioVoiceSnapshot(FalloutActiveRadioConversationSnapshot Conversation,
    long Generation, int ResponseIndex, FalloutDialogueVoiceBinding Binding, string AudioSha256, string? LipSha256,
    string DecodedSha256, FalloutPcmPlaybackSnapshot Samples, bool Advance, bool ForceSubtitles,
    FalloutScriptResultReceipt? BeginResults = null, FalloutScriptResultReceipt? EndResults = null);

internal partial class RuntimeNativeSpeech
{
    private readonly Dictionary<string, WeakReference<NativeOwnedPcmData>> _radioPcm = new(StringComparer.OrdinalIgnoreCase);
    public override void _Notification(int what)
    {
        if (what != NotificationPaused && what != NotificationUnpaused) return;
        // Native player pause fades still invoke Mix, and Play has no remembered
        // paused-before-start flag. Bind the authoritative source clock itself.
        foreach (var voice in _channels.Values) voice.Pcm?.SetSuspended(!CanProcess());
    }
    private static bool InactiveVoiceSettled(Voice voice) => voice.Info is null && !voice.Player.Playing &&
        voice.PackageCompleted is null && voice.ResponseCompleted is null && voice.NpcExchange is null &&
        voice.Radio is null && voice.PackageEvent is null;

    private bool CanCaptureRadioVoice(Voice voice)
    {
        if (voice.Info is null || voice.Radio is not { Active: true } radio || voice.Pcm is null || voice.Binding is null ||
            voice.ResponseSound is not null || voice.ResponseCompleted is not null || voice.PackageCompleted is not null ||
            voice.PackageEvent is not null || voice.NpcExchange is not null || voice.Listener is not null ||
            voice.CommandKind != "radio-conversation" || voice.Command is null || !HasOwnedResultPrefix(voice) ||
            !_radioConversations.TryGetValue(voice.Reference, out var current) || !ReferenceEquals(current, radio) ||
            radio.Info != voice.Info || radio.Topic != voice.Topic || voice.Player.Stream != voice.Pcm.Stream ||
            !voice.Player.HasStreamPlayback() || (uint)voice.ResponseIndex >= voice.Info.Responses.Count) return false;
        var response = voice.Info.Responses[voice.ResponseIndex];
        return response.Sound is null && response.SpeakerAnimation is null && response.ListenerAnimation is null;
    }

    internal bool CanCaptureState => CanCaptureFinishedState || Error is null && !_emptyCompletions.Active &&
        _deferredRequests.Count == 0 && _npcDialogueParticipants.Count == 0 &&
        _channels.Values.All(voice => InactiveVoiceSettled(voice) || CanCaptureRadioVoice(voice)) &&
        _radioConversations.Where(pair => pair.Value.Active).All(pair =>
            _channels.TryGetValue(pair.Key, out var voice) && CanCaptureRadioVoice(voice));

    internal FalloutNativeFinishedSpeechSnapshot CaptureState()
    {
        if (!CanCaptureState) throw new NotSupportedException("Active speech has an unbound voice, result or callback continuation.");
        AudioServer.Lock();
        try
        {
            var snapshot = CaptureHistory() with
            {
                ActiveRadio = _channels.Values.Where(voice => voice.Info is not null)
                    .OrderBy(voice => voice.Reference.ToString(), StringComparer.Ordinal).Select(voice =>
                        new FalloutActiveRadioVoiceSnapshot(voice.Radio!.CaptureActiveState(), voice.Generation,
                            voice.ResponseIndex, voice.Binding!, voice.Pcm!.Data.MediaSha256,
                            voice.LipSha256, voice.Pcm.Data.Sha256, voice.Pcm.Capture(), voice.Advance,
                            voice.Command!.ForceSubtitles, voice.BeginResults, voice.EndResults)).ToArray()
            };
            ValidateFinishedState(_stack, _references!, snapshot);
            return snapshot;
        }
        finally { AudioServer.Unlock(); }
    }

    private static void ValidateActiveRadio(FalloutPluginStack records, FalloutReferenceWorld world,
        FalloutNativeFinishedSpeechSnapshot snapshot, IReadOnlyDictionary<FalloutFormKey, FalloutNativeVoiceHistory> voices)
    {
        if (snapshot.ActiveRadio is not { } radios) return;
        if (radios.Count > voices.Count || radios.Count != 0 && snapshot.Failure is not null)
            throw new InvalidDataException("Active radio conflicts with its finite speech history.");
        var stations = (snapshot.FinishedRadio ?? []).Select(radio => radio.Station.Reference).ToHashSet();
        foreach (var voice in radios)
        {
            if (voice is null || voice.Conversation is null || voice.Binding is null || voice.Samples is null)
                throw new InvalidDataException("Active radio is missing a continuation owner.");
            var info = FalloutRadioConversation.ValidateActiveState(records, voice.Conversation);
            var station = voice.Conversation.Identity.Station;
            if (!stations.Add(station.Reference) || !voices.TryGetValue(station.Reference, out var history) ||
                voice.Generation != history.Generation || voice.Generation <= 0 ||
                voice.Conversation.Identity.CompletedLines >= voice.Generation || !world.IsEnabled(station.Reference) ||
                (uint)voice.ResponseIndex >= info.Responses.Count)
                throw new InvalidDataException("Active radio has an invalid station, response or voice generation.");
            RequireResultPrefix(info, world.DialogueSubject(station.Reference), voice.BeginResults, voice.EndResults);
            var response = info.Responses[voice.ResponseIndex];
            var identity = info.Speaker is { } speaker ? FalloutDialogueSpeaker.Read(records, speaker) : world.DialogueIdentity(station.Reference);
            if (response.Sound is not null || response.SpeakerAnimation is not null || response.ListenerAnimation is not null)
                throw new NotSupportedException("Active radio response SOUN/IDLE continuation requires its independent owner.");
            if (voice.Binding.Info != info.Record.FormKey || voice.Binding.Actor != identity.Actor ||
                voice.Binding.TraitsOwner != identity.TraitsOwner || voice.Binding.VoiceType != identity.VoiceType ||
                voice.Binding.VoiceName != identity.VoiceName || voice.Binding.WinningPlugin != info.Record.Plugin.Name ||
                voice.Binding.Response != response.Number || !ValidMediaHash(voice.AudioSha256) ||
                !ValidMediaHash(voice.DecodedSha256) || voice.LipSha256 is { } lip && !ValidMediaHash(lip))
                throw new InvalidDataException("Active radio differs from its selected source voice/media identity.");
            voice.Samples.Validate();
            if (voice.Samples.Loop != new FalloutSoundLoop(FalloutSoundLoopMode.None, 0, 0) || voice.Samples.Loops != 0 ||
                voice.Samples.Releasing || voice.Advance && (voice.Samples.Playing || voice.Samples.StartPending) ||
                voice.Samples.Playing && voice.Samples.Position >= voice.Samples.Frames ||
                voice.Samples.StartPending && voice.Samples.Position != 0 ||
                !voice.Samples.Playing && !voice.Samples.StartPending && voice.Samples.Position != voice.Samples.Frames)
                throw new InvalidDataException("Active radio has an invalid finite response clock.");
        }
    }

    private static bool HasOwnedResultPrefix(Voice voice)
    {
        if (voice.Info is null || voice.BeginResults is null || ((voice.Info.Flags & 8) != 0) != (voice.EndResults is not null)) return false;
        try { RequireResultPrefix(voice.Info, voice.DialogueSubject, voice.BeginResults, voice.EndResults); return true; }
        catch (Exception error) when (error is InvalidDataException or InvalidOperationException or NotSupportedException) { return false; }
    }

    private static void RequireResultPrefix(FalloutDialogueInfo info, FalloutFormKey caller,
        FalloutScriptResultReceipt? begin, FalloutScriptResultReceipt? end)
    {
        if (begin is null || ((info.Flags & 8) != 0) != (end is not null))
            throw new NotSupportedException("Active radio has no proven shared result-authority prefix; legacy results cannot be replayed or retroactively receipted.");
        begin.Require(FalloutScriptScope.Dialogue(info.Record, true), caller);
        end?.Require(FalloutScriptScope.Dialogue(info.Record, false), caller);
    }

    private static bool ValidMediaHash(string hash) => hash is { Length: 64 } && hash.All(Uri.IsHexDigit);

    internal void RestoreState(FalloutNativeFinishedSpeechSnapshot? snapshot)
    {
        if (snapshot is null) return;
        if (_channels.Count != 0 || Active || _restoredVoiceHistory.Count != 0 || _radioConversations.Count != 0)
            throw new InvalidOperationException("Native speech restoration needs a fresh inactive owner.");
        ValidateFinishedState(_stack, _references!, snapshot);
        var prepared = new List<(Voice Voice, FalloutRadioConversation Radio)>();
        try
        {
            foreach (var saved in snapshot.ActiveRadio ?? [])
            {
                var radio = new FalloutRadioConversation(_stack, _references!,
                    _quests ?? throw new InvalidOperationException("Active radio has no quest state."), EvaluateRadioCondition, _questStage, _said, _dialogueRandom);
                radio.RestoreActiveState(saved.Conversation);
                var station = radio.Station!.Reference;
                var voice = new Voice(station, new AudioStreamPlayer { Name = "OwnedActorVoice" });
                prepared.Add((voice, radio));
                BindRadioSpeaker(voice, _stack.GetEffective(station), _presentation?.Invoke(station), radio);
                voice.Command = new(station.ToString(), "", radio.Topic!.Value.ToString(), saved.ForceSubtitles);
                voice.CommandKind = "radio-conversation"; voice.Topic = radio.Topic;
                voice.Info = radio.Info; voice.ResponseIndex = saved.ResponseIndex;
                voice.Generation = saved.Generation; voice.BeginResults = saved.BeginResults; voice.EndResults = saved.EndResults;
                voice.CompletedCommands = snapshot.Voices.Single(history => history.Speaker == station).CompletedCommands;
                voice.Player.Finished += () => voice.Advance = true;
                // Prepare every original resource before publishing any channel.
                // StartCore would reselect and replay the committed result prefix.
                PlayResponse(voice, saved, start: false);
            }
            RestoreFinishedState(snapshot with { ActiveRadio = null });
            foreach (var entry in prepared)
            {
                _radioConversations.Add(entry.Voice.Reference, entry.Radio);
                _channels.Add(entry.Voice.Reference, entry.Voice);
                AddChild(entry.Voice.Player);
                _lastVoice = entry.Voice;
            }
            foreach (var entry in prepared) entry.Voice.Player.Play();
        }
        catch (Exception error)
        {
            foreach (var entry in prepared)
            {
                _channels.Remove(entry.Voice.Reference);
                _radioConversations.Remove(entry.Voice.Reference);
                RetireRadioPcm(entry.Voice);
                entry.Voice.Player.Free();
            }
            _lastVoice = null;
            Fail(error);
            throw;
        }
    }

    private NativeOwnedPcmData RadioPcm(string path)
    {
        if (_radioPcm.TryGetValue(path, out var cached) && cached.TryGetTarget(out var decoded)) return decoded;
        using var source = NativeOwnedMediaLoader.LoadAudio(path);
        var data = new NativeOwnedPcmData(source);
        _radioPcm[path] = new(data);
        return data;
    }
}
