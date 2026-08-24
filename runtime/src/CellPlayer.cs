using Godot;

namespace OpenNV.Runtime;

internal partial class CellPlayer : CharacterBody3D
{
    private RuntimeConfiguration _configuration = null!;
    private Camera3D _camera = null!;
    private GameplaySession? _session;
    private XROrigin3D? _xrOrigin;
    private XRController3D? _leftHand;
    private XRController3D? _rightHand;
    private OpenXRRenderModelManager? _xrRenderModels;
    private Node3D? _xrWeaponMount;
    private PoolTableInstance? _activePool;
    private Node3D? _poolCueMount;
    private Marker3D? _poolCueTip;
    private MeshInstance3D? _xrMuzzleFlash;
    private OmniLight3D? _xrMuzzleLight;
    private Vector3 _xrWeaponRestPosition;
    private float _xrWeaponFeedbackSeconds;
    private bool _useXr;
    private bool _xrActivatePressed;
    private bool _xrFirePressed;
    private bool _xrSavePressed;
    private bool _xrReloadPressed;
    private bool _xrSnapTurnReady = true;
    private bool _xrEyeHeightCalibrated;
    private int _xrTrackedFrames;
    private int _xrHealthFrames;

    internal Camera3D Camera => _camera;
    internal bool UsesXr => _useXr;
    internal XROrigin3D? XrOrigin => _xrOrigin;
    internal XRController3D? LeftHand => _leftHand;
    internal XRController3D? RightHand => _rightHand;
    internal OpenXRRenderModelManager? XrRenderModels => _xrRenderModels;
    internal bool HasHeldWeapon => _xrWeaponMount?.FindChild("HeldWeapon", true, false) is Node3D;
    internal bool HasMuzzleFeedback => _xrMuzzleFlash is not null && _xrMuzzleLight is not null;
    internal bool HasHeldPoolCue => _poolCueMount is not null && _poolCueTip is not null;
    internal float DesiredEyeHeightMeters => _configuration.Xr.DesiredEyeHeightMeters;

