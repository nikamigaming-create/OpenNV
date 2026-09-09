using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

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
    private Transform3D? _sourceCamera;
    private float _jumpHeightMeters;
    internal bool Sprinting { get; private set; }
    internal int JumpCount { get; private set; }
    internal int StepCount { get; private set; }
    internal string? BlockingShape { get; private set; }
    internal bool CollisionResident { get; private set; } = true;
    internal object[] CollisionContacts => Enumerable.Range(0, GetSlideCollisionCount()).Select(index =>
    {
        var contact = GetSlideCollision(index);
        var normal = contact.GetNormal();
        return (object)new { body = (contact.GetCollider() as Node)?.GetPath().ToString(), normal = new[] { normal.X, normal.Y, normal.Z } };
    }).ToArray();
    internal Func<Vector3, bool>? CanOccupyPosition { get; set; }

    internal void ConfigureLocomotion(FalloutPluginStack records)
    {
        _jumpHeightMeters = checked((float)FalloutGameSettingFloats.Read(records, "fJumpHeightMin")) * UnitsToMeters;
        if (!float.IsFinite(_jumpHeightMeters) || _jumpHeightMeters <= 0)
            throw new InvalidDataException("Source jump height must be finite and positive.");
        SetMeta("opennv_jump_height_meters", _jumpHeightMeters);
        SetMeta("opennv_sprint_policy", "OpenNV optional hold-to-sprint");
    }

    internal Camera3D Camera => _camera;
    internal float ViewPitchRadians => _pitchRadians;
    internal FalloutActorActivityState Activity { get; } = new();
    internal bool RolloverTextEnabled { get; private set; } = true;
    internal float UnitsToMeters => _configuration.World.GameUnitsToMeters;
    internal Func<Node, bool>? ActivateReference { get; set; }
    internal Action? SaveGame { get; set; }

    internal void ApplySourceCamera(Transform3D transformFromFeet)
    {
        _sourceCamera = transformFromFeet;
        if (_xr is not null) return;
        _camera.Transform = transformFromFeet * new Transform3D(new Basis(Vector3.Right, _pitchRadians), Vector3.Zero);
    }

    internal void ApplyCameraPath(FalloutNifAnimatedNodePath animation, float sourceTime, float yaw = 0)
    {
        var transform = Transform3D.Identity;
        foreach (var (rest, sample) in animation.Sample(sourceTime))
        {
            var translation = sample?.Translation ?? rest.Translation;
            var scale = sample?.Scale ?? rest.Scale;
            var basis = sample?.Rotation is { } rotation
                ? new Basis(new Quaternion(rotation.X, rotation.Z, -rotation.Y, rotation.W).Normalized()).Scaled(Vector3.One * scale)
                : GamebryoCoordinate.ConvertBasis(rest.RotationRowMajor, scale, "source camera parent");
            transform *= new Transform3D(basis, GamebryoCoordinate.ConvertVector(new(translation.X, translation.Y, translation.Z)) * UnitsToMeters);
        }
        ApplySourceCamera(new(transform.Basis.Orthonormalized() * new Basis(Vector3.Up, yaw), transform.Origin));
    }

    internal void ReleaseSourceCamera()
    {
        _sourceCamera = null;
        if (_xr is not null) return;
        _camera.Transform = new Transform3D(new Basis(Vector3.Right, _pitchRadians),
            Vector3.Up * _configuration.Player.SpawnCenterHeightMeters + _configuration.Player.DesktopCameraOffsetMeters.Vector3());
    }

    internal void SetModalInput(bool modal)
    {
        _modalInput = modal;
        if (modal)
        {
            _shotPending = false;
            Velocity = Vector3.Zero;
            _aiming = false;
            _reloadPressed = false; _holdHandled = false; CancelWeaponAction();
            if (_firstPersonPixels is not null) _firstPersonPixels.Visible = false;
        }
    }

    internal void ApplySourceControls(FalloutPlayerControlState state)
    {
        _movementEnabled = state.Movement;
        _lookingEnabled = state.Looking;
        _activationEnabled = state.Movement;
        RolloverTextEnabled = state.RolloverText;
        if (!_movementEnabled)
            Velocity = Vector3.Zero;
        SetMeta("opennv_source_movement_enabled", state.Movement);
        SetMeta("opennv_source_pipboy_enabled", state.PipBoy);
        SetMeta("opennv_source_fighting_enabled", state.Fighting);
        SetMeta("opennv_source_pointofview_enabled", state.PointOfView);
        if (!state.Fighting) { _reloadPressed = false; _shotPending = false; CancelWeaponAction(); }
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
            Position = Vector3.Up * configuration.Player.SpawnCenterHeightMeters,
            Shape = new CapsuleShape3D
            {
                Radius = configuration.Player.CapsuleRadiusMeters,
                Height = configuration.Player.CapsuleHeightMeters,
            },
        });
        var projection = FalloutCameraProjection.Read(FalloutInstallationSettings.Read(RuntimeLiveContentSource.Current!));
        _camera = new Camera3D
        {
            Name = "NativePlayerCamera",
            Position = Vector3.Up * configuration.Player.SpawnCenterHeightMeters +
                configuration.Player.DesktopCameraOffsetMeters.Vector3(),
            Fov = projection.VerticalFovDegrees,
            KeepAspect = Camera3D.KeepAspectEnum.Height,
            Near = projection.NearGameUnits * configuration.World.GameUnitsToMeters,
            Far = configuration.Player.CameraFarMeters,
            Current = true,
        };
        AddChild(_camera);
        GD.Print($"OPENNV_NATIVE_CAMERA_PROJECTION verticalFov={projection.VerticalFovDegrees:R} " +
            $"nearGameUnits={projection.NearGameUnits:R} source=owned-display-settings referenceAspect=4:3 " +
            "farClip=unverified matchedFrame=unverified");
        Teleport(authoredFloorTransform);
        SetMeta("opennv_source", "live-retail-files");
        SetMeta("opennv_content_source", "live-owned-files");
    }

    internal void Teleport(Transform3D authoredFloorTransform)
    {
        if (_furniturePhase != 0) throw new NotSupportedException("Teleporting an active furniture interaction needs interruption/phase restoration.");
        if (_configuration is null)
            throw new InvalidOperationException("Native player is not configured.");
        var basis = authoredFloorTransform.Basis.Orthonormalized();
        GlobalTransform = new Transform3D(
            basis,
            authoredFloorTransform.Origin);
        _pitchRadians = 0.0f;
        if (_xr is null) _camera.Rotation = Vector3.Zero;
        Velocity = Vector3.Zero;
    }

    internal void RestoreTransform(
        IReadOnlyList<float> position,
        IReadOnlyList<float> rotation, float viewPitchRadians = 0)
    {
        if (_furniturePhase != 0) throw new InvalidOperationException("Player restoration requires no active furniture interaction.");
        if (position.Count != 3 || rotation.Count != 4 ||
            position.Any(value => !float.IsFinite(value)) ||
            rotation.Any(value => !float.IsFinite(value)) ||
            !float.IsFinite(viewPitchRadians) || MathF.Abs(viewPitchRadians) > MathF.PI / 2)
            throw new InvalidDataException("Native saved player transform is invalid.");
        var quaternion = new Quaternion(
            rotation[0], rotation[1], rotation[2], rotation[3]).Normalized();
        GlobalTransform = new Transform3D(
            new Basis(quaternion),
            new Vector3(position[0], position[1], position[2]));
        _pitchRadians = viewPitchRadians;
        if (_xr is null) _camera.Rotation = new Vector3(_pitchRadians, 0, 0);
        Velocity = Vector3.Zero;
    }

    public override void _Ready()
    {
        if (DisplayServer.GetName() != "headless")
            Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    public override void _UnhandledInput(InputEvent inputEvent)
    {
        if (_xr is not null) return;
        var input = _configuration.Player.DesktopInput;
        if (PresentationInput(inputEvent)) { GetViewport().SetInputAsHandled(); return; }
        if (!_modalInput && _furniturePhase == 0 && GetMeta("opennv_source_pipboy_enabled", false).AsBool() && inputEvent.IsActionPressed(input.PipBoy.Action))
        {
            OpenPipBoy?.Invoke(); GetViewport().SetInputAsHandled(); return;
        }
        if (!_modalInput && _furniturePhase == 0 && inputEvent.IsActionPressed(input.Save.Action))
        {
            SaveGame?.Invoke();
            GetViewport().SetInputAsHandled();
            return;
        }
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
            if (_furniturePhase != 0)
            {
                RequestFurnitureExit();
                GetViewport().SetInputAsHandled();
                return;
            }
            var accepted = TryActivateLiveObject();
            GD.Print($"OPENNV_NATIVE_PLAYER_ACTION action=activate accepted={accepted}");
            GetViewport().SetInputAsHandled();
            return;
        }
        if (_modalInput || !_lookingEnabled || Input.MouseMode != Input.MouseModeEnum.Captured ||
            inputEvent is not InputEventMouseMotion motion)
            return;
        if (_furniturePhase is 1 or 2 or 4) return;
        if (_furniturePhase == 3) _furnitureLookYaw -= motion.Relative.X * _configuration.Player.MouseSensitivityRadiansPerPixel;
        else RotateY(-motion.Relative.X * _configuration.Player.MouseSensitivityRadiansPerPixel);
        _pitchRadians = Math.Clamp(
            _pitchRadians - motion.Relative.Y *
                _configuration.Player.MouseSensitivityRadiansPerPixel,
            -_configuration.Player.VerticalLookLimitRadians,
            _configuration.Player.VerticalLookLimitRadians);
        if (_furniturePhase == 3) PublishFurnitureCamera();
        else if (_sourceCamera is { } authored) ApplySourceCamera(authored);
        else _camera.Rotation = new Vector3(_pitchRadians, 0.0f, 0.0f);
    }

    internal event Action? OpenPipBoy;

    public override void _PhysicsProcess(double delta)
    {
        PublishXrPointer();
        if (_xr is not null && GetTree().Paused) return;
        if (_furniturePhase != 0) { AdvanceFurniture(delta); return; }
        var input = _configuration.Player.DesktopInput;
        var movement = _movementEnabled && !_modalInput
            ? _xr?.Movement ?? Input.GetVector(
                input.MoveLeft.Action,
                input.MoveRight.Action,
                input.MoveBackward.Action,
                input.MoveForward.Action)
            : Vector2.Zero;
        var forward = -_camera.GlobalBasis.Z;
        var right = _camera.GlobalBasis.X;
        forward.Y = 0.0f;
        right.Y = 0.0f;
        var direction = right.Normalized() * movement.X + forward.Normalized() * movement.Y;
        Sprinting = _movementEnabled && !_modalInput && movement.Y > 0 && (_xr?.Sprint ?? Input.IsActionPressed(input.Sprint.Action));
        var speed = _configuration.Player.MoveSpeedMetersPerSecond * (Sprinting ? _configuration.Player.SprintSpeedMultiplier : 1);
        var velocity = direction * speed;
        velocity.Y = IsOnFloor()
            ? MathF.Min(Velocity.Y, 0.0f)
            : Velocity.Y -
                _configuration.Simulation.GravityMetersPerSecondSquared * (float)delta;
        if (_movementEnabled && !_modalInput && IsOnFloor() && _jumpHeightMeters > 0 &&
            (_xr?.ConsumeJump() ?? Input.IsActionJustPressed(input.Jump.Action)))
        {
            velocity.Y = MathF.Sqrt(2 * _configuration.Simulation.GravityMetersPerSecondSquared * _jumpHeightMeters);
            ++JumpCount;
            GD.Print($"OPENNV_NATIVE_PLAYER_JUMP heightMeters={_jumpHeightMeters:R} count={JumpCount}");
        }
        var horizontalMotion = new Vector3(velocity.X, 0, velocity.Z) * (float)delta;
        CollisionResident = CanOccupyPosition?.Invoke(GlobalPosition + horizontalMotion) != false;
        if (!CollisionResident)
        {
            velocity.X = velocity.Z = 0;
            horizontalMotion = Vector3.Zero;
        }
        Velocity = velocity;
        if (NativeCharacterStep.TryStep(this, horizontalMotion, _configuration.Player.StepHeightMeters))
        {
            ++StepCount;
            Velocity = Vector3.Down * 0.01f;
            MoveAndSlide();
            Velocity = new(velocity.X, 0, velocity.Z);
        }
        else MoveAndSlide();
        BlockingShape = IsOnWall() && GetSlideCollisionCount() > 0
            ? (GetSlideCollision(GetSlideCollisionCount() - 1).GetCollider() as Node)?.GetPath().ToString() : null;
    }

    private bool TryActivateLiveObject()
    {
        var collider = AimedObject();
        return collider is not null && ActivateReference?.Invoke(collider) == true;
    }

    internal bool ModalInput => _modalInput;
    internal Node? AimedObject()
    {
        var aim = (Node3D?)_xr?.RightAim ?? _camera;
        var from = aim.GlobalPosition;
        var to = from - aim.GlobalBasis.Z * (_xr?.WorldPointer == true ? 12f : _configuration.Player.ActivationDistanceMeters);
        _aimHit = to;
        var query = PhysicsRayQueryParameters3D.Create(from, to, CollisionMask | CollisionLayer);
        query.CollideWithAreas = true;
        query.Exclude = SelfQueryBodies;
        query.HitBackFaces = false;
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (hit.TryGetValue("position", out var point)) _aimHit = point.AsVector3();
        if (!hit.TryGetValue("collider", out var colliderValue) ||
            colliderValue.AsGodotObject() is not Node collider)
            return null;
        return collider;
    }
}
