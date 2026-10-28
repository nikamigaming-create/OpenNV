using OpenNV.Runtime.Content;
using OpenNV.Runtime.Diagnostics;

namespace OpenNV.Runtime.World.Actors;

internal partial class RuntimeNativeSpeech
{
    private sealed record NpcDialogueExchange(FalloutFormKey Speaker, FalloutFormKey Listener,
        FalloutFormKey Topic, FalloutSayToCommand Command, Action? Completed, bool Ambient)
    {
        internal FalloutSpeechCompletionReceipt? Completion { get; set; }
    }

    private readonly Dictionary<FalloutFormKey, NpcDialogueExchange> _npcDialogueParticipants = [];
    private FalloutDialogueTopicCache _npcDialogueTopics = null!;
    internal bool IsNpcDialogueActive(FalloutFormKey actor) => _npcDialogueParticipants.ContainsKey(actor);
    internal bool IsDialogueBusy(FalloutFormKey actor) => IsTalking(actor) || IsNpcDialogueActive(actor) ||
        _playerDialogueSpeaker == actor || _deferredRequests.ContainsKey(actor);

    internal void StartNpcConversation(FalloutFormKey speaker, FalloutFormKey target, FalloutFormKey? topic, Action completed)
    {
        var opening = FalloutNpcDialogueLinks.OpeningTopic(_stack, topic);
        ExecuteCommand(new(speaker.ToString(), target.ToString(), opening.ToString()), speaker, opening, target, completed,
            npcConversation: true, defaultNpcOpening: topic is null);
    }

    private (FalloutFormKey Topic, FalloutDialogueInfo Info)? SelectNpcConversationOpening(
        FalloutFormKey speaker, FalloutFormKey target, FalloutFormKey? topic)
    {
        foreach (var candidate in FalloutNpcDialogueLinks.OpeningTopics(_stack, topic))
        {
            var info = SelectSpeechInfo(_npcDialogueTopics.Read(candidate), speaker, target, npcConversation: true);
            if (info is not null) return (candidate, info);
        }
        return null;
    }

    // Ambient activity attempts may legitimately find no opening line. Do not
    // manufacture a completed package or a SayToDone receipt in that case.
    // A successful attempt passes its selected INFO into playback so conditions
    // and random selection are evaluated once, before participant reservation.
    internal bool TryStartNpcConversation(FalloutFormKey speaker, FalloutFormKey target,
        FalloutFormKey? topic)
    {
        if (Error is not null) throw new InvalidOperationException(Error);
        if (speaker == target || target == _stack.RuntimeFormKey(0x14)) return false;
        var references = _references ?? throw new NotSupportedException("Ambient conversation has no reference owner.");
        var source = references.Get(speaker);
        using (RuntimeFrameWork.Measure(RuntimeFrameWork.Owner.AmbientAdmission))
            if (references.BlocksAmbientConversation(speaker) || references.BlocksAmbientConversationTarget(target) ||
                IsDialogueBusy(speaker) || IsDialogueBusy(target)) return false;
        using (RuntimeFrameWork.Measure(RuntimeFrameWork.Owner.AmbientRelationship))
            if (references.BlocksAmbientConversationRelationship(speaker, target)) return false;
        source.ConversationSuppressionSeconds = FalloutConversationSuppression.Sample(_stack.NumericSettings, source.SoundRandom.NextUnitFloat);
        references.RememberConversationAttempt(speaker, target);
        using (RuntimeFrameWork.Measure(RuntimeFrameWork.Owner.AmbientVoice))
        {
            if (!FalloutDialogueSpeaker.HasConversationVoice(_stack, source.Base, source.Templates)) return false;
            var listener = references.Get(target);
            if (!FalloutDialogueSpeaker.HasConversationVoice(_stack, listener.Base, listener.Templates)) return false;
            _ = ResidentSpeaker(_stack.GetEffective(speaker));
            _ = ResidentSpeaker(_stack.GetEffective(target));
        }
        (FalloutFormKey Topic, FalloutDialogueInfo Info)? opening;
        using (RuntimeFrameWork.Measure(RuntimeFrameWork.Owner.AmbientTopicSelection))
            opening = SelectNpcConversationOpening(speaker, target, topic);
        if (opening is not { } selected) return false;
        try
        {
            using var scope = RuntimeFrameWork.Measure(RuntimeFrameWork.Owner.AmbientStart);
            StartCore(new(speaker.ToString(), target.ToString(), selected.Topic.ToString()), _stack.GetEffective(speaker),
                selected.Topic, target, selectedInfo: selected.Info, forceNpcConversation: true, ambientConversation: true);
        }
        catch (Exception error)
        {
            // StartCore reserves both actors before binding, results and playback.
            // Keep the failed result prefix visible and stop the shared voices;
            // an AI caller must not leave an apparently healthy partial exchange.
            Fail(error); throw;
        }
        return true;
    }

