using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.InputSystem;

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
    private bool _requestedModalInput;
    private int _modalInputLeases;
    private Transform3D? _sourceCamera;
    private float _sourceScale = 1;
    internal void ApplySourceScale(float scale)
    {
        if (_sourceScale == scale && Scale.IsEqualApprox(Vector3.One * scale)) return;
        if (FalloutScriptSession.PlayerScaleValue(scale) != scale) throw new InvalidDataException("Player presentation scale is not source-valid.");
        _sourceScale = scale;
        Scale = Vector3.One * scale;
    }
    private float _jumpHeightMeters;
    private NativeCharacterStep.QueryResources? _stepQueries;
    private FalloutInputControls? _inputControls;
    private FalloutFlatMouseLook? _flatMouseLook;
    internal FalloutMouseLookScale FlatMouseLookScale => _flatMouseLook?.Resolve(
        new FalloutCameraProjection(_camera.Fov, _camera.Near / UnitsToMeters).ReferenceHorizontalFovDegrees)
        ?? throw new InvalidOperationException("Flat mouse look has no owned settings.");

    internal void ConfigureInputControls(FalloutInputControls controls, Func<int, bool> physicalKeyPressed)
    {
        if (_inputControls is not null) throw new InvalidOperationException("Player input controls already have an owner.");
        _inputControls = controls;
        var adapter = new RuntimeNativeInputControls(controls, _configuration.Player.DesktopInput, physicalKeyPressed);
        adapter.Remapped += changed =>
        {
            if (changed.Contains(27)) ReleaseGrab();
            if (changed.Any(index => index is 4 or 6 or 7))
            {
                _reloadPressed = false; _weaponTriggerHeld = false; _pendingShotCount = 0;
                _holdHandled = false; _aiming = false; CancelWeaponAction();
            }
        };
        AddChild(adapter);
    }
    internal bool Running { get; private set; }
    internal int JumpCount { get; private set; }
    internal int StepCount { get; private set; }
    internal string? StepBlocked { get; private set; }
    internal string? BlockingShape { get; private set; }
    internal bool CollisionResident { get; private set; } = true;
    internal object[] CollisionContacts => Enumerable.Range(0, GetSlideCollisionCount()).Select(index =>
    {
        var contact = GetSlideCollision(index);
        var normal = contact.GetNormal();
        var body = contact.GetCollider() as Node3D;
        static float[] Point(Vector3 value) => [value.X, value.Y, value.Z];
        return (object)new
        {
            body = body?.GetPath().ToString(),
            normal = Point(normal),
            position = Point(contact.GetPosition()),
            depth = contact.GetDepth(),
            colliderPosition = body is null ? null : Point(body.GlobalPosition),
            colliderScale = body is null ? null : Point(body.GlobalBasis.Scale),
            colliderType = body?.GetType().Name,
            colliderShape = (contact.GetColliderShape() as Node)?.Name.ToString(),
            rigidVelocity = body is RigidBody3D rigid ? Point(rigid.LinearVelocity) : null,
        };
    }).ToArray();
    internal Func<Vector3, bool>? CanOccupyPosition { get; set; }
    internal Func<bool>? IsDefeated { get; set; }
    internal float CombatRadius => _configuration.Player.CapsuleRadiusMeters;
    internal Vector3 CombatTargetPoint => SourceCombatTargetPoint();

    internal void ConfigureLocomotion(FalloutPluginStack records, Func<GameplayVitals?> vitals)
    {
        _grabRecords = records;
        _jumpHeightMeters = checked((float)FalloutGameSettingFloats.ReadRetained(records, "fJumpHeightMin", nameof(RuntimeNativePlayer))) * UnitsToMeters;
        if (!float.IsFinite(_jumpHeightMeters) || _jumpHeightMeters <= 0)
            throw new InvalidDataException("Source jump height must be finite and positive.");
        ConfigureLimbLocomotion(records, vitals);
        ConfigureFlatMouseLook(records.IniSettings);
        _inputControls?.ConfigureAlwaysRun(records.IniSettings.Boolean("bAlwaysRunByDefault:Controls"));
        SetMeta("opennv_jump_height_meters", _jumpHeightMeters);
        SetMeta("opennv_run_policy", "Owned always-run preference XOR remappable Run control");
    }

    internal Camera3D Camera => _camera;
    internal void ConfigureFlatMouseLook(FalloutNumericIniSettings settings) =>
        _flatMouseLook = new(settings,
            new FalloutCameraProjection(_camera.Fov, _camera.Near / UnitsToMeters).ReferenceHorizontalFovDegrees);
    internal float ViewPitchRadians => _pitchRadians;
    internal FalloutActorActivityState Activity { get; } = new();
    internal bool RolloverTextEnabled { get; private set; } = true;
    internal FalloutPlayerControlState SourceControls { get; private set; } = FalloutPlayerControlState.AllEnabled;
    internal float UnitsToMeters => _configuration.World.GameUnitsToMeters;
    internal Func<Node, bool>? ActivateReference { get; set; }
    internal Action? NoActivationFeedback { get; set; }
    internal Action? SaveGame { get; set; }
    internal Action? LoadGame { get; set; }

    internal void ApplySourceCamera(Transform3D transformFromFeet)
    {
        _sourceCamera = transformFromFeet;
        PublishSelfCameraVisibility();
        if (_xr is not null) return;
        _camera.Transform = transformFromFeet * new Transform3D(new Basis(Vector3.Right, _pitchRadians), Vector3.Zero);
        PublishDialogueView();
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
        PublishSelfCameraVisibility();
        if (_xr is not null) return;
        _camera.Transform = new Transform3D(new Basis(Vector3.Right, _pitchRadians),
            Vector3.Up * _configuration.Player.SpawnCenterHeightMeters + _configuration.Player.DesktopCameraOffsetMeters.Vector3());
    }

    internal void SetModalInput(bool modal)
    {
        _requestedModalInput = modal;
        ApplyModalInput(modal || _modalInputLeases > 0);
    }

    internal Action AcquireModalInput()
    {
        _modalInputLeases++;
        try { ApplyModalInput(true); }
        catch
        {
            _modalInputLeases--;
            _modalInput = _requestedModalInput || _modalInputLeases > 0;
            throw;
        }
        var released = false;
        return () =>
        {
            if (released) return;
            released = true;
            _modalInputLeases--;
            ApplyModalInput(_requestedModalInput || _modalInputLeases > 0);
        };
    }

    internal Action AcquirePausedSaveInput()
    {
        if (!IsInsideTree() || !GetTree().Paused)
            throw new InvalidOperationException("Save input protection requires an already paused native world.");
        _modalInputLeases++;
        _modalInput = true;
        var released = false;
        return () =>
        {
            if (released) return;
            released = true;
            _modalInputLeases--;
            // A save pause must not cancel an authoritative action or velocity.
            _modalInput = _requestedModalInput || _modalInputLeases > 0;
        };
    }

    private void ApplyModalInput(bool modal)
    {
        _modalInput = modal;
        if (modal)
        {
            ReleaseGrab();
            _weaponTriggerHeld = false;
            _pendingShotCount = 0;
            Velocity = Vector3.Zero;
            _previousKnockbackVertical = 0;
            _aiming = false;
            _reloadPressed = false; _holdHandled = false; CancelWeaponAction();
            if (_firstPersonPixels is not null) _firstPersonPixels.Visible = false;
        }
    }

    internal void ApplySourceControls(FalloutPlayerControlState state)
    {
        SourceControls = state;
        _movementEnabled = state.Movement;
        _lookingEnabled = state.Looking;
        _activationEnabled = state.Movement;
        if (!_activationEnabled) ReleaseGrab();
        RolloverTextEnabled = state.RolloverText;
        if (!_movementEnabled)
        {
            Velocity = Vector3.Zero;
            _previousKnockbackVertical = 0;
        }
        SetMeta("opennv_source_movement_enabled", state.Movement);
        SetMeta("opennv_source_pipboy_enabled", state.PipBoy);
        SetMeta("opennv_source_fighting_enabled", state.Fighting);
        SetMeta("opennv_source_pointofview_enabled", state.PointOfView);
        if (!state.Fighting) { _reloadPressed = false; _weaponTriggerHeld = false; _pendingShotCount = 0; CancelWeaponAction(); }
        SetMeta("opennv_source_looking_enabled", state.Looking);
    }

    internal void Configure(RuntimeConfiguration configuration, Transform3D authoredFloorTransform)
        => Configure(configuration, authoredFloorTransform,
            FalloutCameraProjection.Read(FalloutInstallationSettings.Read(RuntimeLiveContentSource.Current!)));

    internal void Configure(RuntimeConfiguration configuration, Transform3D authoredFloorTransform, FalloutCameraProjection projection)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (!float.IsFinite(projection.VerticalFovDegrees) || projection.VerticalFovDegrees is <= 0 or >= 180 ||
            !float.IsFinite(projection.NearGameUnits) || projection.NearGameUnits <= 0)
            throw new InvalidDataException("Native player projection is invalid.");
        _configuration = configuration;
        Name = "NativePlayer";
        MotionMode = MotionModeEnum.Grounded;
        CollisionLayer = configuration.Player.CollisionLayer;
        CollisionMask = configuration.Player.CollisionMask;
        FloorSnapLength = configuration.Player.CapsuleRadiusMeters;
        FloorMaxAngle = Mathf.DegToRad(configuration.Player.MaximumWalkableSlopeDegrees);
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
        ConfigureSelfCameraVisibility();
        AddChild(_camera);
        GD.Print($"OPENNV_NATIVE_CAMERA_PROJECTION verticalFov={projection.VerticalFovDegrees:R} " +
            $"nearGameUnits={projection.NearGameUnits:R} source=caller-projection referenceAspect=4:3 " +
            "farClip=unverified matchedFrame=unverified");
        Teleport(authoredFloorTransform);
        SetMeta("opennv_source", "live-retail-files");
        SetMeta("opennv_content_source", "live-owned-files");
    }

    internal void Teleport(Transform3D authoredFloorTransform)
    {
        ReleaseGrab();
        if (_furniturePhase != 0) throw new NotSupportedException("Teleporting an active furniture interaction needs interruption/phase restoration.");
        if (_configuration is null)
            throw new InvalidOperationException("Native player is not configured.");
        var basis = authoredFloorTransform.Basis.Orthonormalized().Scaled(Vector3.One * _sourceScale);
        GlobalTransform = new Transform3D(
            basis,
            authoredFloorTransform.Origin);
        _pitchRadians = 0.0f;
        if (_xr is null) _camera.Rotation = Vector3.Zero;
        Velocity = Vector3.Zero;
        _previousKnockbackVertical = 0;
        _xrBodyAlignmentPending = _xr is not null;
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
            new Basis(quaternion).Scaled(Vector3.One * _sourceScale),
            new Vector3(position[0], position[1], position[2]));
        _pitchRadians = viewPitchRadians;
        if (_xr is null) _camera.Rotation = new Vector3(_pitchRadians, 0, 0);
        Velocity = Vector3.Zero;
        _previousKnockbackVertical = 0;
        _xrBodyAlignmentPending = _xr is not null;
    }

    public override void _Ready()
    {
        if (DisplayServer.GetName() != "headless")
            Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    public override void _UnhandledInput(InputEvent inputEvent)
    {
        ObserveSaveInput(inputEvent, "unhandled");
        if (VatsInput(inputEvent)) { GetViewport().SetInputAsHandled(); return; }
        if (InventoryHotkeyInput(inputEvent)) { GetViewport().SetInputAsHandled(); return; }
        if (_xr is not null) return;
        var input = _configuration.Player.DesktopInput;
        if (_inputControls is not null && !_modalInput && _movementEnabled && _furniturePhase == 0 &&
            inputEvent.IsActionPressed(RuntimeNativeInputControls.Action(10), allowEcho: false))
        {
            _inputControls.ToggleAlwaysRun();
            GetViewport().SetInputAsHandled();
            return;
        }
        if (GrabInput(inputEvent)) { GetViewport().SetInputAsHandled(); return; }
        if (_inputControls is null && !_modalInput && _movementEnabled && _furniturePhase == 0 && _sourceCamera is null &&
            inputEvent is InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.Q or Key.H } wheel)
        {
            OpenCombatWheel?.Invoke(wheel.PhysicalKeycode == Key.H);
            GetViewport().SetInputAsHandled(); return;
        }
        if (PresentationInput(inputEvent)) { GetViewport().SetInputAsHandled(); return; }
        if (!_modalInput && _furniturePhase == 0 && GetMeta("opennv_source_pipboy_enabled", false).AsBool() &&
            inputEvent is InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.F1 or Key.F2 or Key.F3 } pageKey)
        {
            OpenPipBoyPage?.Invoke((OpenNV.Runtime.Gameplay.State.FalloutPipBoyPage)(pageKey.PhysicalKeycode - Key.F1));
            GetViewport().SetInputAsHandled(); return;
        }
        if (!_modalInput && _furniturePhase == 0 && GetMeta("opennv_source_pipboy_enabled", false).AsBool() && inputEvent.IsActionPressed(input.PipBoy.Action))
        {
            OpenPipBoy?.Invoke(); GetViewport().SetInputAsHandled(); return;
        }
        if (!_modalInput && _furniturePhase == 0 && inputEvent.IsActionPressed(input.Save.Action))
        {
            ObserveSaveInput(inputEvent, "dispatch");
            SaveGame?.Invoke();
            ObserveSaveInput(inputEvent, "returned");
            GetViewport().SetInputAsHandled();
            return;
        }
        if (_inputControls is not null && !_modalInput && _furniturePhase == 0 &&
            inputEvent.IsActionPressed(RuntimeNativeInputControls.Action(26), allowEcho: false))
        {
            LoadGame?.Invoke();
            GetViewport().SetInputAsHandled();
            return;
        }
        // A modal owns Cancel itself. Consuming it here while modal blocks
        // later unhandled-input owners, including the source recipe menu.
        if (!_modalInput && inputEvent.IsActionPressed(input.Cancel.Action))
        {
            OpenPauseMenu?.Invoke();
            GetViewport().SetInputAsHandled();
            return;
        }
        if (inputEvent.IsActionPressed(input.CaptureMouse.Action))
        {
            Input.MouseMode = Input.MouseModeEnum.Captured;
            GetViewport().SetInputAsHandled();
            return;
        }
        if (!_modalInput && inputEvent.IsActionPressed(input.Activate.Action))
        {
            if (!_activationEnabled)
            {
                RejectActivation(); GetViewport().SetInputAsHandled(); return;
            }
            if (_furniturePhase != 0)
            {
                RequestFurnitureExit();
                GetViewport().SetInputAsHandled();
                return;
            }
            var accepted = TryActivateLiveObject();
            if (!accepted) RejectActivation();
            GD.Print($"OPENNV_NATIVE_PLAYER_ACTION action=activate accepted={accepted}");
            GetViewport().SetInputAsHandled();
            return;
        }
        if (_modalInput || !_lookingEnabled ||
            inputEvent is not InputEventMouseMotion motion)
            return;
        if (Input.MouseMode != Input.MouseModeEnum.Captured &&
            !OpenNV.Runtime.Diagnostics.Parity.RuntimeLiveHarness.IsHeadlessLookInput(motion)) return;
        if (_furniturePhase is 1 or 2 or 4) return;
        var look = FlatMouseLookScale;
        if (_furniturePhase == 3) _furnitureLookYaw -= motion.Relative.X * look.Horizontal;
        else RotateY(-motion.Relative.X * look.Horizontal);
        _pitchRadians = Math.Clamp(
            _pitchRadians - motion.Relative.Y *
                look.Vertical,
            -_configuration.Player.VerticalLookLimitRadians,
            _configuration.Player.VerticalLookLimitRadians);
        if (_furniturePhase == 3) PublishFurnitureCamera();
        else if (_sourceCamera is { } authored) ApplySourceCamera(authored);
        else _camera.Rotation = new Vector3(_pitchRadians, 0.0f, 0.0f);
    }

    internal event Action? OpenPipBoy;
    internal event Action<OpenNV.Runtime.Gameplay.State.FalloutPipBoyPage>? OpenPipBoyPage;
    internal event Action<bool>? OpenCombatWheel;
    internal event Action? OpenPauseMenu;

    public override void _PhysicsProcess(double delta)
    {
        using var frameWork = OpenNV.Runtime.Diagnostics.RuntimeFrameWork.Measure(OpenNV.Runtime.Diagnostics.RuntimeFrameWork.Owner.PlayerPhysics);
        PublishXrPointer();
        AdvanceGrab();
        if (_xr is not null && GetTree().Paused) return;
        AdvanceExplosionExposure(delta);
        if (_furniturePhase != 0) { _ = AdvanceHitKnockback(delta); AdvanceFurniture(delta); return; }
        var input = _configuration.Player.DesktopInput;
        var alive = IsDefeated?.Invoke() != true;
        var movement = alive && _movementEnabled && !_modalInput && !VatsExecuting
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
        var requestedRunning = _xr is not null ? !_xr.Sprint : _inputControls is not null
            ? _inputControls.AlwaysRun ^ Input.IsActionPressed(RuntimeNativeInputControls.Action(9))
            : !Input.IsActionPressed(input.Sprint.Action);
        Running = movement.LengthSquared() > 0 && requestedRunning;
        var speed = movement.LengthSquared() > 0 ? MovementSpeed(Running) : 0;
        Activity.SetMovement(Running, Activity.Sneaking);
        var knockback = AdvanceHitKnockback(delta);
        var velocity = direction * speed;
        velocity.Y = IsOnFloor()
            ? MathF.Min(Velocity.Y, 0.0f)
            : NativeCharacterGravity.AirVelocity(Velocity.Y - _previousKnockbackVertical,
                _configuration.Simulation.GravityMetersPerSecondSquared, (float)delta);
        if (alive && _movementEnabled && !_modalInput && !VatsExecuting && IsOnFloor() && _jumpHeightMeters > 0 &&
            (_xr?.ConsumeJump() ?? Input.IsActionJustPressed(input.Jump.Action)))
        {
            velocity.Y = MathF.Sqrt(2 * _configuration.Simulation.GravityMetersPerSecondSquared * _jumpHeightMeters);
            ++JumpCount;
            GD.Print($"OPENNV_NATIVE_PLAYER_JUMP heightMeters={_jumpHeightMeters:R} count={JumpCount}");
        }
        velocity += knockback;
        _previousKnockbackVertical = knockback.Y;
        var horizontalMotion = new Vector3(velocity.X, 0, velocity.Z) * (float)delta;
        CollisionResident = CanOccupyPosition?.Invoke(GlobalPosition + horizontalMotion) != false;
        if (!CollisionResident)
        {
            velocity.X = velocity.Z = 0;
            horizontalMotion = Vector3.Zero;
        }
        Velocity = velocity;
        var positionBeforeMotion = GlobalPosition;
        var stepped = NativeCharacterStep.TryStep(this, horizontalMotion, _configuration.Player.StepHeightMeters, out var stepBlocked,
            _stepQueries ??= new(this));
        StepBlocked = stepBlocked;
        if (stepped)
        {
            ++StepCount;
            Velocity = Vector3.Down * 0.01f;
            MoveAndSlide();
            Velocity = new(velocity.X, 0, velocity.Z);
            _previousKnockbackVertical = 0;
        }
        else MoveAndSlide();
        // At a convex edge the solver can stop downward travel while reporting
        // a side normal. Keep the constrained velocity instead of accumulating
        // an unbounded fall that prevents horizontal input from escaping.
        if (delta > 0 && Velocity.Y < 0 && GetSlideCollisionCount() > 0)
        {
            var actualVerticalSpeed = Math.Min(0, (GlobalPosition.Y - positionBeforeMotion.Y) / (float)delta);
            if (actualVerticalSpeed > Velocity.Y + .01f)
            {
                Velocity = new(Velocity.X, actualVerticalSpeed, Velocity.Z);
                _previousKnockbackVertical = 0;
            }
        }
        if (IsOnFloor() || IsOnCeiling()) _previousKnockbackVertical = 0;
        PushContactProps(velocity, (float)delta);
        BlockingShape = IsOnWall() && GetSlideCollisionCount() > 0
            ? (GetSlideCollision(GetSlideCollisionCount() - 1).GetCollider() as Node)?.GetPath().ToString() : null;
    }

    private void PushContactProps(Vector3 desiredVelocity, float delta)
    {
        if (delta <= 0 || delta > .05f) return;
        var pushed = new HashSet<ulong>();
        for (var index = 0; index < GetSlideCollisionCount(); index++)
        {
            var contact = GetSlideCollision(index);
            if (contact.GetCollider() is not RuntimeNifRigidBody { Freeze: false } body ||
                !pushed.Add(body.GetInstanceId())) continue;
            var normal = contact.GetNormal();
            if (Math.Abs(normal.Y) > .75f) continue;
            var closingSpeed = Math.Max(0, (desiredVelocity - body.LinearVelocity).Dot(-normal));
            // CharacterBody movement resolves the player but supplies no prop
            // impulse. Both presentation modes use this bounded contact force,
            // while the source mass, friction and restitution govern the prop.
            var impulse = Math.Min(body.Mass * closingSpeed, 120 * delta);
            if (impulse > 0) body.ApplyImpulse(-normal * impulse, contact.GetPosition() - body.GlobalPosition);
        }
    }

    private bool TryActivateLiveObject()
    {
        var collider = ActivationObject();
        return collider is not null && ActivateReference?.Invoke(collider) == true;
    }

    private void RejectActivation() =>
        (NoActivationFeedback ?? throw new NotSupportedException("Native activation feedback has no shared sound owner."))();

    internal bool ModalInput => _modalInput;
    internal object InputState => new
    {
        alive = IsDefeated?.Invoke() != true,
        sneaking = Activity.Sneaking,
        hitKnockback = _hitKnockback,
        sneakingBoundary = "source-activity-movement-and-shot-modifiers;camera-capsule-and-body-posture-unbound",
        forward = Input.GetActionStrength(_configuration.Player.DesktopInput.MoveForward.Action),
        backward = Input.GetActionStrength(_configuration.Player.DesktopInput.MoveBackward.Action),
        left = Input.GetActionStrength(_configuration.Player.DesktopInput.MoveLeft.Action),
        right = Input.GetActionStrength(_configuration.Player.DesktopInput.MoveRight.Action),
        controls = _inputControls?.State,
        mouseLook = _flatMouseLook is null ? null : new
        {
            sourceSensitivity = _flatMouseLook.Sensitivity,
            invertY = _flatMouseLook.InvertY,
            defaultReferenceFov = _flatMouseLook.DefaultReferenceFov,
            scale = FlatMouseLookScale,
        },
        grab = GrabState,
        saveControl = SaveInputState,
    };
    internal Node? AimedObject()
    {
        var collider = TraceActivation(out _aimHit);
        return collider;
    }

    internal Vector3 ActivationOrigin => ((Node3D?)_xr?.RightAim ?? _camera).GlobalPosition;

    internal Node? ActivationObject()
    {
        var collider = AimedObject();
        if (collider is null || _xr is null) return collider;
        // The long visual pointer may reach ordinary references, but only
        // source-admitted remote pickups inherit its extended activation reach.
        return ResolveXrTarget?.Invoke(collider)?.RemotePickup == true ||
            ActivationOrigin.DistanceTo(_aimHit) <= _configuration.Player.ActivationDistanceMeters ? collider : null;
    }

    internal object ObserveActivation(Func<Node, object?> reference)
    {
        var collider = TraceActivation(out var point);
        var origin = ActivationOrigin;
        return new
        {
            enabled = _activationEnabled,
            collider = collider?.GetPath().ToString(),
            reference = collider is null ? null : reference(collider),
            origin = new[] { origin.X, origin.Y, origin.Z },
            hitPosition = new[] { point.X, point.Y, point.Z },
            distance = origin.DistanceTo(point),
            activationDistance = _configuration.Player.ActivationDistanceMeters,
        };
    }

    private Node? TraceActivation(out Vector3 hitPosition)
    {
        var aim = (Node3D?)_xr?.RightAim ?? _camera;
        var from = aim.GlobalPosition;
        var to = from - aim.GlobalBasis.Z * (_xr?.WorldPointer == true ? 12f : _configuration.Player.ActivationDistanceMeters);
        hitPosition = to;
        using var query = PhysicsRayQueryParameters3D.Create(from, to, CollisionMask | CollisionLayer);
        query.CollideWithAreas = true;
        query.Exclude = SelfQueryBodies;
        query.HitBackFaces = false;
        using var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (hit.TryGetValue("position", out var point)) hitPosition = point.AsVector3();
        if (!hit.TryGetValue("collider", out var colliderValue) ||
            colliderValue.AsGodotObject() is not Node collider)
            return null;
        return collider;
    }
}
