using Godot;
using OpenNV.Runtime.World.Portals;


using OpenNV.Runtime.InputSystem;
using OpenNV.Runtime.Presentation.OpenXR;
using OpenNV.Runtime.World.Interactions;
using OpenNV.Runtime.Compatibility.Jam;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Gameplay.Settings;

namespace OpenNV.Runtime.World.Cells;

internal static class CellPlayerNumericContracts
{
    // Immutable format, source-art, geometry, and acceptance contracts.
    // Runtime-tunable Fallout 1 behavior remains in the versioned runtime recipe.
    internal const float PresentationFloat0Point22f = 0.22f;
    internal const float PresentationFloat0Point48f = 0.48f;
    internal const float PresentationFloat0Point80f = 0.80f;
    internal const float PresentationFloat0Point86f = 0.86f;
    internal const float PresentationFloat0Point90f = 0.90f;
    internal const float PresentationFloat1Point15f = 1.15f;
    internal const float PresentationFloat10Point0f = 10.0f;
    internal const float PresentationFloat11Point05f = 11.05f;
    internal const float PresentationFloat11Point5f = 11.5f;
    internal const float PresentationFloat18Point0f = 18.0f;
    internal const float PresentationFloat8Point0f = 8.0f;
}

internal partial class CellPlayer : CharacterBody3D
{
    internal const float DioramaInitialSizeMeters = 18.0f;
    internal const float DioramaMinimumSizeMeters = 6.0f;
    internal const float DioramaMaximumSizeMeters = 64.0f;
    internal const float DioramaYawStepRadians = MathF.PI / 3.0f;
    internal const float DioramaPanSpeedMetersPerSecond = 7.5f;
    private const float MinimumStepProgressFraction = 0.1f;
    private const float RepeatedTangentOriginToleranceMeters = 0.06f;
    private const float StepDownHeightMultiplier = 2.0f;
    private const float StepForwardClearanceRadii = 1.1f;

    private RuntimeConfiguration _configuration = null!;
    private RuntimeSettingsState? _settings;
    private Camera3D _camera = null!;
    private GameplaySession? _session;
    private XROrigin3D? _xrOrigin;
    private Node3D? _dioramaOrbit;
    private XRController3D? _leftGrip;
    private XRController3D? _rightGrip;
    private XRController3D? _leftAim;
    private XRController3D? _rightAim;
    private FirstPersonRig.LoadedRig? _firstPersonRig;
    private readonly PlayerControlTelemetry _controlTelemetry = new();
    private Node3D? _weaponMount;
    private string? _heldWeaponFormId;
    private PoolTableInstance? _activePool;
    private PickupInstance? _heldPickup;
    private Node3D? _poolCueMount;
    private Marker3D? _poolCueTip;
    private MeshInstance3D? _muzzleFlash;
    private OmniLight3D? _muzzleLight;
    private Vector3 _weaponRestPosition;
    private float _weaponFeedbackSeconds;
    private bool _useXr;
    private bool _useClassicDiorama;
    private float _dioramaTargetYawRadians;
    private float _dioramaTargetSizeMeters = DioramaInitialSizeMeters;
    private Vector3 _dioramaHomePosition;
    private float _dioramaHomeSizeMeters = DioramaInitialSizeMeters;
    private Aabb? _dioramaFramingBounds;
    private bool _xrActivatePressed;
    private bool _xrGrabPressed;
    private bool _xrFirePressed;
    private bool _xrSavePressed;
    private bool _xrReloadPressed;
    private bool _xrSnapTurnReady = true;
    private bool _xrEyeHeightCalibrated;
    private int _xrTrackedFrames;
    private int _xrHealthFrames;
    private float? _xrLastFloorY;
    private Func<Node?, bool>? _externalActivationHandler;
    private bool _movementEnabled = true;
    private bool _lookEnabled = true;
    private bool _activationEnabled = true;
    private bool _combatEnabled = true;
    private bool _saveEnabled = true;
    private CellNavigationGraph? _navigation;
    private Node3D? _navigationRoot;
    private Vector3 _navigationOriginGameUnits;
    private JamJvsSprintContract? _jamJvsSprint;
    private JamJbtBulletTimeContract? _jamJbtBulletTime;
    private bool _jamBulletTimeActive;
    private CellPortalTravel? _portalTravel;
    private Vector3? _lastFloorTangentOrigin;

    internal Camera3D Camera => _camera;
    internal bool UsesXr => _useXr;
    internal bool UsesClassicDiorama => _useClassicDiorama;
    internal Node3D? DioramaOrbit => _dioramaOrbit;
    internal float DioramaTargetYawRadians => _dioramaTargetYawRadians;
    internal float DioramaTargetSizeMeters => _dioramaTargetSizeMeters;
    internal Aabb? DioramaFramingBounds => _dioramaFramingBounds;
    internal XROrigin3D? XrOrigin => _xrOrigin;
    internal XRController3D? LeftGrip => _leftGrip;
    internal XRController3D? RightGrip => _rightGrip;
    internal XRController3D? LeftAim => _leftAim;
    internal XRController3D? RightAim => _rightAim;
    internal bool HasLeftHand => _firstPersonRig?.Left.VisibleMeshes > 0;
    internal bool HasRightHand => _firstPersonRig?.Right.VisibleMeshes > 0;
    internal string HandProvider => _firstPersonRig is null ? "missing" : FirstPersonRig.Provider;
    internal bool HasHeldWeapon => _weaponMount?.FindChild("HeldWeapon", true, false) is Node3D;
    internal bool HasMuzzleFeedback => _muzzleFlash is not null && _muzzleLight is not null;
    internal bool HasHeldPoolCue => _poolCueMount is not null && _poolCueTip is not null;
    internal PickupInstance? HeldPickup => _heldPickup;
    internal float DesiredEyeHeightMeters => _configuration.Xr.DesiredEyeHeightMeters;
    internal PlayerControlTelemetry.Snapshot ControlTelemetry => _controlTelemetry.Report();
    internal IReadOnlyList<CellPortalTravel.Transition> PortalTransitions =>
        _portalTravel?.Transitions ?? Array.Empty<CellPortalTravel.Transition>();
    internal string LastStepAttempt { get; private set; } = "none";
    internal string LastMovementCollision { get; private set; } = "none";
    internal string LastMovementState { get; private set; } = "none";
    internal string LastActivationCollider { get; private set; } = "none";
    internal string LastActivationDoorFormId { get; private set; } = "none";
    internal int DesktopActivationEdges { get; private set; }
    internal Vector3 LastBlockingNormal { get; private set; }
    internal Node? LastBlockingCollider { get; private set; }
    internal Vector3? LastBlockingPosition { get; private set; }