    private NpcDialogueExchange BeginNpcExchange(FalloutFormKey speaker, FalloutFormKey listener, FalloutFormKey topic,
        FalloutSayToCommand command, Action? completed, bool ambient)
    {
        if (listener == _stack.RuntimeFormKey(0x14))
            throw new NotSupportedException("NPC dialogue cannot invent a player response.");
        if (IsDialogueBusy(speaker) || IsDialogueBusy(listener))
            throw new NotSupportedException("Competing NPC dialogue requires participant arbitration.");
        var exchange = new NpcDialogueExchange(speaker, listener, topic, command, completed, ambient);
        _npcDialogueParticipants.Add(speaker, exchange);
        if (listener != speaker) _npcDialogueParticipants.Add(listener, exchange);
        return exchange;
    }

    private FalloutDialogueInfo? SelectSpeechInfo(FalloutDialogueTopic topic, FalloutFormKey speaker, FalloutFormKey? target,
        bool npcConversation)
    {
        var identity = SpeakerIdentity(speaker);
        var listenerReference = target is { } listener && listener != _stack.RuntimeFormKey(0x14) ? listener : (FalloutFormKey?)null;
        var conditions = new FalloutDialogueConditions(_stack,
            _quests ?? throw new NotSupportedException("Scripted speech selection has no shared quest state."), DialogueSubject(speaker), identity,
            _conditionContext, actorValue: _actorValue, playerFemale: _playerFemale, actorRace: _actorRace,
            listener: listenerReference is null ? target : DialogueSubject(listenerReference.Value),
            listenerIdentity: listenerReference is { } reference ? SpeakerIdentity(reference) : null,
            currentPackage: _currentPackage, vampireQuery: _vampireQuery, itemCount: _itemCount, referenceDistance: _referenceDistance,
            referenceInZone: _referenceInZone, sitting: _sitting, deadCount: _deadCount, isDead: _isDead);
        return (_selection ?? throw new NotSupportedException("Scripted speech has no shared dialogue quest selection owner."))
            .Select(topic, identity.Actor, _said, _questStage, conditions.Evaluate, _dialogueRandom,
                npcConversation: npcConversation, immediateResults: true);
    }

    private void AdvanceNpcExchange(NpcDialogueExchange exchange, Voice voice, FalloutDialogueInfo completed, Action settled)
    {
        var goodbye = _stack.RuntimeFormKey(0xd4);
        var turns = FalloutNpcDialogueLinks.Candidates(completed.NextSpeaker, completed.Flags, completed.Choices,
            voice.Reference, voice.Listener ?? throw new InvalidDataException("NPC dialogue lost its physical listener."), goodbye);
        foreach (var turn in turns.OrderByDescending(turn => FalloutDialogueTopic.Priority(_stack, turn.Topic)))
        {
            var topic = _npcDialogueTopics.Read(turn.Topic);
            var info = SelectSpeechInfo(topic, turn.Speaker, turn.Listener, npcConversation: true);
            if (info is null) continue;
            StartCore(new(turn.Speaker.ToString(), turn.Listener.ToString(), turn.Topic.ToString(), exchange.Command.ForceSubtitles),
                _stack.GetEffective(turn.Speaker), turn.Topic, turn.Listener, exchange: exchange, selectedInfo: info);
            settled();
            return;
        }
        if (turns.Count != 0)
            throw new NotSupportedException($"NPC dialogue {completed.Record.FormKey} has no eligible linked continuation.");
        _npcDialogueParticipants.Remove(exchange.Speaker);
        _npcDialogueParticipants.Remove(exchange.Listener);
        if (exchange.Ambient) settled();
        else if (exchange.Completed is { } completedPackage) { completedPackage(); ++_completedPackages; settled(); }
        else
        {
            _emptyCompletions.Complete(exchange.Completion ??
                throw new InvalidDataException("NPC dialogue lost its original source voice completion."), DispatchSourceCompletion, () =>
                {
                    ++Channel(exchange.Speaker).CompletedCommands; ++_completedCommands;
                    settled();
                });
        }
    }
}
