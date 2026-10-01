using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Actors;

internal partial class RuntimeNativeSpeech : Node
{
    private sealed class Voice(FalloutFormKey reference, AudioStreamPlayer player)
    {
        internal readonly FalloutFormKey Reference = reference;
        internal readonly AudioStreamPlayer Player = player;
        internal NativeOwnedAnimationSoundPlayer? ResponseSound;
        internal FalloutSayToCommand? Command;
        internal string? CommandKind;
        internal long CompletedCommands, Generation;
        internal FalloutFormKey? Topic;
        internal FalloutDialogueInfo? Info;
        internal int ResponseIndex;
        internal FalloutDialogueSpeaker? Identity;
        internal FalloutDialogueVoiceBinding? Binding;
        internal string? LipSha256;
        internal bool Advance;
        internal FaceGenLipAnimation? Lip;
        internal float[] LipWeights = [];
        internal RuntimeNativeNpc? Speaker;
        internal RuntimeNativeCreature? Creature;
        internal Action? ResponseCompleted;
    }

    private readonly Dictionary<FalloutFormKey, Voice> _channels = [];
    private Voice? _lastVoice, _conversationVoice;
    private readonly Dictionary<string, FalloutDialogueTopic> _topics = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<FalloutFormKey> _said = [];
    private FalloutPluginStack _stack = null!;
    private FaceGenLipConfiguration _lipConfiguration = null!;
    private Func<FalloutFormKey, float> _questStage = null!;
    private FalloutQuestState? _quests;
    private Func<bool>? _playerFemale;
    private Func<FalloutFormKey, FalloutFormKey>? _actorRace;
    private Func<FalloutCondition, float>? _conditionContext;
    private Func<FalloutFormKey, FalloutActorTemplateSelection?>? _templates;
    private Func<FalloutFormKey, FalloutSoundRandomState>? _soundRandom;
    private float _unitsToMetres;
    private long _completedCommands;
    private readonly FalloutSpeechCompletionEvents _emptyCompletions = new();
    private FalloutDialogueVoiceIndex _voiceIndex = null!;
    private readonly SortedSet<string> _unbound = new(StringComparer.Ordinal);
    internal IReadOnlyCollection<string> Unbound => _unbound;
    internal Action<FalloutDialogueInfo, FalloutFormKey, bool>? ExecuteResults { get; set; }
    internal event Action<FalloutFormKey>? InfoCompleted;
    internal event Action<FalloutFormKey, IReadOnlySet<FalloutFormKey>>? SayToCompleted;
    internal Action<FalloutSpeechSubtitle>? PrepareSubtitle { get; set; }
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
    internal bool Active => Error is not null || _channels.Values.Any(voice => voice.Info is not null) || _emptyCompletions.Active;
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