    internal void SetControlPolicy(
        bool movement,
        bool look,
        bool activation,
        bool combat,
        bool save)
    {
        _movementEnabled = movement;
        _lookEnabled = look;
        _activationEnabled = activation;
        _combatEnabled = combat;
        _saveEnabled = save;
        if (!activation && _heldPickup is not null)
            DropHeldPickup();
        if (!movement)
            Velocity = new Vector3(0.0f, Velocity.Y, 0.0f);
    }

    internal void SetExternalActivationHandler(Func<Node?, bool>? handler) =>
        _externalActivationHandler = handler;

    internal void ConfigureJamJvsSprint(JamJvsSprintContract sprint)
    {
        if (_useXr || _useClassicDiorama)
            throw new InvalidOperationException(
                "The bounded JVS sprint transport currently supports desktop first-person movement only.");
        _jamJvsSprint = sprint;
    }

    internal void ConfigureJamJbtBulletTime(JamJbtBulletTimeContract bulletTime)
    {
        if (_useXr || _useClassicDiorama)
            throw new InvalidOperationException(
                "The bounded JBT transport currently supports desktop first-person play only.");
        _jamJbtBulletTime = bulletTime;
        SetJamBulletTime(false);
    }

    internal void ConfigureOwnedNavigation(
        CellNavigationGraph navigation,
        Node3D root,
        Vector3 originGameUnits)
    {
        _navigation = navigation;
        _navigationRoot = root;
        _navigationOriginGameUnits = originGameUnits;
    }

    internal void ConfigurePortalTravel(CellPortalTravel portalTravel) =>
        _portalTravel = portalTravel;

    internal void ApplyPortalArrival(
        Node3D targetRoot,
        Vector3 targetOriginGameUnits,
        DoorInstance.TeleportDestination destination)
    {
        var floorPosition = ResolvePortalArrivalFloorPosition(
            targetRoot,
            targetOriginGameUnits,
            destination);
        var targetBasis = targetRoot.GlobalBasis.Orthonormalized() *
            new Basis(Vector3.Up, destination.YawGodotRadians);
        var bodyPosition = floorPosition +
            Vector3.Up * _configuration.Player.SpawnCenterHeightMeters;
        if (_useXr)
        {
            var headOffset = _camera.GlobalPosition - GlobalPosition;
            headOffset.Y = 0.0f;
            var localHeadOffset = GlobalBasis.Orthonormalized().Inverse() * headOffset;
            var rotatedHeadOffset = targetBasis * localHeadOffset;
            bodyPosition -= new Vector3(rotatedHeadOffset.X, 0.0f, rotatedHeadOffset.Z);
        }
        var scale = GlobalBasis.Scale;
        GlobalTransform = new Transform3D(targetBasis.Scaled(scale), bodyPosition);
        Velocity = Vector3.Zero;
    }

    internal static Vector3 ResolvePortalArrivalFloorPosition(
        Node3D targetRoot,
        Vector3 targetOriginGameUnits,
        DoorInstance.TeleportDestination destination)
    {
        var localPosition = new Vector3(
            destination.PositionGameUnits.X - targetOriginGameUnits.X,
            destination.PositionGameUnits.Z - targetOriginGameUnits.Z,
            -(destination.PositionGameUnits.Y - targetOriginGameUnits.Y));
        return targetRoot.ToGlobal(localPosition);
    }

    internal void ClearOwnedNavigation()
    {
        _navigation = null;
        _navigationRoot = null;
        Velocity = Vector3.Zero;
    }

    internal void ApplyAuthoredCameraTransform(Transform3D transformFromFloor)
    {
        if (_useXr)
            return;
        transformFromFloor.Origin -=
            Vector3.Up * _configuration.Player.SpawnCenterHeightMeters;
        _camera.Transform = transformFromFloor;
    }

    internal void Configure(
        float yaw,
        GameplaySession session,
        RuntimeConfiguration configuration,
        bool useXr = false,
        bool useClassicDiorama = false,
        RuntimeSettingsState? settings = null)
    {
        if (useXr && useClassicDiorama)
            throw new ArgumentException(
                "Classic Diorama and OpenXR require separate presentation adapters.");
        _configuration = configuration;
        _settings = settings;
        _session = session;
        _useXr = useXr;
        _useClassicDiorama = useClassicDiorama;
        Name = "Player";
        Position = Vector3.Up * (useClassicDiorama
            ? 0.0f
            : configuration.Player.SpawnCenterHeightMeters);
        Rotation = new Vector3(0.0f, useClassicDiorama ? 0.0f : yaw, 0.0f);
        CollisionLayer = useClassicDiorama ? 0u : configuration.Player.CollisionLayer;
        CollisionMask = useClassicDiorama ? 0u : configuration.Player.CollisionMask;
        if (!useClassicDiorama)
        {
            AddChild(new CollisionShape3D
            {
                Name = "Capsule",
                Shape = new CapsuleShape3D
                {
                    Radius = configuration.Player.CapsuleRadiusMeters,
                    Height = configuration.Player.CapsuleHeightMeters,
                },
            });
        }
        if (useXr)
            BuildXrRig();
        else if (useClassicDiorama)
            BuildClassicDioramaRig(yaw);
        else
            BuildDesktopRig();
    }

