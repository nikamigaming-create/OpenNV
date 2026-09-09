using Godot;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Presentation.OpenXR;

/// <summary>Tracked presentation/input adapter for the ordinary native player.</summary>
internal sealed partial class NativeXrRig : Node3D
{
    private readonly RuntimeConfiguration _configuration;
    private readonly Dictionary<string, bool> _buttons = [];
    private readonly NativeXrCanvas _canvas;
    private RuntimeNativePlayer? _player;
    private bool _snapReady = true;
    private int _stableFrames;
    private bool _calibrated;
    private bool _rightInputReady;
    private bool _pointerPressed;
    private float _previousHeadHeight;
    internal XROrigin3D Origin { get; }
    internal XRCamera3D Camera { get; }
    internal XRController3D LeftGrip { get; }
    internal XRController3D RightGrip { get; }
    internal XRController3D RightAim { get; }
    internal Vector2 Movement { get; private set; }
    internal bool JumpRequested { get; private set; }
    internal bool Sprint { get; private set; }
    internal int TrackedFrames { get; private set; }
    internal int SnapTurns { get; private set; }
    internal float MaximumSnapPivotError { get; private set; }
    internal Func<bool>? Modal { get; set; }
    internal Func<Transform3D, bool, bool, bool>? PointAtPipBoy { get; set; }
    internal Action<Transform3D>? PoseWristDevice { get; set; }
    internal Action<bool>? SetPipBoyHeld { get; set; }
    internal bool WorldPointer { get; private set; }
    internal object State => new
    {
        mode = "native-owned-openxr",
        calibrated = _calibrated,
        trackedFrames = TrackedFrames,
        referenceSpace = Pose(Origin),
        head = Pose(Camera),
        left = Pose(LeftGrip),
        right = Pose(RightGrip),
        aim = Pose(RightAim),
        leftTracked = LeftGrip.GetHasTrackingData(),
        rightTracked = RightGrip.GetHasTrackingData(),
        movement = new[] { Movement.X, Movement.Y },
        snapTurns = SnapTurns,
        maximumSnapPivotErrorMeters = MaximumSnapPivotError,
        canvas = _canvas.State,
        hands = "owned-gloves-and-hand-skins; analog-grip-trigger-and-thumb-touch",
        physicalAcceptance = "pending"
    };
    private static object Pose(Node3D node) => new
    {
        position = new[] { node.GlobalPosition.X, node.GlobalPosition.Y, node.GlobalPosition.Z },
        rotation = new[] { node.GlobalBasis.GetRotationQuaternion().X, node.GlobalBasis.GetRotationQuaternion().Y,
            node.GlobalBasis.GetRotationQuaternion().Z, node.GlobalBasis.GetRotationQuaternion().W }
    };

