using Godot;
using OpenNV.Runtime.Campaigns.NewVegas.Opening;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal partial class RuntimeNativePlayer : CharacterBody3D
{
    private RuntimeConfiguration _configuration = null!;
    private Camera3D _camera = null!;
    private float _pitchRadians;
    private bool _movementEnabled = true;
    private bool _lookingEnabled = true;
    private bool _activationEnabled = true;
    private bool _modalInput;

    internal Camera3D Camera => _camera;

    internal void SetModalInput(bool modal)
    {
        _modalInput = modal;
        if (modal)
            Velocity = Vector3.Zero;
    }

    internal void ApplySourceControls(FalloutPlayerControlState state)
    {
        _movementEnabled = state.Movement;
        _lookingEnabled = state.Looking;
        _activationEnabled = state.Movement;
        if (!_movementEnabled)
            Velocity = Vector3.Zero;
        SetMeta("opennv_source_movement_enabled", state.Movement);
        SetMeta("opennv_source_pipboy_enabled", state.PipBoy);
        SetMeta("opennv_source_fighting_enabled", state.Fighting);
        SetMeta("opennv_source_looking_enabled", state.Looking);
    }

    internal void Configure(RuntimeConfiguration configuration, Transform3D authoredFloorTransform)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        _configuration = configuration;
        Name = "NativePlayer";
        MotionMode = MotionModeEnum.Grounded;
        CollisionLayer = configuration.Player.CollisionLayer;
        CollisionMask = configuration.Player.CollisionMask;
        FloorSnapLength = configuration.Player.CapsuleRadiusMeters;
        AddChild(new CollisionShape3D
        {
            Name = "NativePlayerCapsule",
            Shape = new CapsuleShape3D
            {
                Radius = configuration.Player.CapsuleRadiusMeters,
                Height = configuration.Player.CapsuleHeightMeters,
            },
        });
        _camera = new Camera3D
        {
            Name = "NativePlayerCamera",
            Position = configuration.Player.DesktopCameraOffsetMeters.Vector3(),
            Near = configuration.Player.CameraNearMeters,
            Far = configuration.Player.CameraFarMeters,
            Current = true,
        };
        AddChild(_camera);
        Teleport(authoredFloorTransform);
        SetMeta("opennv_source", "live-retail-files");
        SetMeta("opennv_content_source", "live-owned-files");
    }

    internal void Teleport(Transform3D authoredFloorTransform)
    {
        if (_configuration is null)
            throw new InvalidOperationException("Native player is not configured.");
        var basis = authoredFloorTransform.Basis.Orthonormalized();
        GlobalTransform = new Transform3D(
            basis,
            authoredFloorTransform.Origin +
                Vector3.Up * _configuration.Player.SpawnCenterHeightMeters);
        _pitchRadians = 0.0f;
        _camera.Rotation = Vector3.Zero;
        Velocity = Vector3.Zero;
    }

    internal void RestoreTransform(
        IReadOnlyList<float> position,
        IReadOnlyList<float> rotation)
    {
        if (position.Count != 3 || rotation.Count != 4 ||
            position.Any(value => !float.IsFinite(value)) ||
            rotation.Any(value => !float.IsFinite(value)))
            throw new InvalidDataException("Native saved player transform is invalid.");
        var quaternion = new Quaternion(
            rotation[0], rotation[1], rotation[2], rotation[3]).Normalized();
        GlobalTransform = new Transform3D(
            new Basis(quaternion),
            new Vector3(position[0], position[1], position[2]));
        _pitchRadians = 0.0f;
        _camera.Rotation = Vector3.Zero;
        Velocity = Vector3.Zero;
    }

    public override void _Ready()
    {
        if (DisplayServer.GetName() != "headless")
            Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    public override void _UnhandledInput(InputEvent inputEvent)
    {
        var input = _configuration.Player.DesktopInput;
        if (inputEvent.IsActionPressed(input.Cancel.Action))
        {
            Input.MouseMode = Input.MouseModeEnum.Visible;
            GetViewport().SetInputAsHandled();
            return;
        }
        if (inputEvent.IsActionPressed(input.CaptureMouse.Action))
        {
            Input.MouseMode = Input.MouseModeEnum.Captured;
            GetViewport().SetInputAsHandled();
            return;
        }
        if (!_modalInput && _activationEnabled && inputEvent.IsActionPressed(input.Activate.Action))
        {
            var accepted = TryActivateLiveObject();
            GD.Print($"OPENNV_NATIVE_PLAYER_ACTION action=activate accepted={accepted}");
            GetViewport().SetInputAsHandled();
            return;
        }
        if (_modalInput || !_lookingEnabled || Input.MouseMode != Input.MouseModeEnum.Captured ||
            inputEvent is not InputEventMouseMotion motion)
            return;
        RotateY(-motion.Relative.X * _configuration.Player.MouseSensitivityRadiansPerPixel);
        _pitchRadians = Math.Clamp(
            _pitchRadians - motion.Relative.Y *
                _configuration.Player.MouseSensitivityRadiansPerPixel,
            -_configuration.Player.VerticalLookLimitRadians,
            _configuration.Player.VerticalLookLimitRadians);
        _camera.Rotation = new Vector3(_pitchRadians, 0.0f, 0.0f);
    }

    public override void _PhysicsProcess(double delta)
    {
        var input = _configuration.Player.DesktopInput;
        var movement = _movementEnabled && !_modalInput
            ? Input.GetVector(
                input.MoveLeft.Action,
                input.MoveRight.Action,
                input.MoveBackward.Action,
                input.MoveForward.Action)
            : Vector2.Zero;
        var forward = -_camera.GlobalBasis.Z;
        var right = _camera.GlobalBasis.X;
        forward.Y = 0.0f;
        right.Y = 0.0f;
        var direction = (right.Normalized() * movement.X +
            forward.Normalized() * movement.Y).Normalized();
        var velocity = direction * _configuration.Player.MoveSpeedMetersPerSecond;
        velocity.Y = IsOnFloor()
            ? MathF.Min(Velocity.Y, 0.0f)
            : Velocity.Y -
                _configuration.Simulation.GravityMetersPerSecondSquared * (float)delta;
        Velocity = velocity;
        MoveAndSlide();
    }

    private bool TryActivateLiveObject()
    {
        var from = _camera.GlobalPosition;
        var to = from - _camera.GlobalBasis.Z *
            _configuration.Player.ActivationDistanceMeters;
        var query = PhysicsRayQueryParameters3D.Create(from, to, CollisionMask);
        query.Exclude = [GetRid()];
        query.HitBackFaces = false;
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (!hit.TryGetValue("collider", out var colliderValue) ||
            colliderValue.AsGodotObject() is not Node collider)
            return false;
        for (Node? current = collider; current is not null; current = current.GetParent())
        {
            var vigor = current.GetChildren().OfType<RuntimeNativeVigorActivator>().SingleOrDefault();
            if (vigor is not null)
            {
                vigor.Activate();
                return true;
            }
            var portal = current.GetChildren().OfType<RuntimeNativeDoorPortal>().SingleOrDefault();
            if (portal is null)
                continue;
            portal.Activate();
            return true;
        }
        return false;
    }
}
