using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Diagnostics.Parity;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.InputSystem;

public partial class NativeRecordedInputAudit : Node
{
    private int _clicks;
    public override async void _Ready()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-native-input-" + Guid.NewGuid().ToString("N"));
        RuntimeLiveHarness? harness = null;
        try
        {
            ExerciseBotActionBindings();
            Directory.CreateDirectory(directory);
            var pluginPath = Path.Combine(directory, "Input.esm");
            File.WriteAllBytes(pluginPath, Fixture());
            using var records = LoadRecords(directory);
            var checkpointPath = Path.Combine(directory, "checkpoint.json");
            var tapePath = Path.Combine(directory, "input.jsonl");
            var blocked = true;
            RuntimeSaveSlotMetadata? restored = null;
            var checkpoint = new RuntimeSaveSlotMetadata(Guid.NewGuid().ToString("N"), checkpointPath,
                "synthetic-save-owner-fixture", null, "fixture", null, DateTime.UtcNow);
            var button = new Button { Text = "Go", Position = new(2, 2), Size = new(48, 24) };
            button.Pressed += () => ++_clicks; AddChild(button);
            harness = new();
            harness.Configure(directory, sequence => new(ParityEngine.OpenNv, sequence, 0, 0, 0, "fixture", []),
                () => new { fixture = true }, () => records);
            harness.ConfigureCheckpoints(_ =>
            {
                if (blocked) throw new NotSupportedException("Synthetic active procedure has no save continuation.");
                File.WriteAllText(checkpointPath, "{\"synthetic\":true}");
                return checkpoint;
            }, (_, _) => restored = checkpoint, () => false, () => restored);
            AddChild(harness);
            var request = 0L;
            async Task<JsonElement> Send(object command, bool accepted = true)
            {
                var number = ++request;
                var path = Path.Combine(directory, $"{number:D10}.command");
                File.WriteAllText(path + ".pending", JsonSerializer.Serialize(command));
                File.Move(path + ".pending", path);
                var receiptPath = Path.Combine(directory, $"{number:D10}.receipt.json");
                var deadline = Time.GetTicksMsec() + 5000;
                while (!File.Exists(receiptPath) && Time.GetTicksMsec() < deadline)
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                using var receipt = JsonDocument.Parse(File.ReadAllText(receiptPath));
                Require(receipt.RootElement.GetProperty("delivered").GetBoolean() == accepted,
                    "Native recorded-input command returned an unexpected receipt: " + receipt.RootElement);
                return receipt.RootElement.Clone();
            }
            await Send(new { op = "input.record.start", path = tapePath }, false);
            Require(!File.Exists(tapePath), "A blocked save owner emitted a replayable segment.");
            blocked = false;
            await Send(new { op = "input.record.start", path = tapePath });
            await Send(new { op = "key", key = "W", pressed = true, leaseMilliseconds = 1000 });
            await Send(new { op = "key", key = "W", pressed = true, leaseMilliseconds = 1000 });
            Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = Key.A, Keycode = Key.A, Pressed = true });
            await ToSignal(GetTree().CreateTimer(.45), SceneTreeTimer.SignalName.Timeout);
            await Send(new { op = "button", path = button.GetPath().ToString() });
            await Send(new { op = "pointer", x = 60, y = 60, button = "left", pressed = true });
            await Send(new { op = "input.record.stop" });
            var tape = RecordedInputTape.Read(tapePath);
            Require(tape.Inputs.Count(step => step.Input.GetProperty("op").GetString() == "key" &&
                step.Input.GetProperty("key").GetString() == "W") == 3, "Native dispatch was recorded twice or lost a lease renewal.");
            Require(tape.Inputs.Count(step => step.Input.GetProperty("op").GetString() == "key" &&
                step.Input.GetProperty("key").GetString() == "A") >= 3, "Physical hold lost its renewal or release.");
            Require(_clicks == 1 && !Input.IsPhysicalKeyPressed(Key.W) && !Input.IsPhysicalKeyPressed(Key.A),
                $"Recording stop lost its observed click or held-key release: clicks={_clicks} W={Input.IsPhysicalKeyPressed(Key.W)} A={Input.IsPhysicalKeyPressed(Key.A)} viewport={GetViewport().GetVisibleRect()}.");
            await Send(new { op = "input.replay.start", path = tapePath }, false);
            await Send(new { op = "checkpoint.load", id = checkpoint.Id });
            await Send(new { op = "input.replay.start", path = tapePath, maximumLatenessMicroseconds = 250000 });
            async Task<JsonElement> Playback()
            {
                await ToSignal(GetTree().CreateTimer(.05), SceneTreeTimer.SignalName.Timeout);
                await Send(new { op = "input.state" });
                string text;
                var statePath = Path.Combine(directory, "live-state.json");
                while (!LiveHarnessAtomicFile.TryRead(statePath, out text, out _))
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                using var state = JsonDocument.Parse(text);
                return state.RootElement.GetProperty("recordedInput").GetProperty("playback").Clone();
            }
            var limit = Time.GetTicksMsec() + 5000;
            JsonElement playback;
            do { playback = await Playback(); }
            while (playback.GetProperty("active").GetBoolean() && Time.GetTicksMsec() < limit);
            Require(playback.GetProperty("complete").GetBoolean() && _clicks == 2 &&
                !Input.IsPhysicalKeyPressed(Key.W) && !Input.IsPhysicalKeyPressed(Key.A),
                "Native playback failed its delivered segment or left a held key: " + playback);
            await Send(new { op = "input.record.start", path = Path.Combine(directory, "abandoned.jsonl") });
            await Send(new { op = "key", key = "W", pressed = true, leaseMilliseconds = 1000 });
            harness.Free(); harness = null;
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(!Input.IsPhysicalKeyPressed(Key.W), "Retiring the recorder left a held control.");
            try { RecordedInputTape.Read(Path.Combine(directory, "abandoned.jsonl")); throw new InvalidOperationException("Abandoned tape passed."); }
            catch (InvalidDataException) { }
            GD.Print("OPENNV_NATIVE_RECORDED_INPUT_PASS saveGuard=true sourceBound=true leases=true physicalKeys=true " +
                "ordinaryButton=true playback=true ownerRetirement=true framesRecorded=false fixture=true campaign=false parity=unverified");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError("OPENNV_NATIVE_RECORDED_INPUT_FAIL " + error); GetTree().Quit(1); }
        finally
        {
            harness?.Free();
            // Only files in this freshly created, flat fixture directory belong to this audit.
            if (Directory.Exists(directory))
            {
                foreach (var path in Directory.EnumerateFiles(directory)) File.Delete(path);
                Directory.Delete(directory);
            }
        }
    }
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidDataException(message); }

    private static void ExerciseBotActionBindings()
    {
        const string action = "opennv_native_audit_bot_aim";
        Require(!InputMap.HasAction(action), "The isolated bot binding fixture already has an owner.");
        InputMap.AddAction(action);
        try
        {
            using var keyboard = NativeScriptKeys.Create(56);
            using var mouse = NativeScriptKeys.Create(257);
            InputMap.ActionAddEvent(action, keyboard);
            InputMap.ActionAddEvent(action, mouse);
            Require(RuntimeLiveHarness.PhysicalBotAction(action) is { Key: Key.None, Mouse: MouseButton.Right },
                "Bot aim lost the source mouse binding to an unsupported side-specific keyboard event.");
            InputMap.ActionEraseEvent(action, mouse);
            var rejected = false;
            try { RuntimeLiveHarness.PhysicalBotAction(action); }
            catch (NotSupportedException error) { rejected = error.Message.Contains("physical-side", StringComparison.Ordinal); }
            Require(rejected, "A side-specific keyboard binding was silently delivered without its native location.");
            InputMap.ActionEraseEvents(action);
            using var reload = NativeScriptKeys.Create(19);
            InputMap.ActionAddEvent(action, reload);
            Require(RuntimeLiveHarness.PhysicalBotAction(action) is { Key: Key.R, Mouse: MouseButton.None },
                "Ordinary source keyboard input was replaced by a guessed mouse binding.");
        }
        finally { InputMap.EraseAction(action); }
        GD.Print("OPENNV_NATIVE_BOT_ACTION_BINDINGS_PASS sourceDualBinding=true mappedMousePreferred=true sideSpecificKeyRefused=true ordinaryKeyboardRetained=true");
    }
    private static FalloutPluginStack LoadRecords(string directory)
    {
        var args = OS.GetCmdlineUserArgs();
        if (args is []) return FalloutPluginStack.Load(directory, ["Input.esm"]);
        if (args is [var ownedRoot]) return FalloutPluginStack.Load(Path.Combine(ownedRoot, "Data"), ["FalloutNV.esm"]);
        if (args is [var baseRoot, var mod, var modRoot, .. var dependencies])
        {
            var installation = new FalloutModStackSelection([new(mod, modRoot, dependencies)]).Resolve(baseRoot);
            RuntimeLiveContentSource.Configure(baseRoot, RuntimeLiveContentSource.FalloutNewVegasGame,
                installation.ContentRoots.Skip(1).ToArray(), installation.ActivePlugins, installation.Settings);
            return FalloutPluginStack.Load(RuntimeLiveContentSource.Current!.PluginSources);
        }
        throw new ArgumentException("Use no arguments for the synthetic gate, an owned root, or the owned mod stack and dependencies.");
    }
    private static byte[] Fixture()
    {
        var bytes = new byte[42];
        Encoding.ASCII.GetBytes("TES4").CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), 18);
        Encoding.ASCII.GetBytes("HEDR").CopyTo(bytes, 24);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(28), 12);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(30), 1.34f);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(38), 0x800);
        return bytes;
    }
}
