using Godot;

namespace OpenNV.Runtime;

internal partial class CellPlayer : CharacterBody3D
{
    private const float MoveSpeed = 3.6f;
    private const float MouseSensitivity = 0.0022f;
    private const float Gravity = 9.8f;
    private const float XrActionThreshold = 0.70f;
    private const float XrSnapTurnThreshold = 0.75f;
    private const float XrSnapTurnRadians = MathF.PI / 6.0f;

    private Camera3D _camera = null!;
    private GameplaySession? _session;
    private XROrigin3D? _xrOrigin;
    private XRController3D? _leftHand;
    private XRController3D? _rightHand;
    private OpenXRRenderModelManager? _xrRenderModels;
    private bool _useXr;
    private bool _xrActivatePressed;
    private bool _xrFirePressed;
    private bool _xrSavePressed;
    private bool _xrSnapTurnReady = true;

    internal Camera3D Camera => _camera;
    internal bool UsesXr => _useXr;
    internal XROrigin3D? XrOrigin => _xrOrigin;
    internal XRController3D? LeftHand => _leftHand;
    internal XRController3D? RightHand => _rightHand;
    internal OpenXRRenderModelManager? XrRenderModels => _xrRenderModels;

    internal void Configure(
        float yaw,
        GameplaySession session,
        bool useXr = false,
        bool enableXrRuntimeFeatures = false)
    {
        _session = session;
        _useXr = useXr;
        Name = "Player";
        Position = new Vector3(0.0f, 0.9f, 0.0f);
        Rotation = new Vector3(0.0f, yaw, 0.0f);
        CollisionLayer = 2;
        CollisionMask = 1;
        AddChild(new CollisionShape3D
        {
            Name = "Capsule",
            Shape = new CapsuleShape3D { Radius = 0.32f, Height = 1.8f },
        });
        if (useXr)
            BuildXrRig(enableXrRuntimeFeatures);
        else
            BuildDesktopRig();
    }

