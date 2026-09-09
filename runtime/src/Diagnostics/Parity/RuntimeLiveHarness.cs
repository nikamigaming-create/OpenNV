using System.Diagnostics;
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
    private Func<object> _captureSummary = null!;
    private Func<(string StateKey, ulong EventOrdinal)> _captureIdentity = null!;
    private double _lastStateWriteMilliseconds;
    private double _lastSnapshotMilliseconds, _lastSerializeMilliseconds, _lastFileMilliseconds;
    private Task<double>? _stateWrite;
    private long _statePublicationFailures;
    private string? _lastStatePublicationFailure;
    private readonly Dictionary<Key, ulong> _held = [];
    private readonly HashSet<BaseButton> _controls = [];
    private readonly HashSet<Viewport> _viewports = [];
    private readonly FrameIntervalWindow _frameIntervals = new();
    private ulong _nextRequest = 1;
    private string? _commandReadFailure;
    private ulong _pendingCapture;
    private ulong _lastStateMilliseconds;
    private ulong _lastStopWrite;
    private LiveHarnessFrameBuffer? _liveFrames;
    private RuntimeRenderTrace? _trace;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly bool JitOptimizationDisabled = typeof(RuntimeLiveHarness).Assembly
        .GetCustomAttributes(typeof(DebuggableAttribute), false).OfType<DebuggableAttribute>()
        .SingleOrDefault()?.IsJITOptimizerDisabled ?? false;

    public override void _EnterTree()
    {
        foreach (var button in GetTree().Root.FindChildren("*", nameof(BaseButton), true, false).OfType<BaseButton>()) _controls.Add(button);
        TrackNode(GetTree().Root);
        foreach (var viewport in GetTree().Root.FindChildren("*", nameof(Viewport), true, false)) TrackNode(viewport);
        GetTree().NodeAdded += TrackNode;
        GetTree().NodeRemoved += UntrackNode;
    }
    private void TrackNode(Node node)
    {
        if (node is BaseButton button) _controls.Add(button);
        if (node is Viewport viewport && _viewports.Add(viewport))
            RenderingServer.ViewportSetMeasureRenderTime(viewport.GetViewportRid(), true);
    }
    private void UntrackNode(Node node)
    {
        if (node is BaseButton button) _controls.Remove(button);
        if (node is Viewport viewport && _viewports.Remove(viewport))
            RenderingServer.ViewportSetMeasureRenderTime(viewport.GetViewportRid(), false);
    }

    internal void Configure(string directory, Func<ulong, ParityTelemetryFrame> captureState, Func<object> captureGameplay,
        Func<FalloutPluginStack?>? stack = null, Func<object>? captureSummary = null,
        Func<(string StateKey, ulong EventOrdinal)>? captureIdentity = null)
    {
        if (!Path.IsPathFullyQualified(directory))
            throw new ArgumentException("--live-harness requires an absolute private command directory.");
        _directory = directory;
        Directory.CreateDirectory(directory);
        _captureState = captureState;
        _captureGameplay = captureGameplay;
        _captureSummary = captureSummary ?? captureGameplay;
        _captureIdentity = captureIdentity ?? (() => { var frame = captureState(0); return (frame.StateKey, frame.EventOrdinal); });
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
            // Atomic publication can briefly overlap a Windows rename handle.
            // Keep this request pending on I/O failure; never consume an input
            // before its complete command has been read.
            if (!LiveHarnessAtomicFile.TryRead(path, out var command, out _commandReadFailure))
                break;
            var request = _nextRequest++;
            try
            {
                using var document = JsonDocument.Parse(command);
                Dispatch(document.RootElement, request);
                Receipt(request, true, "Delivered to Godot input; resulting gameplay state is observed separately.");
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidDataException or JsonException or InvalidOperationException or KeyNotFoundException)
            {
                Receipt(request, false, exception.Message);
            }
        }
        if (now - _lastStateMilliseconds >= 250)
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
            case "pointer":
                var position = new Vector2(command.GetProperty("x").GetSingle(), command.GetProperty("y").GetSingle());
                if (!position.IsFinite() || !GetViewport().GetVisibleRect().HasPoint(position))
                    throw new ArgumentException("Pointer input must lie inside the observed viewport.");
                if (command.TryGetProperty("button", out var requestedButton))
                {
                    if (!Enum.TryParse<MouseButton>(requestedButton.GetString(), true, out var mouseButton) || mouseButton == MouseButton.None)
                        throw new ArgumentException("Unknown pointer button.");
                    var explicitPress = command.TryGetProperty("pressed", out var requestedPress);
                    GetViewport().PushInput(new InputEventMouseButton
                    {
                        Position = position,
                        GlobalPosition = position,
                        ButtonIndex = mouseButton,
                        Pressed = !explicitPress || requestedPress.GetBoolean()
                    }, true);
                    if (!explicitPress) GetViewport().PushInput(new InputEventMouseButton
                    {
                        Position = position,
                        GlobalPosition = position,
                        ButtonIndex = mouseButton,
                        Pressed = false
                    }, true);
                }
                else
                    GetViewport().PushInput(new InputEventMouseMotion { Position = position, GlobalPosition = position }, true);
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
            case "frames":
                _liveFrames?.Dispose(); _liveFrames = null;
                if (command.GetProperty("enabled").GetBoolean())
                    _liveFrames = new LiveHarnessFrameBuffer(command.GetProperty("channel").GetString()!, true);
                break;
            case "capture":
                if (_pendingCapture != 0)
                    throw new InvalidOperationException("A native frame capture is already pending.");
                _pendingCapture = request;
                break;
            case "state":
                AtomicWrite(Path.Combine(_directory, $"{request:D10}.state.json"), JsonSerializer.Serialize(_captureGameplay(), Json));
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
        try { WriteState(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // A diagnostic reader can deny Windows delete-sharing during the
            // atomic replacement. Keep ordinary input/lease expiry running and
            // report every failed publication in the next successful snapshot.
            ReportPublicationFailure(error);
        }
    }

    private void ReportPublicationFailure(Exception error)
    {
        _statePublicationFailures++;
        if (_lastStatePublicationFailure != error.Message) GD.PushWarning("OPENNV_STATE_PUBLICATION_LOSS " + error.Message);
        _lastStatePublicationFailure = error.Message;
    }

    private void CompleteStateWrite()
    {
        var pending = _stateWrite;
        _stateWrite = null;
        if (pending is not null) _lastFileMilliseconds = pending.GetAwaiter().GetResult();
    }

    private void WriteState()
    {
        if (_stateWrite is { IsCompleted: false })
            throw new IOException("Live-state publication exceeded its interval; one pending snapshot is retained.");
        CompleteStateWrite();
        var started = Stopwatch.GetTimestamp();
        var identity = _captureIdentity();
        var controls = _controls.Where(button => button.IsVisibleInTree() && !button.Disabled)
            .Select(button =>
            {
                using var path = button.GetPath();
                var rect = button.GetGlobalRect();
                return new
                {
                    path = path.ToString(),
                    text = button is Button labelled ? labelled.Text : button is NativeBitmapMenuButton bitmap ? bitmap.Text : button is NativeOwnedTileTarget owned ? owned.Text : button.Name.ToString(),
                    rect = new[] { rect.Position.X, rect.Position.Y, rect.Size.X, rect.Size.Y }
                };
            })
            .ToArray();
        var snapshot = new
        {
            schema = "opennv-live-harness-state/v1",
            engine = "opennv",
            process = System.Environment.ProcessId,
            sampledNanoseconds = Stopwatch.GetTimestamp() * (1_000_000_000.0 / Stopwatch.Frequency),
            drawCount = Engine.GetFramesDrawn(),
            physicsCount = Engine.GetPhysicsFrames(),
            stateKey = identity.StateKey,
            semanticEventOrdinal = identity.EventOrdinal,
            statePublicationFailures = _statePublicationFailures,
            lastStatePublicationFailure = _lastStatePublicationFailure,
            nextCommandRequest = _nextRequest,
            commandReadFailure = _commandReadFailure,
            gameplay = _captureSummary(),
            performance = new
            {
                jitOptimizationDisabled = JitOptimizationDisabled,
                framesPerSecond = Godot.Performance.GetMonitor(Godot.Performance.Monitor.TimeFps),
                processMilliseconds = 1000 * Godot.Performance.GetMonitor(Godot.Performance.Monitor.TimeProcess),
                physicsMilliseconds = 1000 * Godot.Performance.GetMonitor(Godot.Performance.Monitor.TimePhysicsProcess),
                previousSummaryMilliseconds = _lastStateWriteMilliseconds,
                previousSnapshotMilliseconds = _lastSnapshotMilliseconds,
                previousSerializeMilliseconds = _lastSerializeMilliseconds,
                previousFileMilliseconds = _lastFileMilliseconds,
                hostDrawIntervals = _frameIntervals.Capture(),
                drawCalls = Godot.Performance.GetMonitor(Godot.Performance.Monitor.RenderTotalDrawCallsInFrame),
                renderSetupCpuMilliseconds = RenderingServer.GetFrameSetupTimeCpu(),
                viewports = _viewports.Select(viewport => new
                {
                    name = viewport.Name.ToString(),
                    cpuMilliseconds = RenderingServer.ViewportGetMeasuredRenderTimeCpu(viewport.GetViewportRid()),
                    gpuMilliseconds = RenderingServer.ViewportGetMeasuredRenderTimeGpu(viewport.GetViewportRid())
                }).ToArray(),
            },
            held = _held.Keys.Select(key => key.ToString()).ToArray(),
            controls,
            trace = _trace?.Status,
        };
        _lastSnapshotMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        var phase = Stopwatch.GetTimestamp();
        var json = JsonSerializer.Serialize(snapshot, Json);
        _lastSerializeMilliseconds = Stopwatch.GetElapsedTime(phase).TotalMilliseconds;
        var path = Path.Combine(_directory, "live-state.json");
        // Freeze all engine/gameplay reads and serialization on their owner
        // thread. Only immutable text and file IO cross to this single writer.
        _stateWrite = Task.Run(() =>
        {
            var writeStarted = Stopwatch.GetTimestamp();
            AtomicWrite(path, json);
            return Stopwatch.GetElapsedTime(writeStarted).TotalMilliseconds;
        });
        _lastStateWriteMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
    }

    private void BeforeDraw() => _trace?.BeforeDraw();

    private void AfterDraw()
    {
        _frameIntervals.Record(Stopwatch.GetTimestamp() * (1000.0 / Stopwatch.Frequency));
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

    private static void AtomicWrite(string path, string text) => LiveHarnessAtomicFile.Write(path, text);

    public override void _ExitTree()
    {
        try { CompleteStateWrite(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { ReportPublicationFailure(error); }
        GetTree().NodeAdded -= TrackNode;
        GetTree().NodeRemoved -= UntrackNode;
        _controls.Clear();
        foreach (var viewport in _viewports.Where(IsInstanceValid))
            RenderingServer.ViewportSetMeasureRenderTime(viewport.GetViewportRid(), false);
        _viewports.Clear();
        RenderingServer.FramePostDraw -= AfterDraw;
        RenderingServer.FramePreDraw -= BeforeDraw;
        _trace?.Dispose();
        _liveFrames?.Dispose();
        _liveFrames = null;
        ReleaseAll();
    }
}
