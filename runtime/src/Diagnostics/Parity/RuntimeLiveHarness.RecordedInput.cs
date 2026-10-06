using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Presentation.Ui;

namespace OpenNV.Runtime.Diagnostics.Parity;

internal sealed partial class RuntimeLiveHarness
{
    private Func<FalloutPluginStack?>? _inputSource;
    private RecordedInputWriter? _inputRecording;
    private RecordedInputPlayback? _inputPlayback;
    private long _inputStarted;
    private string? _inputTapePath;
    private string? _inputRecordingError;
    private bool _deliveringReplay;
    private bool _replayCheckpointPrepared;
    private ulong _inputReplayRequest;
    private readonly Dictionary<MouseButton, Vector2> _heldPointerButtons = [];
    private readonly Dictionary<Key, ulong> _recordedHumanKeys = [];
    private const string HarnessInputMetadata = "opennv_harness_input";
    private long InputMicroseconds => checked((long)((decimal)(Stopwatch.GetTimestamp() - _inputStarted) * 1_000_000 / Stopwatch.Frequency));

    private RecordedInputBinding InputBinding(string checkpoint)
    {
        var source = _inputSource?.Invoke() ?? throw new NotSupportedException("Input recording requires an owned source stack.");
        using var bytes = new FileStream(checkpoint, FileMode.Open, System.IO.FileAccess.Read, FileShare.Read);
        return new(Convert.ToHexString(SHA256.HashData(bytes)), _captureIdentity().StateKey,
            source.Plugins.Select(plugin => new RecordedInputPlugin(plugin.Plugin.Name, plugin.Sha256)).ToArray());
    }

    private bool DispatchRecordedInput(JsonElement command, ulong request)
    {
        switch (command.GetProperty("op").GetString())
        {
            case "input.record.start":
                if (_inputRecording is not null || _inputPlayback?.Active == true)
                    throw new InvalidOperationException("Input recording or playback already owns the input segment.");
                var recordPath = command.GetProperty("path").GetString()!;
                RequireTapePath(recordPath);
                if (File.Exists(recordPath)) throw new IOException("Input recording never replaces an existing segment.");
                _campaignBot.Stop(); _bot?.Stop(); ReleaseAll();
                // The shared owner must capture a complete reached state first.
                // A pending procedure/result is not replaced by a partial tape.
                var slot = (_saveCheckpoint ?? throw new NotSupportedException("Input recording has no campaign save owner."))(Guid.NewGuid());
                var binding = InputBinding(slot.Path);
                _inputRecording = new(recordPath, new(RecordedInputTape.Schema, "opennv", binding));
                _inputPlayback = null;
                _inputReplayRequest = 0;
                _inputTapePath = recordPath; _inputRecordingError = null; _inputStarted = Stopwatch.GetTimestamp();
                _lastCheckpoint = new { operation = "input.record.start", slot, owner = "shared-campaign-save", ordinaryInput = false };
                PublishState(); return true;
            case "input.record.stop":
                ReleaseAll(); FinishInputRecording(); PublishState(); return true;
            case "input.replay.start":
                if (_inputRecording is not null || _inputPlayback?.Active == true)
                    throw new InvalidOperationException("Input recording or playback already owns the input segment.");
                if (_checkpointTransitioning?.Invoke() == true)
                    throw new InvalidOperationException("Input playback requires a completed cold checkpoint load.");
                if (!_replayCheckpointPrepared)
                    throw new InvalidOperationException("Cold-load the input segment checkpoint before replay; intervening input invalidates preparation.");
                var replayPath = command.GetProperty("path").GetString()!;
                RequireTapePath(replayPath);
                var tape = RecordedInputTape.Read(replayPath);
                var restored = _restoredCheckpoint?.Invoke() ?? throw new InvalidOperationException("Load the segment's campaign checkpoint before replay.");
                var lateness = command.TryGetProperty("maximumLatenessMicroseconds", out var limit) ? limit.GetInt64() : 250_000;
                // Validate the whole journal and binding before releasing or
                // delivering any input. Retail measurements remain evidence.
                var playback = new RecordedInputPlayback(tape, InputBinding(restored.Path), lateness);
                _campaignBot.Stop(); _bot?.Stop(); ReleaseAll();
                _inputPlayback = playback; _inputTapePath = replayPath; _inputStarted = Stopwatch.GetTimestamp();
                _inputReplayRequest = request;
                _replayCheckpointPrepared = false;
                PublishState(); return true;
            case "input.replay.stop":
                _inputPlayback?.Stop("Playback explicitly stopped.", ReleaseAll); PublishState(); return true;
            case "input.state":
                PublishState(); return true;
            default: return false;
        }
    }

