using System.Globalization;
using System.Text.RegularExpressions;
using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Actors;

internal partial class RuntimeNativeSpeech : Node
{
    private readonly Dictionary<string, FalloutDialogueTopic> _topics = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<FalloutFormKey> _said = [];
    private FalloutPluginStack _stack = null!;
    private FaceGenLipConfiguration _lipConfiguration = null!;
    private Action<string, short> _stageResult = null!;
    private Func<FalloutFormKey, float> _questStage = null!;
    private AudioStreamPlayer _voice = null!;
    private FalloutSayToCommand? _command;
    private FalloutDialogueInfo? _info;
    private int _responseIndex;
    private string[] _voicePaths = [];
    private bool _advance;
    private FaceGenLipAnimation? _lip;
    private float[] _lipWeights = [];
    private RuntimeNativeNpc? _speaker;
    internal event Action<string>? ResultCommand;
    internal event Action<FalloutFormKey>? InfoCompleted;
    internal string? Error { get; private set; }
    internal bool Active => _info is not null;
    internal object State => new
    {
        info = _info?.Record.FormKey.ToString(),
        speaker = _command?.SpeakerEditorId,
        response = _info?.Responses[_responseIndex].Number,
        text = _info?.Responses[_responseIndex].Text,
        positionSeconds = _voice?.GetPlaybackPosition() ?? 0.0,
        lipWeights = _lipWeights,
        facePoseOwner = _speaker is null ? "unbound" : "owned-tri-lip-morphs",
        face = _speaker?.FaceState,
        lipHeadMotionOwner = "unbound",
        speakerAnimation = _info?.Responses[_responseIndex].SpeakerAnimation?.ToString(),
        speakerAnimationOwner = _info?.Responses[_responseIndex].SpeakerAnimation is null
            ? "package-idle" : "owned-response-idle",
        spatialAudioOwner = "unbound",
        said = _said.Select(key => key.ToString()).ToArray(),
        error = Error,
    };