    public override void _Ready()
    {
        if (!_useXr && DisplayServer.GetName() != "headless")
            Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    public override void _PhysicsProcess(double delta)
    {
        var input = ReadMovement();
        var forward = -_camera.GlobalBasis.Z;
        var right = _camera.GlobalBasis.X;
        forward.Y = 0.0f;
        right.Y = 0.0f;
        forward = forward.Normalized();
        right = right.Normalized();
        var direction = right * input.X + forward * input.Y;
        direction.Y = 0.0f;
        direction = direction.Normalized();

        var velocity = Velocity;
        velocity.X = direction.X * MoveSpeed;
        velocity.Z = direction.Z * MoveSpeed;
        velocity.Y = IsOnFloor() ? MathF.Min(velocity.Y, 0.0f) : velocity.Y - Gravity * (float)delta;
        Velocity = velocity;
        MoveAndSlide();
        if (_useXr)
            PollXrActions();
    }

    public override void _UnhandledInput(InputEvent inputEvent)
    {
        if (_useXr)
            return;
        if (inputEvent is InputEventKey key && key.Pressed && !key.Echo)
        {
            if (key.PhysicalKeycode == Key.E)
                Activate(_camera);
            else if (key.PhysicalKeycode == Key.F5)
                _session!.SaveAndNotify();
            else if (key.PhysicalKeycode == Key.Escape)
                Input.MouseMode = Input.MouseModeEnum.Visible;
        }
        else if (inputEvent is InputEventMouseButton button && button.Pressed)
        {
            if (button.ButtonIndex == MouseButton.Left)
                _session!.Fire(_camera);
            else if (button.ButtonIndex == MouseButton.Right)
                Input.MouseMode = Input.MouseModeEnum.Captured;
        }
        else if (inputEvent is InputEventMouseMotion motion && Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            RotateY(-motion.Relative.X * MouseSensitivity);
            var cameraRotation = _camera.Rotation;
            cameraRotation.X = Math.Clamp(
                cameraRotation.X - motion.Relative.Y * MouseSensitivity,
                -1.45f,
                1.45f);
            _camera.Rotation = cameraRotation;
        }
    }

    private void Activate(Node3D aimSource)
    {
        var collider = Cast(aimSource, 2.5f);
        var pickup = Ancestor<PickupInstance>(collider);
        if (pickup is not null)
        {
            _session!.Collect(pickup);
            return;
        }
        var container = Ancestor<ContainerInstance>(collider);
        if (container is not null)
        {
            _session!.OpenContainer(container);
            return;
        }
        var door = Ancestor<DoorInstance>(collider);
        if (door is null)
            return;
        door.SetOpen(!door.IsOpen);
        _session!.DoorChanged(door);
        GD.Print($"OPENNV_DOOR_STATE form={door.Name.ToString().Replace("DOOR_", "")} open={door.IsOpen}");
    }

    private Node? Cast(Node3D aimSource, float distance)
    {
        var from = aimSource.GlobalPosition;
        var to = from - aimSource.GlobalBasis.Z * distance;
        var query = PhysicsRayQueryParameters3D.Create(from, to, 1);
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
        return hit.Count == 0 ? null : hit["collider"].AsGodotObject() as Node;
    }

    private void BuildDesktopRig()
    {
        _camera = new Camera3D
        {
            Name = "DataDerivedEntryCamera",
            Position = new Vector3(0.0f, 0.72f, 0.0f),
            Near = 0.02f,
            Far = 200.0f,
            Current = true,
        };
        AddChild(_camera);
    }

    private void BuildXrRig(bool enableRuntimeFeatures)
    {
        _xrOrigin = new XROrigin3D
        {
            Name = "XrOrigin",
            Position = new Vector3(0.0f, -0.9f, 0.0f),
            WorldScale = 1.0f,
            Current = true,
        };
        AddChild(_xrOrigin);
        _camera = new XRCamera3D
        {
            Name = "TrackedHead",
            Near = 0.02f,
            Far = 200.0f,
            Current = true,
        };
        _xrOrigin.AddChild(_camera);
        _leftHand = BuildHand("LeftHand", "/user/hand/left");
        _rightHand = BuildHand("RightHand", "/user/hand/right");
        _xrOrigin.AddChild(_leftHand);
        _xrOrigin.AddChild(_rightHand);
        if (enableRuntimeFeatures)
        {
            _xrRenderModels = new OpenXRRenderModelManager
            {
                Name = "RuntimeControllerModels",
                Tracker = OpenXRRenderModelManager.RenderModelTracker.Any,
            };
            _xrOrigin.AddChild(_xrRenderModels);
        }
        _session!.AttachXrHud(_leftHand);
    }

    private static XRController3D BuildHand(string name, string tracker) => new()
    {
        Name = name,
        Tracker = tracker,
        Pose = "aim",
    };

    private Vector2 ReadMovement()
    {
        if (_useXr)
        {
            var stick = _leftHand!.GetVector2("move");
            return stick.Length() < 0.18f ? Vector2.Zero : stick;
        }
        var left = Input.IsPhysicalKeyPressed(Key.A) ? 1.0f : 0.0f;
        var right = Input.IsPhysicalKeyPressed(Key.D) ? 1.0f : 0.0f;
        var forward = Input.IsPhysicalKeyPressed(Key.W) ? 1.0f : 0.0f;
        var backward = Input.IsPhysicalKeyPressed(Key.S) ? 1.0f : 0.0f;
        return new Vector2(right - left, forward - backward);
    }

    private void PollXrActions()
    {
        var turn = _rightHand!.GetVector2("turn").X;
        if (MathF.Abs(turn) >= XrSnapTurnThreshold && _xrSnapTurnReady)
        {
            SnapTurn(-MathF.Sign(turn) * XrSnapTurnRadians);
            _xrSnapTurnReady = false;
        }
        else if (MathF.Abs(turn) < 0.35f)
        {
            _xrSnapTurnReady = true;
        }

        var activate = _rightHand.GetFloat("activate") >= XrActionThreshold;
        if (activate && !_xrActivatePressed)
            Activate(_rightHand);
        _xrActivatePressed = activate;

        var fire = _rightHand.GetFloat("fire") >= XrActionThreshold;
        if (fire && !_xrFirePressed && _session!.Fire(_rightHand))
            _rightHand.TriggerHapticPulse("haptic", 0.0, 0.45, 0.06, 0.0);
        _xrFirePressed = fire;

        var save = _leftHand!.IsButtonPressed("save");
        if (save && !_xrSavePressed)
            _session!.SaveAndNotify();
        _xrSavePressed = save;
    }

    private void SnapTurn(float radians)
    {
        var headOffset = _camera.GlobalPosition - GlobalPosition;
        headOffset.Y = 0.0f;
        RotateY(radians);
        GlobalPosition += headOffset - headOffset.Rotated(Vector3.Up, radians);
    }

    private static T? Ancestor<T>(Node? node)
        where T : Node
    {
        while (node is not null)
        {
            if (node is T match)
                return match;
            node = node.GetParent();
        }
        return null;
    }
}
