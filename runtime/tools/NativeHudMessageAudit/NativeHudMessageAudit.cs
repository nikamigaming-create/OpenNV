using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.Presentation.Ui;

public partial class NativeHudMessageAudit : Node
{
    private NativeOwnedHudMessages _hud = null!;
    private FalloutHudNotifications _queue = null!;
    private FalloutPluginStack _records = null!;
    private RuntimeLiveContentSource _source = null!;
    private int _frames;
    private string? _capture;
    private bool _shown;
    private int _pendingWhileHidden;
    private bool _objectiveMode;
    public override void _Ready()
    {
        try
        {
            var args = OS.GetCmdlineUserArgs();
            _objectiveMode = args.Length is 4 or 5 && args[1] == "--objective";
            if (!_objectiveMode && args.Length is < 1 or > 2) throw new ArgumentException("Expected Data [capture] or Data --objective quest index [capture].");
            RuntimeLiveContentSource.Configure(args[0], RuntimeLiveContentSource.FalloutNewVegasGame);
            _source = RuntimeLiveContentSource.Current!;
            _records = FalloutPluginStack.Load(_source.PluginSources);
            var inventory = new FalloutPlayerInventory();
            _queue = inventory.Notifications;
            var quests = new FalloutQuestState(_records, _queue);
            if (_objectiveMode)
            {
                // Component presentation check, never an ordinary campaign save
                // or evidence that the player earned this objective.
                quests.ApplyObjective(FalloutDialogueTopic.Find(_records, "QUST", args[2]).FormKey,
                    uint.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture), true, true);
                _capture = args.Length == 5 ? args[4] : null;
            }
            else
            {
                var scripts = new FalloutQuestScripts(_records, quests, new HashSet<FalloutFormKey>(), inventory);
                // A fresh quest clock has its authored initial phase; zero elapsed
                // time is not an invocation. Advance bounded simulation frames.
                for (var frame = 0; frame < 600 && _queue.Capture().Pending.Count == 0; frame++) scripts.Advance(1d / 60);
                _capture = args.Length == 2 ? args[1] : null;
            }
            if (_queue.Capture().Pending.Count == 0) throw new InvalidDataException("Selected source scripts emitted no HUD events.");
            _pendingWhileHidden = _queue.Capture().Pending.Count;
            _hud = new NativeOwnedHudMessages(_records, _queue, () => _shown, quests);
            AddChild(_hud);
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }
    public override void _Process(double delta)
    {
        if (_hud is null || ++_frames < 4) return;
        if (!_shown)
        {
            if (_queue.Current is not null || _queue.Capture().Pending.Count != _pendingWhileHidden)
            {
                GD.PushError("OPENNV_HUD_AUDIT_FAIL Hidden gameplay consumed unseen notifications.");
                GetTree().Quit(1); return;
            }
            _shown = true; _frames = 0; return;
        }
        if (_hud.Error is not null || _queue.Current is null)
        {
            GD.PushError("OPENNV_HUD_AUDIT_FAIL " + _hud.Error);
            GetTree().Quit(1);
            return;
        }
        if (_objectiveMode && _queue.Elapsed < 0.6) return;
        GD.Print("OPENNV_HUD_AUDIT_STATE " + JsonSerializer.Serialize(_hud.State));
        if (_capture is not null && DisplayServer.GetName() != "headless")
            GetViewport().GetTexture().GetImage().SavePng(_capture);
        GD.Print($"OPENNV_HUD_AUDIT_PASS source={(_objectiveMode ? "owned-objective-component" : "actual-start-enabled-scripts")} queue=source-order hidden=retained drawing=original-tiles-and-fonts parity=unverified");
        GetTree().Quit();
    }
    public override void _ExitTree()
    {
        _records?.Dispose();
        _source?.Dispose();
    }
}
