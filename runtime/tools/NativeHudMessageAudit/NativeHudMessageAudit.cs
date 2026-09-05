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
    public override void _Ready()
    {
        try
        {
            var args = OS.GetCmdlineUserArgs();
            if (args.Length is < 1 or > 2) throw new ArgumentException("Expected owned Data root and optional private capture path.");
            RuntimeLiveContentSource.Configure(args[0], RuntimeLiveContentSource.FalloutNewVegasGame);
            _source = RuntimeLiveContentSource.Current!;
            _records = FalloutPluginStack.Load(_source.PluginSources);
            var inventory = new FalloutPlayerInventory();
            var scripts = new FalloutQuestScripts(_records, new FalloutQuestState(_records), new HashSet<FalloutFormKey>(), inventory);
            scripts.Advance(0);
            _queue = inventory.Notifications;
            if (_queue.Capture().Pending.Count == 0) throw new InvalidDataException("Selected source scripts emitted no HUD events.");
            _hud = new NativeOwnedHudMessages(_records, _queue);
            AddChild(_hud);
            _capture = args.Length == 2 ? args[1] : null;
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }
    public override void _Process(double delta)
    {
        if (_hud is null || ++_frames < 4) return;
        if (_hud.Error is not null || _queue.Current is null)
        {
            GD.PushError("OPENNV_HUD_AUDIT_FAIL " + _hud.Error);
            GetTree().Quit(1);
            return;
        }
        GD.Print("OPENNV_HUD_AUDIT_STATE " + JsonSerializer.Serialize(_hud.State));
        if (_capture is not null && DisplayServer.GetName() != "headless")
            GetViewport().GetTexture().GetImage().SavePng(_capture);
        GD.Print("OPENNV_HUD_AUDIT_PASS source=actual-start-enabled-scripts queue=source-order drawing=original-tiles-and-fonts parity=unverified");
        GetTree().Quit();
    }
    public override void _ExitTree()
    {
        _records?.Dispose();
        _source?.Dispose();
    }
}
