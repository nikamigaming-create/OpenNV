using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.World.Actors;

internal partial class RuntimeNativeSpeech : Node
{
    private sealed record PackageEventSpeech(FalloutFormKey Package, string Event);
    private sealed class Voice(FalloutFormKey reference, AudioStreamPlayer player)
    {
        internal readonly FalloutFormKey Reference = reference;
        internal readonly AudioStreamPlayer Player = player;
        internal NativeOwnedAnimationSoundPlayer? ResponseSound;
        internal FalloutSayToCommand? Command;
        internal string? CommandKind;
        internal long CompletedCommands, Generation;
        internal FalloutFormKey? Topic;
        internal FalloutFormKey? Listener;
        internal FalloutDialogueInfo? Info;
        internal int ResponseIndex;
        internal FalloutDialogueSpeaker? Identity;
        internal FalloutDialogueVoiceBinding? Binding;
        internal string? LipSha256;
        internal bool Advance;
        internal FaceGenLipAnimation? Lip;
        internal float[] LipWeights = [];
        internal RuntimeNativeNpc? Speaker;
        internal RuntimeNativeNpc? ListenerAnimationOwner;
        internal RuntimeNativeCreature? Creature;
        internal Node3D? Presentation;
        internal bool TalkingActivator;
        internal FalloutFormKey DialogueSubject;
        internal Action? ResponseCompleted;
        internal Action? PackageCompleted;
        internal PackageEventSpeech? PackageEvent;
        internal NpcDialogueExchange? NpcExchange;
        internal FalloutRadioConversation? Radio;
    }

    private readonly Dictionary<FalloutFormKey, Voice> _channels = [];
    private Voice? _lastVoice, _conversationVoice;
    private readonly Dictionary<string, FalloutDialogueTopic> _topics = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<FalloutFormKey> _said = [];
    private FalloutPluginStack _stack = null!;
    private FaceGenLipConfiguration _lipConfiguration = null!;
    private Func<FalloutFormKey, float> _questStage = null!;
    private FalloutQuestState? _quests;
    private FalloutDialogueQuestSelection? _selection;
    private Func<bool>? _playerFemale;
    private Func<FalloutFormKey, FalloutFormKey>? _actorRace;
    private Func<FalloutFormKey, int, float>? _actorValue;
    private Func<FalloutFormKey, FalloutFormKey?>? _currentPackage;
    private Func<int>? _vampireQuery;
    private Func<FalloutFormKey, FalloutFormKey, double>? _itemCount;
    private Func<FalloutFormKey, FalloutFormKey, float>? _referenceDistance;
    private Func<FalloutFormKey, FalloutFormKey, bool>? _referenceInZone;
    private Func<FalloutFormKey, int>? _sitting;
    private Func<FalloutFormKey, int>? _deadCount;
    private Func<uint, uint>? _dialogueRandom;
    private FalloutReferenceWorld? _references;
    private Func<FalloutCondition, float>? _conditionContext;
    private Func<FalloutFormKey, FalloutActorTemplateSelection?>? _templates;
    private Func<FalloutFormKey, FalloutSoundRandomState>? _soundRandom;
    private Func<FalloutFormKey, Node3D?>? _presentation;
    private float _unitsToMetres;
    private long _completedCommands, _completedPackages;
    private long _requestedPackageEventTopics, _completedPackageEventTopics;
    private readonly FalloutSpeechCompletionEvents _emptyCompletions = new();
    private FalloutDialogueVoiceIndex? _voiceIndex;
    private long _disabledCommands;
    private FalloutFormKey? _lastDisabledParticipant;
    private readonly SortedSet<string> _unbound = new(StringComparer.Ordinal);
    internal IReadOnlyCollection<string> Unbound => _unbound;
    internal Action<FalloutDialogueInfo, FalloutFormKey, bool>? ExecuteResults { get; set; }
    internal event Action<FalloutFormKey>? InfoCompleted;
    internal event Action<FalloutSpeechCompletionReceipt>? SayToCompleted;
    internal Action<FalloutSpeechSubtitle>? PrepareSubtitle { get; set; }
    internal Action<string> ReportDivergence { get; set; } = GD.PushError;
    // Overlapping audio is actor-owned. HUD subtitle arbitration is a separate
    // contract: retain every candidate and expose ambiguity rather than guess.
    internal FalloutSpeechSubtitle? Subtitle
    {
        get
        {
            if (Error is not null) return null;
            var candidates = SubtitleCandidates();
            return candidates.Length == 1 ? candidates[0] : null;
        }
    }
    private FalloutSpeechSubtitle[] SubtitleCandidates() => _channels.Values.Select(SubtitleFor)
        .OfType<FalloutSpeechSubtitle>().ToArray();
    private static FalloutSpeechSubtitle? SubtitleFor(Voice voice) => voice.Info is not null && voice.Topic is { } topic ?
        new(voice.Reference, voice.Info.Record.FormKey, topic, voice.Info.Responses[voice.ResponseIndex].Text,
            voice.Command!.ForceSubtitles) : null;
    internal string? Error { get; private set; }
    internal bool Active => Error is not null || _deferredRequests.Count != 0 || _npcDialogueParticipants.Count != 0 || _channels.Values.Any(voice => voice.Info is not null) || _emptyCompletions.Active;
    internal bool IsTalking(FalloutFormKey reference)
    {
        return _channels.TryGetValue(reference, out var voice) && voice.Info is not null;
    }
    internal void SkipResponse()
    {
        if (_conversationVoice is not { Info: not null } voice || Error is not null) return;
        voice.Player.Stop();
        ClearResponseSound(voice);
        voice.Advance = true;
    }

