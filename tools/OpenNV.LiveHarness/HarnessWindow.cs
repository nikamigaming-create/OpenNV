using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using OpenNV.Runtime.Diagnostics.Parity;

namespace OpenNV.LiveHarness;

internal sealed class HarnessWindow : Form
{
    private readonly HarnessConfiguration _configuration;
    private readonly LiveGameView _retail;
    private readonly LiveGameView _openNv;
    private readonly Label _status = new() { AutoSize = true };
    private readonly Label _driver = new() { AutoSize = true };
    private readonly RichTextBox _console = new() { ReadOnly = true, BorderStyle = BorderStyle.None, Dock = DockStyle.Fill };
    private readonly TextBox _command = new() { PlaceholderText = "Enter a harness action as JSON. Both people and the agent use this same console.", Dock = DockStyle.Fill };
    private readonly ComboBox _target = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };
    private readonly FlowLayoutPanel _retailControls = new() { Dock = DockStyle.Fill, AutoScroll = true };
    private readonly FlowLayoutPanel _openNvControls = new() { Dock = DockStyle.Fill, AutoScroll = true };
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 100 };
    private readonly Dictionary<string, string> _userHeld = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(string Engine, string Key), long> _inputGenerations = [];
    private long _nextInputGeneration;
    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly List<object> _events = [];
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Dictionary<string, JsonElement> _states = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _nextCommands = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _receiptContents = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _controlContents = new(StringComparer.Ordinal);
    private readonly Dictionary<string, LiveHarnessSurface> _surfaces = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _displayedFrames = new(StringComparer.Ordinal);
    private readonly Dictionary<string, LiveHarnessFrameMailbox> _frameMailboxes = new(StringComparer.Ordinal)
    {
        ["retail"] = new(), ["opennv"] = new(),
    };
    private readonly string _instance = Guid.NewGuid().ToString("N");
    private TaskCompletionSource _changed = NewSignal();
    private long _revision;
    private long _humanUntil;
    private long _lastHeartbeat;
    private long _retailLogOffset;
    private string _lastDriver = "Idle";
    private HarnessRecording? _recording;
    private readonly HashSet<string> _recordingStreamFailures = [];

    private static readonly Dictionary<string, int> RetailKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["W"] = 17, ["A"] = 30, ["S"] = 31, ["D"] = 32,
        ["E"] = 18, ["F"] = 33, ["R"] = 19, ["Q"] = 16,
        ["Space"] = 57, ["Tab"] = 15, ["Enter"] = 28, ["Escape"] = 1,
        ["Up"] = 200, ["Down"] = 208, ["Left"] = 203, ["Right"] = 205,
        ["Shift"] = 42, ["Control"] = 29, ["1"] = 2, ["2"] = 3,
    };

    internal HarnessWindow(HarnessConfiguration configuration, bool background = false)
    {
        _configuration = configuration;
        if (background) { Opacity = 0; ShowInTaskbar = false; }
        Text = "OpenNV — LIVE RETAIL / GODOT DRIVE CONSOLE";
        BackColor = Color.FromArgb(18, 23, 31);
        ForeColor = Color.FromArgb(224, 231, 240);
        Font = new Font("Segoe UI", 10);
        MinimumSize = new Size(1100, 720);
        WindowState = FormWindowState.Maximized;
        KeyPreview = true;
        _retail = new LiveGameView(configuration.RetailProcessId);
        _openNv = new LiveGameView(configuration.OpenNvProcessId);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 7, Padding = new Padding(12) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        foreach (var height in new[] { 34, 0, 54, 40, 38, 0, 34 })
            layout.RowStyles.Add(new RowStyle(height == 0 ? SizeType.Percent : SizeType.Absolute, height == 0 ? (layout.RowStyles.Count == 1 ? 78 : 22) : height));
        layout.Controls.Add(Title("RETAIL · Fallout: New Vegas", Color.FromArgb(225, 171, 78)), 0, 0);
        layout.Controls.Add(Title("OPENNV · Godot", Color.FromArgb(83, 177, 227)), 1, 0);
        layout.Controls.Add(_retail, 0, 1);
        layout.Controls.Add(_openNv, 1, 1);
        layout.Controls.Add(_retailControls, 0, 2);
        layout.Controls.Add(_openNvControls, 1, 2);
        var controls = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        _target.Items.AddRange(["Both", "Retail", "OpenNV"]);
        _target.SelectedIndex = 0;
        controls.Controls.Add(_target);
        controls.Controls.Add(Button("STOP · release all", () => Stop("user"), Color.FromArgb(149, 45, 45)));
        foreach (var (label, key) in new[] { ("W Forward", "W"), ("A Left", "A"), ("S Back", "S"), ("D Right", "D") })
        {
            var button = Button(label, () => { });
            button.MouseDown += (_, _) => UserKey(key, true);
            button.MouseUp += (_, _) => UserKey(key, false);
            button.MouseCaptureChanged += (_, _) => { if (!button.Capture) UserKey(key, false); };
            controls.Controls.Add(button);
        }
        controls.Controls.Add(Button("Jump", () => UserTap("Space")));
        controls.Controls.Add(Button("Activate", () => UserTap("E")));
        controls.Controls.Add(Button("Pip-Boy", () => UserTap("Tab")));
        controls.Controls.Add(Button("Enter / OK", () => UserTap("Enter")));
        controls.Controls.Add(Button("Escape", () => UserTap("Escape")));
        controls.Controls.Add(Button("Capture both", () => RunUser(new { op = "capture", target = "both" })));
        controls.Controls.Add(Button("Trace ON", () => RunUser(new { op = "trace", target = "opennv", enabled = true })));
        controls.Controls.Add(Button("Trace OFF", () => RunUser(new { op = "trace", target = "opennv", enabled = false })));
        controls.Controls.Add(Button("Inspect trace", () => RunUser(new { op = "trace.inspect" })));
        layout.Controls.Add(controls, 0, 3);
        layout.SetColumnSpan(controls, 2);
        var statusRow = new FlowLayoutPanel { Dock = DockStyle.Fill };
        statusRow.Controls.Add(_driver);
        statusRow.Controls.Add(_status);
        layout.Controls.Add(statusRow, 0, 4);
        layout.SetColumnSpan(statusRow, 2);
        _console.BackColor = Color.FromArgb(9, 12, 17);
        _console.ForeColor = Color.FromArgb(185, 205, 219);
        _console.Font = new Font("Consolas", 10);
        layout.Controls.Add(_console, 0, 5);
        layout.SetColumnSpan(_console, 2);
        var commandRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        commandRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        commandRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        commandRow.Controls.Add(_command, 0, 0);
        commandRow.Controls.Add(Button("Run", RunTextCommand), 1, 0);
        layout.Controls.Add(commandRow, 0, 6);
        layout.SetColumnSpan(commandRow, 2);
        Controls.Add(layout);
        _command.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; RunTextCommand(); } };
        KeyDown += OnKeyDown;
        KeyUp += OnKeyUp;
        Deactivate += (_, _) => ReleaseUserKeys();
        Resize += (_, _) => RefreshViews();
        _timer.Tick += (_, _) => Tick();
        Shown += (_, _) =>
        {
            Start();
            if (background) Hide();
            else Activate();
        };
        FormClosing += (_, _) => { Stop("system"); _shutdown.Cancel(); _timer.Stop(); foreach (var watcher in _watchers) watcher.Dispose(); _recording?.Stop().GetAwaiter().GetResult(); };
    }

    private void Start()
    {
        foreach (var (target, directory) in new[] { ("retail", _configuration.RetailCommandDirectory), ("opennv", _configuration.OpenNvCommandDirectory) })
        {
            _nextCommands[target] = Directory.EnumerateFiles(directory, "*.command")
                .Select(path => long.TryParse(Path.GetFileNameWithoutExtension(path), out var number) ? number : 0).DefaultIfEmpty().Max() + 1;
            var watcher = new FileSystemWatcher(directory) { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName };
            FileSystemEventHandler update = (_, e) => Post(() => ObserveFile(target, e.FullPath));
            watcher.Created += update;
            watcher.Changed += update;
            watcher.Renamed += (_, e) => Post(() => ObserveFile(target, e.FullPath));
            watcher.EnableRaisingEvents = true;
            _watchers.Add(watcher);
            foreach (var path in Directory.EnumerateFiles(directory, "*.json")) ObserveFile(target, path);
        }
        Log("system", "both", "ready", "Live windows connected; keyboard and console share the same input bridge.");
        _ = Task.Run(ServeClients);
        _ = Task.Run(() => WatchFrames("retail"));
        _ = Task.Run(() => WatchFrames("opennv"));
        _timer.Start();
        RefreshViews();
    }

    private void Tick()
    {
        RefreshViews();
        var now = Environment.TickCount64;
        if (now - _lastHeartbeat >= 400)
        {
            _lastHeartbeat = now;
            foreach (var held in _userHeld) DispatchKey(held.Key, true, held.Value, "user", false);
            if (_userHeld.Count != 0) _humanUntil = now + 1500;
        }
        _driver.Text = $"DRIVER: {_lastDriver}    HELD: {(_userHeld.Count == 0 ? "none" : string.Join(' ', _userHeld.Select(held => held.Key + ":" + held.Value)))}    ";
        _status.Text = $"Retail {FrameStatus("retail")} · OpenNV {FrameStatus("opennv")} · event {_revision} · state/pixel alignment unestablished";
        if (_recording is { Active: true })
        {
            foreach (var target in Targets("both"))
                if ((!_surfaces.TryGetValue(target, out var surface) || FrameAge(surface) > 1000) && _recordingStreamFailures.Add(target))
                {
                    _recording.Journal("stream-loss", new { target, revision = _revision, timestamp = DateTimeOffset.UtcNow });
                    _recording.Fail($"{target} native frame stream missing/stale during recording.");
                    Log("system", target, "recording-failure", "Native frame stream missing/stale. This run cannot pass comparison.");
                }
            _status.Text = (_recordingStreamFailures.Count == 0 ? "RECORDING · " : "CAPTURE FAILED · ") + _status.Text;
        }
        ObserveRetailLog();
    }

    private void RefreshViews() { if (IsHandleCreated) { _retail.RefreshWindow(); _openNv.RefreshWindow(); } }
    private string SelectedTarget => _target.SelectedItem!.ToString()!.ToLowerInvariant();

    private void RunTextCommand()
    {
        try { using var json = JsonDocument.Parse(_command.Text); Execute(json.RootElement, "user"); _command.Clear(); }
        catch (Exception exception) { Log("user", SelectedTarget, "error", exception.Message); }
    }

    private void RunUser(object command)
    {
        try { Execute(JsonSerializer.SerializeToElement(command, Program.Json), "user"); }
        catch (Exception exception) { Log("user", SelectedTarget, "error", exception.Message); }
    }

    private void UserTap(string key) => RunUser(new { op = "tap", target = SelectedTarget, key });
    private void UserKey(string key, bool pressed)
    {
        string target;
        if (pressed)
        {
            target = SelectedTarget;
            if (!_userHeld.TryAdd(key, target)) return;
        }
        else if (!_userHeld.Remove(key, out target!)) return;
        RunUser(new { op = "key", target, key, pressed });
    }
    private void ReleaseUserKeys() { foreach (var key in _userHeld.Keys.ToArray()) UserKey(key, false); }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (_command.ContainsFocus || _target.ContainsFocus) return;
        var key = KeyName(e.KeyCode);
        if (key is null) return;
        e.SuppressKeyPress = true;
        UserKey(key, true);
    }
    private void OnKeyUp(object? sender, KeyEventArgs e)
    {
        var key = KeyName(e.KeyCode);
        if (key is null || !_userHeld.ContainsKey(key)) return;
        e.SuppressKeyPress = true;
        UserKey(key, false);
    }
    private static string? KeyName(Keys key) => key switch
    {
        Keys.Return => "Enter", Keys.ShiftKey => "Shift", Keys.ControlKey => "Control",
        Keys.D1 => "1", Keys.D2 => "2", _ => RetailKeys.ContainsKey(key.ToString()) ? key.ToString() : null,
    };

    private object Execute(JsonElement command, string driver)
    {
        var operation = command.GetProperty("op").GetString() ?? throw new ArgumentException("Missing op.");
        if (operation == "state") return Snapshot();
        if (operation == "display")
        {
            var visible = command.GetProperty("visible").GetBoolean();
            if (visible) { Opacity = 1; ShowInTaskbar = true; Show(); }
            else { Stop(driver); Hide(); ShowInTaskbar = false; }
            Signal();
            return new { visible = Visible };
        }
        if (operation == "observe") return SaveObservation(command.GetProperty("directory").GetString()!);
        if (operation == "trace.compare") return HarnessByteCompare.Compare(command.GetProperty("left"), command.GetProperty("right"));
        if (operation == "trace.inspect")
        {
            var report = HarnessTraceInspector.LoadLatest(_configuration.OpenNvCommandDirectory);
            var inspector = new HarnessTraceInspector(report);
            inspector.Show(this);
            return new { report, opened = true };
        }
        if (operation == "record.start")
        {
            if (_recording is not null && !_recording.Finished) throw new InvalidOperationException("A recording is already active or finalizing.");
            RequireFreshStreams();
            _recordingStreamFailures.Clear();
            _recording = new HarnessRecording(command.GetProperty("directory").GetString()!, command.GetProperty("ffmpeg").GetString()!, Snapshot());
            Log(driver, "both", "recording", "Recording native frames and the state/input timeline.");
            return _recording.Status;
        }
        if (operation == "record.stop")
        {
            if (_recording is null) throw new InvalidOperationException("No recording has been started.");
            _ = _recording.Stop();
            return _recording.Status;
        }
        if (operation == "issue") return LogIssue(command, driver);
        if (operation == "close") { BeginInvoke(Close); return new { closing = true }; }
        if (operation == "stop") { Stop(driver); return Snapshot(); }
        if (driver == "agent" && Environment.TickCount64 < _humanUntil)
            throw new InvalidOperationException("User is driving; agent input is paused until the user releases controls.");
        if (driver == "user") _humanUntil = Environment.TickCount64 + 1500;
        _lastDriver = driver;
        var target = command.TryGetProperty("target", out var targetValue) ? targetValue.GetString()! : "both";
        if (target is not ("retail" or "opennv" or "both")) throw new ArgumentException("Target must be retail, opennv, or both.");
        switch (operation)
        {
            case "key":
                DispatchKey(command.GetProperty("key").GetString()!, command.GetProperty("pressed").GetBoolean(), target, driver, true);
                break;
            case "tap":
                var key = command.GetProperty("key").GetString()!;
                var duration = command.TryGetProperty("milliseconds", out var value) ? value.GetInt32() : 120;
                if (duration is < 20 or > 1000) throw new ArgumentException("Tap duration must be 20–1000 milliseconds.");
                // Each engine enforces the requested deadline locally. A load
                // transition may postpone delivery of the explicit key-up.
                DispatchKey(key, true, target, driver, true, duration);
                var generations = Targets(target).ToDictionary(engine => engine,
                    engine => _inputGenerations[(engine, key.ToUpperInvariant())]);
                _ = ReleaseAfter(key, generations, driver, duration);
                break;
            case "console":
                if (target != "retail") throw new ArgumentException("Native retail console commands must target retail explicitly.");
                Send(target, command.GetProperty("text").GetString()!, driver, "console");
                break;
            case "look":
                var dx = command.GetProperty("dx").GetInt32();
                var dy = command.GetProperty("dy").GetInt32();
                if (dx is < -32767 or > 32767 || dy is < -32767 or > 32767)
                    throw new ArgumentException("Relative mouse counts must be between -32767 and 32767.");
                foreach (var engine in Targets(target))
                    Send(engine, engine == "retail" ? $"native.look {dx} {dy}" :
                        JsonSerializer.Serialize(new { op = "look", dx, dy }, Program.Json), driver, "look");
                break;
            case "text":
                if (target != "opennv") throw new ArgumentException("Native retail text adapter is not yet available.");
                Send(target, JsonSerializer.Serialize(new { op = "text", text = command.GetProperty("text").GetString() }, Program.Json), driver, "text");
                break;
            case "button":
                if (target == "both")
                {
                    RequireFreshStreams();
                    var retailCommand = command.GetProperty("nativeCommand").GetString()!;
                    var openNvPath = command.GetProperty("path").GetString()!;
                    var retailButton = RequireObservedButton("retail", "nativeCommand", retailCommand);
                    var openNvButton = RequireObservedButton("opennv", "path", openNvPath);
                    if (retailButton.GetProperty("text").GetString() != openNvButton.GetProperty("text").GetString())
                        throw new InvalidOperationException("Paired buttons do not describe the same observed action.");
                    _recording?.Journal("paired-button", new { retailButton, openNvButton, state = Snapshot() });
                    Send("retail", retailCommand, driver, "paired button");
                    Send("opennv", JsonSerializer.Serialize(new { op = "button", path = openNvPath }, Program.Json), driver, "paired button");
                    break;
                }
                Send(target, target == "retail" ? command.GetProperty("nativeCommand").GetString()! : JsonSerializer.Serialize(new { op = "button", path = command.GetProperty("path").GetString() }, Program.Json), driver, "button");
                break;
            case "capture":
                foreach (var engine in Targets(target)) Send(engine, engine == "retail" ? "native.capture" : "{\"op\":\"capture\"}", driver, "capture");
                break;
            case "trace":
            case "trace.capture":
                if (target != "opennv") throw new ArgumentException("Render trace currently has an OpenNV producer; retail draw/material tracing is unbound.");
                Send(target, command.GetRawText(), driver, "telemetry");
                break;
            default: throw new ArgumentException($"Unknown harness operation: {operation}");
        }
        return Snapshot();
    }

    private async Task ReleaseAfter(string key, IReadOnlyDictionary<string, long> generations, string driver, int delay)
    {
        try { await Task.Delay(delay, _shutdown.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) { return; }
        Post(() =>
        {
            foreach (var generation in generations)
                if (_inputGenerations.GetValueOrDefault((generation.Key, key.ToUpperInvariant())) == generation.Value)
                    DispatchKey(key, false, generation.Key, driver, true);
        });
    }

    private void DispatchKey(string key, bool pressed, string target, string driver, bool record, int leaseMilliseconds = 900)
    {
        if (!RetailKeys.TryGetValue(key, out var scanCode)) throw new ArgumentException($"Unmapped key: {key}");
        foreach (var engine in Targets(target))
        {
            var command = engine == "retail"
                ? pressed ? $"native.hold {scanCode} {leaseMilliseconds}" : $"ReleaseKey {scanCode}"
                : JsonSerializer.Serialize(new { op = "key", key, pressed, leaseMilliseconds }, Program.Json);
            Send(engine, command, driver, pressed ? $"{key} down" : $"{key} up", record);
            var identity = (engine, key.ToUpperInvariant());
            if (!pressed) _inputGenerations.Remove(identity);
            else if (record || !_inputGenerations.ContainsKey(identity))
                _inputGenerations[identity] = ++_nextInputGeneration;
        }
    }

    private void Stop(string driver)
    {
        _userHeld.Clear();
        _inputGenerations.Clear();
        foreach (var engine in Targets("both"))
        {
            var directory = EngineDirectory(engine);
            AtomicWrite(Path.Combine(directory, "stop.request"), DateTimeOffset.UtcNow.ToString("O"));
        }
        _lastDriver = "Stopped";
        _humanUntil = Environment.TickCount64 + (driver == "user" ? 1500 : 0);
        Log(driver, "both", "stop", "Release all held inputs immediately.");
    }

    private void Send(string target, string command, string driver, string action, bool record = true)
    {
        if (command.Length is < 1 or > 4096 || (target == "retail" && Encoding.UTF8.GetByteCount(command) > 512) || command.IndexOfAny(['\r', '\n', '\0']) >= 0)
            throw new ArgumentException("Command must be a single bounded line.");
        var sequence = _nextCommands[target]++;
        AtomicWrite(Path.Combine(EngineDirectory(target), $"{sequence:D10}.command"), command);
        if (record) Log(driver, target, "sent", $"#{sequence} {action}: {command}");
    }

    private string EngineDirectory(string target) => target == "retail" ? _configuration.RetailCommandDirectory : _configuration.OpenNvCommandDirectory;
    private static IEnumerable<string> Targets(string target) => target == "both" ? ["retail", "opennv"] : [target];
    private static void AtomicWrite(string path, string text)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".pending";
        File.WriteAllText(temporary, text, new UTF8Encoding(false));
        File.Move(temporary, path, true);
    }

    private void ObserveFile(string target, string path)
    {
        if (!File.Exists(path) || !path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) return;
        var filename = Path.GetFileName(path);
        if (filename != "live-state.json" && filename != "live-ui.json" && !filename.EndsWith(".receipt.json", StringComparison.Ordinal) && !filename.EndsWith(".frame.json", StringComparison.Ordinal)) return;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            var text = reader.ReadToEnd();
            if (_receiptContents.GetValueOrDefault(path) == text) return;
            _receiptContents[path] = text;
            using var document = JsonDocument.Parse(text);
            if (filename is "live-state.json" or "live-ui.json")
            {
                _states[target + "/" + filename] = document.RootElement.Clone();
                _recording?.Journal("state", new { target, filename, state = document.RootElement.Clone() });
                if (document.RootElement.TryGetProperty("process", out var process))
                    (target == "retail" ? _retail : _openNv).SetProcess(process.GetInt32());
                RefreshControls(target, document.RootElement);
                Signal();
            }
            else Log("engine", target, filename.EndsWith(".frame.json", StringComparison.Ordinal) ? "pixels" : "receipt", text);
        }
        catch (IOException) { }
        catch (JsonException) { }
    }

    private void ObserveRetailLog()
    {
        var path = Path.Combine(_configuration.RetailCommandDirectory, "bridge.log");
        if (!File.Exists(path)) return;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (stream.Length <= _retailLogOffset) return;
            stream.Position = _retailLogOffset;
            using var reader = new StreamReader(stream);
            var text = reader.ReadToEnd();
            _retailLogOffset = stream.Position;
            foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                if (line.StartsWith("returned ", StringComparison.Ordinal) || line.StartsWith("capture ", StringComparison.Ordinal) || line.Contains("failed", StringComparison.Ordinal))
                    Log("engine", "retail", "receipt", line.Trim());
        }
        catch (IOException) { }
    }

    private void RefreshControls(string target, JsonElement state)
    {
        if (!state.TryGetProperty("controls", out var controls)) return;
        var identity = controls.GetRawText();
        if (_controlContents.GetValueOrDefault(target) == identity) return;
        _controlContents[target] = identity;
        var panel = target == "retail" ? _retailControls : _openNvControls;
        panel.SuspendLayout();
        foreach (var control in panel.Controls.Cast<Control>().ToArray()) control.Dispose();
        panel.Controls.Clear();
        foreach (var item in controls.EnumerateArray())
        {
            var captured = item.Clone();
            var label = item.GetProperty("text").GetString() ?? "Button";
            panel.Controls.Add(Button(label, () =>
            {
                var action = target == "retail"
                    ? JsonSerializer.SerializeToElement(new { op = "button", target, nativeCommand = captured.GetProperty("nativeCommand").GetString() }, Program.Json)
                    : JsonSerializer.SerializeToElement(new { op = "button", target, path = captured.GetProperty("path").GetString() }, Program.Json);
                try { Execute(action, "user"); } catch (Exception exception) { Log("user", target, "error", exception.Message); }
            }));
        }
        panel.ResumeLayout();
    }

    private void Log(string driver, string target, string kind, string message)
    {
        var entry = new { revision = ++_revision, timestamp = DateTimeOffset.UtcNow, driver, target, kind, message };
        _events.Add(entry);
        _recording?.Journal("event", entry);
        if (_events.Count > 100) _events.RemoveAt(0);
        File.AppendAllText(Path.Combine(_configuration.Directory, "events.jsonl"), JsonSerializer.Serialize(entry, Program.Json) + "\n");
        _console.AppendText($"{DateTime.Now:HH:mm:ss.fff} {driver,-6} {target,-7} {kind,-8} {message}\n");
        if (_console.TextLength > 100000) { _console.Select(0, 30000); _console.SelectedText = ""; }
        _console.SelectionStart = _console.TextLength;
        _console.ScrollToCaret();
        Signal(false);
    }

    private void Signal(bool increment = true)
    {
        if (increment) ++_revision;
        var previous = _changed;
        _changed = NewSignal();
        previous.TrySetResult();
    }
    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private object Snapshot() => new
    {
        session = _configuration.Session, instance = _instance, revision = _revision, driver = _lastDriver,
        comparisonVisible = Visible,
        recording = _recording?.Status,
        userHasControl = Environment.TickCount64 < _humanUntil,
        retailWindow = _retail.Connected, openNvWindow = _openNv.Connected,
        presentation = new { retail = _retail.Presentation, opennv = _openNv.Presentation },
        states = _states.ToDictionary(pair => pair.Key, pair => pair.Value),
        frames = _surfaces.ToDictionary(pair => pair.Key, pair => new { pair.Value.Sequence, pair.Value.Draw, pair.Value.Width, pair.Value.Height, pair.Value.Pitch, pair.Value.Format, pair.Value.Nanoseconds, bytes = pair.Value.Bytes.Length,
            ageMilliseconds = FrameAge(pair.Value), stale = FrameAge(pair.Value) > 1000,
            replacedPreviewFrames = _frameMailboxes[pair.Key].Replaced }),
        events = _events.TakeLast(12).ToArray(),
    };

    private static double FrameAge(LiveHarnessSurface surface) => Math.Max(0,
        Stopwatch.GetTimestamp() * (1000.0 / Stopwatch.Frequency) - surface.Nanoseconds / 1_000_000.0);

    private string FrameStatus(string target)
    {
        if (!_surfaces.TryGetValue(target, out var surface)) return "waiting for pixels";
        var age = FrameAge(surface);
        return $"{(age > 1000 ? "STALE" : "live")} {age:0} ms · draw {surface.Draw}";
    }

    private void WatchFrames(string target)
    {
        while (!_shutdown.IsCancellationRequested)
        {
            try
            {
                using var frames = new LiveHarnessFrameBuffer(_configuration.Session + "." + target, false);
                while (WaitHandle.WaitAny([frames.Ready, _shutdown.Token.WaitHandle]) == 0)
                {
                    var frame = frames.ReadLatest();
                    if (frame is not null) _recording?.Accept(target, frame);
                    if (frame is not null && _frameMailboxes[target].Publish(frame))
                        Post(() =>
                        {
                            var latest = _frameMailboxes[target].TakeLatest();
                            if (latest is not null)
                            {
                                _surfaces[target] = latest;
                                var view = target == "retail" ? _retail : _openNv;
                                if (_displayedFrames.GetValueOrDefault(target) != latest.Sequence)
                                {
                                    // Present on frame arrival. The status/lease timer is not a video clock.
                                    view.SetNativePreview(Preview(latest), latest.Sequence, latest.Nanoseconds);
                                    _displayedFrames[target] = latest.Sequence;
                                }
                                Signal();
                            }
                        });
                }
                return;
            }
            catch (Exception exception) when (exception is FileNotFoundException or WaitHandleCannotBeOpenedException)
            {
                if (_shutdown.Token.WaitHandle.WaitOne(500)) return;
            }
            catch (Exception exception)
            {
                Post(() => Log("engine", target, "frame-error", exception.Message));
                return;
            }
        }
    }

    private object SaveObservation(string directory, bool allowStale = false)
    {
        if (!Path.IsPathFullyQualified(directory) || Directory.Exists(directory))
            throw new ArgumentException("Observation requires a new absolute private directory.");
        if (!allowStale) RequireFreshStreams();
        Directory.CreateDirectory(directory);
        using var dashboard = new Bitmap(Width, Height);
        DrawToBitmap(dashboard, new Rectangle(Point.Empty, Size));
        using (var graphics = Graphics.FromImage(dashboard))
        {
            foreach (var view in new[] { _retail, _openNv })
            {
                var origin = view.PointToScreen(Point.Empty) - (Size)Location;
                // DrawToBitmap may contain a previous, differently fitted preview.
                using var background = new SolidBrush(view.BackColor);
                graphics.FillRectangle(background, new Rectangle(origin, view.ClientSize));
            }
            foreach (var (target, surface) in _surfaces)
            {
                File.WriteAllBytes(Path.Combine(directory, target + ".pixels"), surface.Bytes);
                using var image = Preview(surface);
                image.Save(Path.Combine(directory, target + ".png"), ImageFormat.Png);
                var view = target == "retail" ? _retail : _openNv;
                var rectangle = view.DisplayBounds(surface.Width, surface.Height);
                rectangle.Location = view.PointToScreen(rectangle.Location) - (Size)Location;
                graphics.DrawImage(image, rectangle);
                if (FrameAge(surface) > 1000)
                    graphics.DrawString($"STALE {target} · {FrameAge(surface):0} ms", Font, Brushes.Red, rectangle.Location);
            }
        }
        var path = Path.Combine(directory, "harness.png");
        dashboard.Save(path, ImageFormat.Png);
        File.WriteAllText(Path.Combine(directory, "state.json"), JsonSerializer.Serialize(Snapshot(), Program.Json));
        Log("agent", "both", "observed", $"Native pixels and live console: {directory}");
        return new { image = path, state = Path.Combine(directory, "state.json"), snapshot = Snapshot() };
    }

    private object LogIssue(JsonElement command, string driver)
    {
        var id = command.GetProperty("id").GetString()!;
        if (id.Length is < 1 or > 80 || id.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('-' or '_')))
            throw new ArgumentException("Issue id must be a bounded stable identifier.");
        var status = command.TryGetProperty("status", out var state) ? state.GetString() : "open";
        if (status is not ("open" or "fixed-awaiting-retest" or "verified" or "reopened")) throw new ArgumentException("Invalid issue status.");
        var directory = Path.Combine(_configuration.Directory, "issues", $"{id}-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss-fff}");
        // A missing stream is itself a discrepancy and must remain loggable.
        var observation = SaveObservation(directory, allowStale: true);
        var issue = new
        {
            id, status, timestamp = DateTimeOffset.UtcNow, revision = _revision,
            description = command.GetProperty("description").GetString(),
            expected = command.GetProperty("expected").GetString(),
            observed = command.GetProperty("observed").GetString(),
            owner = command.GetProperty("owner").GetString(),
            evidence = directory, recording = _recording?.Status,
        };
        File.AppendAllText(Path.Combine(_configuration.Directory, "issues.jsonl"), JsonSerializer.Serialize(issue, Program.Json) + "\n");
        _recording?.Journal("issue", issue);
        Log(driver, "both", "issue", $"{id} {status}: {issue.description}");
        return new { issue, observation };
    }

    private void RequireFreshStreams()
    {
        foreach (var target in Targets("both"))
            if (!_surfaces.TryGetValue(target, out var surface) || FrameAge(surface) > 1000)
                throw new InvalidOperationException($"{target} native frame stream is missing/stale. Paired capture cannot start.");
    }

    private JsonElement RequireObservedButton(string target, string field, string value)
    {
        var filename = target == "retail" ? "live-ui.json" : "live-state.json";
        if (!_states.TryGetValue(target + "/" + filename, out var state))
            throw new InvalidOperationException($"{target} has no observed menu state.");
        var timestamp = state.GetProperty(target == "retail" ? "endNanoseconds" : "sampledNanoseconds").GetDouble();
        var age = Stopwatch.GetTimestamp() * (1000.0 / Stopwatch.Frequency) - timestamp / 1_000_000;
        if (age > 5000 || (target == "retail" && !state.GetProperty("menuStateValid").GetBoolean()))
            throw new InvalidOperationException($"{target} menu observation is stale or invalid.");
        var matches = state.GetProperty("controls").EnumerateArray()
            .Where(button => button.GetProperty(field).GetString() == value).ToArray();
        if (matches.Length != 1)
            throw new InvalidOperationException($"{target} button is no longer uniquely observed and enabled.");
        return matches[0];
    }

    private static Bitmap Preview(LiveHarnessSurface surface)
    {
        var bitmap = new Bitmap(surface.Width, surface.Height, PixelFormat.Format32bppArgb);
        var locked = bitmap.LockBits(new Rectangle(0, 0, surface.Width, surface.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            var row = new byte[surface.Width * 4];
            var stride = surface.Format == 1 ? 3 : 4;
            for (var y = 0; y < surface.Height; ++y)
            {
                for (var x = 0; x < surface.Width; ++x)
                {
                    var input = y * surface.Pitch + x * stride;
                    var output = x * 4;
                    var rgb = surface.Format is 1 or 2;
                    row[output] = surface.Bytes[input + (rgb ? 2 : 0)];
                    row[output + 1] = surface.Bytes[input + 1];
                    row[output + 2] = surface.Bytes[input + (rgb ? 0 : 2)];
                    row[output + 3] = surface.Format is 2 or 4 ? surface.Bytes[input + 3] : (byte)255;
                }
                Marshal.Copy(row, 0, locked.Scan0 + y * locked.Stride, row.Length);
            }
        }
        finally { bitmap.UnlockBits(locked); }
        return bitmap;
    }

    private async Task ServeClients()
    {
        while (!_shutdown.IsCancellationRequested)
        {
            var pipe = new NamedPipeServerStream(Program.PipeName(_configuration.Session), PipeDirection.InOut, 8, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            try { await pipe.WaitForConnectionAsync(_shutdown.Token); _ = HandleClient(pipe); }
            catch (OperationCanceledException) { pipe.Dispose(); break; }
        }
    }

    private async Task HandleClient(NamedPipeServerStream pipe)
    {
        using (pipe)
        using (var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true))
        using (var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true })
        {
            try
            {
                var line = await reader.ReadLineAsync(_shutdown.Token) ?? throw new IOException("Client disconnected.");
                if (line.Length > 16384) throw new InvalidDataException("Request exceeds the harness command limit.");
                using var document = JsonDocument.Parse(line);
                var command = document.RootElement.Clone();
                if (command.GetProperty("op").GetString() == "wait")
                {
                    var after = command.GetProperty("afterRevision").GetInt64();
                    var milliseconds = command.TryGetProperty("timeoutMilliseconds", out var value) ? value.GetInt32() : 30000;
                    if (milliseconds is < 1 or > 60000) throw new ArgumentException("Wait timeout must be 1–60000 ms.");
                    var ready = new TaskCompletionSource<Task>(TaskCreationOptions.RunContinuationsAsynchronously);
                    var instance = command.TryGetProperty("instance", out var instanceValue) ? instanceValue.GetString() : null;
                    Post(() => ready.SetResult(instance is not null && instance != _instance || _revision > after ? Task.CompletedTask : _changed.Task));
                    var signal = await ready.Task;
                    await Task.WhenAny(signal, Task.Delay(milliseconds, _shutdown.Token));
                    command = JsonSerializer.SerializeToElement(new { op = "state" }, Program.Json);
                }
                var response = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
                Post(() => { try { response.SetResult(new { ok = true, result = Execute(command, "agent") }); } catch (Exception exception) { response.SetResult(new { ok = false, error = exception.Message }); } });
                await writer.WriteLineAsync(JsonSerializer.Serialize(await response.Task, Program.Json));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                try { await writer.WriteLineAsync(JsonSerializer.Serialize(new { ok = false, error = exception.Message }, Program.Json)); } catch (IOException) { }
            }
        }
    }

    private void Post(Action action)
    {
        if (!IsDisposed && IsHandleCreated && !_shutdown.IsCancellationRequested)
            try { BeginInvoke(action); } catch (InvalidOperationException) { }
    }
    private static Label Title(string text, Color color) => new() { Text = text, ForeColor = color, Font = new Font("Segoe UI Semibold", 13), AutoSize = true };
    private static Button Button(string text, Action action, Color? color = null)
    {
        var button = new Button { Text = text, AutoSize = true, Height = 30, FlatStyle = FlatStyle.Flat, BackColor = color ?? Color.FromArgb(35, 45, 59), ForeColor = Color.White, Margin = new Padding(3), TabStop = false };
        button.Click += (_, _) => action();
        return button;
    }
}
