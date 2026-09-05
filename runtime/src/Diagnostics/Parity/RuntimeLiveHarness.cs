using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Presentation.Ui;

namespace OpenNV.Runtime.Diagnostics.Parity;

internal sealed partial class RuntimeLiveHarness : Node
{
    private string _directory = "";
    private Func<ulong, ParityTelemetryFrame> _captureState = null!;
    private Func<object> _captureGameplay = null!;
    private readonly Dictionary<Key, ulong> _held = [];
    private ulong _nextRequest = 1;
    private ulong _pendingCapture;
    private ulong _lastStateMilliseconds;
    private ulong _lastStopWrite;
    private LiveHarnessFrameBuffer? _liveFrames;
    private RuntimeRenderTrace? _trace;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    internal void Configure(string directory, Func<ulong, ParityTelemetryFrame> captureState, Func<object> captureGameplay,
        Func<FalloutPluginStack?>? stack = null)
    {
        if (!Path.IsPathFullyQualified(directory))
            throw new ArgumentException("--live-harness requires an absolute private command directory.");
        _directory = directory;
        Directory.CreateDirectory(directory);
        _captureState = captureState;
        _captureGameplay = captureGameplay;
        _trace = new RuntimeRenderTrace(this, directory, stack ?? (() => null), captureGameplay);
        var channel = System.Environment.GetEnvironmentVariable("OPENNV_LIVE_HARNESS_CHANNEL");
        if (!string.IsNullOrEmpty(channel))
            _liveFrames = new LiveHarnessFrameBuffer(channel + ".opennv", true);
        ProcessMode = ProcessModeEnum.Always;
        ProcessPriority = int.MinValue;
        RenderingServer.FramePostDraw += AfterDraw;
        RenderingServer.FramePreDraw += BeforeDraw;
    }