    internal void StartResponse(FalloutFormKey speaker, FalloutDialogueInfo info, int response, Action completed,
        FalloutFormKey? dialogueSubject = null, FalloutDialogueSpeaker? identity = null)
    {
        if (Error is not null) throw new InvalidOperationException(Error);
        var voice = Channel(speaker);
        if (voice.Info is not null || voice.Player.Playing || _conversationVoice is { Info: not null })
            throw new InvalidOperationException("Dialogue actor voice owner is busy.");
        var record = _stack.GetEffective(speaker);
        var name = record.ReadSubrecords().SingleOrDefault(field => field.Signature == "EDID").Data;
        voice.Command = new(name.IsEmpty ? speaker.ToString() : FalloutDialogueTopic.Text(name.Span), "player", info.Record.FormKey.ToString());
        voice.CommandKind = "conversation";
        voice.Radio = null; voice.Player.VolumeDb = 0;
        voice.Listener = _stack.RuntimeFormKey(0x14);
        voice.Topic = null;
        BindSpeaker(voice, record, ResidentSpeaker(record));
        if (dialogueSubject is { } subject) voice.DialogueSubject = subject;
        if (identity is not null) voice.Identity = identity;
        voice.Info = info; voice.ResponseIndex = response; voice.ResponseCompleted = completed;
        voice.PackageCompleted = null; voice.PackageEvent = null;
        ++voice.Generation; _lastVoice = _conversationVoice = voice;
        try { PlayResponse(voice); }
        catch (Exception error) { Fail(error); throw; }
    }
    internal object State
    {
        get
        {
            var voice = _channels.Values.FirstOrDefault(channel => channel.Info is not null) ?? _lastVoice;
            return new
            {
                info = voice?.Info?.Record.FormKey.ToString(),
                speaker = voice?.Command?.SpeakerEditorId,
                speakerReference = voice?.Reference.ToString(),
                listenerReference = voice?.Listener?.ToString(),
                command = voice?.CommandKind,
                listenerLookOwner = voice?.CommandKind == "SayTo" ? "unbound" : "not-requested",
                completedCommands = _completedCommands,
                completedPackages = _completedPackages,
                requestedPackageEventTopics = _requestedPackageEventTopics,
                completedPackageEventTopics = _completedPackageEventTopics,
                packageEvent = voice?.PackageEvent,
                disabledCommands = _disabledCommands,
                lastDisabledParticipant = _lastDisabledParticipant?.ToString(),
                emptyCompletions = _emptyCompletions.State,
                finishedFailureCapturable = CanCaptureFinishedFailure,
                finishedStateCapturable = CanCaptureFinishedState,
                finishedFailureReceipt = _emptyCompletions.FinishedFailureReceipt,
                lastCompletedFailure = _lastCompletedFailure,
                voiceBinding = voice?.Binding,
                responseSound = voice?.ResponseSound?.LastEvent,
                audioSha256 = voice?.Player.Stream?.GetMeta("opennv_owned_media_sha256", "").AsString(),
                lipSha256 = voice?.LipSha256,
                unbound = _unbound.ToArray(),
                response = voice?.Info?.Responses[voice.ResponseIndex].Number,
                text = voice?.Info?.Responses[voice.ResponseIndex].Text,
                positionSeconds = voice?.Player.GetPlaybackPosition() ?? 0.0,
                lipWeights = voice?.LipWeights ?? [],
                facePoseOwner = voice?.Lip is null ? "source-lip-absent" : voice.TalkingActivator ? "talking-activator-lip-animation-unbound" :
                    voice.Speaker is null ? "creature-speech-face-unbound" : !IsInstanceValid(voice.Speaker) ?
                    "speaker-presentation-unloaded" : "owned-tri-lip-morphs",
                face = IsInstanceValid(voice?.Speaker) ? voice!.Speaker!.FaceState : null,
                lipHeadMotionOwner = "unbound",
                speakerAnimation = voice?.Info?.Responses[voice.ResponseIndex].SpeakerAnimation?.ToString(),
                speakerAnimationOwner = voice?.Info?.Responses[voice.ResponseIndex].SpeakerAnimation is null
                    ? "package-idle" : "owned-response-idle",
                spatialAudioOwner = "unbound",
                forceSubtitles = voice?.Command?.ForceSubtitles == true,
                sayToTopic = voice?.Topic?.ToString(),
                said = _said.Select(key => key.ToString()).ToArray(),
                channels = _channels.Values.Select(ChannelState).ToArray(),
                deferredRequests = _deferredRequests.Values.Select(request => new
                { speaker = request.Speaker.ToString(), topic = request.Topic.ToString(), target = request.Target?.ToString() }).ToArray(),
                radio = RadioState,
                subtitleCandidates = SubtitleCandidates(),
                error = Error,
            };
        }
    }
    private static object ChannelState(Voice voice) => new
    {
        packageEvent = voice.PackageEvent,
        speakerReference = voice.Reference.ToString(),
        listenerReference = voice.Listener?.ToString(),
        info = voice.Info?.Record.FormKey.ToString(),
        command = voice.CommandKind,
        radioCompletedLines = voice.Radio?.CompletedLines,
        completedCommands = voice.CompletedCommands,
        generation = voice.Generation,
        active = voice.Info is not null,
        playing = voice.Player.Playing,
        voiceBinding = voice.Binding,
        responseSound = voice.ResponseSound?.LastEvent,
        audioSha256 = voice.Player.Stream?.GetMeta("opennv_owned_media_sha256", "").AsString(),
        lipSha256 = voice.LipSha256,
        response = voice.Info?.Responses[voice.ResponseIndex].Number,
        text = voice.Info?.Responses[voice.ResponseIndex].Text,
        positionSeconds = voice.Player.GetPlaybackPosition(),
        lipWeights = voice.LipWeights,
        face = IsInstanceValid(voice.Speaker) ? voice.Speaker!.FaceState : null,
        speakerAnimation = voice.Info?.Responses[voice.ResponseIndex].SpeakerAnimation?.ToString(),
        forceSubtitles = voice.Command?.ForceSubtitles == true,
        sayToTopic = voice.Topic?.ToString(),
    };

