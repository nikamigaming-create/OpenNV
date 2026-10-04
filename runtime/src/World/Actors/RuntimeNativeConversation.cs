using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Presentation.Ui;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.World.Actors;

internal partial class RuntimeNativeConversation : Node
{
    private FalloutConversation _conversation = null!;
    internal bool Active => _pending.Count != 0 || _conversation.Phase != "closed";
    internal string? ExecutionFault => Error ?? _conversation?.Error;
    internal FalloutFormKey? PresentedSpeaker => _menu is not null && Error is null &&
        _conversation.Error is null && _conversation.Phase != "closed" ? _speaker : null;
    internal FalloutFormKey? PendingSpeaker => _pending.TryPeek(out var request) ? request.Speaker : null;
    private RuntimeNativeSpeech _speech = null!;
    private RuntimeNativePlayer _player = null!;
    private FalloutPluginStack _records = null!;
    private FalloutFormKey _speaker;
    private FalloutFormKey _dialogueSubject;
    private FalloutDialogueSpeaker _identity = null!;
    private Node3D? _facingSpeaker;
    private string _speakerName = "";
    private FalloutDialogueConditions? _conditions;
    private Func<FalloutCondition, float> _runtimeConditions = null!;
    private Func<FalloutFormKey, FalloutCondition, FalloutFormKey?>? _resolveRunOnCell;
    private Func<FalloutFormKey, float>? _healthPercentage;
    private Func<FalloutFormKey, int, float>? _actorValue;
    private Func<FalloutFormKey, FalloutActorTemplateSelection?>? _templates;
    private Action<FalloutFormKey>? _recordTalkedToPlayer;
    private Func<FalloutFormKey, bool>? _talkedToPlayer;
    private Func<FalloutFormKey, IReadOnlyDictionary<FalloutFormKey, sbyte>>? _factions;
    private Func<bool>? _playerFemale;
    private Func<FalloutFormKey, FalloutFormKey>? _actorRace;
    private Func<FalloutFormKey, FalloutFormKey?>? _currentPackage;
    private Func<int>? _vampireQuery;
    private Func<FalloutFormKey, FalloutFormKey, double>? _itemCount;
    private Func<FalloutFormKey, FalloutFormKey, float>? _referenceDistance;
    private Func<FalloutFormKey, FalloutFormKey, bool>? _referenceInZone;
    private Func<FalloutFormKey, int>? _sitting;
    private Func<FalloutFormKey, int>? _deadCount;
    private FalloutQuestState _quests = null!;
    private CanvasLayer? _layer;
    private NativeOwnedDialogueMenu? _menu;
    private readonly Queue<(FalloutFormKey Speaker, FalloutFormKey? Topic, Action? Completed)> _pending = [];
    private Action? _completed;
    internal string? Error { get; private set; }
    internal object State => new
    {
        phase = _conversation.Phase,
        info = _conversation.Info?.Record.FormKey.ToString(),
        response = _conversation.ResponseIndex,
        speakerReference = _speaker.ToString(),
        dialogueSubject = _dialogueSubject.ToString(),
        voiceType = _identity?.VoiceType.ToString(),
        choices = _conversation.Choices,
        pending = _pending.Count,
        error = Error ?? _conversation.Error
    };

    internal void Configure(FalloutPluginStack records, FalloutQuestState quests, RuntimeNativePlayer player, RuntimeNativeSpeech speech,
        Func<FalloutCondition, float> evaluate, Action<FalloutDialogueInfo, FalloutFormKey, bool> results,
        HashSet<FalloutFormKey>? saidInfos = null,
        Func<FalloutFormKey, FalloutCondition, FalloutFormKey?>? resolveRunOnCell = null,
        Func<FalloutFormKey, float>? healthPercentage = null,
        Func<FalloutFormKey, int, float>? actorValue = null,
        Func<FalloutFormKey, FalloutActorTemplateSelection?>? templates = null,
        Action<FalloutFormKey>? recordTalkedToPlayer = null,
        Func<FalloutFormKey, bool>? talkedToPlayer = null,
        Func<FalloutFormKey, IReadOnlyDictionary<FalloutFormKey, sbyte>>? factions = null, Func<bool>? playerFemale = null,
        Func<FalloutFormKey, FalloutFormKey>? actorRace = null, Func<uint, uint>? dialogueRandom = null,
        Func<FalloutFormKey, FalloutFormKey?>? currentPackage = null, Func<int>? vampireQuery = null,
        Func<FalloutFormKey, FalloutFormKey, double>? itemCount = null,
        Func<FalloutFormKey, FalloutFormKey, float>? referenceDistance = null,
        Func<FalloutFormKey, FalloutFormKey, bool>? referenceInZone = null, Func<FalloutFormKey, int>? sitting = null,
        Func<FalloutFormKey, int>? deadCount = null)
    {
        _records = records; _quests = quests; _player = player; _speech = speech; _runtimeConditions = evaluate;
        _resolveRunOnCell = resolveRunOnCell;
        _healthPercentage = healthPercentage;
        _actorValue = actorValue;
        _templates = templates;
        _recordTalkedToPlayer = recordTalkedToPlayer;
        _talkedToPlayer = talkedToPlayer;
        _factions = factions;
        _playerFemale = playerFemale;
        _actorRace = actorRace;
        _currentPackage = currentPackage;
        _vampireQuery = vampireQuery;
        _itemCount = itemCount;
        _referenceDistance = referenceDistance;
        _referenceInZone = referenceInZone;
        _sitting = sitting;
        _deadCount = deadCount;
        _conversation = new(records, quests, condition => _conditions!.Evaluate(condition), (info, begin) => results(info, _dialogueSubject, begin), saidInfos, dialogueRandom);
    }