    public override void _Ready()
    {
        if (!_useXr && !_useClassicDiorama && DisplayServer.GetName() != "headless")
            Input.MouseMode = Input.MouseModeEnum.Captured;
        else if (_useClassicDiorama)
            Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    public override void _Process(double delta)
    {
        if (!_useClassicDiorama)
            return;
        _dioramaOrbit!.Rotation = new Vector3(
            0.0f,
            Mathf.LerpAngle(
                _dioramaOrbit.Rotation.Y,
                _dioramaTargetYawRadians,
                Math.Clamp((float)delta * CellPlayerNumericContracts.PresentationFloat8Point0f, 0.0f, 1.0f)),
            0.0f);
        _camera.Size = Mathf.Lerp(
            _camera.Size,
            _dioramaTargetSizeMeters,
            Math.Clamp((float)delta * CellPlayerNumericContracts.PresentationFloat10Point0f, 0.0f, 1.0f));
    }

    public override void _Input(InputEvent @event)
    {
        if (_useXr || _session is null || @event is InputEventKey { Echo: true })
            return;
        var input = _configuration.Player.DesktopInput;
        if (@event.IsActionPressed(input.PipBoy.Action))
        {
            _session.TogglePipBoy();
            GetViewport().SetInputAsHandled();
            GD.Print($"OPENNV_FLAT_ACTION action=pipboy open={_session.IsPipBoyOpen}");
            return;
        }
        if (_session.IsPipBoyOpen && @event.IsActionPressed(input.Cancel.Action))
        {
            _session.ClosePipBoy();
            GetViewport().SetInputAsHandled();
            GD.Print("OPENNV_FLAT_ACTION action=pipboy-close accepted=True");
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_useClassicDiorama)
        {
            UpdateClassicDioramaPan((float)delta);
            return;
        }
        if (_useXr)
            UpdateXrCalibrationAndHealth();
        if (_jamJbtBulletTime is not null &&
            Input.IsActionJustPressed(JamJbtBulletTimeContract.InputAction))
            SetJamBulletTime(!_jamBulletTimeActive);
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

        var movementSpeed = _jamJvsSprint?.MovementSpeed(
            _configuration.Player.MoveSpeedMetersPerSecond,
            input,
            Input.IsActionPressed(JamJvsSprintContract.InputAction)) ??
            _configuration.Player.MoveSpeedMetersPerSecond;
        var horizontalVelocity = direction * movementSpeed;
        if (!_movementEnabled)
            Velocity = Vector3.Zero;
        else if (_navigation is not null)
            MoveOnOwnedNavigation(horizontalVelocity, (float)delta);
        else
            MoveWithPhysics(horizontalVelocity, (float)delta);
        UpdateWeaponFeedback((float)delta);
        if (_useXr)
        {
            PollXrActions();
            UpdateTrackedPoolCue(delta);
            UpdateXrControlTelemetry();
        }
        else
            PollDesktopActions();
    }

    public override void _ExitTree()
    {
        if (_heldPickup is not null)
        {
            _heldPickup.Drop();
            _heldPickup = null;
        }
        if (_jamBulletTimeActive)
            SetJamBulletTime(false);
    }

    private void SetJamBulletTime(bool active)
    {
        _jamBulletTimeActive = active;
        Engine.TimeScale = active
            ? _jamJbtBulletTime!.EffectiveTimeMultiplier
            : 1.0;
        GD.Print(
            $"OPENNV_JAM_JBT active={active} timeScale={Engine.TimeScale:F2} " +
            "completeJbtReady=False");
    }

    private void MoveOnOwnedNavigation(Vector3 horizontalVelocity, float delta)
    {
        if (Mathf.IsZeroApprox(delta))
        {
            Velocity = Vector3.Zero;
            return;
        }
        var before = GlobalPosition;
        var foot = before - Vector3.Up * _configuration.Player.SpawnCenterHeightMeters;
        var desiredFoot = foot + horizontalVelocity * delta;
        var reachable = _navigation!.FindReachablePoint(
            NavigationWorldToGame(foot),
            NavigationWorldToGame(desiredFoot));
        GlobalPosition = NavigationGameToWorld(reachable) +
            Vector3.Up * _configuration.Player.SpawnCenterHeightMeters;
        Velocity = (GlobalPosition - before) / delta;
    }

    private void MoveWithPhysics(Vector3 horizontalVelocity, float delta)
    {
        var before = GlobalPosition;
        var wasOnFloor = IsOnFloor();
        var beforeVelocity = Velocity;
        var velocity = Velocity;
        velocity.X = horizontalVelocity.X;
        velocity.Z = horizontalVelocity.Z;
        velocity.Y = IsOnFloor()
            ? MathF.Min(velocity.Y, 0.0f)
            : velocity.Y - _configuration.Simulation.GravityMetersPerSecondSquared * delta;
        Velocity = velocity;
        MoveAndSlide();
        var expectedHorizontalMotion = horizontalVelocity * delta;
        var horizontalProgress = GlobalPosition - before;
        horizontalProgress.Y = 0.0f;
        if (wasOnFloor &&
            !expectedHorizontalMotion.IsZeroApprox() &&
            horizontalProgress.Length() <
                expectedHorizontalMotion.Length() * MinimumStepProgressFraction)
        {
            LastMovementCollision = DescribeSlideCollisions();
            LastMovementState =
                $"requested={horizontalVelocity} expected={expectedHorizontalMotion} " +
                $"progress={horizontalProgress} beforeVelocity={beforeVelocity} " +
                $"afterVelocity={Velocity} wasOnFloor={wasOnFloor} onFloor={IsOnFloor()}";
            var repeatedTangent = _lastFloorTangentOrigin is { } tangentOrigin &&
                GlobalPosition.DistanceTo(tangentOrigin) <=
                    RepeatedTangentOriginToleranceMeters;
            if (repeatedTangent)
            {
                _lastFloorTangentOrigin = null;
            }
            else if (TryFloorTangentMotion(expectedHorizontalMotion, delta))
            {
                return;
            }
            var remainingHorizontalMotion = expectedHorizontalMotion - horizontalProgress;
            remainingHorizontalMotion.Y = 0.0f;
            if (repeatedTangent && !remainingHorizontalMotion.IsZeroApprox())
            {
                remainingHorizontalMotion = remainingHorizontalMotion.Normalized() *
                    _configuration.Player.CapsuleRadiusMeters *
                    StepForwardClearanceRadii;
            }
            var blockingCollision = Enumerable.Range(0, GetSlideCollisionCount())
                .Select(GetSlideCollision)
                .FirstOrDefault(collision =>
                    collision.GetNormal().Dot(Vector3.Up) < MathF.Cos(FloorMaxAngle));
            if (blockingCollision is not null)
                RecordBlockingCollision(blockingCollision);
            var blockingNormal = blockingCollision?.GetNormal() ?? Vector3.Zero;
            _ = TryStepUp(
                GlobalTransform,
                before,
                remainingHorizontalMotion,
                delta,
                blockingNormal.IsZeroApprox() ? null : blockingNormal);
        }
        else
        {
            _lastFloorTangentOrigin = null;
            if (!expectedHorizontalMotion.IsZeroApprox())
                ClearBlockingCollision();
        }
    }

    private bool TryFloorTangentMotion(Vector3 horizontalMotion, float delta)
    {
        var walkableNormalMinimum = MathF.Cos(FloorMaxAngle);
        if (GetSlideCollisionCount() == 0 ||
            Enumerable.Range(0, GetSlideCollisionCount()).Any(index =>
                GetSlideCollision(index).GetNormal().Dot(Vector3.Up) < walkableNormalMinimum))
            return false;
        var tangentMotion = horizontalMotion.Slide(
            GetSlideCollision(0).GetNormal().Normalized());
        var tangentHorizontalMotion = tangentMotion;
        tangentHorizontalMotion.Y = 0.0f;
        if (tangentHorizontalMotion.IsZeroApprox())
            return false;
        tangentMotion *= horizontalMotion.Length() / tangentHorizontalMotion.Length();
        var beforeTransform = GlobalTransform;
        var beforePosition = GlobalPosition;
        _ = MoveAndCollide(
            tangentMotion,
            testOnly: false,
            safeMargin: 0.0f,
            recoveryAsCollision: false);
        var progress = GlobalPosition - beforePosition;
        var horizontalProgress = progress;
        horizontalProgress.Y = 0.0f;
        if (horizontalProgress.Length() <
            horizontalMotion.Length() * MinimumStepProgressFraction)
        {
            GlobalTransform = beforeTransform;
            return false;
        }
        Velocity = progress / delta;
        _lastFloorTangentOrigin = beforePosition;
        LastStepAttempt =
            $"floor-tangent-sweep:{horizontalProgress.Length():F4}/" +
            $"{horizontalMotion.Length():F4}";
        return true;
    }

    private bool TryStepUp(
        Transform3D stepStartTransform,
        Vector3 beforePosition,
        Vector3 horizontalMotion,
        float delta,
        Vector3? blockingNormal)
    {
        var stepHeight = _configuration.Player.CapsuleRadiusMeters;
        if (blockingNormal is { } normal)
        {
            normal.Y = 0.0f;
            if (!normal.IsZeroApprox())
            {
                var crossingMotion = -normal.Normalized() *
                    stepHeight * StepForwardClearanceRadii;
                if (crossingMotion.Dot(horizontalMotion) > 0.0f)
                    horizontalMotion = crossingMotion;
            }
        }
        var upMotion = Vector3.Up * stepHeight;
        var upCollision = new KinematicCollision3D();
        if (TestMove(
                stepStartTransform,
                upMotion,
                upCollision,
                safeMargin: 0.0f,
                recoveryAsCollision: false))
        {
            RecordBlockingCollision(upCollision);
            LastStepAttempt =
                $"up-blocked:normal={upCollision.GetNormal()}:" +
                $"travel={upCollision.GetTravel()}:" +
                $"collider={upCollision.GetCollider()}";
            return false;
        }
        var raised = stepStartTransform;
        raised.Origin += upMotion;
        var forwardCollision = new KinematicCollision3D();
        if (TestMove(
                raised,
                horizontalMotion,
                forwardCollision,
                safeMargin: 0.0f,
                recoveryAsCollision: false))
        {
            RecordBlockingCollision(forwardCollision);
            LastStepAttempt =
                $"forward-blocked:normal={forwardCollision.GetNormal()}:" +
                $"travel={forwardCollision.GetTravel()}:" +
                $"collider={forwardCollision.GetCollider()}";
            return false;
        }
        var advanced = raised;
        advanced.Origin += horizontalMotion;
        var downCollision = new KinematicCollision3D();
        if (!TestMove(
                advanced,
                Vector3.Down * stepHeight * StepDownHeightMultiplier,
                downCollision,
                SafeMargin,
                recoveryAsCollision: false))
        {
            LastStepAttempt = "no-step-floor";
            return false;
        }
        if (downCollision.GetNormal().Dot(Vector3.Up) < MathF.Cos(FloorMaxAngle))
        {
            LastStepAttempt = $"steep-step-floor:{downCollision.GetNormal()}";
            return false;
        }
        advanced.Origin += downCollision.GetTravel();
        GlobalTransform = advanced;
        Velocity = (GlobalPosition - beforePosition) / delta;
        LastStepAttempt = "accepted";
        ClearBlockingCollision();
        return true;
    }

    private void ClearBlockingCollision()
    {
        LastBlockingNormal = Vector3.Zero;
        LastBlockingCollider = null;
        LastBlockingPosition = null;
    }

    private void RecordBlockingCollision(KinematicCollision3D collision)
    {
        LastBlockingNormal = collision.GetNormal();
        LastBlockingCollider = collision.GetCollider() as Node;
        LastBlockingPosition = collision.GetPosition();
    }

    private string DescribeSlideCollisions()
    {
        if (GetSlideCollisionCount() == 0)
            return "none";
        return string.Join(
            ";",
            Enumerable.Range(0, GetSlideCollisionCount()).Select(index =>
            {
                var collision = GetSlideCollision(index);
                var collider = collision.GetCollider() as Node;
                return $"{collider?.GetPath().ToString() ?? "unknown"}" +
                    $"@{collision.GetPosition()} normal={collision.GetNormal()}";
            }));
    }

    private Vector3 NavigationWorldToGame(Vector3 world)
    {
        var local = _navigationRoot!.ToLocal(world);
        return new Vector3(
            local.X + _navigationOriginGameUnits.X,
            -local.Z + _navigationOriginGameUnits.Y,
            local.Y + _navigationOriginGameUnits.Z);
    }

    private Vector3 NavigationGameToWorld(Vector3 game) =>
        _navigationRoot!.ToGlobal(new Vector3(
            game.X - _navigationOriginGameUnits.X,
            game.Z - _navigationOriginGameUnits.Z,
            -(game.Y - _navigationOriginGameUnits.Y)));

    public override void _UnhandledInput(InputEvent inputEvent)
    {
        if (_useXr)
            return;
        if (_useClassicDiorama)
        {
            HandleClassicDioramaInput(inputEvent);
            return;
        }
        if (_lookEnabled &&
            inputEvent is InputEventMouseMotion motion &&
            Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            var sensitivity = _settings?.ApplyMouseSensitivity(
                _configuration.Player.MouseSensitivityRadiansPerPixel) ??
                _configuration.Player.MouseSensitivityRadiansPerPixel;
            RotateY(-motion.Relative.X * sensitivity);
            var cameraRotation = _camera.Rotation;
            cameraRotation.X = Math.Clamp(
                cameraRotation.X - motion.Relative.Y * sensitivity,
                -_configuration.Player.VerticalLookLimitRadians,
                _configuration.Player.VerticalLookLimitRadians);
            _camera.Rotation = cameraRotation;
        }
    }

    private bool Activate(Node3D aimSource)
    {
        if (_heldPickup is not null)
        {
            var held = _heldPickup;
            _heldPickup = null;
            held.Drop();
            if (held is ScriptedActivatorInstance heldActivator)
                return heldActivator.Activate();
            _session!.Collect(held);
            return true;
        }
        var collider = Cast(aimSource, _configuration.Player.ActivationDistanceMeters);
        LastActivationCollider = collider?.GetPath().ToString() ?? "none";
        LastActivationDoorFormId = Ancestor<DoorInstance>(collider)?.ReferenceFormId ?? "none";
        if (_externalActivationHandler?.Invoke(collider) == true)
            return true;
        var poolBall = Ancestor<PoolBallInstance>(collider);
        if (poolBall is not null)
        {
            EnterPool(poolBall.Table);
            return true;
        }
        var pool = Ancestor<PoolTableInstance>(collider);
        if (pool is not null)
        {
            EnterPool(pool);
            return true;
        }
        var scriptedActivator = Ancestor<ScriptedActivatorInstance>(collider);
        if (scriptedActivator is not null)
            return scriptedActivator.Activate();
        var pickup = Ancestor<PickupInstance>(collider);
        if (pickup is not null)
        {
            _session!.Collect(pickup);
            return true;
        }
        var container = Ancestor<ContainerInstance>(collider);
        if (container is not null)
        {
            _session!.OpenContainer(container);
            return true;
        }
        var craftingStation = Ancestor<CraftingStationInstance>(collider);
        if (craftingStation is not null)
        {
            _session!.OpenCrafting(craftingStation);
            return true;
        }
        var door = Ancestor<DoorInstance>(collider);
        if (door is null)
        {
            if (collider is null &&
                _portalTravel?.TryActivateFacing(
                    aimSource,
                    this,
                    _configuration.Player.ActivationDistanceMeters,
                    out var facingDoorFormId) == true)
            {
                LastActivationDoorFormId = facingDoorFormId;
                return true;
            }
            return false;
        }
        if (_portalTravel?.TryActivate(door, this) == true)
        {
            GD.Print(
                $"OPENNV_DOOR_TRAVEL form={door.ReferenceFormId} " +
                $"activeCell={_session!.ActiveCellFormId}");
            return true;
        }
        door.SetOpen(!door.IsOpen);
        _session!.DoorChanged(door);
        GD.Print($"OPENNV_DOOR_STATE form={door.Name.ToString().Replace("DOOR_", "")} open={door.IsOpen}");
        return true;
    }

    private Node? Cast(Node3D aimSource, float distance)
    {
        var from = aimSource.GlobalPosition;
        var to = from - aimSource.GlobalBasis.Z * distance;
        var query = PhysicsRayQueryParameters3D.Create(from, to, CollisionMask);
        query.HitBackFaces = false;
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

    private void BuildClassicDioramaRig(float yaw)
    {
        _dioramaTargetYawRadians = yaw + MathF.PI / 4.0f;
        _dioramaTargetSizeMeters = DioramaInitialSizeMeters;
        _dioramaOrbit = new Node3D
        {
            Name = "ClassicDioramaOrbit",
            Rotation = new Vector3(0.0f, _dioramaTargetYawRadians, 0.0f),
        };
        AddChild(_dioramaOrbit);
        _camera = new Camera3D
        {
            Name = "ClassicDioramaCamera",
            Position = new Vector3(0.0f, CellPlayerNumericContracts.PresentationFloat11Point5f, CellPlayerNumericContracts.PresentationFloat11Point5f),
            Projection = Camera3D.ProjectionType.Orthogonal,
            KeepAspect = Camera3D.KeepAspectEnum.Height,
            Size = DioramaInitialSizeMeters,
            Near = _configuration.Player.CameraNearMeters,
            Far = _configuration.Player.CameraFarMeters,
            Current = true,
        };
        _dioramaOrbit.AddChild(_camera);
        _camera.Rotation = new Vector3(-MathF.Atan2(CellPlayerNumericContracts.PresentationFloat11Point05f, CellPlayerNumericContracts.PresentationFloat11Point5f), 0.0f, 0.0f);
    }

    internal void FrameClassicDiorama(Aabb worldBounds)
    {
        if (!_useClassicDiorama || _dioramaOrbit is null)
            throw new InvalidOperationException(
                "Classic Diorama framing requires its camera adapter.");
        if (worldBounds.Size.X <= 0.0f || worldBounds.Size.Y <= 0.0f ||
            worldBounds.Size.Z <= 0.0f)
            throw new InvalidOperationException(
                $"Classic Diorama received invalid world bounds: {worldBounds}");

        _dioramaFramingBounds = worldBounds;
        _dioramaHomePosition = worldBounds.GetCenter();
        Position = _dioramaHomePosition;
        var horizontalSpan = MathF.Max(worldBounds.Size.X, worldBounds.Size.Z);
        _dioramaHomeSizeMeters = Math.Clamp(
            MathF.Max(horizontalSpan * CellPlayerNumericContracts.PresentationFloat0Point48f, worldBounds.Size.Y * CellPlayerNumericContracts.PresentationFloat0Point80f + horizontalSpan * CellPlayerNumericContracts.PresentationFloat0Point22f),
            DioramaInitialSizeMeters,
            DioramaMaximumSizeMeters);
        _dioramaTargetSizeMeters = _dioramaHomeSizeMeters;
        _camera.Size = _dioramaHomeSizeMeters;
        var cameraDistance = MathF.Max(CellPlayerNumericContracts.PresentationFloat18Point0f, _dioramaHomeSizeMeters * CellPlayerNumericContracts.PresentationFloat1Point15f);
        var cameraHeight = cameraDistance * CellPlayerNumericContracts.PresentationFloat0Point90f;
        _camera.Position = new Vector3(0.0f, cameraHeight, cameraDistance);
        _camera.Rotation = new Vector3(-MathF.Atan2(cameraHeight, cameraDistance), 0.0f, 0.0f);
    }

    private void HandleClassicDioramaInput(InputEvent inputEvent)
    {
        if (inputEvent is InputEventKey key && key.Pressed && !key.Echo)
        {
            if (key.PhysicalKeycode == Key.Q)
                _dioramaTargetYawRadians += DioramaYawStepRadians;
            else if (key.PhysicalKeycode == Key.E)
                _dioramaTargetYawRadians -= DioramaYawStepRadians;
            else if (key.PhysicalKeycode == Key.Home)
            {
                _dioramaTargetSizeMeters = _dioramaHomeSizeMeters;
                Position = _dioramaHomePosition;
            }
            else if (key.PhysicalKeycode == Key.F5)
                _session!.SaveAndNotify();
        }
        else if (inputEvent is InputEventMouseButton button && button.Pressed)
        {
            if (button.ButtonIndex == MouseButton.WheelUp)
                SetClassicDioramaZoom(_dioramaTargetSizeMeters * CellPlayerNumericContracts.PresentationFloat0Point86f);
            else if (button.ButtonIndex == MouseButton.WheelDown)
                SetClassicDioramaZoom(_dioramaTargetSizeMeters / CellPlayerNumericContracts.PresentationFloat0Point86f);
        }
    }

    private void SetClassicDioramaZoom(float sizeMeters)
    {
        _dioramaTargetSizeMeters = Math.Clamp(
            sizeMeters,
            DioramaMinimumSizeMeters,
            DioramaMaximumSizeMeters);
    }

    private void UpdateClassicDioramaPan(float delta)
    {
        var input = ReadMovement();
        var forward = -_camera.GlobalBasis.Z;
        var right = _camera.GlobalBasis.X;
        forward.Y = 0.0f;
        right.Y = 0.0f;
        forward = forward.Normalized();
        right = right.Normalized();
        var direction = (right * input.X + forward * input.Y).Normalized();
        var zoomScale = _dioramaTargetSizeMeters / DioramaInitialSizeMeters;
        Position += direction * DioramaPanSpeedMetersPerSecond * zoomScale * delta;
    }

    private void BuildXrRig()
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
        _leftGrip = BuildController("LeftGrip", "left_hand", "grip");
        _rightGrip = BuildController("RightGrip", "right_hand", "grip");
        _leftAim = BuildController("LeftAim", "left_hand", "aim");
        _rightAim = BuildController("RightAim", "right_hand", "aim");
        _xrOrigin.AddChild(_leftGrip);
        _xrOrigin.AddChild(_rightGrip);
        _xrOrigin.AddChild(_leftAim);
        _xrOrigin.AddChild(_rightAim);
        _session!.AttachXrHud(_leftGrip, _leftAim);
    }

    internal void AttachFirstPersonRig(FirstPersonRig.Contract contract, float unitsToMeters)
    {
        if (_firstPersonRig is not null)
            throw new InvalidOperationException("OpenNV first-person rig is already attached.");
        Node3D leftAnchor = _useXr ? _leftGrip! : _camera;
        Node3D rightAnchor = _useXr ? _rightGrip! : _camera;
        _firstPersonRig = FirstPersonRig.Attach(
            contract,
            leftAnchor,
            rightAnchor,
            _useXr,
            unitsToMeters,
            _configuration);
    }

    internal void AttachHeldWeapon(
        Node3D weapon,
        string weaponFormId,
        float unitsToMeters,
        Vector3 muzzlePositionGodotUnits)
    {
        if (_firstPersonRig is null || _weaponMount is not null)
            throw new InvalidOperationException("Held weapon requires one configured first-person rig.");
        _weaponRestPosition = Vector3.Zero;
        Node3D weaponAnchor = _useXr ? _rightGrip! : _camera;
        var weaponBone = _firstPersonRig.Value.WeaponBoneWorld;
        _weaponMount = new Node3D
        {
            Name = "RetailWeaponBoneMount",
            Transform = weaponAnchor.GlobalTransform.AffineInverse() * new Transform3D(
                weaponBone.Basis.Orthonormalized(),
                weaponBone.Origin),
        };
        weaponAnchor.AddChild(_weaponMount);
        _weaponRestPosition = _weaponMount.Position;
        _heldWeaponFormId = FalloutFormId.Normalize(weaponFormId);
        weapon.Name = "HeldWeapon";
        weapon.Scale = Vector3.One * unitsToMeters;
        _weaponMount.AddChild(weapon);

        var flashMaterial = new StandardMaterial3D
        {
            AlbedoColor = _configuration.Xr.DiagnosticMuzzleFlash.AlbedoColorRgba.Color(),
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            EmissionEnabled = true,
            Emission = _configuration.Xr.DiagnosticMuzzleFlash.EmissionColorRgba.Color(),
            EmissionEnergyMultiplier = _configuration.Xr.DiagnosticMuzzleFlash.EmissionEnergy,
        };
        _muzzleFlash = new MeshInstance3D
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
        weapon.AddChild(_muzzleFlash);
        _muzzleLight = new OmniLight3D
        {
            Name = "MuzzleLight",
            Position = muzzlePositionGodotUnits,
            LightColor = _configuration.Xr.DiagnosticMuzzleFlash.LightColorRgba.Color(),
            LightEnergy = _configuration.Xr.DiagnosticMuzzleFlash.LightEnergy,
            OmniRange = _configuration.Xr.DiagnosticMuzzleFlash.LightRangeGameUnits * unitsToMeters,
            ShadowEnabled = false,
            Visible = false,
        };
        weapon.AddChild(_muzzleLight);
    }

    internal void SynchronizeHeldWeapon(string? equippedWeaponFormId)
    {
        if (_weaponMount?.FindChild("HeldWeapon", true, false) is not Node3D heldWeapon)
            return;
        heldWeapon.Visible = equippedWeaponFormId is not null &&
            _heldWeaponFormId!.Equals(
                FalloutFormId.Normalize(equippedWeaponFormId),
                StringComparison.OrdinalIgnoreCase);
    }

    private static XRController3D BuildController(string name, string tracker, string pose) => new()
    {
        Name = name,
        Tracker = tracker,
        Pose = pose,
    };

    private Vector2 ReadMovement()
    {
        if (!_movementEnabled || _session?.IsPipBoyOpen == true)
            return Vector2.Zero;
        if (_useXr)
        {
            var stick = _leftGrip!.GetVector2("move");
            return stick.Length() < _configuration.Xr.MovementDeadzone ? Vector2.Zero : stick;
        }
        var input = _configuration.Player.DesktopInput;
        return Input.GetVector(
            input.MoveLeft.Action,
            input.MoveRight.Action,
            input.MoveBackward.Action,
            input.MoveForward.Action);
    }

    private void PollDesktopActions()
    {
        var input = _configuration.Player.DesktopInput;
        if (_session!.IsPipBoyOpen)
        {
            DropHeldPickup();
            return;
        }
        if (_activationEnabled && _activePool is null &&
            Input.IsActionJustPressed(input.Grab.Action))
            _ = TryGrabPickup(_camera);
        if (_heldPickup is not null && Input.IsActionPressed(input.Grab.Action))
            UpdateHeldPickup(_camera);
        if (_heldPickup is not null && Input.IsActionJustReleased(input.Grab.Action))
            DropHeldPickup();
        if (_activationEnabled && Input.IsActionJustPressed(input.Activate.Action))
        {
            DesktopActivationEdges++;
            bool accepted;
            if (_activePool is null)
                accepted = Activate(_camera);
            else
            {
                ExitPool();
                accepted = true;
            }
            GD.Print($"OPENNV_FLAT_ACTION action=activate accepted={accepted}");
        }
        if (_combatEnabled && Input.IsActionJustPressed(input.Fire.Action))
        {
            var accepted = _activePool is not null
                ? _activePool.StrikeFlat(-_camera.GlobalBasis.Z)
                : _session!.Fire(_camera, CollisionMask);
            if (accepted && _activePool is null)
                _weaponFeedbackSeconds = _configuration.Xr.WeaponFeedbackSeconds;
            GD.Print($"OPENNV_FLAT_ACTION action=fire accepted={accepted}");
        }
        if (_combatEnabled && Input.IsActionJustPressed(input.Reload.Action))
        {
            bool accepted;
            if (_activePool is not null)
            {
                _activePool.ResetAuthored();
                accepted = true;
            }
            else
                accepted = _session!.Reload();
            GD.Print($"OPENNV_FLAT_ACTION action=reload accepted={accepted}");
        }
        if (_saveEnabled && Input.IsActionJustPressed(input.Save.Action))
        {
            _session!.SaveAndNotify();
            GD.Print("OPENNV_FLAT_ACTION action=save accepted=True");
        }
        if (Input.IsActionJustPressed(input.Cancel.Action))
        {
            if (_activePool is not null)
                ExitPool();
            Input.MouseMode = Input.MouseModeEnum.Visible;
        }
        if (_lookEnabled && Input.IsActionJustPressed(input.CaptureMouse.Action))
            Input.MouseMode = Input.MouseModeEnum.Captured;
        if (_activePool is not null && Input.IsActionJustPressed(input.PoolPowerUp.Action))
            _activePool.CycleFlatPower(1);
        if (_activePool is not null && Input.IsActionJustPressed(input.PoolPowerDown.Action))
            _activePool.CycleFlatPower(-1);
    }

    private void PollXrActions()
    {
        var turn = _lookEnabled ? _rightGrip!.GetVector2("turn").X : 0.0f;
        if (MathF.Abs(turn) >= _configuration.Xr.SnapTurnActivationThreshold && _xrSnapTurnReady)
        {
            SnapTurn(-MathF.Sign(turn) * Mathf.DegToRad(_configuration.Xr.SnapTurnDegrees));
            _xrSnapTurnReady = false;
        }
        else if (MathF.Abs(turn) < _configuration.Xr.SnapTurnResetThreshold)
        {
            _xrSnapTurnReady = true;
        }

        var activate = _activationEnabled &&
            _rightGrip!.GetFloat("activate") >= _configuration.Xr.ActionThreshold;
        if (activate && !_xrActivatePressed)
        {
            bool accepted;
            if (_activePool is null)
                accepted = Activate(_rightAim!);
            else
            {
                ExitPool();
                accepted = true;
            }
            _controlTelemetry.RecordActivation(accepted);
            GD.Print($"OPENNV_XR_ACTION action=activate accepted={accepted}");
        }
        _xrActivatePressed = activate;

        var grab = _activationEnabled && _activePool is null &&
            _rightGrip!.IsButtonPressed("grab");
        if (grab && !_xrGrabPressed)
            _ = TryGrabPickup(_rightAim!);
        if (grab && _heldPickup is not null)
            UpdateHeldPickup(_rightAim!);
        if (!grab && _xrGrabPressed)
            DropHeldPickup();
        _xrGrabPressed = grab;

        var fire = _combatEnabled &&
            _rightGrip!.GetFloat("fire") >= _configuration.Xr.ActionThreshold;
        if (fire && !_xrFirePressed)
        {
            var accepted = _activePool is null && _session!.Fire(_rightAim!, CollisionMask);
            _controlTelemetry.RecordFire(accepted);
            if (accepted)
            {
                _weaponFeedbackSeconds = _configuration.Xr.WeaponFeedbackSeconds;
                TriggerHaptic(_configuration.Xr.FireHaptic);
            }
            GD.Print($"OPENNV_XR_ACTION action=fire accepted={accepted}");
        }
        _xrFirePressed = fire;

        var save = _saveEnabled && _leftGrip!.IsButtonPressed("save");
        if (save && !_xrSavePressed)
        {
            _session!.SaveAndNotify();
            _controlTelemetry.RecordSave();
            GD.Print("OPENNV_XR_ACTION action=save accepted=True");
        }
        _xrSavePressed = save;

        var reload = _combatEnabled && _rightGrip!.IsButtonPressed("reload");
        if (reload && !_xrReloadPressed)
        {
            bool accepted;
            if (_activePool is not null)
            {
                _activePool.ResetAuthored();
                accepted = true;
            }
            else
                accepted = _session!.Reload();
            _controlTelemetry.RecordReload(accepted);
            if (accepted)
                TriggerHaptic(_configuration.Xr.ReloadHaptic);
            GD.Print($"OPENNV_XR_ACTION action=reload accepted={accepted}");
        }
        _xrReloadPressed = reload;
    }

    private void UpdateXrCalibrationAndHealth()
    {
        if (_leftGrip!.GetHasTrackingData() && _rightGrip!.GetHasTrackingData())
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
            $"leftActive={_leftGrip!.GetIsActive()} leftTracked={_leftGrip.GetHasTrackingData()} " +
            $"rightActive={_rightGrip!.GetIsActive()} rightTracked={_rightGrip.GetHasTrackingData()} " +
            $"bodyY={GlobalPosition.Y:F3} grounded={IsOnFloor()} eyeY={_camera.GlobalPosition.Y:F3} " +
            $"floorY={(_xrLastFloorY is { } floor ? floor.ToString("F3") : "missing")} " +
            $"relativeEye={(_xrLastFloorY is { } relativeFloor ? (_camera.GlobalPosition.Y - relativeFloor).ToString("F3") : "missing")} " +
            $"move={_leftGrip.GetVector2("move")} turn={_rightGrip.GetVector2("turn")} " +
            $"grip={_rightGrip.GetFloat("activate"):F2} trigger={_rightGrip.GetFloat("fire"):F2}");
    }

