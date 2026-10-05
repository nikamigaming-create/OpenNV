using System.Text.Json;
using Godot;
using OpenNV.Runtime;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.InputSystem;
using OpenNV.Runtime.World.Cells;

public partial class NativeReferenceEventsAudit
{
    private async Task SaveInputReceipts()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-save-input-" + Guid.NewGuid().ToString("N"));
        var profile = Path.Combine(directory, "input-controls.json");
        var configuration = RuntimeConfiguration.Load();
        RuntimeNativePlayer? player = null;
        var callbacks = 0;
        string Source(string name) => name == "QuickSave" ? "003FFFFF" : "00FFFFFF";
        RuntimeNativePlayer Create(FalloutInputControls controls)
        {
            DesktopInputMap.Configure(configuration.Player.DesktopInput);
            var result = new RuntimeNativePlayer { SaveGame = () => callbacks++ };
            result.Configure(configuration, Transform3D.Identity, FalloutCameraProjection.FromReferenceFov(75, 1));
            result.ConfigureInputControls(controls, _ => false);
            AddChild(result);
            result.SetProcess(false);
            result.SetPhysicsProcess(false);
            return result;
        }
        JsonElement State() => JsonSerializer.SerializeToElement(player!.SaveInputState);
        async Task Press(Godot.Key key, bool echo = false)
        {
            using var down = new InputEventKey { PhysicalKeycode = key, Keycode = key, Pressed = true, Echo = echo };
            Input.ParseInputEvent(down);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            using var up = new InputEventKey { PhysicalKeycode = key, Keycode = key };
            Input.ParseInputEvent(up);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        static void RequireMap(JsonElement state, int expected)
        {
            foreach (var name in new[] { "sourceMap", "aliasMap" })
            {
                var map = state.GetProperty(name);
                Require(map.GetProperty("exists").GetBoolean() && map.GetProperty("events").GetArrayLength() == 1 &&
                    map.GetProperty("events")[0].GetProperty("physicalCode").GetInt32() == expected,
                    "The receipt lost the actual native Save action binding.");
            }
        }
        static void RequireDispatch(JsonElement state, int physicalCode, bool bound = true)
        {
            var phases = new[] { "lastIngress", "lastUnhandled", "lastDispatch", "lastReturned" }
                .Select(name => state.GetProperty(name)).ToArray();
            Require(phases.All(receipt => receipt.GetProperty("physicalCode").GetInt32() == physicalCode &&
                receipt.GetProperty("eventOwner").GetUInt64() == phases[0].GetProperty("eventOwner").GetUInt64() &&
                receipt.GetProperty("nativeOwner").GetUInt64() == state.GetProperty("nativeOwner").GetUInt64() &&
                receipt.GetProperty("matchesSource").GetBoolean() && receipt.GetProperty("matchesAlias").GetBoolean() &&
                receipt.GetProperty("callbackBound").GetBoolean() == bound && !receipt.GetProperty("modal").GetBoolean()),
                "Native Save ingress, unhandled admission and callback return did not retain one actual event.");
        }
        try
        {
            var controls = new FalloutInputControls(Source, profile);
            player = Create(controls);
            Require(State().GetProperty("inputEnabled").GetBoolean() && State().GetProperty("unhandledInputEnabled").GetBoolean(),
                "The real player's native input callbacks were disabled.");
            await Press(Godot.Key.F5);
            Require(callbacks == 1, "Ordinary native F5 did not invoke Save once.");
            RequireMap(State(), 63); RequireDispatch(State(), 63);

            var dispatched = State().GetProperty("lastDispatch").GetProperty("eventOwner").GetUInt64();
            player.SetModalInput(true);
            await Press(Godot.Key.F5);
            Require(callbacks == 1 && State().GetProperty("lastUnhandled").GetProperty("modal").GetBoolean() &&
                State().GetProperty("lastDispatch").GetProperty("eventOwner").GetUInt64() == dispatched,
                "Modal Save refusal lost its observed guard or dispatched anyway.");
            player.SetModalInput(false);
            await Press(Godot.Key.F5, echo: true);
            Require(callbacks == 1 && State().GetProperty("lastUnhandled").GetProperty("echo").GetBoolean() &&
                !State().GetProperty("lastUnhandled").GetProperty("matchesAlias").GetBoolean(),
                "An echoed Save key was not observed independently of callback admission.");

            controls.Set(25, 64);
            RequireMap(State(), 64);
            await Press(Godot.Key.F5);
            Require(callbacks == 1 && !State().GetProperty("lastUnhandled").GetProperty("matchesAlias").GetBoolean(),
                "An old Save key still invoked the remapped callback.");
            await Press(Godot.Key.F6);
            Require(callbacks == 2, "A source-remapped Save key did not invoke the callback.");
            RequireDispatch(State(), 64);

            InputMap.ActionEraseEvents(configuration.Player.DesktopInput.Save.Action);
            await Press(Godot.Key.F6);
            var missing = State();
            Require(callbacks == 2 && missing.GetProperty("sourceMap").GetProperty("events").GetArrayLength() == 1 &&
                missing.GetProperty("aliasMap").GetProperty("events").GetArrayLength() == 0 &&
                missing.GetProperty("lastUnhandled").GetProperty("matchesSource").GetBoolean() &&
                !missing.GetProperty("lastUnhandled").GetProperty("matchesAlias").GetBoolean() &&
                missing.GetProperty("callbackBound").GetBoolean(),
                "A missing native alias map was hidden or replaced with a routing workaround.");
            controls.Flush();
            var warmOwner = player.GetInstanceId();
            player.Free(); player = null;
            player = Create(controls);
            Require(player.GetInstanceId() != warmOwner && State().GetProperty("lastIngress").ValueKind == JsonValueKind.Null,
                "Scene reentry retained the old native player or stale input receipt.");
            await Press(Godot.Key.F6);
            Require(callbacks == 3, "Scene reentry did not rebuild the source Save alias.");
            RequireMap(State(), 64); RequireDispatch(State(), 64);

            player.SaveGame = null;
            await Press(Godot.Key.F6);
            Require(callbacks == 3, "An absent Save delegate invented a callback.");
            RequireDispatch(State(), 64, bound: false);
            player.Free(); player = null;
            var cold = new FalloutInputControls(_ => throw new InvalidDataException("Cold profile fell back to source defaults."), profile);
            player = Create(cold);
            Require(cold.Get(25) == 64 && State().GetProperty("lastDispatch").ValueKind == JsonValueKind.Null,
                "Cold profile restoration lost the remap or invented a prior dispatch.");
            await Press(Godot.Key.F6);
            Require(callbacks == 4, "Cold profile Save admission did not reach the actual callback.");
            RequireMap(State(), 64); RequireDispatch(State(), 64);
            GD.Print($"OPENNV_NATIVE_SAVE_INPUT_PASS ownerMvid={typeof(RuntimeNativePlayer).Assembly.ManifestModule.ModuleVersionId} " +
                "nativeEvent=true sourceMap=true aliasMap=true callback=true modal=true echo=true remap=true " +
                "missingAliasVisible=true reentry=true coldProfile=true absentCallbackVisible=true " +
                "historical233=unresolved campaign=unverified recording=false fixture=true parity=unverified");
        }
        finally
        {
            if (GodotObject.IsInstanceValid(player)) player!.Free();
            DesktopInputMap.Configure(configuration.Player.DesktopInput);
            if (File.Exists(profile)) File.Delete(profile);
            if (Directory.Exists(directory)) Directory.Delete(directory);
        }
    }
}
