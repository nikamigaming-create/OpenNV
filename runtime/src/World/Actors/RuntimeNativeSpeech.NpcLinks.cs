using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Actors;

internal partial class RuntimeNativeSpeech
{
    private sealed record NpcDialogueExchange(FalloutFormKey Speaker, FalloutFormKey Listener,
        FalloutFormKey Topic, FalloutSayToCommand Command, Action? Completed);

    private readonly Dictionary<FalloutFormKey, NpcDialogueExchange> _npcDialogueParticipants = [];
    internal bool IsNpcDialogueActive(FalloutFormKey actor) => _npcDialogueParticipants.ContainsKey(actor);
    internal bool IsDialogueBusy(FalloutFormKey actor) => IsTalking(actor) || IsNpcDialogueActive(actor) ||
        _playerDialogueSpeaker == actor || _deferredRequests.ContainsKey(actor);

    internal void StartNpcConversation(FalloutFormKey speaker, FalloutFormKey target, FalloutFormKey topic, Action completed) =>
        ExecuteCommand(new(speaker.ToString(), target.ToString(), topic.ToString()), speaker, topic, target, completed, npcConversation: true);

    private NpcDialogueExchange BeginNpcExchange(FalloutFormKey speaker, FalloutFormKey listener, FalloutFormKey topic,
        FalloutSayToCommand command, Action? completed)
    {
        if (listener == _stack.RuntimeFormKey(0x14))
            throw new NotSupportedException("NPC dialogue cannot invent a player response.");
        if (IsDialogueBusy(speaker) || IsDialogueBusy(listener))
            throw new NotSupportedException("Competing NPC dialogue requires participant arbitration.");
        var exchange = new NpcDialogueExchange(speaker, listener, topic, command, completed);
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
            referenceInZone: _referenceInZone);
        return (_selection ?? throw new NotSupportedException("Scripted speech has no shared dialogue quest selection owner."))
            .Select(topic, identity.Actor, _said, _questStage, conditions.Evaluate, _dialogueRandom,
                npcConversation: npcConversation, immediateResults: true);
    }

    private void AdvanceNpcExchange(NpcDialogueExchange exchange, Voice voice, FalloutDialogueInfo completed)
    {
        var goodbye = FalloutDialogueTopic.Find(_stack, "DIAL", "GOODBYE").FormKey;
        var turns = FalloutNpcDialogueLinks.Candidates(completed.NextSpeaker, completed.Flags, completed.Choices,
            voice.Reference, voice.Listener ?? throw new InvalidDataException("NPC dialogue lost its physical listener."), goodbye);
        foreach (var turn in turns.OrderByDescending(turn => FalloutDialogueTopic.Priority(_stack, turn.Topic)))
        {
            var topic = FalloutDialogueTopic.Read(_stack, turn.Topic);
            var info = SelectSpeechInfo(topic, turn.Speaker, turn.Listener, npcConversation: true);
            if (info is null) continue;
            StartCore(new(turn.Speaker.ToString(), turn.Listener.ToString(), turn.Topic.ToString(), exchange.Command.ForceSubtitles),
                _stack.GetEffective(turn.Speaker), turn.Topic, turn.Listener, exchange: exchange, selectedInfo: info);
            return;
        }
        if (turns.Count != 0)
            throw new NotSupportedException($"NPC dialogue {completed.Record.FormKey} has no eligible linked continuation.");
        _npcDialogueParticipants.Remove(exchange.Speaker);
        _npcDialogueParticipants.Remove(exchange.Listener);
        if (exchange.Completed is { } completedPackage) { completedPackage(); ++_completedPackages; }
        else
        {
            (SayToCompleted ?? throw new NotSupportedException("SayTo has no completion-event owner."))
                (exchange.Speaker, new HashSet<FalloutFormKey> { exchange.Topic });
            ++Channel(exchange.Speaker).CompletedCommands; ++_completedCommands;
        }
    }
}