    internal void StartResponse(FalloutFormKey speaker, FalloutDialogueInfo info, int response, Action completed)
    {
        if (Error is not null) throw new InvalidOperationException(Error);
        var voice = Channel(speaker);
        if (voice.Info is not null || voice.Player.Playing || _conversationVoice is { Info: not null })
            throw new InvalidOperationException("Dialogue actor voice owner is busy.");
        var record = _stack.GetEffective(speaker);
        var name = record.ReadSubrecords().SingleOrDefault(field => field.Signature == "EDID").Data;
        voice.Command = new(name.IsEmpty ? speaker.ToString() : FalloutDialogueTopic.Text(name.Span), "player", info.Record.FormKey.ToString());
        voice.CommandKind = "conversation";
        voice.Topic = null;
        BindSpeaker(voice, record, ResidentSpeaker(record));
        voice.Info = info; voice.ResponseIndex = response; voice.ResponseCompleted = completed;
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
                command = voice?.CommandKind,
                listenerLookOwner = voice?.CommandKind == "SayTo" ? "unbound" : "not-requested",
                completedCommands = _completedCommands,
                emptyCompletions = _emptyCompletions.State,
                voiceBinding = voice?.Binding,
                responseSound = voice?.ResponseSound?.LastEvent,
                audioSha256 = voice?.Player.Stream?.GetMeta("opennv_owned_media_sha256", "").AsString(),
                lipSha256 = voice?.LipSha256,
                unbound = _unbound.ToArray(),
                response = voice?.Info?.Responses[voice.ResponseIndex].Number,
                text = voice?.Info?.Responses[voice.ResponseIndex].Text,
                positionSeconds = voice?.Player.GetPlaybackPosition() ?? 0.0,
                lipWeights = voice?.LipWeights ?? [],
                facePoseOwner = voice?.Lip is null ? "source-lip-absent" : voice.Speaker is null ? "creature-speech-face-unbound" : "owned-tri-lip-morphs",
                face = voice?.Speaker?.FaceState,
                lipHeadMotionOwner = "unbound",
                speakerAnimation = voice?.Info?.Responses[voice.ResponseIndex].SpeakerAnimation?.ToString(),
                speakerAnimationOwner = voice?.Info?.Responses[voice.ResponseIndex].SpeakerAnimation is null
                    ? "package-idle" : "owned-response-idle",
                spatialAudioOwner = "unbound",
                forceSubtitles = voice?.Command?.ForceSubtitles == true,
                sayToTopic = voice?.Topic?.ToString(),
                said = _said.Select(key => key.ToString()).ToArray(),
                channels = _channels.Values.Select(ChannelState).ToArray(),
                subtitleCandidates = SubtitleCandidates(),
                error = Error,
            };
        }
    }
    private static object ChannelState(Voice voice) => new
    {
        speakerReference = voice.Reference.ToString(),
        info = voice.Info?.Record.FormKey.ToString(),
        command = voice.CommandKind,
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
        face = voice.Speaker?.FaceState,
        speakerAnimation = voice.Info?.Responses[voice.ResponseIndex].SpeakerAnimation?.ToString(),
        forceSubtitles = voice.Command?.ForceSubtitles == true,
        sayToTopic = voice.Topic?.ToString(),
    };

    internal void Configure(FalloutPluginStack stack, FaceGenLipConfiguration lipConfiguration,
        Func<FalloutFormKey, float> questStage,
        Func<FalloutCondition, float>? conditionContext = null, HashSet<FalloutFormKey>? saidInfos = null,
        Func<FalloutFormKey, FalloutActorTemplateSelection?>? templates = null,
        Func<FalloutFormKey, FalloutSoundRandomState>? soundRandom = null, float unitsToMetres = 0, FalloutQuestState? quests = null,
        Func<bool>? playerFemale = null, Func<FalloutFormKey, FalloutFormKey>? actorRace = null)
    {
        _stack = stack;
        _lipConfiguration = lipConfiguration;
        _questStage = questStage;
        _quests = quests;
        _playerFemale = playerFemale;
        _actorRace = actorRace;
        _conditionContext = conditionContext;
        _templates = templates;
        _soundRandom = soundRandom; _unitsToMetres = unitsToMetres;
        _said = saidInfos ?? [];
        _voiceIndex = new((RuntimeLiveContentSource.Current ?? throw new InvalidOperationException("Owned source is absent.")).ResourcePathsUnder("sound/voice"));
        Name = "SourceSpeech";
    }

    private Voice Channel(FalloutFormKey speaker)
    {
        if (_channels.TryGetValue(speaker, out var voice)) return voice;
        var player = new AudioStreamPlayer { Name = "OwnedActorVoice" };
        voice = new(speaker, player);
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
        if (!command.TargetEditorId.Equals("player", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("SayTo target needs its runtime actor owner.");
        var speakers = new[] { "ACHR", "ACRE" }.SelectMany(signature => _stack.EffectiveRecords(signature)).Where(record =>
            record.ReadSubrecords().Any(field => field.Signature == "EDID" &&
                FalloutDialogueTopic.Text(field.Data.Span).Equals(command.SpeakerEditorId, StringComparison.OrdinalIgnoreCase))).ToArray();
        if (speakers.Length != 1) throw new InvalidDataException("SayTo speaker reference is absent or ambiguous.");
        var speaker = speakers[0];
        StartCore(command, speaker, FalloutDialogueTopic.Find(_stack, "DIAL", command.TopicEditorId).FormKey);
    }

    internal void SayTo(FalloutFormKey speaker, FalloutFormKey target, FalloutFormKey topic, bool forceSubtitles = false)
    {
        if (_stack.RuntimeFormId(target) != 0x14) throw new NotSupportedException("SayTo target needs its runtime actor owner.");
        ExecuteCommand(new(speaker.ToString(), "player", topic.ToString(), forceSubtitles), speaker, topic);
    }
    internal void Say(FalloutFormKey speaker, FalloutFormKey topic, bool forceSubtitles = false) =>
        ExecuteCommand(new(speaker.ToString(), "", topic.ToString(), forceSubtitles), speaker, topic);
    private void ExecuteCommand(FalloutSayToCommand command, FalloutFormKey speaker, FalloutFormKey topic)
    {
        if (Error is not null) throw new InvalidOperationException(Error);
        try { StartCore(command, _stack.GetEffective(speaker), topic); }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException or FileNotFoundException or InvalidOperationException)
        {
            Fail(error); throw;
        }
    }

    private void StartCore(FalloutSayToCommand command, FalloutPluginRecord speaker, FalloutFormKey topicForm)
    {
        var npcKey = FalloutDialogueTopic.RequiredForm(speaker, "NAME");
        var npc = _stack.GetEffective(npcKey);
        if (npc.Signature is not ("NPC_" or "CREA")) throw new NotSupportedException("SayTo speaker is not an actor.");
        if (!_topics.TryGetValue(command.TopicEditorId, out var topic))
            _topics.Add(command.TopicEditorId, topic = FalloutDialogueTopic.Read(_stack, topicForm));
        var actor = ResidentSpeaker(speaker);
        var identity = FalloutDialogueSpeaker.Read(_stack, npcKey, _templates?.Invoke(speaker.FormKey));
        var conditions = new FalloutDialogueConditions(_stack,
            _quests ?? throw new NotSupportedException("Scripted speech selection has no shared quest state."), speaker.FormKey, identity,
            _conditionContext, playerFemale: _playerFemale, actorRace: _actorRace);
        var info = topic.Select(npcKey, _said, _questStage, conditions.Evaluate);
        if (info is null)
        {
            if (IsTalking(speaker.FormKey))
                throw new NotSupportedException("Empty speech interrupting the actor's active voice requires its interruption owner.");
            _emptyCompletions.Mark(speaker.FormKey, topicForm);
            return;
        }
        var voice = Channel(speaker.FormKey);
        if (voice.Info is not null || voice.Player.Playing)
            throw new NotSupportedException("Replacing the actor's active speech requires its interruption owner.");
        BindSpeaker(voice, speaker, actor);
        voice.Command = command;
        voice.CommandKind = command.TargetEditorId.Length == 0 ? "Say" : "SayTo";
        voice.Topic = topicForm;
        voice.Info = info;
        voice.ResponseIndex = 0;
        voice.Binding = null; voice.LipSha256 = null; voice.Player.Stream = null;
        voice.Lip = null; voice.LipWeights = [];
        ++voice.Generation; _lastVoice = voice;
        if ((info.Flags & 4) != 0) _said.Add(info.Record.FormKey);
        RunResults(info, speaker.FormKey, true);
        PlayResponse(voice);
    }

    private Node3D ResidentSpeaker(FalloutPluginRecord speaker)
    {
        var actors = GetTree().Root.FindChildren("*", "", true, false).OfType<Node3D>()
            .Where(actor => (actor is RuntimeNativeNpc npc && npc.Appearance.Reference == speaker.FormKey ||
                actor is RuntimeNativeCreature creature && creature.Appearance.Reference == speaker.FormKey) && actor.IsVisibleInTree()).ToArray();
        if (actors.Length != 1) throw new NotSupportedException($"Source speaker {speaker.FormKey} has {actors.Length} resident runtime actors.");
        return actors[0];
    }

    private void BindSpeaker(Voice voice, FalloutPluginRecord speaker, Node3D actor)
    {
        voice.Speaker?.ClearSpeechFace();
        voice.Speaker?.EndResponseAnimation();
        voice.Speaker = actor as RuntimeNativeNpc;
        voice.Creature = actor as RuntimeNativeCreature;
        voice.Identity = FalloutDialogueSpeaker.Read(_stack, FalloutDialogueTopic.RequiredForm(speaker, "NAME"), _templates?.Invoke(speaker.FormKey));
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
        if (response.ListenerAnimation is not null)
            throw new NotSupportedException($"Response listener IDLE {response.ListenerAnimation} requires its target animation owner.");
        if (voice.Creature is not null && response.SpeakerAnimation is not null)
            throw new NotSupportedException($"Creature response IDLE {response.SpeakerAnimation} requires its animation blend owner.");
        voice.Speaker?.BeginResponseAnimation(_stack, response.SpeakerAnimation);
        if (response.Sound is { } sound)
        {
            if (info.Speaker is { } specified && specified != voice.Identity!.Actor)
                throw new InvalidDataException("Explicit dialogue sound belongs to a different actor.");
            var actor = (Node3D?)voice.Speaker ?? voice.Creature ?? throw new InvalidOperationException("Sound response lost its speaker.");
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
        voice.Binding = _voiceIndex.Resolve(voice.Identity ?? throw new InvalidOperationException("Source voice type was lost."), info, voice.ResponseIndex);
        var lipPath = voice.Binding.LipPath;
        if (RuntimeLiveContentSource.Current!.TryRead(lipPath, null, out var lipBytes, out _))
        {
            voice.Lip = FaceGenLipAnimation.Read(lipBytes, _lipConfiguration);
            voice.LipSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(lipBytes));
            voice.LipWeights = new float[voice.Lip.TargetNames.Count];
            voice.Speaker?.ValidateSpeechFace(_lipConfiguration);
        }
        else _unbound.Add("missing-source-lip:" + lipPath);
        if (voice.Creature is not null) _unbound.Add("creature-speech-face:" + voice.Reference);
        voice.Player.Stream = NativeOwnedMediaLoader.LoadAudio(voice.Binding.AudioPath);
        voice.Player.Play();
        GD.Print($"OPENNV_NATIVE_SPEECH_BEGIN info={info.Record.FormKey} response={response.Number} " +
            $"speaker={voice.Command!.SpeakerEditorId} voice={voice.Binding.AudioPath} lip={lipPath} voiceType={voice.Binding.VoiceType} " +
            $"speakerIdle={response.SpeakerAnimation} lipLoaded={voice.Lip is not null} facePose={(voice.Speaker is null ? "creature-unbound" : "owned-tri-lip-morphs")} headMotion=unbound spatialAudio=unbound parity=unmeasured");
    }

    public override void _Process(double delta)
    {
        if (Error is not null) return;
        try
        {
            var frame = _channels.Values.Where(voice => voice.Info is not null)
                .Select(voice => (Voice: voice, voice.Generation)).ToArray();
            _emptyCompletions.Drain((speaker, topics) => (SayToCompleted ??
                throw new NotSupportedException("SayTo has no completion-event owner."))(speaker, topics),
                () => IsInsideTree() && IsProcessing() && !GetTree().Paused);
            if (GetTree().Paused) return;
            foreach (var entry in frame)
                if (entry.Voice.Info is not null && entry.Voice.Generation == entry.Generation)
                    ProcessVoice(entry.Voice);
        }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException or FileNotFoundException or InvalidOperationException)
        {
            Fail(error);
        }
    }

    private void ProcessVoice(Voice voice)
    {
        if (voice.Lip is not null)
        {
            voice.Lip.Sample(voice.Player.GetPlaybackPosition(), voice.LipWeights);
            voice.Speaker?.ApplySpeechFace(_lipConfiguration, voice.LipWeights);
        }
        if (!voice.Advance) return;
        voice.Advance = false;
        ClearResponseSound(voice);
        voice.Speaker?.EndResponseAnimation();
        if (voice.ResponseCompleted is { } completion)
        {
            voice.ResponseCompleted = null; voice.Info = null; voice.Lip = null; voice.LipWeights = [];
            voice.Speaker?.ClearSpeechFace();
            _conversationVoice = null;
            completion();
            return;
        }
        if (++voice.ResponseIndex < voice.Info!.Responses.Count) { PlayResponse(voice); return; }
        var completed = voice.Info;
        var completedTopic = voice.Topic; voice.Topic = null;
        voice.Info = null; voice.Lip = null; voice.LipWeights = [];
        voice.Speaker?.ClearSpeechFace();
        GD.Print($"OPENNV_NATIVE_SPEECH_END info={completed.Record.FormKey} speaker={voice.Reference} owner=audio-finished");
        RunResults(completed, voice.Reference, false);
        InfoCompleted?.Invoke(completed.Record.FormKey);
        if (completedTopic is { } topic)
        {
            (SayToCompleted ?? throw new NotSupportedException("SayTo has no completion-event owner."))(voice.Reference, new HashSet<FalloutFormKey> { topic });
            ++voice.CompletedCommands; ++_completedCommands;
        }
    }

    private void Fail(Exception error)
    {
        if (Error is not null) return;
        Error = error.Message;
        StopVoices();
        GD.PushError($"OPENNV_NATIVE_SPEECH_DIVERGENCE {error.Message}");
    }

    public override void _ExitTree() => StopVoices();
    private void StopVoices()
    {
        foreach (var voice in _channels.Values)
        {
            voice.Player.Stop(); ClearResponseSound(voice);
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
        if (voice.ResponseSound is null) return;
        voice.ResponseSound.Free(); voice.ResponseSound = null;
    }
}