    internal void Request(FalloutFormKey speaker, FalloutFormKey target, FalloutFormKey? topic, Action? completed = null)
    {
        if (_records.RuntimeFormId(target) != 0x14) throw new NotSupportedException("NPC-to-NPC conversation travel is unbound.");
        if (Error is not null) throw new InvalidOperationException(Error);
        if (_pending.Count != 0 || _conversation.Phase != "closed") throw new NotSupportedException("Competing conversation requests require arbitration.");
        _pending.Enqueue((speaker, topic, completed));
    }

    public override void _Process(double delta)
    {
        if (Error is not null) return;
        try
        {
            if (_speech.Error is { } error && (_conversation.Phase != "closed" || _pending.Count != 0)) throw new InvalidOperationException(error);
            if (_pending.Count == 0 || _speech.IsDialogueBusy(_pending.Peek().Speaker)) return;
            var request = _pending.Dequeue(); _speaker = request.Speaker; _completed = request.Completed;
            var actor = _records.GetEffective(_speaker);
            var npc = _records.GetEffective(FalloutDialogueTopic.RequiredForm(actor, "NAME"));
            _dialogueSubject = _speech.DialogueSubject(_speaker);
            var identity = _speech.SpeakerIdentity(_speaker);
            _identity = identity;
            var resolveRunOnCell = _resolveRunOnCell;
            Func<FalloutCondition, FalloutFormKey?>? currentCell = resolveRunOnCell is null ? null :
                condition => resolveRunOnCell(_dialogueSubject, condition);
            _conditions = new(_records, _quests, _dialogueSubject, identity,
                _runtimeConditions, currentCell, _healthPercentage, _actorValue, _talkedToPlayer, _factions, _playerFemale, _actorRace,
                currentPackage: _currentPackage, vampireQuery: _vampireQuery, itemCount: _itemCount, referenceDistance: _referenceDistance,
                referenceInZone: _referenceInZone, sitting: _sitting, deadCount: _deadCount);
            _speakerName = FalloutDialogueTopic.Text(npc.ReadSubrecords().Single(field => field.Signature == "FULL").Data.Span);
            _speech.BeginPlayerDialogue(_speaker);
            _layer = new CanvasLayer { Layer = 95 }; AddChild(_layer);
            _menu = new(() => _speech.SkipResponse(), Fail); _layer.AddChild(_menu);
            _player.SetModalInput(true); Input.MouseMode = Input.MouseModeEnum.Visible;
            _conversation.Start(identity.Actor, request.Topic ?? FalloutDialogueTopic.Find(_records, "DIAL", "GREETING").FormKey);
            _recordTalkedToPlayer?.Invoke(_dialogueSubject);
            _facingSpeaker = _speech.ResolveSpeaker(_speaker);
            var turnSpeed = FalloutGameSettingFloats.Read(_records, "fCharacterDefaultTurningSpeed");
            if (_facingSpeaker is RuntimeNativeNpc facingNpc)
                facingNpc.BeginConversationFacing(() => _player.Camera.GlobalPosition, turnSpeed);
            else if (_facingSpeaker is RuntimeNativeCreature facingCreature)
                facingCreature.BeginConversationFacing(() => _player.Camera.GlobalPosition, turnSpeed);
            Present();
        }
        catch (Exception error) { Fail(error); }
    }

    private void Present()
    {
        if (_conversation.Phase == "closed")
        {
            ReleaseFacing();
            _layer?.QueueFree(); _layer = null; _menu = null;
            _player.SetModalInput(false);
            Input.MouseMode = _player.ModalInput ? Input.MouseModeEnum.Visible : Input.MouseModeEnum.Captured;
            var completed = _completed; _completed = null; completed?.Invoke();
            return;
        }
        _menu!.Show(_speakerName, _conversation, topic => Guard(() => { _conversation.Choose(topic); Present(); }));
        if (_conversation.Phase == "speaking")
            _speech.StartResponse(_speaker, _conversation.Info!, _conversation.ResponseIndex,
                () => Guard(() => { _conversation.CompleteResponse(); Present(); }), _dialogueSubject, _identity);
    }
    private void Guard(Action action) { try { action(); } catch (Exception error) { Fail(error); } }
    private void ReleaseFacing()
    {
        _speech.EndPlayerDialogue(_speaker);
        if (IsInstanceValid(_facingSpeaker))
        {
            if (_facingSpeaker is RuntimeNativeNpc npc) npc.EndConversationFacing();
            else if (_facingSpeaker is RuntimeNativeCreature creature) creature.EndConversationFacing();
        }
        _facingSpeaker = null;
    }
    public override void _ExitTree() => ReleaseFacing();
    private void Fail(Exception error)
    {
        if (Error is not null) return;
        Error = error.Message;
        ReleaseFacing();
        _layer?.QueueFree(); _layer = null; _menu = null;
        _player.SetModalInput(false);
        Input.MouseMode = _player.ModalInput ? Input.MouseModeEnum.Visible : Input.MouseModeEnum.Captured;
        GD.PushError($"OPENNV_NATIVE_CONVERSATION_DIVERGENCE speaker={_speaker}: {error.Message}");
    }
}
