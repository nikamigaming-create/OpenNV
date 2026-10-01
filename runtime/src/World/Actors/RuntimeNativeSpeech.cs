using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Actors;

internal partial class RuntimeNativeSpeech : Node
{
    private readonly Dictionary<string, FalloutDialogueTopic> _topics = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<FalloutFormKey> _said = [];
    private FalloutPluginStack _stack = null!;
    private FaceGenLipConfiguration _lipConfiguration = null!;
    private Func<FalloutFormKey, float> _questStage = null!;
    private FalloutQuestState? _quests;
    private Func<bool>? _playerFemale;
    private Func<FalloutCondition, float>? _conditionContext;
    private Func<FalloutFormKey, FalloutActorTemplateSelection?>? _templates;
    private AudioStreamPlayer _voice = null!;
    private NativeOwnedAnimationSoundPlayer? _responseSound;
    private Func<FalloutFormKey, FalloutSoundRandomState>? _soundRandom;
    private float _unitsToMetres;
    private FalloutSayToCommand? _command;
    private string? _commandKind;
    private long _completedCommands;
    private FalloutFormKey? _sayToTopic;
    private FalloutDialogueInfo? _info;
    private int _responseIndex;
    private FalloutDialogueVoiceIndex _voices = null!;
    private FalloutDialogueSpeaker? _identity;
    private FalloutDialogueVoiceBinding? _binding;
    private string? _lipSha256;
    private bool _advance;
    private FaceGenLipAnimation? _lip;
    private float[] _lipWeights = [];
    private RuntimeNativeNpc? _speaker;
    private RuntimeNativeCreature? _creatureSpeaker;
    private FalloutFormKey? _speakerReference;
    private readonly SortedSet<string> _unbound = new(StringComparer.Ordinal);
    internal IReadOnlyCollection<string> Unbound => _unbound;
    private Action? _responseCompleted;
    internal Action<FalloutDialogueInfo, FalloutFormKey, bool>? ExecuteResults { get; set; }
    internal event Action<FalloutFormKey>? InfoCompleted;
    internal event Action<FalloutFormKey, FalloutFormKey>? SayToCompleted;
    internal Action<FalloutSpeechSubtitle>? PrepareSubtitle { get; set; }
    internal FalloutSpeechSubtitle? Subtitle => Error is null && _info is not null && _sayToTopic is { } topic ?
        new(_speakerReference!.Value, _info.Record.FormKey, topic, _info.Responses[_responseIndex].Text,
            _command!.ForceSubtitles) : null;
    internal string? Error { get; private set; }
    internal bool Active => _info is not null;
    internal bool IsTalking(FalloutFormKey reference)
    {
        return _info is not null && _speakerReference == reference;
    }
    internal void SkipResponse()
    {
        if (_info is null || Error is not null) return;
        _voice.Stop();
        ClearResponseSound();
        _advance = true;
    }

    internal void StartResponse(FalloutFormKey speaker, FalloutDialogueInfo info, int response, Action completed)
    {
        if (_info is not null || Error is not null) throw new InvalidOperationException("Dialogue voice owner is busy or faulted.");
        var record = _stack.GetEffective(speaker);
        var name = record.ReadSubrecords().SingleOrDefault(field => field.Signature == "EDID").Data;
        _command = new(name.IsEmpty ? speaker.ToString() : FalloutDialogueTopic.Text(name.Span), "player", info.Record.FormKey.ToString());
        _commandKind = "conversation";
        _sayToTopic = null;
        BindSpeaker(record);
        _info = info; _responseIndex = response; _responseCompleted = completed;
        try { PlayResponse(); }
        catch (Exception error) { Fail(error); throw; }
    }
    internal object State => new
    {
        info = _info?.Record.FormKey.ToString(),
        speaker = _command?.SpeakerEditorId,
        speakerReference = _speakerReference?.ToString(),
        command = _commandKind,
        listenerLookOwner = _commandKind == "SayTo" ? "unbound" : "not-requested",
        completedCommands = _completedCommands,
        voiceBinding = _binding,
        responseSound = _responseSound?.LastEvent,
        audioSha256 = _voice?.Stream?.GetMeta("opennv_owned_media_sha256", "").AsString(),
        lipSha256 = _lipSha256,
        unbound = _unbound.ToArray(),
        response = _info?.Responses[_responseIndex].Number,
        text = _info?.Responses[_responseIndex].Text,
        positionSeconds = _voice?.GetPlaybackPosition() ?? 0.0,
        lipWeights = _lipWeights,
        facePoseOwner = _lip is null ? "source-lip-absent" : _speaker is null ? "creature-speech-face-unbound" : "owned-tri-lip-morphs",
        face = _speaker?.FaceState,
        lipHeadMotionOwner = "unbound",
        speakerAnimation = _info?.Responses[_responseIndex].SpeakerAnimation?.ToString(),
        speakerAnimationOwner = _info?.Responses[_responseIndex].SpeakerAnimation is null
            ? "package-idle" : "owned-response-idle",
        spatialAudioOwner = "unbound",
        forceSubtitles = _command?.ForceSubtitles == true,
        sayToTopic = _sayToTopic?.ToString(),
        said = _said.Select(key => key.ToString()).ToArray(),
        error = Error,
    };

