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
    private string _speakerName = "";
    private FalloutDialogueConditions? _conditions;
    private Func<FalloutCondition, float> _runtimeConditions = null!;
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
        Func<FalloutCondition, float> evaluate, Action<FalloutDialogueInfo, FalloutFormKey, bool> results, HashSet<FalloutFormKey>? saidInfos = null)
    {
        _records = records; _quests = quests; _player = player; _speech = speech; _runtimeConditions = evaluate;
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
            _conditions = new(_records, _quests, _speaker, FalloutDialogueSpeaker.Read(_records, npc.FormKey), _runtimeConditions);
            _speakerName = FalloutDialogueTopic.Text(npc.ReadSubrecords().Single(field => field.Signature == "FULL").Data.Span);
            _layer = new CanvasLayer { Layer = 95 }; AddChild(_layer);
            _menu = new(() => _speech.SkipResponse(), Fail); _layer.AddChild(_menu);
            _player.SetModalInput(true); Input.MouseMode = Input.MouseModeEnum.Visible;
            _conversation.Start(npc.FormKey, request.Topic ?? FalloutDialogueTopic.Find(_records, "DIAL", "GREETING").FormKey);
            Present();
        }
        catch (Exception error) { Fail(error); }
    }

    private void Present()
    {
        if (_conversation.Phase == "closed")
        {
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
    private void Fail(Exception error)
    {
        if (Error is not null) return;
        Error = error.Message;
        _layer?.QueueFree(); _layer = null; _menu = null;
        _player.SetModalInput(false);
        Input.MouseMode = Input.MouseModeEnum.Captured;
        GD.PushError($"OPENNV_NATIVE_CONVERSATION_DIVERGENCE speaker={_speaker}: {error.Message}");
    }
}