    private static void RequireTapePath(string path)
    {
        if (!Path.IsPathFullyQualified(path) || !path.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("An input segment requires an absolute private .jsonl path.");
    }

    private void RecordInput(JsonElement input, string? stateKey = null)
    {
        if (_inputRecording is null) return;
        try { _inputRecording.Append(InputMicroseconds, stateKey ?? _captureIdentity().StateKey, input); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException or JsonException)
        {
            FailInputRecording(error.Message);
            GD.PushError("OPENNV_INPUT_RECORDING_FAILED " + error.Message);
        }
    }

    private void FinishInputRecording(string? error = null)
    {
        if (error is not null) _inputRecordingError = error;
        var writer = _inputRecording; _inputRecording = null;
        if (writer is null) return;
        try { writer.Finish(InputMicroseconds, error); }
        finally { writer.Dispose(); }
    }

    private void FailInputRecording(string error)
    {
        FinishInputRecording(error);
        ReleaseAll();
    }

    private void DeliverPointerButton(Vector2 position, MouseButton button, bool pressed, string? stateKey = null)
    {
        if (pressed && button is MouseButton.Left or MouseButton.Right or MouseButton.Middle)
            _heldPointerButtons[button] = position;
        else _heldPointerButtons.Remove(button);
        PushHarnessInput(GetViewport(), new InputEventMouseButton
        { Position = position, GlobalPosition = position, ButtonIndex = button, Pressed = pressed });
        RecordInput(JsonSerializer.SerializeToElement(new
        {
            op = "pointer",
            x = position.X,
            y = position.Y,
            button = button.ToString(),
            pressed
        }, Json), stateKey);
    }

    private void DeliverLook(float dx, float dy)
    {
        var stateKey = _inputRecording is null ? null : _captureIdentity().StateKey;
        ParseHarnessInput(new InputEventMouseMotion { Relative = new(dx, dy) });
        RecordInput(JsonSerializer.SerializeToElement(new { op = "look", dx, dy }, Json), stateKey);
    }

    private static void ParseHarnessInput(InputEvent input)
    {
        input.SetMeta(HarnessInputMetadata, true);
        Input.ParseInputEvent(input);
    }

    private static void PushHarnessInput(Viewport viewport, InputEvent input)
    {
        input.SetMeta(HarnessInputMetadata, true);
        viewport.PushInput(input, true);
    }

    public override void _Input(InputEvent input)
    {
        if (input.HasMeta(HarnessInputMetadata)) return;
        if (input is not (InputEventKey or InputEventMouseMotion or InputEventMouseButton))
        {
            if (_inputRecording is not null) FailInputRecording("This physical input device has no recorded adapter.");
            return;
        }
        _replayCheckpointPrepared = false;
        _inputPlayback?.Stop("Playback yielded to physical player input.", ReleaseAll);
        if (_inputRecording is null) return;
        var scene = _captureIdentity().StateKey;
        switch (input)
        {
            case InputEventKey key when !key.Echo:
                if (key.Unicode != 0 && GetViewport().GuiGetFocusOwner() is LineEdit &&
                    System.Text.Rune.TryCreate((int)key.Unicode, out var rune) && !System.Text.Rune.IsControl(rune))
                {
                    if (key.Pressed) RecordInput(JsonSerializer.SerializeToElement(new { op = "text", text = rune.ToString() }, Json), scene);
                    break;
                }
                var physical = key.PhysicalKeycode == Key.None ? key.Keycode : key.PhysicalKeycode;
                if (key.Pressed) _recordedHumanKeys[physical] = Time.GetTicksMsec();
                else _recordedHumanKeys.Remove(physical);
                RecordInput(JsonSerializer.SerializeToElement(new
                {
                    op = "key",
                    key = physical.ToString(),
                    pressed = key.Pressed,
                    leaseMilliseconds = 900
                }, Json), scene);
                break;
            case InputEventMouseMotion mouse:
                RecordInput(Input.MouseMode == Input.MouseModeEnum.Captured
                    ? JsonSerializer.SerializeToElement(new { op = "look", dx = mouse.Relative.X, dy = mouse.Relative.Y }, Json)
                    : JsonSerializer.SerializeToElement(new { op = "pointer", x = mouse.Position.X, y = mouse.Position.Y }, Json), scene);
                break;
            case InputEventMouseButton mouse:
                if (mouse.Pressed && mouse.ButtonIndex is MouseButton.Left or MouseButton.Right or MouseButton.Middle)
                    _heldPointerButtons[mouse.ButtonIndex] = mouse.Position;
                else _heldPointerButtons.Remove(mouse.ButtonIndex);
                RecordInput(JsonSerializer.SerializeToElement(new
                {
                    op = "pointer",
                    x = mouse.Position.X,
                    y = mouse.Position.Y,
                    button = mouse.ButtonIndex.ToString(),
                    pressed = mouse.Pressed
                }, Json), scene);
                break;
        }
    }

    private void RenewRecordedHumanKeys(ulong now)
    {
        if (_inputRecording is null) return;
        foreach (var (key, renewed) in _recordedHumanKeys.ToArray())
        {
            if (now - renewed < 400) continue;
            _recordedHumanKeys[key] = now;
            RecordInput(JsonSerializer.SerializeToElement(new
            {
                op = "key",
                key = key.ToString(),
                pressed = true,
                leaseMilliseconds = 900
            }, Json));
        }
    }

    private static string ButtonText(BaseButton button) => button is Button labelled ? labelled.Text :
        button is NativeBitmapMenuButton bitmap ? bitmap.Text : button is NativeOwnedTileTarget owned ? owned.Text : button.Name.ToString();

    private void AdvanceInputPlayback()
    {
        if (_inputPlayback?.Active != true) return;
        _deliveringReplay = true;
        try
        {
            _inputPlayback.Advance(InputMicroseconds, () => _captureIdentity().StateKey, input =>
            {
                Dispatch(input, 0);
                AtomicWrite(Path.Combine(_directory, "input-replay.receipt.json"), JsonSerializer.Serialize(new
                {
                    input = _inputPlayback.Cursor + 1,
                    delivered = true,
                    microseconds = InputMicroseconds,
                    physicsCount = Engine.GetPhysicsFrames(),
                    drawCount = Engine.GetFramesDrawn(),
                    gameplayParity = "unverified"
                }, Json));
            }, ReleaseAll);
        }
        finally { _deliveringReplay = false; }
    }
}
