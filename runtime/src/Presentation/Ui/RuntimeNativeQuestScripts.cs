using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Presentation.Ui;

internal sealed partial class RuntimeNativeQuestScripts : Node
{
    private readonly FalloutPluginStack _records;
    internal FalloutQuestScripts Scripts { get; }
    private CanvasLayer? _layer;
    private FalloutSourceMessage? _current;
    private bool _pausedBefore;
    private string? _error;
    private readonly FalloutPlayerInventory _inventory;
    private NativeOwnedHudMessages? _hud;
    private bool _worldActive;
    internal object State => new { scripts = Scripts.State, worldActive = _worldActive, message = _current, hud = _hud?.State, error = _error };

    internal FalloutQuestScriptsSnapshot Capture() => Scripts.Capture(_current);
    internal void ActivateWorld() => _worldActive = true;

    internal RuntimeNativeQuestScripts(FalloutPluginStack records, FalloutQuestState quests, IReadOnlySet<FalloutFormKey> claimed,
        FalloutPlayerInventory inventory, FalloutGlobalState? globals = null, FalloutReferenceWorld? references = null)
    {
        Name = "NativeQuestScripts";
        _records = records;
        _inventory = inventory;
        Scripts = new(records, quests, claimed, inventory, globals, references: references);
        ProcessMode = ProcessModeEnum.Always;
        ProcessPriority = int.MinValue + 1;
    }

    public override void _Ready()
    {
        try
        {
            var hudLayer = new CanvasLayer { Name = "NativeHudLayer", Layer = 1, ProcessMode = ProcessModeEnum.Always };
            AddChild(hudLayer);
            _hud = new NativeOwnedHudMessages(_records, _inventory.Notifications);
            hudLayer.AddChild(_hud);
        }
        catch (Exception error)
        {
            _error = error.Message;
            GD.PushError($"OPENNV_HUD_OWNER_UNBOUND {error.Message}");
        }
    }

    public override void _Process(double delta)
    {
        if (_error is not null) return;
        if (!_worldActive || _layer is not null || GetTree().Paused)
        {
            Scripts.Advance(delta, gameMode: false);
            return;
        }
        if (Scripts.TryTakeMessage(out var restored)) { Show(restored!); return; }
        Scripts.Advance(delta);
        if (Scripts.TryTakeMessage(out var message)) Show(message!);
    }

    private void Show(FalloutSourceMessage message)
    {
        _current = message;
        _pausedBefore = GetTree().Paused;
        GetTree().Paused = true;
        _layer = new CanvasLayer { Name = "NativeMessageLayer", Layer = 100, ProcessMode = ProcessModeEnum.Always };
        AddChild(_layer);
        try
        {
            _layer.AddChild(new NativeOwnedMessageMenu(message, _records, choice =>
            {
                GD.Print($"OPENNV_SOURCE_MESSAGE_ACCEPT source={message.Form} choice={choice}");
                _layer?.QueueFree();
                _layer = null;
                _current = null;
                GetTree().Paused = _pausedBefore;
                if (Scripts.TryTakeMessage(out var next)) Show(next!);
            }, error => Fail(message, error)));
            GD.Print($"OPENNV_SOURCE_MESSAGE_OPEN source={message.Form}");
        }
        catch (Exception error)
        {
            Fail(message, error);
        }
    }
    private void Fail(FalloutSourceMessage message, Exception error)
    {
        _error = error.Message;
        GD.PushError($"OPENNV_SOURCE_MESSAGE_UNBOUND source={message.Form} error={error.Message}");
    }
}