    private void UpdateXrControlTelemetry()
    {
        _xrLastFloorY = ProbeFloorY();
        var acceptance = _configuration.Xr.SimulatorAcceptance;
        var floorSupportsBody = _xrLastFloorY is { } floor && IsOnFloor() &&
            MathF.Abs(
                GlobalPosition.Y - _configuration.Player.SpawnCenterHeightMeters - floor) <=
            acceptance.EyeHeightToleranceMeters;
        _controlTelemetry.Observe(
            GlobalPosition,
            _leftGrip!.GlobalPosition,
            _rightGrip!.GlobalPosition,
            _leftGrip.GetVector2("move"),
            _rightGrip.GetVector2("turn"),
            _leftGrip.GetHasTrackingData() && _rightGrip.GetHasTrackingData(),
            floorSupportsBody,
            _camera.GlobalPosition.Y,
            _xrLastFloorY,
            _configuration.Xr.DesiredEyeHeightMeters);
    }

    private float? ProbeFloorY()
    {
        var acceptance = _configuration.Xr.SimulatorAcceptance;
        var from = _camera.GlobalPosition + Vector3.Up * acceptance.FloorProbeAboveEyeMeters;
        var to = from - Vector3.Up * acceptance.FloorProbeDistanceMeters;
        var query = PhysicsRayQueryParameters3D.Create(from, to, CollisionMask);
        query.Exclude = new Godot.Collections.Array<Rid> { GetRid() };
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
        return hit.Count == 0 ? null : hit["position"].AsVector3().Y;
    }