    internal void Configure(FalloutPluginStack stack, FaceGenLipConfiguration lipConfiguration,
        Func<FalloutFormKey, float> questStage,
        Func<FalloutCondition, float>? conditionContext = null, HashSet<FalloutFormKey>? saidInfos = null,
        Func<FalloutFormKey, FalloutActorTemplateSelection?>? templates = null,
        Func<FalloutFormKey, FalloutSoundRandomState>? soundRandom = null, float unitsToMetres = 0, FalloutQuestState? quests = null,
        Func<bool>? playerFemale = null)
    {
        _stack = stack;
        _lipConfiguration = lipConfiguration;
        _questStage = questStage;
        _quests = quests;
        _playerFemale = playerFemale;
        _conditionContext = conditionContext;
        _templates = templates;
        _soundRandom = soundRandom; _unitsToMetres = unitsToMetres;
        _said = saidInfos ?? [];
        _voices = new((RuntimeLiveContentSource.Current ?? throw new InvalidOperationException("Owned source is absent.")).ResourcePathsUnder("sound/voice"));
        Name = "SourceSpeech";
        _voice = new AudioStreamPlayer { Name = "OwnedVoice" };
        _voice.Finished += () => _advance = true;
        AddChild(_voice);
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
        if (_info is not null || _voice.Playing) throw new InvalidOperationException("Source speaker already has an active voice.");
        var npcKey = FalloutDialogueTopic.RequiredForm(speaker, "NAME");
        var npc = _stack.GetEffective(npcKey);
        if (npc.Signature is not ("NPC_" or "CREA")) throw new NotSupportedException("SayTo speaker is not an actor.");
        if (!_topics.TryGetValue(command.TopicEditorId, out var topic))
            _topics.Add(command.TopicEditorId, topic = FalloutDialogueTopic.Read(_stack, topicForm));
        BindSpeaker(speaker);
        var conditions = new FalloutDialogueConditions(_stack,
            _quests ?? throw new NotSupportedException("Scripted speech selection has no shared quest state."), speaker.FormKey, _identity!,
            _conditionContext, playerFemale: _playerFemale);
        var info = topic.Select(npcKey, _said, _questStage, conditions.Evaluate) ??
            throw new InvalidOperationException($"No eligible source INFO in {command.TopicEditorId}.");
        _command = command;
        _commandKind = command.TargetEditorId.Length == 0 ? "Say" : "SayTo";
        _sayToTopic = topicForm;
        _info = info;
        _responseIndex = 0;
        if ((info.Flags & 4) != 0) _said.Add(info.Record.FormKey);
        RunResults(info, speaker.FormKey, true);
        PlayResponse();
    }

    private void BindSpeaker(FalloutPluginRecord speaker)
    {
        var actors = GetTree().Root.FindChildren("*", "", true, false).OfType<Node3D>()
            .Where(actor => (actor is RuntimeNativeNpc npc && npc.Appearance.Reference == speaker.FormKey ||
                actor is RuntimeNativeCreature creature && creature.Appearance.Reference == speaker.FormKey) && actor.IsVisibleInTree()).ToArray();
        if (actors.Length != 1) throw new NotSupportedException($"Source speaker {speaker.FormKey} has {actors.Length} resident runtime actors.");
        _speaker?.ClearSpeechFace();
        _speaker?.EndResponseAnimation();
        _speaker = actors[0] as RuntimeNativeNpc;
        _creatureSpeaker = actors[0] as RuntimeNativeCreature;
        _speakerReference = speaker.FormKey;
        _identity = FalloutDialogueSpeaker.Read(_stack, FalloutDialogueTopic.RequiredForm(speaker, "NAME"), _templates?.Invoke(speaker.FormKey));
    }