    internal void Configure(FalloutPluginStack stack, FaceGenLipConfiguration lipConfiguration,
        Func<FalloutFormKey, float> questStage,
        Func<FalloutCondition, float>? conditionContext = null, HashSet<FalloutFormKey>? saidInfos = null,
        Func<FalloutFormKey, FalloutActorTemplateSelection?>? templates = null,
        Func<FalloutFormKey, FalloutSoundRandomState>? soundRandom = null, float unitsToMetres = 0, FalloutQuestState? quests = null,
        Func<bool>? playerFemale = null, Func<FalloutFormKey, FalloutFormKey>? actorRace = null,
        Func<FalloutFormKey, int, float>? actorValue = null, Func<uint, uint>? dialogueRandom = null,
        FalloutReferenceWorld? references = null, Func<FalloutFormKey, FalloutFormKey?>? currentPackage = null,
        Func<int>? vampireQuery = null, Func<FalloutFormKey, FalloutFormKey, double>? itemCount = null,
        Func<FalloutFormKey, Node3D?>? presentation = null,
        Func<FalloutFormKey, FalloutFormKey, float>? referenceDistance = null,
        Func<FalloutFormKey, FalloutFormKey, bool>? referenceInZone = null, Func<FalloutFormKey, int>? sitting = null,
        Func<FalloutFormKey, int>? deadCount = null)
    {
        _stack = stack;
        _lipConfiguration = lipConfiguration;
        _questStage = questStage;
        _quests = quests;
        _selection = quests is null ? null : new(stack, quests);
        _playerFemale = playerFemale;
        _actorRace = actorRace;
        _actorValue = actorValue;
        _currentPackage = currentPackage;
        _vampireQuery = vampireQuery;
        _itemCount = itemCount;
        _referenceDistance = referenceDistance;
        _referenceInZone = referenceInZone;
        _sitting = sitting;
        _deadCount = deadCount;
        _dialogueRandom = dialogueRandom;
        _references = references;
        _conditionContext = conditionContext;
        _templates = templates;
        _soundRandom = soundRandom; _unitsToMetres = unitsToMetres;
        _presentation = presentation;
        _said = saidInfos ?? [];
        _voiceIndex = null;
        Name = "SourceSpeech";
    }

    private Voice Channel(FalloutFormKey speaker)
    {
        if (_channels.TryGetValue(speaker, out var voice)) return voice;
        var player = new AudioStreamPlayer { Name = "OwnedActorVoice" };
        voice = new(speaker, player);
        if (_restoredVoiceHistory.TryGetValue(speaker, out var restored))
        { voice.Generation = restored.Generation; voice.CompletedCommands = restored.CompletedCommands; }
        var channel = voice;
        player.Finished += () => channel.Advance = true;
        _channels.Add(speaker, voice);
        AddChild(player);
        return voice;
    }