    internal void Configure(
        float yaw,
        GameplaySession session,
        RuntimeConfiguration configuration,
        bool useXr = false,
        bool enableXrRuntimeFeatures = false)
    {
        _configuration = configuration;
        _session = session;
        _useXr = useXr;
        Name = "Player";
        Position = Vector3.Up * configuration.Player.SpawnCenterHeightMeters;
        Rotation = new Vector3(0.0f, yaw, 0.0f);
        CollisionLayer = configuration.Player.CollisionLayer;
        CollisionMask = configuration.Player.CollisionMask;
        AddChild(new CollisionShape3D
        {
            Name = "Capsule",
            Shape = new CapsuleShape3D
            {
                Radius = configuration.Player.CapsuleRadiusMeters,
                Height = configuration.Player.CapsuleHeightMeters,
            },
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
        if (_useXr)
            UpdateXrCalibrationAndHealth();
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
        velocity.X = direction.X * _configuration.Player.MoveSpeedMetersPerSecond;
        velocity.Z = direction.Z * _configuration.Player.MoveSpeedMetersPerSecond;
        velocity.Y = IsOnFloor()
            ? MathF.Min(velocity.Y, 0.0f)
            : velocity.Y - _configuration.Simulation.GravityMetersPerSecondSquared * (float)delta;
        Velocity = velocity;
        MoveAndSlide();
        if (_useXr)
        {
            PollXrActions();
            UpdateTrackedPoolCue(delta);
            UpdateXrWeaponFeedback((float)delta);
        }
    }

    public override void _UnhandledInput(InputEvent inputEvent)
    {
        if (_useXr)
            return;
        if (inputEvent is InputEventKey key && key.Pressed && !key.Echo)
        {
            if (key.PhysicalKeycode == Key.E)
            {
                if (_activePool is null)
                    Activate(_camera);
                else
                    ExitPool();
            }
            else if (key.PhysicalKeycode == Key.R)
            {
                if (_activePool is not null)
                    _activePool.ResetAuthored();
                else
                    _session!.Reload();
            }
            else if (key.PhysicalKeycode == Key.F5)
                _session!.SaveAndNotify();
            else if (key.PhysicalKeycode == Key.Escape)
            {
                if (_activePool is not null)
                    ExitPool();
                Input.MouseMode = Input.MouseModeEnum.Visible;
            }
        }
        else if (inputEvent is InputEventMouseButton button && button.Pressed)
        {
            if (button.ButtonIndex == MouseButton.Left)
            {
                if (_activePool is not null)
                    _activePool.StrikeFlat(-_camera.GlobalBasis.Z);
                else
                    _session!.Fire(_camera);
            }
            else if (button.ButtonIndex == MouseButton.Right)
                Input.MouseMode = Input.MouseModeEnum.Captured;
            else if (_activePool is not null && button.ButtonIndex == MouseButton.WheelUp)
                _activePool.CycleFlatPower(1);
            else if (_activePool is not null && button.ButtonIndex == MouseButton.WheelDown)
                _activePool.CycleFlatPower(-1);
        }
        else if (inputEvent is InputEventMouseMotion motion && Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            RotateY(-motion.Relative.X * _configuration.Player.MouseSensitivityRadiansPerPixel);
            var cameraRotation = _camera.Rotation;
            cameraRotation.X = Math.Clamp(
                cameraRotation.X - motion.Relative.Y * _configuration.Player.MouseSensitivityRadiansPerPixel,
                -_configuration.Player.VerticalLookLimitRadians,
                _configuration.Player.VerticalLookLimitRadians);
            _camera.Rotation = cameraRotation;
        }
    }

    private void Activate(Node3D aimSource)
    {
        var collider = Cast(aimSource, _configuration.Player.ActivationDistanceMeters);
        var poolBall = Ancestor<PoolBallInstance>(collider);
        if (poolBall is not null)
        {
            EnterPool(poolBall.Table);
            return;
        }
        var pool = Ancestor<PoolTableInstance>(collider);
        if (pool is not null)
        {
            EnterPool(pool);
            return;
        }
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
        var query = PhysicsRayQueryParameters3D.Create(from, to, _configuration.Player.CollisionMask);
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
        return hit.Count == 0 ? null : hit["collider"].AsGodotObject() as Node;
    }

    private void BuildDesktopRig()
    {
        _camera = new Camera3D
        {
            Name = "DataDerivedEntryCamera",
            Position = _configuration.Player.DesktopCameraOffsetMeters.Vector3(),
            Near = _configuration.Player.CameraNearMeters,
            Far = _configuration.Player.CameraFarMeters,
            Current = true,
        };
        AddChild(_camera);
    }

    private void BuildXrRig(bool enableRuntimeFeatures)
    {
        _xrOrigin = new XROrigin3D
        {
            Name = "XrOrigin",
            Position = Vector3.Up * _configuration.Xr.OriginYOffsetMeters,
            WorldScale = _configuration.Xr.WorldScale,
            Current = true,
        };
        AddChild(_xrOrigin);
        _camera = new XRCamera3D
        {
            Name = "TrackedHead",
            Near = _configuration.Player.CameraNearMeters,
            Far = _configuration.Player.CameraFarMeters,
            Current = true,
        };
        _xrOrigin.AddChild(_camera);
        _leftHand = BuildHand("LeftHand", "left_hand");
        _rightHand = BuildHand("RightHand", "right_hand");
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

    internal void AttachXrHeldWeapon(
        Node3D weapon,
        float unitsToMeters,
        Vector3 muzzlePositionGodotUnits)
    {
        if (!_useXr || _rightHand is null || _xrWeaponMount is not null)
            throw new InvalidOperationException("XR held weapon requires one configured right hand.");
        _xrWeaponRestPosition = Vector3.Zero;
        _xrWeaponMount = new Node3D
        {
            Name = "RightHandWeaponMount",
            Position = _xrWeaponRestPosition,
            RotationDegrees = _configuration.Xr.WeaponMountRotationDegrees.Vector3(),
        };
        _rightHand.AddChild(_xrWeaponMount);
        weapon.Name = "HeldWeapon";
        weapon.Scale = Vector3.One * unitsToMeters;
        _xrWeaponMount.AddChild(weapon);

        var flashMaterial = new StandardMaterial3D
        {
            AlbedoColor = _configuration.Xr.DiagnosticMuzzleFlash.AlbedoColorRgba.Color(),
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            EmissionEnabled = true,
            Emission = _configuration.Xr.DiagnosticMuzzleFlash.EmissionColorRgba.Color(),
            EmissionEnergyMultiplier = _configuration.Xr.DiagnosticMuzzleFlash.EmissionEnergy,
        };
        _xrMuzzleFlash = new MeshInstance3D
        {
            Name = "MuzzleFlash",
            Mesh = new SphereMesh
            {
                Radius = _configuration.Xr.DiagnosticMuzzleFlash.SphereRadiusGameUnits,
                Height = _configuration.Xr.DiagnosticMuzzleFlash.SphereHeightGameUnits,
            },
            MaterialOverride = flashMaterial,
            Position = muzzlePositionGodotUnits,
            Visible = false,
        };
        weapon.AddChild(_xrMuzzleFlash);
        _xrMuzzleLight = new OmniLight3D
        {
            Name = "MuzzleLight",
            Position = muzzlePositionGodotUnits,
            LightColor = _configuration.Xr.DiagnosticMuzzleFlash.LightColorRgba.Color(),
            LightEnergy = _configuration.Xr.DiagnosticMuzzleFlash.LightEnergy,
            OmniRange = _configuration.Xr.DiagnosticMuzzleFlash.LightRangeGameUnits * unitsToMeters,
            ShadowEnabled = false,
            Visible = false,
        };
        weapon.AddChild(_xrMuzzleLight);
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
            return stick.Length() < _configuration.Xr.MovementDeadzone ? Vector2.Zero : stick;
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
        if (MathF.Abs(turn) >= _configuration.Xr.SnapTurnActivationThreshold && _xrSnapTurnReady)
        {
            SnapTurn(-MathF.Sign(turn) * Mathf.DegToRad(_configuration.Xr.SnapTurnDegrees));
            _xrSnapTurnReady = false;
        }
        else if (MathF.Abs(turn) < _configuration.Xr.SnapTurnResetThreshold)
        {
            _xrSnapTurnReady = true;
        }

        var activate = _rightHand.GetFloat("activate") >= _configuration.Xr.ActionThreshold;
        if (activate && !_xrActivatePressed)
        {
            if (_activePool is null)
                Activate(_rightHand);
            else
                ExitPool();
        }
        _xrActivatePressed = activate;

        var fire = _rightHand.GetFloat("fire") >= _configuration.Xr.ActionThreshold;
        if (_activePool is null && fire && !_xrFirePressed && _session!.Fire(_rightHand))
        {
            _xrWeaponFeedbackSeconds = _configuration.Xr.WeaponFeedbackSeconds;
            TriggerHaptic(_configuration.Xr.FireHaptic);
        }
        _xrFirePressed = fire;

        var save = _leftHand!.IsButtonPressed("save");
        if (save && !_xrSavePressed)
            _session!.SaveAndNotify();
        _xrSavePressed = save;

        var reload = _rightHand.IsButtonPressed("reload");
        if (reload && !_xrReloadPressed)
        {
            if (_activePool is not null)
                _activePool.ResetAuthored();
            else if (_session!.Reload())
                TriggerHaptic(_configuration.Xr.ReloadHaptic);
        }
        _xrReloadPressed = reload;
    }

    private void UpdateXrCalibrationAndHealth()
    {
        if (_leftHand!.GetHasTrackingData() && _rightHand!.GetHasTrackingData())
            _xrTrackedFrames++;
        else
            _xrTrackedFrames = 0;
        if (!_xrEyeHeightCalibrated &&
            _xrTrackedFrames >= _configuration.Xr.EyeHeightCalibrationTrackedFrames)
        {
            var before = _camera.GlobalPosition.Y;
            _xrOrigin!.Position += Vector3.Up * (_configuration.Xr.DesiredEyeHeightMeters - before);
            _xrEyeHeightCalibrated = true;
            GD.Print(
                $"OPENNV_XR_EYE_HEIGHT_CALIBRATED before={before:F3} " +
                $"after={_camera.GlobalPosition.Y:F3} target={_configuration.Xr.DesiredEyeHeightMeters:F3}");
        }

        _xrHealthFrames++;
        if (_xrHealthFrames < _configuration.Xr.InputHealthReportFrames)
            return;
        _xrHealthFrames = 0;
        var leftTracker = XRServer.GetTracker("left_hand") as XRPositionalTracker;
        var rightTracker = XRServer.GetTracker("right_hand") as XRPositionalTracker;
        GD.Print(
            $"OPENNV_XR_INPUT_HEALTH " +
            $"leftProfile={leftTracker?.Profile.ToString() ?? "missing"} " +
            $"rightProfile={rightTracker?.Profile.ToString() ?? "missing"} " +
            $"leftActive={_leftHand!.GetIsActive()} leftTracked={_leftHand.GetHasTrackingData()} " +
            $"rightActive={_rightHand!.GetIsActive()} rightTracked={_rightHand.GetHasTrackingData()} " +
            $"eyeY={_camera.GlobalPosition.Y:F3} " +
            $"move={_leftHand.GetVector2("move")} turn={_rightHand.GetVector2("turn")} " +
            $"grip={_rightHand.GetFloat("activate"):F2} trigger={_rightHand.GetFloat("fire"):F2}");
    }

    private void UpdateXrWeaponFeedback(float delta)
    {
        if (_xrWeaponMount is null || _xrMuzzleFlash is null || _xrMuzzleLight is null)
            return;
        _xrWeaponFeedbackSeconds = MathF.Max(0.0f, _xrWeaponFeedbackSeconds - delta);
        var strength = _xrWeaponFeedbackSeconds / _configuration.Xr.WeaponFeedbackSeconds;
        _xrWeaponMount.Position =
            _xrWeaponRestPosition + Vector3.Back * (_configuration.Xr.WeaponRecoilMeters * strength);
        var flashVisible = _xrWeaponFeedbackSeconds > _configuration.Xr.MuzzleFlashVisibleSeconds;
        _xrMuzzleFlash.Visible = flashVisible;
        _xrMuzzleLight.Visible = flashVisible;
    }

    private void EnterPool(PoolTableInstance table)
    {
        if (_activePool == table)
            return;
        if (_activePool is not null)
            ExitPool();
        _activePool = table;
        table.SetPlayActive(true);
        if (_xrWeaponMount is not null)
            _xrWeaponMount.Visible = false;
        var presentation = table.CreateCuePresentation();
        _poolCueMount = new Node3D
        {
            Name = "PoolCueMount",
            Position = (_useXr
                ? _configuration.Pool.XrCueMountPositionMeters
                : _configuration.Pool.DesktopCueMountPositionMeters).Vector3(),
            RotationDegrees = (_useXr
                ? _configuration.Pool.XrCueMountRotationDegrees
                : _configuration.Pool.DesktopCueMountRotationDegrees).Vector3(),
        };
        Node3D cueParent = _useXr ? _rightHand! : _camera;
        cueParent.AddChild(_poolCueMount);
        presentation.Visual.Scale = Vector3.One * _configuration.World.GameUnitsToMeters;
        _poolCueMount.AddChild(presentation.Visual);
        _poolCueTip = new Marker3D
        {
            Name = "AuthoredCueTip",
            Position = presentation.TipGodotUnits * _configuration.World.GameUnitsToMeters,
        };
        _poolCueMount.AddChild(_poolCueTip);
    }

    internal void EnterPoolForProof(PoolTableInstance table) => EnterPool(table);

    internal void ExitPoolForProof() => ExitPool();

    private void ExitPool()
    {
        if (_activePool is null)
            return;
        _activePool.SetPlayActive(false);
        _activePool = null;
        _poolCueMount?.QueueFree();
        _poolCueMount = null;
        _poolCueTip = null;
        if (_xrWeaponMount is not null)
            _xrWeaponMount.Visible = true;
        _session!.Save();
    }

    private void UpdateTrackedPoolCue(double delta)
    {
        if (_activePool is null || _poolCueTip is null)
            return;
        if (_activePool.UpdateTrackedCue(_poolCueTip.GlobalPosition, _xrFirePressed, delta))
            TriggerHaptic(_configuration.Pool.StrikeHaptic);
    }

    private void TriggerHaptic(HapticConfiguration haptic)
    {
        _rightHand!.TriggerHapticPulse(
            "haptic",
            haptic.Frequency,
            haptic.Amplitude,
            haptic.DurationSeconds,
            haptic.DelaySeconds);
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
