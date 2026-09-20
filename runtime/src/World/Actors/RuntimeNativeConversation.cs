using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Presentation.Ui;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.World.Actors;

internal partial class RuntimeNativeConversation : Node
{
    private FalloutConversation _conversation = null!;
    internal bool Active => _pending.Count != 0 || _conversation.Phase != "closed";
    private RuntimeNativeSpeech _speech = null!;
    private RuntimeNativePlayer _player = null!;
    private FalloutPluginStack _records = null!;
    private FalloutFormKey _speaker;
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
        Func<FalloutFormKey, IReadOnlyDictionary<FalloutFormKey, sbyte>>? factions = null)
    {
        _records = records; _quests = quests; _player = player; _speech = speech; _runtimeConditions = evaluate;
        _resolveRunOnCell = resolveRunOnCell;
        _healthPercentage = healthPercentage;
        _actorValue = actorValue;
        _templates = templates;
        _recordTalkedToPlayer = recordTalkedToPlayer;
        _talkedToPlayer = talkedToPlayer;
        _factions = factions;
        _conversation = new(records, quests, condition => _conditions!.Evaluate(condition), (info, begin) => results(info, _speaker, begin), saidInfos);
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
            if (_speech.Error is { } error && _conversation.Phase != "closed") throw new InvalidOperationException(error);
            if (_speech.Active || _pending.Count == 0) return;
            var request = _pending.Dequeue(); _speaker = request.Speaker; _completed = request.Completed;
            var actor = _records.GetEffective(_speaker);
            var npc = _records.GetEffective(FalloutDialogueTopic.RequiredForm(actor, "NAME"));
            var resolveRunOnCell = _resolveRunOnCell;
            Func<FalloutCondition, FalloutFormKey?>? currentCell = resolveRunOnCell is null ? null :
                condition => resolveRunOnCell(_speaker, condition);
            _conditions = new(_records, _quests, _speaker, FalloutDialogueSpeaker.Read(_records, npc.FormKey, _templates?.Invoke(_speaker)),
                _runtimeConditions, currentCell, _healthPercentage, _actorValue, _talkedToPlayer, _factions);
            _speakerName = FalloutDialogueTopic.Text(npc.ReadSubrecords().Single(field => field.Signature == "FULL").Data.Span);
            _layer = new CanvasLayer { Layer = 95 }; AddChild(_layer);
            _menu = new(() => _speech.SkipResponse(), Fail); _layer.AddChild(_menu);
            _player.SetModalInput(true); Input.MouseMode = Input.MouseModeEnum.Visible;
            _conversation.Start(npc.FormKey, request.Topic ?? FalloutDialogueTopic.Find(_records, "DIAL", "GREETING").FormKey);
            _recordTalkedToPlayer?.Invoke(_speaker);
            _facingSpeaker = GetTree().Root.FindChildren("*", "", true, false).OfType<Node3D>()
                .Single(value => value is RuntimeNativeNpc humanoid && humanoid.Appearance.Reference == _speaker ||
                    value is RuntimeNativeCreature creature && creature.Appearance.Reference == _speaker);
            var turnSpeed = FalloutGameSettingFloats.Read(_records, "fCharacterDefaultTurningSpeed");
            if (_facingSpeaker is RuntimeNativeNpc facingNpc)
                facingNpc.BeginConversationFacing(() => _player.Camera.GlobalPosition, turnSpeed);
            else ((RuntimeNativeCreature)_facingSpeaker).BeginConversationFacing(() => _player.Camera.GlobalPosition, turnSpeed);
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
            _player.SetModalInput(false); Input.MouseMode = Input.MouseModeEnum.Captured;
            var completed = _completed; _completed = null; completed?.Invoke();
            return;
        }
        _menu!.Show(_speakerName, _conversation, topic => Guard(() => { _conversation.Choose(topic); Present(); }));
        if (_conversation.Phase == "speaking")
            _speech.StartResponse(_speaker, _conversation.Info!, _conversation.ResponseIndex,
                () => Guard(() => { _conversation.CompleteResponse(); Present(); }));
    }
    private void Guard(Action action) { try { action(); } catch (Exception error) { Fail(error); } }
    private void ReleaseFacing()
    {
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
        Input.MouseMode = Input.MouseModeEnum.Captured;
        GD.PushError($"OPENNV_NATIVE_CONVERSATION_DIVERGENCE speaker={_speaker}: {error.Message}");
    }
}
