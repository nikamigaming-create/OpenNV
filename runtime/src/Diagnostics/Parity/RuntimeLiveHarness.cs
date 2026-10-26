using System.Diagnostics;
using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.Bots;
using OpenNV.Runtime.Presentation.Ui;
using OpenNV.Runtime.World.Actors;

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
    private LiveHarnessPendingPublication? _stateWrite;
    private long _statePublicationSequence;
    private readonly LiveHarnessPublicationHistory _statePublicationHistory = new();
    private bool _statePublicationRequested;
    private long _statePublicationDeferredRequests;
    private LiveHarnessFileWrite? _lastFileWrite;
    private long _statePublicationSharingRetries;
    private long _statePublicationFailures;
    private string? _lastStatePublicationFailure;
    private readonly Dictionary<Key, ulong> _held = [];
    private readonly HashSet<BaseButton> _controls = [];
    private readonly HashSet<Viewport> _viewports = [];
    private readonly FrameIntervalWindow _frameIntervals = new();
    private ulong _nextRequest = 1;
    private string? _commandReadFailure;
    private ulong _pendingCapture;
    private readonly LiveHarnessPublicationCadence _statePublicationCadence = new();
    private ulong _lastStopWrite;
    private LiveHarnessFrameBuffer? _liveFrames;
    private RuntimeRenderTrace? _trace;
    private ReactiveReferenceBot? _bot;
    private float _botSensitivity;
    private Key _botForward, _botActivate;
    private Action? _pumpBotInput;
    private object? _lastPhysicsTest;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly bool JitOptimizationDisabled = typeof(RuntimeLiveHarness).Assembly
        .GetCustomAttributes(typeof(DebuggableAttribute), false).OfType<DebuggableAttribute>()
        .SingleOrDefault()?.IsJITOptimizerDisabled ?? false;

    internal void ConfigureBot(Func<string, BotObservation> observe,
        Func<System.Numerics.Vector3, System.Numerics.Vector3, float, BotNavigationRoute> route, float sensitivity, Key forward, Key activate,
        Func<OpenNV.Runtime.Gameplay.Bots.SteeringIntent, bool, bool>? inputOverride = null, Action? pumpInput = null,
        Func<System.Numerics.Vector3, System.Numerics.Vector3, System.Numerics.Vector3, float, float, BotNavigationRoute?>? approachRoute = null,
        Action? cancelRoute = null)
    {
        if (!float.IsFinite(sensitivity) || sensitivity <= 0) throw new ArgumentException("Invalid mouse sensitivity.");
        _botSensitivity = sensitivity; _botForward = forward; _botActivate = activate;
        _pumpBotInput = pumpInput;
        ReactiveReferenceBot.Log = GD.Print;
        _bot = new(identity =>
        {
            var observation = observe(identity);
            _botCombatBindings = observation.Combat?.Bindings;
            return observation;
        }, route, (intent, activation) =>
        {
            if (inputOverride?.Invoke(intent, activation) == true)
            {
                if (_inputRecording is not null) FailInputRecording("Simulator steering has no recorded physical input adapter.");
                return;
            }
            SetKey(_botForward, intent.Forward, 200);
            if (intent.YawRadians != 0 || intent.PitchRadians != 0)
                DeliverLook(-intent.YawRadians / _botSensitivity, -intent.PitchRadians / _botSensitivity);
            SetKey(_botActivate, activation, 50);
            DeliverBotCombatInput(intent);
        }, approachRoute, cancelRoute, _botSkills, PersistBotSkillLibrary);
    }

    public override void _EnterTree()
    {
        foreach (var button in GetTree().Root.FindChildren("*", nameof(BaseButton), true, false).OfType<BaseButton>()) _controls.Add(button);
        TrackNode(GetTree().Root);
        foreach (var viewport in GetTree().Root.FindChildren("*", nameof(Viewport), true, false)) TrackNode(viewport);
        GetTree().NodeAdded += TrackNode;
        GetTree().NodeRemoved += UntrackNode;
        Callable.From(SetupPipelinePhases).CallDeferred();
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
        // An in-process save load replaces this node. Already acknowledged
        // input belongs to the old session and must never be replayed.
        _nextRequest = checked(Directory.EnumerateFiles(directory, "*.receipt.json")
            .Select(path => ulong.TryParse(Path.GetFileName(path).Split('.')[0], out var value) ? value : 0)
            .DefaultIfEmpty().Max() + 1);
        _captureState = captureState;
        _inputSource = stack;
        _captureGameplay = captureGameplay;
        _captureSummary = captureSummary ?? captureGameplay;
        _captureIdentity = captureIdentity ?? (() => { var frame = captureState(0); return (frame.StateKey, frame.EventOrdinal); });
        _trace = new RuntimeRenderTrace(this, directory, stack ?? (() => null), captureGameplay);
        var channel = System.Environment.GetEnvironmentVariable("OPENNV_LIVE_HARNESS_CHANNEL");
        if (!string.IsNullOrEmpty(channel))
            _liveFrames = new LiveHarnessFrameBuffer(channel + ".opennv", true);
        ProcessMode = ProcessModeEnum.Always;
        ProcessPriority = int.MinValue;
        ProcessPhysicsPriority = int.MinValue;
        RenderingServer.FramePostDraw += AfterDraw;
        RenderingServer.FramePreDraw += BeforeDraw;
    }

    public override void _PhysicsProcess(double delta) => _bot?.AdvanceCombatPhysics((float)delta);

    public override void _Process(double delta)
    {
        var now = Time.GetTicksMsec();
        var stop = Path.Combine(_directory, "stop.request");
        if (File.Exists(stop))
        {
            var stamp = (ulong)File.GetLastWriteTimeUtc(stop).Ticks;
            if (stamp != _lastStopWrite)
            {
                _lastStopWrite = stamp;
                _campaignBot.Stop();
                _bot?.Stop();
                _inputPlayback?.Stop("Playback interrupted by the stop request.", ReleaseAll);
                FinishInputRecording("Recording interrupted by the stop request.");
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
        foreach (var button in _heldMouse.Where(pair => pair.Value <= now).Select(pair => pair.Key).ToArray())
            SetMouseButton(button, false, 0);
        RenewRecordedHumanKeys(now);
        AdvanceInputPlayback();
        for (var index = 0; index < 32 && _checkpointTransitioning?.Invoke() != true; ++index)
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
                Receipt(request, true, document.RootElement.GetProperty("op").GetString()?.StartsWith("checkpoint.", StringComparison.Ordinal) == true
                    ? "Handled by the shared save owner; cold loading and resulting gameplay state are observed separately."
                    : document.RootElement.GetProperty("op").GetString() == "bot"
                    ? "Bot policy command accepted; native gameplay receipts and evidence persistence are observed separately."
                    : "Delivered to Godot input; resulting gameplay state is observed separately.");
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidDataException or JsonException or InvalidOperationException or KeyNotFoundException or NotSupportedException or IOException or UnauthorizedAccessException)
            {
                if (_inputRecording is not null) FailInputRecording("Rejected recorded command: " + exception.Message);
                Receipt(request, false, exception.Message);
            }
        }
        TickCampaign((float)delta);
        _bot?.Tick((float)delta);
        try { _pumpBotInput?.Invoke(); }
        catch (Exception error) when (error is IOException or InvalidOperationException)
        { _bot?.Fail("Simulator input transport failed: " + error.Message, "input-adapter"); }
        var publicationNow = Time.GetTicksMsec();
        if (_statePublicationRequested && _stateWrite?.IsCompleted == true ||
            _statePublicationCadence.Due(publicationNow))
        {
            _statePublicationCadence.Scheduled(publicationNow);
            PublishState();
        }
    }

    private void Dispatch(JsonElement command, ulong request)
    {
        if (DispatchRecordedInput(command, request)) return;
        if (!_deliveringReplay && _inputPlayback?.Active == true &&
            command.GetProperty("op").GetString() is "key" or "mouse" or "look" or "button" or "pointer" or "text" or "bot" or "campaign" or "checkpoint.load")
            _inputPlayback.Stop("Playback yielded to external input.", ReleaseAll);
        if (_inputRecording is not null && command.GetProperty("op").GetString() is "checkpoint.load" or "checkpoint.save" or "physics.sever")
            throw new InvalidOperationException("Finish the input segment before changing authoritative state through diagnostics.");
        if (DispatchCheckpoint(command, request)) return;
        if (DispatchCreaturePose(command, request)) return;
        if (DispatchProcessOwners(command, request)) return;
        if (!_deliveringReplay && command.GetProperty("op").GetString() is "key" or "mouse" or "look" or "button" or "pointer" or "text" or "bot" or "physics.sever")
            _replayCheckpointPrepared = false;
        if (command.GetProperty("op").GetString() is "key" or "mouse" or "look" or "button" or "pointer" or "text")
        {
            _campaignBot.Stop();
            _bot?.Stop();
        }
        switch (command.GetProperty("op").GetString())
        {
            case "campaign":
                DispatchCampaign(command);
                break;
            case "physics.sever":
                // Explicit development-harness operation for the requested
                // limb physics check. This is never ordinary player input or
                // evidence that weapon dismemberment selection is implemented.
                var reference = command.GetProperty("reference").GetString()!;
                var part = command.GetProperty("part").GetByte();
                var target = GetTree().Root.FindChildren("*", "Node3D", true, false).OfType<Node3D>()
                    .Single(node => (node is RuntimeNativeNpc or RuntimeNativeCreature) &&
                        node.GetMeta("opennv_reference_form_key", "").AsString() == reference);
                var combat = target is RuntimeNativeNpc npc ? npc.Combat : ((RuntimeNativeCreature)target).Combat;
                (combat ?? throw new InvalidOperationException("Resident actor has no combat owner.")).SeverLimb(part);
                _lastPhysicsTest = new { reference, part, request, operation = "controlled-source-limb-detachment", ordinaryInput = false };
                GD.Print($"OPENNV_DIAGNOSTIC_LIMB_DETACH reference={reference} part={part} request={request}");
                break;
            case "bot":
                if (_bot is null) throw new NotSupportedException("Bot observation/input adapter is unavailable.");
                var mode = command.GetProperty("mode").GetString()!;
                float? minimumCombatHitPoints = command.TryGetProperty("minimumCombatHitPoints", out var combatHealth) ? combatHealth.GetSingle() : null;
                float? maximumCombatSimulationSeconds = command.TryGetProperty("maximumCombatSimulationSeconds", out var combatTime) ? combatTime.GetSingle() : null;
                ReactiveReferenceBot.ValidateCombatLimits(mode, minimumCombatHitPoints, maximumCombatSimulationSeconds);
                if (mode != "observe") _campaignBot.Stop();
                var pauseAfter = !command.TryGetProperty("pauseAfter", out var pause) || pause.GetBoolean();
                if (mode == "observe") { _bot.ObserveCombat(); PublishState(); }
                else if (mode == "stop") _bot.Stop(pauseAfter);
                else
                {
                    EnsureBotSkillLibrary();
                    _bot.Start(command.TryGetProperty("reference", out var botReference) ? botReference.GetString() : null, mode,
                        command.TryGetProperty("distance", out var distance) ? distance.GetSingle() : 1.5f,
                        !command.TryGetProperty("combatTakeover", out var takeover) || takeover.GetBoolean(), pauseAfter,
                        minimumCombatHitPoints, maximumCombatSimulationSeconds);
                }
                break;
            case "key":
                var name = command.GetProperty("key").GetString()!;
                var key = name switch
                {
                    { Length: 1 } when char.IsAsciiDigit(name[0]) => Key.Key0 + (name[0] - '0'),
                    "Control" => Key.Ctrl,
                    _ => name.Length != 0 && !char.IsAsciiDigit(name[0]) && Enum.TryParse<Key>(name, true, out var parsed) &&
                        Enum.IsDefined(parsed) && parsed != Key.None
                        ? parsed : throw new ArgumentException($"Unknown physical key: {name}"),
                };
                var pressed = command.GetProperty("pressed").GetBoolean();
                var lease = command.TryGetProperty("leaseMilliseconds", out var value) ? value.GetInt32() : 900;
                if (lease is < 20 or > 1000)
                    throw new ArgumentException("Input lease must be 20–1000 milliseconds.");
                SetKey(key, pressed, (ulong)lease);
                break;
            case "mouse":
                RecordedInputTape.ValidateInput(command);
                SetMouseButton(Enum.Parse<MouseButton>(command.GetProperty("button").GetString()!),
                    command.GetProperty("pressed").GetBoolean(), (ulong)command.GetProperty("leaseMilliseconds").GetInt32());
                break;
            case "button":
                DeliverObservedButton(command.GetProperty("path").GetString()!,
                    _deliveringReplay ? command.GetProperty("text").GetString() : null);
                break;
            case "look":
                var dx = command.GetProperty("dx").GetSingle();
                var dy = command.GetProperty("dy").GetSingle();
                if (!float.IsFinite(dx) || !float.IsFinite(dy))
                    throw new ArgumentException("Mouse displacement must be finite.");
                DeliverLook(dx, dy);
                break;
            case "pointer":
                var pointerStateKey = _captureIdentity().StateKey;
                var position = new Vector2(command.GetProperty("x").GetSingle(), command.GetProperty("y").GetSingle());
                if (!position.IsFinite() || !GetViewport().GetVisibleRect().HasPoint(position))
                    throw new ArgumentException("Pointer input must lie inside the observed viewport.");
                if (command.TryGetProperty("button", out var requestedButton))
                {
                    if (!Enum.TryParse<MouseButton>(requestedButton.GetString(), true, out var mouseButton) ||
                        mouseButton is not (MouseButton.Left or MouseButton.Right or MouseButton.Middle or MouseButton.WheelUp or MouseButton.WheelDown))
                        throw new ArgumentException("Unknown pointer button.");
                    var explicitPress = command.TryGetProperty("pressed", out var requestedPress);
                    DeliverPointerButton(position, mouseButton, !explicitPress || requestedPress.GetBoolean(), pointerStateKey);
                    if (!explicitPress) DeliverPointerButton(position, mouseButton, false, pointerStateKey);
                }
                else
                {
                    PushHarnessInput(GetViewport(), new InputEventMouseMotion { Position = position, GlobalPosition = position });
                    RecordInput(JsonSerializer.SerializeToElement(new
                    {
                        op = "pointer",
                        x = position.X,
                        y = position.Y,
                    }, Json), pointerStateKey);
                }
                break;
            case "text":
                var textStateKey = _captureIdentity().StateKey;
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
                    PushHarnessInput(GetViewport(), new InputEventKey { Unicode = (uint)rune.Value, Pressed = true });
                    PushHarnessInput(GetViewport(), new InputEventKey { Unicode = (uint)rune.Value, Pressed = false });
                }
                RecordInput(JsonSerializer.SerializeToElement(new { op = "text", text }, Json), textStateKey);
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
            case "publication.mode":
                _publicationSelection.SetMode(command.GetProperty("mode").GetString());
                _frameIntervals.Reset();
                PublishState();
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
        var stateKey = _inputRecording is null ? null : _captureIdentity().StateKey;
        if (pressed)
        {
            var existing = _held.ContainsKey(key);
            _held[key] = Time.GetTicksMsec() + lease;
            if (existing)
            {
                RecordInput(JsonSerializer.SerializeToElement(new { op = "key", key = key.ToString(), pressed, leaseMilliseconds = (int)lease }, Json), stateKey);
                return;
            }
        }
        else if (!_held.Remove(key)) return;
        ParseHarnessInput(new InputEventKey { PhysicalKeycode = key, Keycode = key, Location = HarnessKeyLocation(key), Pressed = pressed, Echo = false });
        RecordInput(JsonSerializer.SerializeToElement(new
        {
            op = "key",
            key = key.ToString(),
            pressed,
            leaseMilliseconds = pressed ? (int)lease : 20
        }, Json), stateKey);
    }

    // The key-name protocol has no side selector. Match NativeScriptKeys.Code's
    // existing default-left convention so source modifier bindings can match.
    private static KeyLocation HarnessKeyLocation(Key key) =>
        key is Key.Shift or Key.Ctrl or Key.Alt ? KeyLocation.Left : KeyLocation.Unspecified;

    private void ReleaseAll()
    {
        ReleaseBotCombatInput();
        foreach (var key in _recordedHumanKeys.Keys.ToArray())
        {
            _recordedHumanKeys.Remove(key);
            ParseHarnessInput(new InputEventKey { PhysicalKeycode = key, Keycode = key, Location = HarnessKeyLocation(key), Pressed = false });
            RecordInput(JsonSerializer.SerializeToElement(new
            {
                op = "key",
                key = key.ToString(),
                pressed = false,
                leaseMilliseconds = 20
            }, Json));
        }
        foreach (var key in _held.Keys.ToArray())
            SetKey(key, false, 0);
        foreach (var button in _heldMouse.Keys.ToArray())
            SetMouseButton(button, false, 0);
        foreach (var (button, position) in _heldPointerButtons.ToArray())
            DeliverPointerButton(position, button, false);
    }

    private void PublishState()
    {
        try { WriteState(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or NotSupportedException or JsonException)
        {
            // A diagnostic reader can deny Windows delete-sharing during the
            // atomic replacement. Keep ordinary input/lease expiry running and
            // report every failed publication in the next successful snapshot.
            ReportPublicationFailure(error);
            PublishFailureMarker(error);
        }
    }

    private void PublishFailureMarker(Exception error)
    {
        // Only the engine owner creates/clears this marker. A pending worker
        // cannot erase a newer timeout with its older serialized snapshot.
        try
        {
            AtomicWrite(Path.Combine(_directory, "state-error.json"), JsonSerializer.Serialize(new
            {
                drawCount = Engine.GetFramesDrawn(),
                error = error.Message,
                failures = _statePublicationFailures,
                publicationTiming = _statePublicationHistory.LatestTiming,
                lastFailedStatePublication = _statePublicationHistory.LastFailed,
            }, Json));
        }
        catch (Exception markerError) when (markerError is IOException or UnauthorizedAccessException)
        {
            GD.PushWarning("OPENNV_STATE_FAILURE_MARKER_LOSS " + markerError.Message);
        }
    }

    private void ReportPublicationFailure(Exception error, bool increment = true)
    {
        if (increment) _statePublicationFailures++;
        if (_lastStatePublicationFailure != error.Message) GD.PushWarning("OPENNV_STATE_PUBLICATION_LOSS " + error.Message);
        _lastStatePublicationFailure = error.Message;
    }

    private void CompleteStateWrite(bool wait = false)
    {
        var pending = _stateWrite;
        if (pending is null) return;
        if (wait) pending.Wait();
        var observation = pending.Observe();
        // Live snapshots retain a late writer's eventual completion. The error
        // marker below keeps its existing first-loss/terminal-writer-error
        // policy, so a late successful completion need not refresh that file.
        _statePublicationHistory.Observe(observation);
        if (observation.Completed) _stateWrite = null;
        if (observation.NewLoss || observation is { Completed: true, Receipt.Error: not null })
        {
            var failure = observation.Failure ?? throw new InvalidOperationException("Publication loss has no failure source.");
            // A terminal I/O fault remains visible even if this same write
            // already reported its deadline. Its loss is counted only once.
            ReportPublicationFailure(failure, observation.NewLoss);
            PublishFailureMarker(failure);
        }
        if (observation.Receipt?.Write is not { } result) return;
        _lastFileWrite = result;
        _lastFileMilliseconds = result.Milliseconds;
        _statePublicationSharingRetries += result.Attempts - 1;
        if (pending.CanClearFailureMarker(_statePublicationFailures))
        {
            var cleared = LiveHarnessAtomicFile.Delete(Path.Combine(_directory, "state-error.json"));
            _statePublicationSharingRetries += cleared.Attempts - 1;
        }
    }

    private object CaptureVisibleControls() => _controls.Where(button => button.IsVisibleInTree() && !button.Disabled)
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

    private void WriteState()
    {
        CompleteStateWrite();
        if (_stateWrite is not null)
        {
            _statePublicationRequested = true;
            _statePublicationDeferredRequests++;
            return;
        }
        var started = Stopwatch.GetTimestamp();
        var renderTiming = SampleRenderTiming();
        var rendererIdentity = Volatile.Read(ref _rendererIdentity);
        var presentationConfiguration = SamplePresentationConfiguration();
        var details = _publicationSelection.Capture(_captureIdentity, _captureSummary, CaptureVisibleControls);
        var snapshot = new
        {
            schema = "opennv-live-harness-state/v1",
            engine = "opennv",
            publicationScope = _publicationSelection.Scope,
            process = System.Environment.ProcessId,
            sampledNanoseconds = Stopwatch.GetTimestamp() * (1_000_000_000.0 / Stopwatch.Frequency),
            drawCount = Engine.GetFramesDrawn(),
            physicsCount = Engine.GetPhysicsFrames(),
            sceneTreePaused = GetTree().Paused,
            stateKey = details?.Identity.StateKey,
            semanticEventOrdinal = details is null ? (ulong?)null : details.Identity.EventOrdinal,
            statePublicationFailures = _statePublicationFailures,
            statePublicationSequence = checked(_statePublicationSequence + 1),
            lastStatePublicationFailure = _lastStatePublicationFailure,
            statePublicationSharingRetries = _statePublicationSharingRetries,
            statePublicationDeferredRequests = _statePublicationDeferredRequests,
            previousStatePublicationTiming = _statePublicationHistory.LatestTiming,
            lastFailedStatePublication = _statePublicationHistory.LastFailed,
            nextCommandRequest = _nextRequest,
            commandReadFailure = _commandReadFailure,
            gameplay = details?.Gameplay,
            bot = _bot?.State,
            campaignBot = _campaignBot.State,
            botSkillStore = BotSkillStoreState,
            physicsTest = _lastPhysicsTest,
            checkpoint = _lastCheckpoint,
            checkpointRestored = _restoredCheckpoint?.Invoke(),
            checkpointTransitioning = _checkpointTransitioning?.Invoke() == true,
            recordedInput = new
            {
                path = _inputTapePath,
                replayRequest = _inputReplayRequest,
                recording = _inputRecording is not null,
                error = _inputRecordingError,
                playback = _inputPlayback?.State,
                gameplayParity = "unverified"
            },
            performance = new
            {
                jitOptimizationDisabled = JitOptimizationDisabled,
                renderThreadModel = ProjectSettings.GetSetting("rendering/driver/threads/thread_model").AsInt32(),
                renderThreadId = renderTiming?.RenderThreadId,
                mainThreadId = renderTiming?.MainThreadId,
                separateRenderThreadObserved = renderTiming is null ? (bool?)null : renderTiming.RenderThreadId != renderTiming.MainThreadId,
                drawCallbackThreadId = _drawCallbackThreadId,
                physicsTicksPerSecond = Engine.PhysicsTicksPerSecond,
                pipelinePhases = CapturePipelinePhases(),
                contentWorkerLimit = FalloutContentWorkers.Concurrency,
                availableProcessors = FalloutContentWorkers.AvailableProcessors,
                currentLogicalProcessor = System.Threading.Thread.GetCurrentProcessorId(),
                hybridCoreTypeCode = CurrentHybridCoreTypeCode(),
                availableMemoryBytes = FalloutContentWorkers.AvailableMemoryBytes,
                platform = OS.GetName(),
                architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
                renderingMethod = rendererIdentity?.RenderingMethod,
                graphicsAdapter = rendererIdentity?.GraphicsAdapter,
                presentationConfiguration,
                presentationConfigurationAgeMilliseconds = Stopwatch.GetElapsedTime(presentationConfiguration.Timestamp).TotalMilliseconds,
                drawCadenceEvidence = "engine-frame-post-draw; display presentation unverified",
                framesPerSecond = Godot.Performance.GetMonitor(Godot.Performance.Monitor.TimeFps),
                processMilliseconds = 1000 * Godot.Performance.GetMonitor(Godot.Performance.Monitor.TimeProcess),
                physicsMilliseconds = 1000 * Godot.Performance.GetMonitor(Godot.Performance.Monitor.TimePhysicsProcess),
                activePhysicsObjects = Godot.Performance.GetMonitor(Godot.Performance.Monitor.Physics3DActiveObjects),
                physicsCollisionPairs = Godot.Performance.GetMonitor(Godot.Performance.Monitor.Physics3DCollisionPairs),
                physicsIslands = Godot.Performance.GetMonitor(Godot.Performance.Monitor.Physics3DIslandCount),
                previousSummaryMilliseconds = _lastStateWriteMilliseconds,
                previousSnapshotMilliseconds = _lastSnapshotMilliseconds,
                previousSerializeMilliseconds = _lastSerializeMilliseconds,
                previousFileMilliseconds = _lastFileMilliseconds,
                previousFileSharingRetries = _lastFileWrite is { } write ? write.Attempts - 1 : 0,
                previousFileRetryError = _lastFileWrite?.LastRetryError,
                previousFileRetryFailure = _lastFileWrite?.LastRetryFailure,
                hostDrawIntervals = _frameIntervals.Capture(),
                particlePublication = OpenNV.Runtime.Formats.Gamebryo.RuntimeNifParticleSystem.PublicationTiming,
                mainThreadOwners = OpenNV.Runtime.Diagnostics.RuntimeFrameWork.Capture(),
                drawCalls = Godot.Performance.GetMonitor(Godot.Performance.Monitor.RenderTotalDrawCallsInFrame),
                renderSetupCpuMilliseconds = renderTiming?.SetupMilliseconds,
                renderMeasurementAgeMilliseconds = renderTiming is null ? (double?)null : Stopwatch.GetElapsedTime(renderTiming.Timestamp).TotalMilliseconds,
                renderMeasurementError = renderTiming?.Error,
                viewports = renderTiming?.Viewports,
            },
            held = _held.Keys.Select(key => key.ToString()).ToArray(),
            heldMouse = _heldMouse.Keys.Select(button => button.ToString()).ToArray(),
            controls = details?.Controls,
            trace = _trace?.Status,
        };
        _lastSnapshotMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        var phase = Stopwatch.GetTimestamp();
        var json = JsonSerializer.Serialize(snapshot, Json);
        _lastSerializeMilliseconds = Stopwatch.GetElapsedTime(phase).TotalMilliseconds;
        var path = Path.Combine(_directory, "live-state.json");
        // Freeze all engine/gameplay reads and serialization on their owner
        // thread. Only immutable text and file IO cross to this single writer.
        _stateWrite = LiveHarnessPendingPublication.Start(checked(++_statePublicationSequence), _statePublicationFailures,
            static () => Stopwatch.GetTimestamp() * (1000.0 / Stopwatch.Frequency),
            phase => LiveHarnessAtomicFile.Write(path, json, phase),
            static work => { _ = Task.Run(work); });
        _statePublicationCadence.Scheduled(Time.GetTicksMsec());
        _statePublicationRequested = false;
        _lastStateWriteMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
    }

    private void BeforeDraw()
    {
        _pipeline?.Draw.Begin();
        _trace?.BeforeDraw();
    }

    private void AfterDraw()
    {
        _pipeline?.Draw.End();
        _drawCallbackThreadId = OS.GetThreadCallerId();
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
        GD.Print($"OPENNV_LIVE_HARNESS_RETIRE phase=enter stateWritePending={_stateWrite is not null}");
        _creaturePoseObservation?.Stop("harness-retired");
        RetirePipelinePhases();
        _bot?.Stop();
        _inputPlayback?.Stop("Playback ended with its runtime owner.", ReleaseAll);
        FinishReplayDeliveryJournal("Playback ended with its runtime owner.");
        FinishInputRecording("Recording ended with its runtime owner.");
        try { CompleteStateWrite(wait: true); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { ReportPublicationFailure(error); PublishFailureMarker(error); }
        GD.Print("OPENNV_LIVE_HARNESS_RETIRE phase=state-write-settled");
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
        GD.Print("OPENNV_LIVE_HARNESS_RETIRE phase=callbacks-retired");
    }
}