    public override void _Process(double delta)
    {
        _ = delta;
        var now = Time.GetTicksMsec();
        var stop = Path.Combine(_directory, "stop.request");
        if (File.Exists(stop))
        {
            var stamp = (ulong)File.GetLastWriteTimeUtc(stop).Ticks;
            if (stamp != _lastStopWrite)
            {
                _lastStopWrite = stamp;
                ReleaseAll();
                // Stop cancels commands already queued, including stale key-down events.
                _nextRequest = checked(Directory.EnumerateFiles(_directory, "*.command")
                    .Select(path => ulong.TryParse(Path.GetFileNameWithoutExtension(path), out var value) ? value : 0)
                    .DefaultIfEmpty().Max() + 1);
                PublishState();
            }
        }
        foreach (var key in _held.Where(pair => pair.Value <= now).Select(pair => pair.Key).ToArray())
            SetKey(key, false, 0);
        for (var index = 0; index < 32; ++index)
        {
            var path = Path.Combine(_directory, $"{_nextRequest:D10}.command");
            if (!File.Exists(path))
                break;
            var request = _nextRequest++;
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(path));
                Dispatch(document.RootElement, request);
                Receipt(request, true, "Delivered to Godot input; resulting gameplay state is observed separately.");
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidDataException or JsonException or InvalidOperationException or KeyNotFoundException)
            {
                Receipt(request, false, exception.Message);
            }
        }
        if (now - _lastStateMilliseconds >= 100)
        {
            _lastStateMilliseconds = now;
            PublishState();
        }
    }

    private void Dispatch(JsonElement command, ulong request)
    {
        switch (command.GetProperty("op").GetString())
        {
            case "key":
                var name = command.GetProperty("key").GetString()!;
                var key = name switch
                {
                    "1" => Key.Key1,
                    "2" => Key.Key2,
                    _ => Enum.TryParse<Key>(name, true, out var parsed) && parsed != Key.None
                        ? parsed : throw new ArgumentException($"Unknown physical key: {name}"),
                };
                var pressed = command.GetProperty("pressed").GetBoolean();
                var lease = command.TryGetProperty("leaseMilliseconds", out var value) ? value.GetInt32() : 900;
                if (lease is < 20 or > 1000)
                    throw new ArgumentException("Input lease must be 20–1000 milliseconds.");
                SetKey(key, pressed, (ulong)lease);
                break;
            case "button":
                var path = command.GetProperty("path").GetString()!;
                var button = GetTree().Root.GetNodeOrNull<BaseButton>(path);
                if (button is null || !button.IsVisibleInTree() || button.Disabled)
                    throw new InvalidOperationException("Observed button is no longer visible and enabled.");
                var center = button.GetGlobalTransformWithCanvas() * (button.Size / 2);
                button.GetViewport().PushInput(new InputEventMouseButton { Position = center, GlobalPosition = center, ButtonIndex = MouseButton.Left, Pressed = true }, true);
                button.GetViewport().PushInput(new InputEventMouseButton { Position = center, GlobalPosition = center, ButtonIndex = MouseButton.Left, Pressed = false }, true);
                break;
            case "look":
                var dx = command.GetProperty("dx").GetSingle();
                var dy = command.GetProperty("dy").GetSingle();
                if (!float.IsFinite(dx) || !float.IsFinite(dy))
                    throw new ArgumentException("Mouse displacement must be finite.");
                Input.ParseInputEvent(new InputEventMouseMotion { Relative = new Vector2(dx, dy) });
                break;
            case "text":
                if (GetViewport().GuiGetFocusOwner() is not LineEdit entry || !entry.IsVisibleInTree() || !entry.Editable)
                    throw new InvalidOperationException("Text input requires a focused editable field.");
                var text = command.GetProperty("text").GetString() ?? throw new ArgumentException("Missing input text.");
                foreach (var rune in text.EnumerateRunes())
                {
                    if (System.Text.Rune.IsControl(rune))
                        throw new ArgumentException("Use key commands for control characters.");
                }
                foreach (var rune in text.EnumerateRunes())
                {
                    GetViewport().PushInput(new InputEventKey { Unicode = (uint)rune.Value, Pressed = true }, true);
                    GetViewport().PushInput(new InputEventKey { Unicode = (uint)rune.Value, Pressed = false }, true);
                }
                break;
            case "capture":
                if (_pendingCapture != 0)
                    throw new InvalidOperationException("A native frame capture is already pending.");
                _pendingCapture = request;
                break;
            case "trace":
                _trace!.SetEnabled(command.GetProperty("enabled").GetBoolean());
                if (_trace.Enabled) _trace.Request(request);
                break;
            case "trace.capture":
                _trace!.Request(request);
                break;
            default:
                throw new ArgumentException("Unsupported harness input operation.");
        }
    }

    private void SetKey(Key key, bool pressed, ulong lease)
    {
        if (pressed)
        {
            var existing = _held.ContainsKey(key);
            _held[key] = Time.GetTicksMsec() + lease;
            if (existing)
                return;
        }
        else
            _held.Remove(key);
        Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = key, Keycode = key, Pressed = pressed, Echo = false });
    }

    private void ReleaseAll()
    {
        foreach (var key in _held.Keys.ToArray())
            SetKey(key, false, 0);
    }

    private void PublishState()
    {
        var frame = _captureState(0);
        var controls = GetTree().Root.FindChildren("*", nameof(BaseButton), true, false)
            .OfType<BaseButton>().Where(button => button.IsVisibleInTree() && !button.Disabled)
            .Select(button => new { path = button.GetPath().ToString(), text = button is Button labelled ? labelled.Text : button is NativeBitmapMenuButton bitmap ? bitmap.Text : button is NativeOwnedTileTarget owned ? owned.Text : button.Name.ToString() })
            .ToArray();
        AtomicWrite(Path.Combine(_directory, "live-state.json"), JsonSerializer.Serialize(new
        {
            schema = "opennv-live-harness-state/v1",
            engine = "opennv",
            process = System.Environment.ProcessId,
            sampledNanoseconds = Stopwatch.GetTimestamp() * (1_000_000_000.0 / Stopwatch.Frequency),
            drawCount = Engine.GetFramesDrawn(),
            physicsCount = Engine.GetPhysicsFrames(),
            stateKey = frame.StateKey,
            semanticEventOrdinal = frame.EventOrdinal,
            gameplay = _captureGameplay(),
            held = _held.Keys.Select(key => key.ToString()).ToArray(),
            controls,
            trace = _trace?.Status,
        }, Json));
    }

    private void BeforeDraw() => _trace?.BeforeDraw();

    private void AfterDraw()
    {
        if (_pendingCapture == 0 && _liveFrames is null && _trace?.Pending != true)
            return;
        var request = _pendingCapture;
        _pendingCapture = 0;
        try
        {
            using var image = GetViewport().GetTexture().GetImage();
            if (image.IsEmpty())
                throw new InvalidDataException("Native viewport image is empty.");
            var bytes = image.GetData();
            _trace?.AfterDraw(image);
            var format = image.GetFormat() switch
            {
                Image.Format.Rgb8 => 1,
                Image.Format.Rgba8 => 2,
                _ => throw new InvalidDataException($"Unsupported native live viewport format: {image.GetFormat()}"),
            };
            _liveFrames?.Publish(checked((ulong)Engine.GetFramesDrawn()),
                checked((long)(Stopwatch.GetTimestamp() * (1_000_000_000.0 / Stopwatch.Frequency))),
                image.GetWidth(), image.GetHeight(), image.GetWidth() * (format == 1 ? 3 : 4), format, bytes);
            if (request == 0)
                return;
            var prefix = Path.Combine(_directory, $"frame-{request:D10}");
            File.WriteAllBytes(prefix + ".pixels", bytes);
            File.WriteAllBytes(prefix + ".png", image.SavePngToBuffer());
            AtomicWrite(prefix + ".frame.json", JsonSerializer.Serialize(new
            {
                schema = "opennv-live-harness-frame/v1",
                request,
                drawCount = Engine.GetFramesDrawn(),
                width = image.GetWidth(),
                height = image.GetHeight(),
                format = image.GetFormat().ToString(),
                semanticEventOrdinal = _captureState(0).EventOrdinal,
                finalFrameCorrespondence = "unobserved",
            }, Json));
        }
        catch (Exception exception)
        {
            Receipt(request, false, "Native frame capture failed: " + exception.Message);
        }
    }

    private void Receipt(ulong request, bool delivered, string message) =>
        AtomicWrite(Path.Combine(_directory, $"{request:D10}.receipt.json"), JsonSerializer.Serialize(new
        {
            request,
            delivered,
            message,
            physicsCount = Engine.GetPhysicsFrames(),
            drawCount = Engine.GetFramesDrawn(),
        }, Json));

    private static void AtomicWrite(string path, string text)
    {
        var pending = path + ".pending";
        File.WriteAllText(pending, text, new UTF8Encoding(false));
        File.Move(pending, path, true);
    }

    public override void _ExitTree()
    {
        RenderingServer.FramePostDraw -= AfterDraw;
        RenderingServer.FramePreDraw -= BeforeDraw;
        _trace?.Dispose();
        _liveFrames?.Dispose();
        _liveFrames = null;
        ReleaseAll();
    }
}