    internal void Start(FalloutSayToCommand command)
    {
        if (Error is not null) return;
        try { StartCore(command); }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException or FileNotFoundException or InvalidOperationException)
        {
            Fail(error);
        }
    }

    private void StartCore(FalloutSayToCommand command)
    {
        var speakers = new[] { "ACHR", "ACRE", "REFR" }.SelectMany(signature => _stack.EffectiveRecords(signature)).Where(record =>
            record.ReadSubrecords().Any(field => field.Signature == "EDID" &&
                FalloutDialogueTopic.Text(field.Data.Span).Equals(command.SpeakerEditorId, StringComparison.OrdinalIgnoreCase))).ToArray();
        if (speakers.Length != 1) throw new InvalidDataException("SayTo speaker reference is absent or ambiguous.");
        var speaker = speakers[0];
        var target = command.TargetEditorId.Equals("player", StringComparison.OrdinalIgnoreCase) ? _stack.RuntimeFormKey(0x14) :
            new[] { "ACHR", "ACRE", "REFR" }.SelectMany(signature => _stack.EffectiveRecords(signature)).Single(record =>
                record.ReadSubrecords().Any(field => field.Signature == "EDID" &&
                    FalloutDialogueTopic.Text(field.Data.Span).Equals(command.TargetEditorId, StringComparison.OrdinalIgnoreCase))).FormKey;
        StartCore(command, speaker, FalloutDialogueTopic.Find(_stack, "DIAL", command.TopicEditorId).FormKey, target);
    }

    internal void SayTo(FalloutFormKey speaker, FalloutFormKey target, FalloutFormKey topic, bool forceSubtitles = false)
    {
        ExecuteCommand(new(speaker.ToString(), target.ToString(), topic.ToString(), forceSubtitles), speaker, topic, target);
    }
    internal void Say(FalloutFormKey speaker, FalloutFormKey topic, bool forceSubtitles = false) =>
        ExecuteCommand(new(speaker.ToString(), "", topic.ToString(), forceSubtitles), speaker, topic, null);
    internal void StartPackageSpeech(FalloutFormKey speaker, FalloutFormKey target, FalloutFormKey topic, Action completed) =>
        ExecuteCommand(new(speaker.ToString(), target.ToString(), topic.ToString()), speaker, topic, target, completed);

    internal void StartPackageEventTopic(FalloutFormKey speaker, FalloutFormKey package, string kind, FalloutFormKey topic)
    {
        var source = FalloutScriptPackage.Read(_stack.GetEffective(package));
        if (!source.EventPrograms.TryGetValue(kind, out var program) || program.Topic != topic)
            throw new InvalidDataException("Package speech differs from its reached source event topic.");
        // A declarative event topic has no invented listener or scripted
        // SayToDone. Its audio/results finish independently of the event IDLE.
        ExecuteCommand(new(speaker.ToString(), "", topic.ToString()), speaker, topic, null,
            () =>
            {
                ++_completedPackageEventTopics;
                GD.Print($"OPENNV_NATIVE_PACKAGE_TOPIC_END speaker={speaker} package={package} event={kind} topic={topic} owner=audio-results-completion");
            }, packageEvent: new(package, kind));
        ++_requestedPackageEventTopics;
        GD.Print($"OPENNV_NATIVE_PACKAGE_TOPIC_REQUEST speaker={speaker} package={package} event={kind} topic={topic} owner=source-package-event");
    }

    private void ExecuteCommand(FalloutSayToCommand command, FalloutFormKey speaker, FalloutFormKey topic, FalloutFormKey? target,
        Action? packageCompleted = null, bool npcConversation = false, PackageEventSpeech? packageEvent = null)
    {
        if (Error is not null) throw new InvalidOperationException(Error);
        try
        {
            if (!DeferScriptedSpeech(command, speaker, topic, target, packageCompleted, npcConversation))
                StartCore(command, _stack.GetEffective(speaker), topic, target, packageCompleted,
                    forceNpcConversation: npcConversation, packageEvent: packageEvent);
        }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException or FileNotFoundException or InvalidOperationException)
        {
            Fail(error); throw;
        }
    }

    private void StartCore(FalloutSayToCommand command, FalloutPluginRecord speaker, FalloutFormKey topicForm, FalloutFormKey? target,
        Action? packageCompleted = null, NpcDialogueExchange? exchange = null, FalloutDialogueInfo? selectedInfo = null,
        bool forceNpcConversation = false, PackageEventSpeech? packageEvent = null, FalloutRadioConversation? radio = null)
    {
        if (speaker.Signature is not ("ACHR" or "ACRE" or "REFR")) throw new InvalidDataException("Scripted speaker is not a dialogue reference.");
        var npcKey = FalloutDialogueTopic.RequiredForm(speaker, "NAME");
        var npc = _stack.GetEffective(npcKey);
        if (!(speaker.Signature == "ACHR" && npc.Signature == "NPC_" || speaker.Signature == "ACRE" && npc.Signature == "CREA" ||
            speaker.Signature == "REFR" && npc.Signature == "TACT")) throw new NotSupportedException("SayTo speaker has no source dialogue owner.");
        if (_stack.GetEffective(topicForm).Signature != "DIAL") throw new InvalidDataException("Scripted speech topic is not DIAL.");
        var references = _references ?? throw new NotSupportedException("Scripted speech has no shared reference enable owner.");
        var listenerReference = target is { } listener && listener != _stack.RuntimeFormKey(0x14) ? _stack.GetEffective(listener) : null;
        if (listenerReference is not null && !(listenerReference.Signature is "ACHR" or "ACRE" ||
            listenerReference.Signature == "REFR" && _stack.GetEffective(FalloutDialogueTopic.RequiredForm(listenerReference, "NAME")).Signature == "TACT"))
            throw new InvalidDataException("SayTo listener is not a source dialogue reference.");
        // Disabled participants have no active speech process. Native commands
        // accept this case without voice selection, results or SayToDone.
        // An enabled participant still requires its actual resident actor.
        var disabled = !references.IsEnabled(speaker.FormKey) ? speaker.FormKey :
            listenerReference is not null && !references.IsEnabled(listenerReference.FormKey) ? listenerReference.FormKey : (FalloutFormKey?)null;
        if (disabled is { } disabledParticipant)
        {
            if (exchange is not null) throw new NotSupportedException("An NPC dialogue participant was disabled during its continuation.");
            if (IsTalking(speaker.FormKey))
                throw new NotSupportedException("Disabling an active speech participant requires its interruption owner.");
            ++_disabledCommands; _lastDisabledParticipant = disabledParticipant;
            return;
        }
        if (listenerReference is not null)
        {
            _ = ResidentSpeaker(listenerReference);
        }
        if (!_topics.TryGetValue(command.TopicEditorId, out var topic))
            _topics.Add(command.TopicEditorId, topic = FalloutDialogueTopic.Read(_stack, topicForm));
        var actor = radio is null ? ResidentSpeaker(speaker) : _presentation?.Invoke(speaker.FormKey);
        var npcConversation = forceNpcConversation || exchange is not null || listenerReference is not null && FalloutDialogueTopic.Type(_stack, topicForm) == 1;
        var info = selectedInfo ?? SelectSpeechInfo(topic, speaker.FormKey, target, npcConversation);
        if (info is null)
        {
            if (IsTalking(speaker.FormKey))
                throw new NotSupportedException("Empty speech interrupting the actor's active voice requires its interruption owner.");
            if (packageCompleted is null) _emptyCompletions.Mark(speaker.FormKey, topicForm);
            else _emptyCompletions.MarkPackage(speaker.FormKey, topicForm, packageCompleted);
            return;
        }
        var voice = Channel(speaker.FormKey);
        if (voice.Info is not null || voice.Player.Playing)
            throw new NotSupportedException("Replacing the actor's active speech requires its interruption owner.");
        if (exchange is null && npcConversation && (forceNpcConversation || packageCompleted is not null && info.Choices.Count != 0))
            exchange = BeginNpcExchange(speaker.FormKey, target!.Value, topicForm, command, packageCompleted);
        if (radio is null) BindSpeaker(voice, speaker, actor!);
        else BindRadioSpeaker(voice, speaker, actor, radio);
        voice.Command = command;
        voice.Radio = radio;
        if (radio is null) voice.Player.VolumeDb = 0;
        voice.CommandKind = radio is not null ? "radio-conversation" : packageEvent is not null ? "package-event-topic" :
            packageCompleted is not null ? "dialogue-package" : command.TargetEditorId.Length == 0 ? "Say" : "SayTo";
        voice.PackageCompleted = exchange is null ? packageCompleted : null;
        voice.PackageEvent = packageEvent;
        voice.NpcExchange = exchange;
        voice.Listener = target;
        voice.Topic = topicForm;
        voice.Info = info;
        voice.ResponseIndex = 0;
        voice.Binding = null; voice.LipSha256 = null; voice.Player.Stream = null;
        voice.Lip = null; voice.LipWeights = [];
        ++voice.Generation; _lastVoice = voice;
        if (exchange is { Completed: null, Completion: null })
            exchange.Completion = new(exchange.Speaker, new HashSet<FalloutFormKey> { exchange.Topic },
                info.Record.FormKey, voice.Generation);
        if ((info.Flags & 4) != 0) _said.Add(info.Record.FormKey);
        RunResults(info, voice.DialogueSubject, true);
        if ((info.Flags & 8) != 0) RunResults(info, voice.DialogueSubject, false);
        PlayResponse(voice);
    }

    internal Node3D ResolveSpeaker(FalloutFormKey speaker) => ResidentSpeaker(_stack.GetEffective(speaker));
    internal FalloutFormKey DialogueSubject(FalloutFormKey speaker) => _references?.DialogueSubject(speaker) ?? speaker;
    internal FalloutDialogueSpeaker SpeakerIdentity(FalloutFormKey speaker) => _references?.DialogueIdentity(speaker) ??
        FalloutDialogueSpeaker.Read(_stack, FalloutDialogueTopic.RequiredForm(_stack.GetEffective(speaker), "NAME"), _templates?.Invoke(speaker));

    private Node3D ResidentSpeaker(FalloutPluginRecord speaker)
    {
        if (_stack.GetEffective(FalloutDialogueTopic.RequiredForm(speaker, "NAME")).Signature == "TACT")
        {
            if (speaker.Signature != "REFR") throw new InvalidDataException("Talking activator speaker is not REFR.");
            var node = (_presentation ?? throw new NotSupportedException("Talking activator speech has no resident presentation owner."))(speaker.FormKey);
            if (node is null || !node.IsVisibleInTree()) throw new NotSupportedException($"Talking activator {speaker.FormKey} has no visible source model.");
            return node;
        }
        if (_presentation is { } presentation)
        {
            var node = presentation(speaker.FormKey);
            if (node is not RuntimeNativeNpc && node is not RuntimeNativeCreature || !node.IsVisibleInTree())
                throw new NotSupportedException($"Source speaker {speaker.FormKey} has no visible resident runtime actor.");
            return node;
        }
        var actors = GetTree().Root.FindChildren("*", "", true, false).OfType<Node3D>()
            .Where(actor => (actor is RuntimeNativeNpc npc && npc.Appearance.Reference == speaker.FormKey ||
                actor is RuntimeNativeCreature creature && creature.Appearance.Reference == speaker.FormKey) && actor.IsVisibleInTree()).ToArray();
        if (actors.Length != 1) throw new NotSupportedException($"Source speaker {speaker.FormKey} has {actors.Length} resident runtime actors.");
        return actors[0];
    }

    private void BindSpeaker(Voice voice, FalloutPluginRecord speaker, Node3D actor)
    {
        EndListenerAnimation(voice);
        if (IsInstanceValid(voice.Speaker))
        {
            voice.Speaker!.ClearSpeechFace();
            voice.Speaker.EndResponseAnimation();
        }
        voice.Speaker = actor as RuntimeNativeNpc;
        voice.Creature = actor as RuntimeNativeCreature;
        voice.Presentation = actor;
        voice.TalkingActivator = _stack.GetEffective(FalloutDialogueTopic.RequiredForm(speaker, "NAME")).Signature == "TACT";
        voice.DialogueSubject = DialogueSubject(speaker.FormKey);
        voice.Identity = SpeakerIdentity(speaker.FormKey);
    }

    private void PlayResponse(Voice voice)
    {
        var info = voice.Info ?? throw new InvalidOperationException("Source INFO was lost.");
        var response = info.Responses[voice.ResponseIndex];
        if (SubtitleFor(voice) is { } subtitle)
            (PrepareSubtitle ?? throw new NotSupportedException("SayTo has no subtitle presentation owner."))(subtitle);
        if (SubtitleCandidates().Length > 1 && _unbound.Add("concurrent-subtitle-selection"))
            GD.PushWarning("OPENNV_NATIVE_SPEECH_UNBOUND concurrent-subtitle-selection candidates=retained-in-telemetry");
        voice.Binding = null; voice.LipSha256 = null; voice.Advance = false;
        voice.Lip = null; voice.LipWeights = [];
        ClearResponseSound(voice);
        EndListenerAnimation(voice);
        if (response.ListenerAnimation is { } listenerAnimation)
        {
            var listener = voice.Listener ?? throw new InvalidDataException("Response listener IDLE has no source listener.");
            voice.ListenerAnimationOwner = ResolveSpeaker(listener) as RuntimeNativeNpc ??
                throw new NotSupportedException($"Response listener IDLE {listenerAnimation} requires its humanoid target animation owner.");
            voice.ListenerAnimationOwner.BeginResponseAnimation(_stack, listenerAnimation);
        }
        if (!IsInstanceValid(voice.Speaker) && response.SpeakerAnimation is not null)
            throw new NotSupportedException($"Response IDLE {response.SpeakerAnimation} requires its live humanoid animation owner.");
        if (IsInstanceValid(voice.Speaker)) voice.Speaker!.BeginResponseAnimation(_stack, response.SpeakerAnimation);
        if (response.Sound is { } sound)
        {
            if (info.Speaker is { } specified && specified != voice.Identity!.Actor)
                throw new InvalidDataException("Explicit dialogue sound belongs to a different actor.");
            var actor = IsInstanceValid(voice.Presentation) ? voice.Presentation! :
                throw new NotSupportedException("Source sound response has no live speaker presentation owner.");
            voice.ResponseSound = new(_stack, RuntimeLiveContentSource.Current!, actor, _unitsToMetres,
                (_soundRandom ?? throw new NotSupportedException("Response SOUN has no retained random owner."))(voice.Reference));
            AddChild(voice.ResponseSound);
            var disposition = voice.ResponseSound.DispatchSound(sound, actor, () => voice.Advance = true);
            if (disposition == "unbound-source-sound")
                throw new NotSupportedException(string.Join("; ", voice.ResponseSound.Unbound));
            foreach (var lane in voice.ResponseSound.Unbound) _unbound.Add(lane);
            GD.Print($"OPENNV_NATIVE_SPEECH_BEGIN info={info.Record.FormKey} response={response.Number} sound={sound} owner=source-SOUN disposition={disposition}");
            return;
        }
        _voiceIndex ??= new((RuntimeLiveContentSource.Current ?? throw new InvalidOperationException("Owned source is absent.")).ResourcePathsUnder("sound/voice"));
        voice.Binding = _voiceIndex.Resolve(voice.Identity ?? throw new InvalidOperationException("Source voice type was lost."), info, voice.ResponseIndex);
        var lipPath = voice.Binding.LipPath;
        if (RuntimeLiveContentSource.Current!.TryRead(lipPath, null, out var lipBytes, out _))
        {
            voice.Lip = FaceGenLipAnimation.Read(lipBytes, _lipConfiguration);
            voice.LipSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(lipBytes));
            voice.LipWeights = new float[voice.Lip.TargetNames.Count];
            if (IsInstanceValid(voice.Speaker)) voice.Speaker!.ValidateSpeechFace(_lipConfiguration);
        }
        else _unbound.Add("missing-source-lip:" + lipPath);
        if (voice.Creature is not null) _unbound.Add("creature-speech-face:" + voice.Reference);
        if (voice.TalkingActivator && voice.Lip is not null) _unbound.Add("talking-activator-lip-animation:" + voice.Reference);
        voice.Player.Stream = NativeOwnedMediaLoader.LoadAudio(voice.Binding.AudioPath);
        voice.Player.Play();
        GD.Print($"OPENNV_NATIVE_SPEECH_BEGIN info={info.Record.FormKey} response={response.Number} " +
            $"speaker={voice.Command!.SpeakerEditorId} voice={voice.Binding.AudioPath} lip={lipPath} voiceType={voice.Binding.VoiceType} " +
            $"speakerIdle={response.SpeakerAnimation} lipLoaded={voice.Lip is not null} facePose={(voice.TalkingActivator ? "talking-activator-unbound" : voice.Speaker is null ? "creature-unbound" : !IsInstanceValid(voice.Speaker) ? "speaker-presentation-unloaded" : "owned-tri-lip-morphs")} headMotion=unbound spatialAudio=unbound parity=unmeasured");
    }

    public override void _Process(double delta)
    {
        if (Error is not null)
        {
            if (!GetTree().Paused && CanCaptureFinishedFailure &&
                _emptyCompletions.FinishedFailureReceipt is { } failed && CanResumeSourceCompletion?.Invoke(failed.Completion()) == true)
                ResumeFinishedSource();
            return;
        }
        try
        {
            var frame = _channels.Values.Where(voice => voice.Info is not null)
                .Select(voice => (Voice: voice, voice.Generation)).ToArray();
            _emptyCompletions.Drain(DispatchSourceCompletion,
                () => IsInsideTree() && IsProcessing() && !GetTree().Paused);
            if (GetTree().Paused) return;
            foreach (var entry in frame)
                if (entry.Voice.Info is not null && entry.Voice.Generation == entry.Generation)
                {
                    if (entry.Voice.Radio is not null) UpdateRadioListener(entry.Voice);
                    ProcessVoice(entry.Voice);
                }
            AdvanceDeferredSpeech();
        }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException or FileNotFoundException or
            InvalidOperationException or KeyNotFoundException or OverflowException)
        {
            Fail(error);
        }
    }

    private void ProcessVoice(Voice voice)
    {
        if (voice.Lip is not null)
        {
            voice.Lip.Sample(voice.Player.GetPlaybackPosition(), voice.LipWeights);
            if (IsInstanceValid(voice.Speaker)) voice.Speaker!.ApplySpeechFace(_lipConfiguration, voice.LipWeights);
        }
        if (!voice.Advance) return;
        voice.Advance = false;
        ClearResponseSound(voice);
        EndListenerAnimation(voice);
        if (IsInstanceValid(voice.Speaker)) voice.Speaker!.EndResponseAnimation();
        if (voice.ResponseCompleted is { } completion)
        {
            voice.ResponseCompleted = null; voice.Info = null; voice.Lip = null; voice.LipWeights = [];
            if (IsInstanceValid(voice.Speaker)) voice.Speaker!.ClearSpeechFace();
            _conversationVoice = null;
            completion();
            return;
        }
        if (++voice.ResponseIndex < voice.Info!.Responses.Count) { PlayResponse(voice); return; }
        var completed = voice.Info;
        var completedTopic = voice.Topic;
        var packageCompleted = voice.PackageCompleted;
        var completedPackageEvent = voice.PackageEvent;
        var completedGeneration = voice.Generation;
        void FinishResponse()
        {
            voice.Topic = null; voice.PackageCompleted = null;
            voice.Info = null; voice.Lip = null; voice.LipWeights = [];
            if (IsInstanceValid(voice.Speaker)) voice.Speaker!.ClearSpeechFace();
            GD.Print($"OPENNV_NATIVE_SPEECH_END info={completed.Record.FormKey} speaker={voice.Reference} owner=audio-finished");
            if ((completed.Flags & 8) == 0) RunResults(completed, voice.DialogueSubject, false);
        }
        if (voice.Radio is null && voice.NpcExchange is null && packageCompleted is null && completedTopic is { } sourceTopic)
        {
            var receipt = new FalloutSpeechCompletionReceipt(voice.Reference,
                new HashSet<FalloutFormKey> { sourceTopic }, completed.Record.FormKey, voice.Generation);
            var identity = FalloutFinishedSpeechSourceBinding.Capture(_stack,
                (_references ?? throw new InvalidOperationException("Finished speech has no shared reference owner.")).Retained(voice.Reference), receipt);
            _emptyCompletions.CompleteFinished(receipt, identity.Source, FinishResponse, DispatchSourceCompletion, () =>
            {
                ++voice.CompletedCommands; ++_completedCommands;
                // The stage owner observes settled speech only after its source
                // completion committed. A failed prefix retains the active receipt.
                InfoCompleted?.Invoke(completed.Record.FormKey);
            });
            return;
        }
        FinishResponse();
        if (voice.Radio is { } radio)
        {
            InfoCompleted?.Invoke(completed.Record.FormKey);
            radio.CompleteLine();
            if (radio.Info is { } next)
                StartCore(voice.Command!, _stack.GetEffective(voice.Reference), radio.Topic!.Value, null,
                    selectedInfo: next, radio: radio);
            else
            {
                if (voice.Generation == completedGeneration && ReferenceEquals(voice.Radio, radio)) voice.Radio = null;
                GD.Print($"OPENNV_NATIVE_RADIO_END station={voice.Reference} lines={radio.CompletedLines} owner=source-links-audio-results");
            }
        }
        else if (voice.NpcExchange is { } exchange)
        {
            voice.NpcExchange = null;
            AdvanceNpcExchange(exchange, voice, completed, () => InfoCompleted?.Invoke(completed.Record.FormKey));
        }
        else
        {
            InfoCompleted?.Invoke(completed.Record.FormKey);
            if (packageCompleted is not null)
            {
                packageCompleted();
                if (completedPackageEvent is null) ++_completedPackages;
                // Results and settled notifications can start the actor's next
                // voice. Retire only this completed generation, after its
                // callback succeeds; never clear a replacement or failed owner.
                if (voice.Generation == completedGeneration) voice.PackageEvent = null;
            }
        }
    }

    private void DispatchSourceCompletion(FalloutSpeechCompletionReceipt receipt) =>
        (SayToCompleted ?? throw new NotSupportedException("SayTo has no completion-event owner."))(receipt);

    private void Fail(Exception error)
    {
        if (Error is not null) return;
        Error = error.Message;
        StopVoices();
        ReportDivergence($"OPENNV_NATIVE_SPEECH_DIVERGENCE {error.Message}");
    }

    public override void _ExitTree() => StopVoices();
    private void StopVoices()
    {
        _npcDialogueParticipants.Clear();
        foreach (var voice in _channels.Values)
        {
            voice.Player.Stop(); ClearResponseSound(voice);
            EndListenerAnimation(voice);
            if (IsInstanceValid(voice.Speaker))
            {
                voice.Speaker!.ClearSpeechFace(); voice.Speaker.EndResponseAnimation();
            }
            voice.Advance = false;
        }
    }

    private void RunResults(FalloutDialogueInfo info, FalloutFormKey speaker, bool begin)
    {
        if (!FalloutDialogueTopic.CodeLines(begin ? info.BeginScript : info.EndScript).Any()) return;
        (ExecuteResults ?? throw new NotSupportedException($"INFO {info.Record.FormKey} has no result-script owner."))(info, speaker, begin);
    }

    private static void ClearResponseSound(Voice voice)
    {
        if (IsInstanceValid(voice.ResponseSound)) voice.ResponseSound!.Free();
        voice.ResponseSound = null;
    }

    private static void EndListenerAnimation(Voice voice)
    {
        if (IsInstanceValid(voice.ListenerAnimationOwner)) voice.ListenerAnimationOwner!.EndResponseAnimation();
        voice.ListenerAnimationOwner = null;
    }
}