    internal NativeXrRig(RuntimeConfiguration configuration)
    {
        Name = "NativeOpenXR"; ProcessMode = ProcessModeEnum.Always; ProcessPriority = -100;
        _configuration = configuration;
        Origin = new XROrigin3D { Name = "TrackingOrigin", WorldScale = configuration.Xr.WorldScale, Current = true };
        AddChild(Origin);
        Camera = new XRCamera3D { Name = "TrackedHead", Current = true, Near = .03f, Far = configuration.Player.CameraFarMeters };
        Camera.CullMask &= ~OpenNV.Runtime.World.Actors.RuntimeNativePlayerActor.SelfHeadLayer;
        Origin.AddChild(Camera);
        LeftGrip = Controller("LeftGrip", "left_hand", "grip");
        RightGrip = Controller("RightGrip", "right_hand", "grip");
        RightAim = Controller("RightAim", "right_hand", "aim");
        _canvas = new(Camera); AddChild(_canvas);
    }
    private XRController3D Controller(string name, string tracker, string pose)
    {
        var controller = new XRController3D { Name = name, Tracker = tracker, Pose = pose, ProcessPriority = -200 };
        Origin.AddChild(controller); return controller;
    }
    public override void _EnterTree() => GetTree().NodeAdded += RouteCanvas;
    public override void _ExitTree() => GetTree().NodeAdded -= RouteCanvas;
    private void RouteCanvas(Node node)
    {
        if (node is not CanvasLayer layer || layer.Name.ToString() is "FirstPersonLayer" or "NativePipBoyLayer") return;
        // Reuse the actual menu nodes and their event owners. No duplicate UI
        // state, screen scraping, desktop focus or OS pointer injection.
        if (layer.GetViewport() != GetViewport()) return;
        Callable.From(() =>
        {
            if (IsInstanceValid(layer) && !layer.IsQueuedForDeletion()) _canvas.Admit(layer);
        }).CallDeferred();
    }
    internal void Attach(RuntimeNativePlayer player)
    {
        _player = player; Reparent(player, false); Transform = Transform3D.Identity;
        Origin.Position = Vector3.Zero; _calibrated = false; _stableFrames = 0;
        Camera.MakeCurrent();
    }
    private bool Edge(string key, bool value)
    {
        var edge = value && !_buttons.GetValueOrDefault(key); _buttons[key] = value; return edge;
    }
    public override void _Process(double delta)
    {
        var head = XRServer.GetTracker(NativeXrActions.Head) as XRPositionalTracker;
        var pose = head?.GetPose(NativeXrActions.DefaultPose);
        var tracked = pose?.HasTrackingData == true;
        if (tracked) TrackedFrames++;
        if (_player is not null && !_calibrated && tracked)
        {
            var height = Camera.Position.Y;
            _stableFrames = height > .3f && MathF.Abs(height - _previousHeadHeight) < .02f ? _stableFrames + 1 : 0;
            _previousHeadHeight = height;
            if (_stableFrames >= _configuration.Xr.EyeHeightCalibrationTrackedFrames)
            {
                Origin.Position = new(-Camera.Position.X, _configuration.Xr.DesiredEyeHeightMeters - height, -Camera.Position.Z);
                _calibrated = true;
                GD.Print($"OPENNV_NATIVE_XR_CALIBRATED sourceHeight={height:R} desired={_configuration.Xr.DesiredEyeHeightMeters:R}");
            }
        }
        var left = LeftGrip.GetHasTrackingData(); var right = RightGrip.GetHasTrackingData();
        if (!right) _rightInputReady = false;
        else if (RightGrip.GetFloat(NativeXrActions.Fire) < _configuration.Xr.ActionThreshold && !RightGrip.IsButtonPressed(NativeXrActions.Reload) && !RightGrip.IsButtonPressed(NativeXrActions.Grab))
            _rightInputReady = true;
        var modal = Modal?.Invoke() ?? _player is null;
        var stick = left ? LeftGrip.GetVector2(NativeXrActions.Move) : Vector2.Zero;
        Movement = tracked && _calibrated && !modal && stick.Length() >= _configuration.Xr.MovementDeadzone ? stick.LimitLength() : Vector2.Zero;
        Sprint = left && LeftGrip.IsButtonPressed(NativeXrActions.Sprint);
        JumpRequested |= Edge("jump", left && LeftGrip.IsButtonPressed(NativeXrActions.Jump)) && !modal;
        var pipboy = left && LeftGrip.GetFloat(NativeXrActions.PipBoy) >= _configuration.Xr.ActionThreshold;
        if (_buttons.GetValueOrDefault("pipboy") != pipboy) SetPipBoyHeld?.Invoke(pipboy);
        _buttons["pipboy"] = pipboy;
        if (Edge("save", left && LeftGrip.IsButtonPressed(NativeXrActions.Save)) && !modal) _player?.SaveGame?.Invoke();
        WorldPointer = right && !modal && PointAtPipBoy is null && RightGrip.GetFloat(NativeXrActions.Activate) >= _configuration.Xr.ActionThreshold;
        var fire = right && _rightInputReady && RightGrip.GetFloat(NativeXrActions.Fire) >= _configuration.Xr.ActionThreshold;
        _pointerPressed = fire;
        var fireEdge = Edge("fire", fire);
        if (PointAtPipBoy is null && modal) _canvas.Point(RightAim, right && RightAim.GetHasTrackingData(), fire);
        else if (PointAtPipBoy is null && fireEdge)
        {
            if (WorldPointer) _player?.XrActivate();
            else _player?.XrFire();
        }
        if (Edge("activate", right && _rightInputReady && RightGrip.IsButtonPressed(NativeXrActions.Grab)) && !modal && PointAtPipBoy is null) _player?.XrActivate();
        var reload = right && _rightInputReady && RightGrip.IsButtonPressed(NativeXrActions.Reload);
        if (_buttons.GetValueOrDefault("reload") != reload && PointAtPipBoy is null) _player?.XrReload(reload);
        _buttons["reload"] = reload;
        var turn = right && !modal && PointAtPipBoy is null ? RightGrip.GetVector2(NativeXrActions.Turn).X : 0;
        if (MathF.Abs(turn) <= _configuration.Xr.SnapTurnResetThreshold) _snapReady = true;
        if (_player is not null && _calibrated && _snapReady && MathF.Abs(turn) >= _configuration.Xr.SnapTurnActivationThreshold)
        {
            var before = Camera.GlobalPosition;
            _player.RotateY(-MathF.Sign(turn) * Mathf.DegToRad(_configuration.Xr.SnapTurnDegrees));
            var correction = before - Camera.GlobalPosition; correction.Y = 0;
            // Rotate the reference space about the HMD, preserving room offset.
            GlobalPosition += correction;
            MaximumSnapPivotError = MathF.Max(MaximumSnapPivotError, new Vector2(Camera.GlobalPosition.X - before.X, Camera.GlobalPosition.Z - before.Z).Length());
            SnapTurns++; _snapReady = false;
        }
        // User VR policy: no floating gameplay HUD. Menus remain stereo world
        // surfaces; the wrist device runs without pausing the world or headset.
        _canvas.Present(modal && PointAtPipBoy is null, modal && PointAtPipBoy is null);
        _canvas.Scroll(right && PointAtPipBoy is null && modal ? RightGrip.GetVector2(NativeXrActions.Turn).Y : 0, delta);
    }
    internal bool ConsumeJump() { var value = JumpRequested; JumpRequested = false; return value; }
    internal void PublishWristInput()
    {
        PoseWristDevice?.Invoke(Camera.GlobalTransform);
        PointAtPipBoy?.Invoke(RightAim.GlobalTransform,
            RightGrip.GetHasTrackingData() && RightAim.GetHasTrackingData(), _pointerPressed);
    }
}