    internal void Configure(FalloutPluginStack stack, FaceGenLipConfiguration lipConfiguration,
        Action<string, short> stageResult, Func<FalloutFormKey, float> questStage)
    {
        _stack = stack;
        _lipConfiguration = lipConfiguration;
        _stageResult = stageResult;
        _questStage = questStage;
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
        if (_voice.Playing) throw new InvalidOperationException("Source speaker already has an active voice.");
        if (!command.TargetEditorId.Equals("player", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("SayTo target needs its runtime actor owner.");
        var speaker = FalloutDialogueTopic.Find(_stack, "ACHR", command.SpeakerEditorId);
        var npcKey = FalloutDialogueTopic.RequiredForm(speaker, "NAME");
        var npc = _stack.GetEffective(npcKey);
        if (npc.Signature != "NPC_") throw new NotSupportedException("SayTo speaker is not an NPC.");
        var actors = GetTree().Root.FindChildren("*", "", true, false).OfType<RuntimeNativeNpc>()
            .Where(actor => actor.Appearance.Reference == speaker.FormKey).ToArray();
        if (actors.Length != 1) throw new NotSupportedException($"SayTo source speaker {speaker.FormKey} has {actors.Length} runtime actors.");
        actors[0].ValidateSpeechFace(_lipConfiguration);
        _speaker?.ClearSpeechFace();
        _speaker?.EndResponseAnimation();
        _speaker = actors[0];
        if (!_topics.TryGetValue(command.TopicEditorId, out var topic))
            _topics.Add(command.TopicEditorId, topic = FalloutDialogueTopic.Read(_stack, command.TopicEditorId));
        var info = topic.Select(npcKey, _said, _questStage) ??
            throw new InvalidOperationException($"No eligible source INFO in {command.TopicEditorId}.");
        if (FalloutDialogueTopic.CodeLines(info.BeginScript).Any())
            throw new NotSupportedException($"INFO {info.Record.FormKey} begin-script owner is unbound.");
        var appearance = FalloutNpcAppearanceResolver.Resolve(_stack, npcKey, speaker.FormKey);
        var voiceOwner = _stack.GetEffective(appearance.TraitsOwner);
        var voiceType = _stack.GetEffective(FalloutDialogueTopic.RequiredForm(voiceOwner, "VTCK"));
        if (voiceType.Signature != "VTYP") throw new InvalidDataException("NPC voice type is not VTYP.");
        var voiceName = FalloutDialogueTopic.Text(voiceType.ReadSubrecords().Single(field => field.Signature == "EDID").Data.Span);
        var owned = RuntimeLiveContentSource.Current ?? throw new InvalidOperationException("Owned source is absent.");
        _voicePaths = owned.ResourcePathsUnder($"sound/voice/{info.Record.Plugin.Name}/{voiceName}").ToArray();
        _command = command;
        _info = info;
        _responseIndex = 0;
        if ((info.Flags & 4) != 0) _said.Add(info.Record.FormKey);
        PlayResponse();
    }

    private void PlayResponse()
    {
        var info = _info ?? throw new InvalidOperationException("Source INFO was lost.");
        var response = info.Responses[_responseIndex];
        if (response.Sound is not null) throw new NotSupportedException("Response SOUN override owner is unbound.");
        var suffix = $"_{info.Record.FormKey.ObjectId:x8}_{response.Number}";
        var audio = _voicePaths.Where(path => Path.GetFileNameWithoutExtension(path).EndsWith(suffix, StringComparison.OrdinalIgnoreCase) &&
            Path.GetExtension(path).Equals(".ogg", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (audio.Length != 1) throw new InvalidDataException($"INFO {info.Record.FormKey}/{response.Number} has {audio.Length} matching owned voices.");
        var lipPath = Path.ChangeExtension(audio[0], ".lip");
        if (!RuntimeLiveContentSource.Current!.TryRead(lipPath, null, out var lipBytes, out _))
            throw new FileNotFoundException($"Source voice LIP is missing: {lipPath}");
        _lip = FaceGenLipAnimation.Read(lipBytes, _lipConfiguration);
        _lipWeights = new float[_lip.TargetNames.Count];
        _voice.Stream = NativeOwnedMediaLoader.LoadAudio(audio[0]);
        if (response.ListenerAnimation is not null)
            throw new NotSupportedException($"Response listener IDLE {response.ListenerAnimation} requires its target animation owner.");
        _speaker!.BeginResponseAnimation(_stack, response.SpeakerAnimation);
        _voice.Play();
        GD.Print($"OPENNV_NATIVE_SPEECH_BEGIN info={info.Record.FormKey} response={response.Number} " +
            $"speaker={_command!.SpeakerEditorId} voice={audio[0]} lip={lipPath} " +
            $"speakerIdle={response.SpeakerAnimation} facePose=owned-tri-lip-morphs headMotion=unbound spatialAudio=unbound parity=unmeasured");
    }

    public override void _Process(double delta)
    {
        if (Error is not null || _info is null) return;
        try
        {
            if (_lip is not null)
            {
                _lip.Sample(_voice.GetPlaybackPosition(), _lipWeights);
                _speaker!.ApplySpeechFace(_lipConfiguration, _lipWeights);
            }
            if (!_advance) return;
            _advance = false;
            _speaker?.EndResponseAnimation();
            if (++_responseIndex < _info.Responses.Count) { PlayResponse(); return; }
            var completed = _info;
            _info = null;
            _lip = null;
            _lipWeights = [];
            _speaker?.ClearSpeechFace();
            GD.Print($"OPENNV_NATIVE_SPEECH_END info={completed.Record.FormKey} owner=audio-finished");
            foreach (var line in FalloutDialogueTopic.CodeLines(completed.EndScript))
            {
                var stage = StagePattern().Match(line);
                if (stage.Success)
                {
                    _stageResult(stage.Groups["quest"].Value, short.Parse(stage.Groups["stage"].Value, CultureInfo.InvariantCulture));
                    continue;
                }
                var commands = FalloutDialogueTopic.SayToCommands(line);
                if (commands.Count == 1) { StartCore(commands[0]); continue; }
                if (ResultCommand is not { } result)
                    throw new NotSupportedException($"INFO {completed.Record.FormKey} result command is unbound: {line}");
                result(line);
            }
            InfoCompleted?.Invoke(completed.Record.FormKey);
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
        _speaker?.ClearSpeechFace();
        _speaker?.EndResponseAnimation();
        _advance = false;
        GD.PushError($"OPENNV_NATIVE_SPEECH_DIVERGENCE {error.Message}");
    }

    [GeneratedRegex(@"^setstage\s+(?<quest>[A-Za-z0-9_]+)\s+(?<stage>[0-9]+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex StagePattern();
}