    private void PlayResponse()
    {
        var info = _info ?? throw new InvalidOperationException("Source INFO was lost.");
        var response = info.Responses[_responseIndex];
        if (Subtitle is { } subtitle)
            (PrepareSubtitle ?? throw new NotSupportedException("SayTo has no subtitle presentation owner."))(subtitle);
        _binding = null; _lipSha256 = null; _advance = false;
        _lip = null; _lipWeights = [];
        ClearResponseSound();
        if (response.ListenerAnimation is not null)
            throw new NotSupportedException($"Response listener IDLE {response.ListenerAnimation} requires its target animation owner.");
        if (_creatureSpeaker is not null && response.SpeakerAnimation is not null)
            throw new NotSupportedException($"Creature response IDLE {response.SpeakerAnimation} requires its animation blend owner.");
        _speaker?.BeginResponseAnimation(_stack, response.SpeakerAnimation);
        if (response.Sound is { } sound)
        {
            if (info.Speaker is { } specified && specified != _identity!.Actor)
                throw new InvalidDataException("Explicit dialogue sound belongs to a different actor.");
            var actor = (Node3D?)_speaker ?? _creatureSpeaker ?? throw new InvalidOperationException("Sound response lost its speaker.");
            _responseSound = new(_stack, RuntimeLiveContentSource.Current!, actor, _unitsToMetres,
                (_soundRandom ?? throw new NotSupportedException("Response SOUN has no retained random owner."))(_speakerReference!.Value));
            AddChild(_responseSound);
            var disposition = _responseSound.DispatchSound(sound, actor, () => _advance = true);
            if (disposition == "unbound-source-sound")
                throw new NotSupportedException(string.Join("; ", _responseSound.Unbound));
            foreach (var lane in _responseSound.Unbound) _unbound.Add(lane);
            GD.Print($"OPENNV_NATIVE_SPEECH_BEGIN info={info.Record.FormKey} response={response.Number} sound={sound} owner=source-SOUN disposition={disposition}");
            return;
        }
        _binding = _voices.Resolve(_identity ?? throw new InvalidOperationException("Source voice type was lost."), info, _responseIndex);
        var lipPath = _binding.LipPath;
        _lip = null; _lipWeights = [];
        if (RuntimeLiveContentSource.Current!.TryRead(lipPath, null, out var lipBytes, out _))
        {
            _lip = FaceGenLipAnimation.Read(lipBytes, _lipConfiguration);
            _lipSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(lipBytes));
            _lipWeights = new float[_lip.TargetNames.Count];
            _speaker?.ValidateSpeechFace(_lipConfiguration);
        }
        else _unbound.Add("missing-source-lip:" + lipPath);
        if (_creatureSpeaker is not null) _unbound.Add("creature-speech-face:" + _speakerReference);
        _voice.Stream = NativeOwnedMediaLoader.LoadAudio(_binding.AudioPath);
        _voice.Play();
        GD.Print($"OPENNV_NATIVE_SPEECH_BEGIN info={info.Record.FormKey} response={response.Number} " +
            $"speaker={_command!.SpeakerEditorId} voice={_binding.AudioPath} lip={lipPath} voiceType={_binding.VoiceType} " +
            $"speakerIdle={response.SpeakerAnimation} lipLoaded={_lip is not null} facePose={(_speaker is null ? "creature-unbound" : "owned-tri-lip-morphs")} headMotion=unbound spatialAudio=unbound parity=unmeasured");
    }

    public override void _Process(double delta)
    {
        if (Error is not null || _info is null) return;
        try
        {
            if (_lip is not null)
            {
                _lip.Sample(_voice.GetPlaybackPosition(), _lipWeights);
                _speaker?.ApplySpeechFace(_lipConfiguration, _lipWeights);
            }
            if (!_advance) return;
            _advance = false;
            ClearResponseSound();
            _speaker?.EndResponseAnimation();
            if (_responseCompleted is { } completion)
            {
                _responseCompleted = null; _info = null; _lip = null; _lipWeights = [];
                _speaker?.ClearSpeechFace();
                completion();
                return;
            }
            if (++_responseIndex < _info.Responses.Count) { PlayResponse(); return; }
            var completed = _info;
            var completedSpeaker = _speakerReference;
            var completedTopic = _sayToTopic; _sayToTopic = null;
            _info = null;
            _lip = null;
            _lipWeights = [];
            _speaker?.ClearSpeechFace();
            GD.Print($"OPENNV_NATIVE_SPEECH_END info={completed.Record.FormKey} owner=audio-finished");
            RunResults(completed, completedSpeaker ?? throw new InvalidOperationException("Completed voice lost its source actor."), false);
            InfoCompleted?.Invoke(completed.Record.FormKey);
            if (completedTopic is { } topic)
            {
                (SayToCompleted ?? throw new NotSupportedException("SayTo has no completion-event owner."))(completedSpeaker!.Value, topic);
                ++_completedCommands;
            }
        }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException or FileNotFoundException or InvalidOperationException)
        {
            Fail(error);
        }
    }

    private void Fail(Exception error)
    {
        Error = error.Message;
        _voice.Stop();
        ClearResponseSound();
        _speaker?.ClearSpeechFace();
        _speaker?.EndResponseAnimation();
        _advance = false;
        GD.PushError($"OPENNV_NATIVE_SPEECH_DIVERGENCE {error.Message}");
    }

    private void RunResults(FalloutDialogueInfo info, FalloutFormKey speaker, bool begin)
    {
        if (!FalloutDialogueTopic.CodeLines(begin ? info.BeginScript : info.EndScript).Any()) return;
        (ExecuteResults ?? throw new NotSupportedException($"INFO {info.Record.FormKey} has no result-script owner."))(info, speaker, begin);
    }

    private void ClearResponseSound()
    {
        if (_responseSound is null) return;
        _responseSound.Free(); _responseSound = null;
    }
}
