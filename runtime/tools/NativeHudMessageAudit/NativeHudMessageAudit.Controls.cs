using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using OpenNV.Runtime;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.InputSystem;
using OpenNV.Runtime.Presentation.Ui;

public partial class NativeHudMessageAudit
{
    private bool _controlMode;

    private async void CheckControlHandoff(string root)
    {
        try
        {
            RuntimeLiveContentSource.Configure(root, RuntimeLiveContentSource.FalloutNewVegasGame);
            _source = RuntimeLiveContentSource.Current!;
            _records = FalloutPluginStack.Load(_source.PluginSources);
            var inventory = new FalloutPlayerInventory();
            _queue = inventory.Notifications;
            var quests = new FalloutQuestState(_records, _queue);
            var plain = FalloutSourceMessage.Read(FalloutDialogueTopic.Find(_records, "MESG", "CGTutorialRelease"));
            var movementRecord = FalloutDialogueTopic.Find(_records, "MESG", "CGTutorialMovement");
            var movement = FalloutSourceMessage.Read(movementRecord);
            var sourceHash = SHA256.HashData(movementRecord.ReadData());
            _queue.Publish([new(FalloutHudEventKind.Message, plain.Form, 0)]);
            var scripts = new RuntimeNativeQuestScripts(_records, quests, new HashSet<FalloutFormKey>(), inventory);
            AddChild(scripts);
            scripts.SetProcess(false); // This fixture owns only HUD presentation.
            var bootstrap = scripts.FindChildren("*", "", true, false).OfType<NativeOwnedHudMessages>().Single();
            for (var frame = 0; frame < 4; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(bootstrap.Error is null && scripts.Scripts.Controls is null && _queue.Current?.Event.Source == plain.Form,
                "A non-control notice required an input owner before gameplay binding.");
            var beforeHandoff = JsonSerializer.Serialize(_queue.Capture());
            scripts.RetireBootstrapHud();
            Require(!bootstrap.IsProcessing() && !bootstrap.Visible,
                "Bootstrap HUD remained an active consumer until deferred deletion.");
            FalloutInputControls? controls = null;
            var shown = false;
            _hud = new NativeOwnedHudMessages(_records, _queue, () => shown, quests, controls: () => controls);
            AddChild(_hud);
            Require(JsonSerializer.Serialize(_queue.Capture()) == beforeHandoff,
                "HUD handoff reset the retained notice ordinal, elapsed time or source queue.");
            for (var frame = 0; frame < 4; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(_hud.AdvanceCount == 0 && JsonSerializer.Serialize(_queue.Capture()) == beforeHandoff,
                "Hidden gameplay or retired bootstrap HUD consumed the retained notice.");
            shown = true;
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var ordinal = _queue.Current!.Ordinal;
            var elapsed = _queue.Elapsed;
            var advanced = _hud.AdvancedSeconds;
            for (var frame = 0; frame < 4; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(_queue.Current?.Ordinal == ordinal && Math.Abs((_queue.Elapsed - elapsed) - (_hud.AdvancedSeconds - advanced)) < 1e-8,
                "The retained notice advanced through more than its single gameplay presenter.");
            var settings = FalloutInstallationSettings.Read(_source);
            controls = new FalloutInputControls(name => settings.Contains("Controls", name) ? settings.Require("Controls", name) : null,
                defaults: name => { var value = settings.ControlDefault(name); return new(value.Keyboard, value.Mouse); });
            AddChild(new RuntimeNativeInputControls(controls, RuntimeConfiguration.Load().Player.DesktopInput, _ => false));
            _queue.Publish([new(FalloutHudEventKind.Message, movement.Form, 0)]);
            var started = Time.GetTicksMsec();
            while (_queue.Current?.Event.Source != movement.Form)
            {
                Require(_hud.Error is null && Time.GetTicksMsec() - started < 20000,
                    "Late-bound tutorial did not reach the live native HUD.");
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            string Text() => JsonSerializer.SerializeToElement(_hud.State).GetProperty("text").GetString()!;
            string Forward()
            {
                var events = InputMap.ActionGetEvents(RuntimeNativeInputControls.Action(0));
                using var eventsOwner = (Godot.Collections.Array)events;
                return events.OfType<InputEventKey>().Single().AsTextPhysicalKeycode();
            }
            var firstText = Text();
            Require(_hud.Error is null && !firstText.Contains("sUActn", StringComparison.OrdinalIgnoreCase) && firstText.Contains("[" + Forward() + "]", StringComparison.Ordinal),
                "Late input binding did not render the actual native mapped key label.");
            ordinal = _queue.Current!.Ordinal; elapsed = _queue.Elapsed;
            controls.Set(0, controls.Get(0) == 17 ? 18u : 17u);
            for (var frame = 0; frame < 4; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(_hud.Error is null && Text() != firstText && Text().Contains("[" + Forward() + "]", StringComparison.Ordinal) &&
                _queue.Current?.Ordinal == ordinal && _queue.Elapsed > elapsed &&
                _queue.Current?.Event.Text is null && sourceHash.AsSpan().SequenceEqual(SHA256.HashData(movementRecord.ReadData())),
                "Rebinding retained a stale native label or reset/changed the source notice.");
            GD.Print("OPENNV_NATIVE_HUD_CONTROL_HANDOFF_PASS nonControlWithoutBindings=true lateBinding=true nativeMapLabel=true rebind=true " +
                "bootstrapRetired=true oneAdvancingOwner=true hiddenRetained=true ordinalAndElapsedRetained=true sourceUnchanged=true glyphParity=unverified");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }

    private static void Require(bool condition, string error)
    {
        if (!condition) throw new InvalidDataException("NativeHudMessageAudit: " + error);
    }
}