    private void UpdateWeaponFeedback(float delta)
    {
        if (_weaponMount is null || _muzzleFlash is null || _muzzleLight is null)
            return;
        _weaponFeedbackSeconds = MathF.Max(0.0f, _weaponFeedbackSeconds - delta);
        var strength = _weaponFeedbackSeconds / _configuration.Xr.WeaponFeedbackSeconds;
        _weaponMount.Position =
            _weaponRestPosition + Vector3.Back * (_configuration.Xr.WeaponRecoilMeters * strength);
        var flashVisible = _weaponFeedbackSeconds > _configuration.Xr.MuzzleFlashVisibleSeconds;
        _muzzleFlash.Visible = flashVisible;
        _muzzleLight.Visible = flashVisible;
    }

    private void EnterPool(PoolTableInstance table)
    {
        if (_activePool == table)
            return;
        if (_activePool is not null)
            ExitPool();
        DropHeldPickup();
        _activePool = table;
        table.SetPlayActive(true);
        if (_weaponMount is not null)
            _weaponMount.Visible = false;
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
        Node3D cueParent = _useXr ? _rightGrip! : _camera;
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

    internal bool BeginPickupHoldForProof(PickupInstance pickup)
    {
        if (_heldPickup is not null || !pickup.BeginHold())
            return false;
        _heldPickup = pickup;
        return true;
    }

    internal void MoveHeldPickupForProof(Vector3 targetGlobalPosition) =>
        _heldPickup?.MoveHeld(targetGlobalPosition);

    internal void DropHeldPickupForProof() => DropHeldPickup();

    private void ExitPool()
    {
        if (_activePool is null)
            return;
        _activePool.SetPlayActive(false);
        _activePool = null;
        _poolCueMount?.QueueFree();
        _poolCueMount = null;
        _poolCueTip = null;
        if (_weaponMount is not null)
            _weaponMount.Visible = true;
        _session!.Save();
    }

    private void UpdateTrackedPoolCue(double delta)
    {
        if (_activePool is null || _poolCueTip is null)
            return;
        if (_activePool.UpdateTrackedCue(_poolCueTip.GlobalPosition, _xrFirePressed, delta))
            TriggerHaptic(_configuration.Pool.StrikeHaptic);
    }

    private bool TryGrabPickup(Node3D aimSource)
    {
        if (_heldPickup is not null)
            return true;
        var collider = Cast(aimSource, _configuration.Player.ActivationDistanceMeters);
        var pickup = Ancestor<PickupInstance>(collider);
        if (pickup is null || !pickup.BeginHold())
            return false;
        _heldPickup = pickup;
        UpdateHeldPickup(aimSource);
        _session!.Notify(
            pickup is ScriptedActivatorInstance
                ? $"Holding {pickup.DisplayName ?? pickup.EditorId} • release to drop"
                : $"Holding {pickup.DisplayName ?? pickup.EditorId} • release to drop • E to take");
        return true;
    }

    private void UpdateHeldPickup(Node3D aimSource)
    {
        _heldPickup?.MoveHeld(
            aimSource.GlobalPosition -
            aimSource.GlobalBasis.Z * _configuration.Pickup.HoldDistanceMeters);
    }

    private void DropHeldPickup()
    {
        if (_heldPickup is null)
            return;
        var dropped = _heldPickup;
        _heldPickup = null;
        dropped.Drop();
        if (dropped is not ScriptedActivatorInstance)
            _session!.PickupMoved(dropped);
    }

    private void TriggerHaptic(HapticConfiguration haptic)
    {
        _rightGrip!.TriggerHapticPulse(
            "haptic",
            haptic.Frequency,
            haptic.Amplitude,
            haptic.DurationSeconds,
            haptic.DelaySeconds);
    }

    private void SnapTurn(float radians)
    {
        var headBefore = _camera.GlobalPosition;
        var headOffset = _camera.GlobalPosition - GlobalPosition;
        headOffset.Y = 0.0f;
        RotateY(radians);
        GlobalPosition += headOffset - headOffset.Rotated(Vector3.Up, radians);
        var pivotError = _camera.GlobalPosition - headBefore;
        pivotError.Y = 0.0f;
        _controlTelemetry.RecordSnapTurn(pivotError.Length());
        GD.Print($"OPENNV_XR_ACTION action=snap-turn accepted=True pivotError={pivotError.Length():F6}");
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
