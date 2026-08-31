using System.Text.Json;
using Godot;


namespace OpenNV.Runtime.Campaigns.Fallout1;

internal readonly record struct Fo1CameraSaveState(
    string Mode,
    float YawRadians,
    float PitchRadians,
    float TacticalZoomMeters,
    float ShoulderDistanceMeters)
{
    private const string Schema = "opennv-fo1-camera-state/v1";

    internal static Fo1CameraSaveState Load(JsonElement source)
    {
        if (source.GetProperty("schema").GetString() != Schema)
            throw new InvalidOperationException(
                "Fallout 1 save contains an unknown camera-state schema.");
        return new Fo1CameraSaveState(
            source.GetProperty("mode").GetString() ?? string.Empty,
            source.GetProperty("yawRadians").GetSingle(),
            source.GetProperty("pitchRadians").GetSingle(),
            source.GetProperty("tacticalZoomMeters").GetSingle(),
            source.GetProperty("shoulderDistanceMeters").GetSingle());
    }

    internal object SaveState() => new
    {
        schema = Schema,
        mode = Mode,
        yawRadians = YawRadians,
        pitchRadians = PitchRadians,
        tacticalZoomMeters = TacticalZoomMeters,
        shoulderDistanceMeters = ShoulderDistanceMeters,
    };
}

internal static class Fo1TacticalCameraNumericContracts
{
    // Immutable format, source-art, geometry, and acceptance contracts.
    // Runtime-tunable Fallout 1 behavior remains in the versioned runtime recipe.
    internal const float PresentationFloat0Point0001f = 0.0001f;
    internal const float PresentationFloat0Point001f = 0.001f;
    internal const float PresentationFloat0Point5f = 0.5f;
    internal const float PresentationFloat1Point0ENEgativE5f = 1.0e-5f;
}

internal partial class Fo1TacticalCamera : Node3D
{
    private Fo1TacticalSession _session = null!;
    private Fo1CameraProfile _profile = null!;
    private Node3D _yawPivot = null!;
    private Node3D _pitchPivot = null!;
    private Camera3D _camera = null!;
    private Vector3 _homeFocus;
    private float _homeSize;
    private float _homeYaw;
    private float _homePitch;
    private float _targetSize;
    private float _targetYaw;
    private float _targetPitch;
    private bool _orbitDragging;
    private bool _panDragging;
    private bool _explorationMode;
    private bool _firstPersonMode;
    private float _explorationDistance;
    private Vector3 _tacticalFocusBeforeExploration;
    private float _tacticalSizeBeforeExploration;
    private float _tacticalYawBeforeExploration;
    private float _tacticalPitchBeforeExploration;
    private Fo1CaveCutaway? _caveCutaway;

    internal Camera3D Camera => _camera;
    internal Node3D YawPivot => _yawPivot;
    internal Node3D PitchPivot => _pitchPivot;
    internal float TargetSizeMeters => _targetSize;
    internal float TargetYawRadians => _targetYaw;
    internal float TargetPitchRadians => _targetPitch;
    internal bool OrbitDragging => _orbitDragging;
    internal bool PanDragging => _panDragging;
    internal bool ExplorationMode => _explorationMode;
    internal bool FirstPersonMode => _firstPersonMode;
    internal Vector3 FirstPersonEyePosition => _camera.GlobalPosition;
    internal Vector3 FirstPersonForward => -_camera.GlobalBasis.Z;
    internal float FirstPersonEyeHeightMeters => _profile.FirstPerson.EyeHeightMeters;
    internal float FirstPersonFovDegrees => _profile.FirstPerson.FovDegrees;
    internal float FirstPersonMoveSpeedMetersPerSecond => _profile.FirstPerson.MoveSpeedMetersPerSecond;

    internal Fo1CameraSaveState CaptureSaveState()
    {
        var result = new Fo1CameraSaveState(
            _firstPersonMode
                ? "first-person"
                : _explorationMode
                    ? "shoulder"
                    : "hex-tactical",
            _targetYaw,
            _targetPitch,
            _targetSize,
            _explorationDistance);
        ValidateSaveState(result);
        return result;
    }

    internal void ValidateSaveState(Fo1CameraSaveState state)
    {
        if (state.Mode is not ("hex-tactical" or "shoulder" or "first-person") ||
            !float.IsFinite(state.YawRadians) ||
            !float.IsFinite(state.PitchRadians) ||
            !float.IsFinite(state.TacticalZoomMeters) ||
            !float.IsFinite(state.ShoulderDistanceMeters) ||
            state.TacticalZoomMeters < _profile.Tactical.MinimumSizeMeters ||
            state.TacticalZoomMeters > _profile.Tactical.MaximumSizeMeters ||
            state.ShoulderDistanceMeters < _profile.Shoulder.MinimumDistanceMeters ||
            state.ShoulderDistanceMeters > _profile.Shoulder.MaximumDistanceMeters)
            throw new InvalidOperationException(
                "Fallout 1 saved camera mode, angle, or zoom is invalid.");
        var minimumPitch = Mathf.DegToRad(state.Mode switch
        {
            "hex-tactical" => _profile.Tactical.MinimumPitchDegrees,
            "shoulder" => _profile.Shoulder.MinimumPitchDegrees,
            _ => _profile.FirstPerson.MinimumPitchDegrees,
        });
        var maximumPitch = Mathf.DegToRad(state.Mode switch
        {
            "hex-tactical" => _profile.Tactical.MaximumPitchDegrees,
            "shoulder" => _profile.Shoulder.MaximumPitchDegrees,
            _ => _profile.FirstPerson.MaximumPitchDegrees,
        });
        if (state.PitchRadians < minimumPitch || state.PitchRadians > maximumPitch)
            throw new InvalidOperationException(
                "Fallout 1 saved camera pitch is outside the selected mode's authored range.");
    }

    internal void ApplySaveState(Fo1CameraSaveState state)
    {
        ValidateSaveState(state);
        if (state.Mode == "first-person")
            SetFirstPersonMode(true);
        else if (state.Mode == "shoulder")
            SetExplorationMode(true);
        else
            SetExplorationMode(false);

        _targetSize = state.TacticalZoomMeters;
        _explorationDistance = state.ShoulderDistanceMeters;
        SetOrbitDegrees(
            Mathf.RadToDeg(state.YawRadians),
            Mathf.RadToDeg(state.PitchRadians));
        if (state.Mode == "hex-tactical")
        {
            Position = Fo1HexMath.Center(_session.PlayerTile);
            _camera.Size = _targetSize;
        }
        else
        {
            Position = _session.PlayerToken.GlobalPosition + Vector3.Up *
                (state.Mode == "first-person"
                    ? _profile.FirstPerson.EyeHeightMeters
                    : _profile.Shoulder.RigHeightMeters);
            _camera.Position = state.Mode == "first-person"
                ? Vector3.Zero
                : new Vector3(
                    _profile.Shoulder.CameraLateralOffsetMeters,
                    _profile.Shoulder.CameraVerticalOffsetMeters,
                    _explorationDistance);
        }
    }

    internal void AttachCaveCutaway(Fo1CaveCutaway caveCutaway)
    {
        _caveCutaway = caveCutaway;
        _caveCutaway.SetMeltEnabled(!_firstPersonMode);
    }

    internal void Configure(
        Fo1TacticalSession session,
        Vector3 homeFocus,
        float homeSize,
        float yawRadians,
        float pitchRadians,
        Fo1CameraProfile profile)
    {
        _session = session;
        _profile = profile;
        ValidateProfile(profile);
        Name = "Fo1TacticalCameraRig";
        _homeFocus = homeFocus;
        _homeSize = Math.Clamp(
            homeSize,
            profile.Tactical.MinimumSizeMeters,
            profile.Tactical.MaximumSizeMeters);
        _homeYaw = yawRadians;
        _homePitch = Math.Clamp(
            pitchRadians,
            Mathf.DegToRad(profile.Tactical.MinimumPitchDegrees),
            Mathf.DegToRad(profile.Tactical.MaximumPitchDegrees));
        _explorationDistance = profile.Shoulder.DefaultDistanceMeters;
        _targetSize = _homeSize;
        _targetYaw = _homeYaw;
        _targetPitch = _homePitch;
        Position = _homeFocus;

        _yawPivot = new Node3D
        {
            Name = "Fo1TacticalYaw",
            Rotation = new Vector3(0.0f, _targetYaw, 0.0f),
        };
        AddChild(_yawPivot);
        _pitchPivot = new Node3D
        {
            Name = "Fo1TacticalPitch",
            Rotation = new Vector3(_targetPitch, 0.0f, 0.0f),
        };
        _yawPivot.AddChild(_pitchPivot);
        _camera = new Camera3D
        {
            Name = "Fo1TacticalOrthographicCamera",
            Projection = Camera3D.ProjectionType.Orthogonal,
            KeepAspect = Camera3D.KeepAspectEnum.Height,
            Size = _targetSize,
            Position = new Vector3(
                0.0f,
                0.0f,
                MathF.Max(
                    profile.Tactical.MinimumCameraDistanceMeters,
                    _homeSize * profile.Tactical.HomeDistanceScale)),
            Near = profile.Tactical.NearClipMeters,
            Far = profile.Tactical.FarClipMeters,
            Current = true,
        };
        _pitchPivot.AddChild(_camera);
        _camera.AddChild(new DirectionalLight3D
        {
            Name = "Fo1TacticalCameraFill",
            LightColor = profile.Tactical.FillLightColor,
            LightEnergy = profile.Tactical.FillLightEnergy,
            ShadowEnabled = false,
        });
    }

    public override void _Ready()
    {
        Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    public override void _Process(double delta)
    {
        if (_session.InventoryOpen)
        {
            _session.SetHoveredTile(-1);
            return;
        }
        if (_firstPersonMode)
            UpdateFirstPersonLocomotion((float)delta);
        var weight = Math.Clamp((float)delta * _profile.SmoothingPerSecond, 0.0f, 1.0f);
        _yawPivot.Rotation = new Vector3(
            0.0f,
            Mathf.LerpAngle(_yawPivot.Rotation.Y, _targetYaw, weight),
            0.0f);
        _pitchPivot.Rotation = new Vector3(
            Mathf.LerpAngle(_pitchPivot.Rotation.X, _targetPitch, weight),
            0.0f,
            0.0f);
        if (_explorationMode)
        {
            var targetRigPosition = _session.PlayerToken.GlobalPosition + Vector3.Up *
                (_firstPersonMode
                    ? _profile.FirstPerson.EyeHeightMeters
                    : _profile.Shoulder.RigHeightMeters);
            Position = Position.Lerp(
                targetRigPosition,
                weight);
            _camera.Position = _camera.Position.Lerp(
                _firstPersonMode
                    ? Vector3.Zero
                    : new Vector3(
                        _profile.Shoulder.CameraLateralOffsetMeters,
                        _profile.Shoulder.CameraVerticalOffsetMeters,
                        _explorationDistance),
                weight);
        }
        else
        {
            _camera.Size = Mathf.Lerp(_camera.Size, _targetSize, weight);
            UpdateKeyboardAndEdgePan((float)delta);
        }
        if (_firstPersonMode)
            _session.SetHoveredTile(-1);
        else if (TryProjectToGround(GetViewport().GetMousePosition(), out var point))
            _session.SetHoveredTile(Fo1HexMath.NearestTile(point));
        else
            _session.SetHoveredTile(-1);
    }

    public override void _UnhandledInput(InputEvent inputEvent)
    {
        if (_session.InventoryOpen)
        {
            if (inputEvent is InputEventKey inventoryKey && inventoryKey.Pressed &&
                !inventoryKey.Echo &&
                (inventoryKey.PhysicalKeycode == Key.Escape ||
                 inventoryKey.PhysicalKeycode == _session.InventoryKey))
                _session.CloseInventory();
            return;
        }
        if (inputEvent is InputEventKey key && key.Pressed && !key.Echo)
        {
            if (key.PhysicalKeycode == Key.C)
            {
                if (_firstPersonMode)
                    SetExplorationMode(false);
                else if (_explorationMode)
                    SetFirstPersonMode(true);
                else
                    SetExplorationMode(true);
            }
            else if (key.PhysicalKeycode == Key.Q)
                _targetYaw += Mathf.DegToRad(_profile.Tactical.KeyboardYawStepDegrees);
            else if (key.PhysicalKeycode == Key.E)
                _targetYaw -= Mathf.DegToRad(_profile.Tactical.KeyboardYawStepDegrees);
            else if (key.PhysicalKeycode == Key.Home)
                ResetHome();
            else if (key.PhysicalKeycode == Key.F)
                FocusPlayer();
            else if (key.PhysicalKeycode == Key.Space)
                _session.EndTurn();
            else if (key.PhysicalKeycode == Key.F5)
                _session.SaveAndNotify();
            else if (key.PhysicalKeycode == Key.X)
                _session.AttackSelected();
            else if (key.PhysicalKeycode == Key.Z)
                _session.AttackSelectedMelee();
            else if (key.PhysicalKeycode == Key.R)
                _session.Reload();
            else if (key.PhysicalKeycode == Key.Tab)
            {
                var target = _session.CycleTarget();
                if (target is not null)
                    FrameCombatPair(_session.PlayerTile, target.Tile);
            }
            else if (key.PhysicalKeycode == Key.G)
                _session.ToggleGrid();
            else if (key.PhysicalKeycode == Key.V)
                _session.ToggleSourceOverlay();
            else if (key.PhysicalKeycode == Key.B)
                _session.Toggle3DBlockout();
            else if (key.PhysicalKeycode == Key.P)
                _session.TogglePipBoy();
            else if (key.PhysicalKeycode == _session.InventoryKey)
                _session.ToggleInventory();
            else if (key.PhysicalKeycode == Key.Escape)
            {
                _orbitDragging = false;
                _panDragging = false;
                if (_firstPersonMode)
                    Input.MouseMode = Input.MouseModeEnum.Visible;
            }
            return;
        }

        if (inputEvent is InputEventMouseButton button)
        {
            if (_firstPersonMode)
            {
                if (button.Pressed && button.ButtonIndex == MouseButton.Left)
                {
                    if (Input.MouseMode != Input.MouseModeEnum.Captured &&
                        DisplayServer.GetName() != "headless")
                        Input.MouseMode = Input.MouseModeEnum.Captured;
                    else
                        _session.FireFirstPerson(
                            _camera.GlobalPosition,
                            -_camera.GlobalBasis.Z);
                }
                else if (button.Pressed && button.ButtonIndex == MouseButton.Right)
                    _session.MeleeFirstPerson(
                        _camera.GlobalPosition,
                        -_camera.GlobalBasis.Z);
                return;
            }
            if (button.ButtonIndex == MouseButton.Middle)
            {
                _orbitDragging = button.Pressed;
                if (button.Pressed)
                    _session.SetCameraStatus("MMB orbit: drag horizontally to rotate and vertically to tilt");
                return;
            }
            if (button.ButtonIndex == MouseButton.Right)
            {
                if (_explorationMode)
                {
                    _orbitDragging = button.Pressed;
                    _panDragging = false;
                    if (button.Pressed)
                        _session.SetCameraStatus(
                            "Shoulder tactical orbit • LMB commands center hexes • C enters continuous FPS");
                    return;
                }
                _panDragging = button.Pressed;
                if (button.Pressed)
                    _session.SetCameraStatus("RMB grab-pan: drag the map under the cursor");
                return;
            }
            if (!button.Pressed)
                return;
            if (button.ButtonIndex == MouseButton.WheelUp)
            {
                if (_explorationMode)
                {
                    _explorationDistance = Math.Clamp(
                        _explorationDistance * _profile.Tactical.CursorZoomFactor,
                        _profile.Shoulder.MinimumDistanceMeters,
                        _profile.Shoulder.MaximumDistanceMeters);
                    return;
                }
                ZoomAt(button.Position, _profile.Tactical.CursorZoomFactor);
                return;
            }
            if (button.ButtonIndex == MouseButton.WheelDown)
            {
                if (_explorationMode)
                {
                    _explorationDistance = Math.Clamp(
                        _explorationDistance / _profile.Tactical.CursorZoomFactor,
                        _profile.Shoulder.MinimumDistanceMeters,
                        _profile.Shoulder.MaximumDistanceMeters);
                    return;
                }
                ZoomAt(button.Position, 1.0f / _profile.Tactical.CursorZoomFactor);
                return;
            }
            if (button.ButtonIndex == MouseButton.Left && TryProjectToGround(button.Position, out var point))
            {
                var tile = Fo1HexMath.NearestTile(point);
                if (tile >= 0)
                {
                    _session.ActivateTile(tile, button.DoubleClick);
                    if (button.DoubleClick)
                        FocusPoint(
                            Fo1HexMath.Center(tile),
                            MathF.Min(_targetSize, _profile.Tactical.TargetFocusMaximumSizeMeters));
                }
            }
            return;
        }

        if (inputEvent is not InputEventMouseMotion motion)
            return;
        if (_firstPersonMode && Input.MouseMode == Input.MouseModeEnum.Captured ||
            _orbitDragging || Input.IsPhysicalKeyPressed(Key.Ctrl))
            ApplyLookMotion(motion.Relative);
        else if (_panDragging)
        {
            PanByPixels(motion.Relative);
        }
    }

    internal void ApplyFirstPersonLook(Vector2 relative)
    {
        if (!_firstPersonMode)
            throw new InvalidOperationException("First-person look requires first-person mode.");
        ApplyLookMotion(relative);
    }

    private void ApplyLookMotion(Vector2 relative)
    {
        _targetYaw -= relative.X * _profile.Tactical.OrbitRadiansPerPixel;
        var verticalDelta = relative.Y * _profile.Tactical.OrbitRadiansPerPixel;
        if (_firstPersonMode)
            verticalDelta = -verticalDelta;
        _targetPitch = Math.Clamp(
            _targetPitch + verticalDelta,
            _firstPersonMode
                ? Mathf.DegToRad(_profile.FirstPerson.MinimumPitchDegrees)
                : _explorationMode
                    ? Mathf.DegToRad(_profile.Shoulder.MinimumPitchDegrees)
                    : Mathf.DegToRad(_profile.Tactical.MinimumPitchDegrees),
            _firstPersonMode
                ? Mathf.DegToRad(_profile.FirstPerson.MaximumPitchDegrees)
                : _explorationMode
                    ? Mathf.DegToRad(_profile.Shoulder.MaximumPitchDegrees)
                    : Mathf.DegToRad(_profile.Tactical.MaximumPitchDegrees));
    }

    internal void ResetHome()
    {
        if (_explorationMode)
            SetExplorationMode(false);
        Position = _homeFocus;
        _targetSize = _homeSize;
        _targetYaw = _homeYaw;
        _targetPitch = _homePitch;
        _session.SetCameraStatus("Route view reset: entry and Vault door framed");
    }

    internal void FocusPlayer()
    {
        if (_explorationMode)
        {
            Position = _session.PlayerToken.GlobalPosition + Vector3.Up *
                (_firstPersonMode
                    ? _profile.FirstPerson.EyeHeightMeters
                    : _profile.Shoulder.RigHeightMeters);
            _session.SetCameraStatus(
                _firstPersonMode
                    ? $"First-person Vault Dweller at hex {_session.PlayerTile} • C tactical view"
                    : $"Third-person Vault Dweller at hex {_session.PlayerTile} • C first-person view");
            return;
        }
        FocusPoint(
            Fo1HexMath.Center(_session.PlayerTile),
            MathF.Min(_targetSize, _profile.Tactical.PlayerFocusMaximumSizeMeters));
        _session.SetCameraStatus($"Focused Vault Dweller at hex {_session.PlayerTile}");
    }

    internal void SetExplorationMode(bool enabled)
    {
        if (enabled && _explorationMode)
        {
            if (_firstPersonMode)
                SetFirstPersonMode(false);
            return;
        }
        if (!enabled && !_explorationMode)
            return;
        _orbitDragging = false;
        _panDragging = false;
        if (enabled)
        {
            SaveTacticalReturnState();
            _session.SetFirstPersonModeActive(false);
            _explorationMode = true;
            _firstPersonMode = false;
            _session.PlayerToken.Visible = true;
            _caveCutaway?.SetMeltEnabled(true);
            _camera.Projection = Camera3D.ProjectionType.Perspective;
            _camera.Fov = _profile.Shoulder.FovDegrees;
            _camera.Near = _profile.Shoulder.NearClipMeters;
            _targetYaw = PlayerBehindYaw();
            _targetPitch = Mathf.DegToRad(_profile.Shoulder.InitialPitchDegrees);
            _yawPivot.Rotation = new Vector3(0.0f, _targetYaw, 0.0f);
            _pitchPivot.Rotation = new Vector3(_targetPitch, 0.0f, 0.0f);
            Position = _session.PlayerToken.GlobalPosition +
                Vector3.Up * _profile.Shoulder.RigHeightMeters;
            _camera.Position = new Vector3(
                _profile.Shoulder.CameraLateralOffsetMeters,
                _profile.Shoulder.CameraVerticalOffsetMeters,
                _explorationDistance);
            _session.SetCameraStatus(
                "SHOULDER TACTICAL • LMB center-hex commands • RMB/MMB orbit • wheel zoom • C FPS");
        }
        else
        {
            _session.SetFirstPersonModeActive(false);
            _explorationMode = false;
            _firstPersonMode = false;
            _session.PlayerToken.Visible = true;
            _caveCutaway?.SetMeltEnabled(true);
            Input.MouseMode = Input.MouseModeEnum.Visible;
            _camera.Projection = Camera3D.ProjectionType.Orthogonal;
            _camera.Near = _profile.Tactical.NearClipMeters;
            Position = _tacticalFocusBeforeExploration;
            _targetSize = _tacticalSizeBeforeExploration;
            _targetYaw = _tacticalYawBeforeExploration;
            _targetPitch = _tacticalPitchBeforeExploration;
            _yawPivot.Rotation = new Vector3(0.0f, _targetYaw, 0.0f);
            _pitchPivot.Rotation = new Vector3(_targetPitch, 0.0f, 0.0f);
            _camera.Size = _targetSize;
            _camera.Position = new Vector3(
                0.0f,
                0.0f,
                MathF.Max(
                    _profile.Tactical.MinimumCameraDistanceMeters,
                    _homeSize * _profile.Tactical.HomeDistanceScale));
            _session.SetCameraStatus(
                "TACTICAL • exact Fallout hex/AP state • every destination centered • C shoulder view");
        }
    }

    internal void SetFirstPersonMode(bool enabled)
    {
        if (_firstPersonMode == enabled)
            return;
        _orbitDragging = false;
        _panDragging = false;
        if (enabled)
        {
            if (!_explorationMode)
            {
                SaveTacticalReturnState();
                _explorationMode = true;
            }
            _firstPersonMode = true;
            _caveCutaway?.SetMeltEnabled(false);
            _session.PlayerToken.Visible = false;
            _camera.Projection = Camera3D.ProjectionType.Perspective;
            _camera.Fov = _profile.FirstPerson.FovDegrees;
            _camera.Near = _profile.FirstPerson.NearClipMeters;
            _targetYaw = PlayerBehindYaw();
            _targetPitch = Mathf.DegToRad(_profile.FirstPerson.InitialPitchDegrees);
            _yawPivot.Rotation = new Vector3(0.0f, _targetYaw, 0.0f);
            _pitchPivot.Rotation = new Vector3(_targetPitch, 0.0f, 0.0f);
            Position = _session.PlayerToken.GlobalPosition +
                Vector3.Up * _profile.FirstPerson.EyeHeightMeters;
            _camera.Position = Vector3.Zero;
            _session.SetFirstPersonModeActive(true);
            if (DisplayServer.GetName() != "headless")
                Input.MouseMode = Input.MouseModeEnum.Captured;
            _session.SetCameraStatus(
                "FPS • continuous WASD • mouse look • LMB fire • ESC release • C tactical");
            return;
        }

        _session.SetFirstPersonModeActive(false);
        _firstPersonMode = false;
        _caveCutaway?.SetMeltEnabled(true);
        _session.PlayerToken.Visible = true;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        _camera.Projection = Camera3D.ProjectionType.Perspective;
        _camera.Fov = _profile.Shoulder.FovDegrees;
        _camera.Near = _profile.Shoulder.NearClipMeters;
        _targetYaw = PlayerBehindYaw();
        _targetPitch = Mathf.DegToRad(_profile.Shoulder.InitialPitchDegrees);
        _yawPivot.Rotation = new Vector3(0.0f, _targetYaw, 0.0f);
        _pitchPivot.Rotation = new Vector3(_targetPitch, 0.0f, 0.0f);
        Position = _session.PlayerToken.GlobalPosition +
            Vector3.Up * _profile.Shoulder.RigHeightMeters;
        _camera.Position = new Vector3(
            _profile.Shoulder.CameraLateralOffsetMeters,
            _profile.Shoulder.CameraVerticalOffsetMeters,
            _explorationDistance);
        _session.SetCameraStatus(
            "SHOULDER TACTICAL • LMB center-hex commands • RMB/MMB orbit • wheel zoom • C FPS");
    }

    internal bool MoveFirstPerson(Vector2 input, float deltaSeconds)
    {
        if (!_firstPersonMode || input.LengthSquared() <= Fo1TacticalCameraNumericContracts.PresentationFloat0Point0001f || deltaSeconds <= 0.0f)
        {
            _session.SetFirstPersonMoving(false);
            return false;
        }
        var forward = -_camera.GlobalBasis.Z;
        var right = _camera.GlobalBasis.X;
        forward.Y = 0.0f;
        right.Y = 0.0f;
        if (forward.LengthSquared() <= Fo1TacticalCameraNumericContracts.PresentationFloat0Point001f || right.LengthSquared() <= Fo1TacticalCameraNumericContracts.PresentationFloat0Point001f)
            return false;
        var desired = (
            right.Normalized() * input.X +
            forward.Normalized() * input.Y).Normalized();
        return _session.TryMoveFirstPerson(
            desired,
            _profile.FirstPerson.MoveSpeedMetersPerSecond * deltaSeconds);
    }

    private void UpdateFirstPersonLocomotion(float deltaSeconds)
    {
        var input = new Vector2(
            (Input.IsPhysicalKeyPressed(Key.D) || Input.IsPhysicalKeyPressed(Key.Right) ? 1.0f : 0.0f) -
            (Input.IsPhysicalKeyPressed(Key.A) || Input.IsPhysicalKeyPressed(Key.Left) ? 1.0f : 0.0f),
            (Input.IsPhysicalKeyPressed(Key.W) || Input.IsPhysicalKeyPressed(Key.Up) ? 1.0f : 0.0f) -
            (Input.IsPhysicalKeyPressed(Key.S) || Input.IsPhysicalKeyPressed(Key.Down) ? 1.0f : 0.0f));
        MoveFirstPerson(input, deltaSeconds);
    }

    private void SaveTacticalReturnState()
    {
        _tacticalFocusBeforeExploration = Position;
        _tacticalSizeBeforeExploration = _targetSize;
        _tacticalYawBeforeExploration = _targetYaw;
        _tacticalPitchBeforeExploration = _targetPitch;
    }

    internal bool StepExploration(Vector2 input)
    {
        if (!_explorationMode || input.LengthSquared() <= 0.0f)
            return false;
        var forward = -_camera.GlobalBasis.Z;
        var right = _camera.GlobalBasis.X;
        forward.Y = 0.0f;
        right.Y = 0.0f;
        if (forward.LengthSquared() <= Fo1TacticalCameraNumericContracts.PresentationFloat0Point001f || right.LengthSquared() <= Fo1TacticalCameraNumericContracts.PresentationFloat0Point001f)
            return false;
        var desired = (
            right.Normalized() * input.X -
            forward.Normalized() * input.Y).Normalized();
        var player = Fo1HexMath.Center(_session.PlayerTile);
        var next = Fo1HexMath.Neighbors(_session.PlayerTile)
            .Where(_session.CanWalk)
            .Select(tile => new
            {
                Tile = tile,
                Alignment = (Fo1HexMath.Center(tile) - player).Normalized().Dot(desired),
            })
            .OrderByDescending(candidate => candidate.Alignment)
            .ThenBy(candidate => candidate.Tile)
            .FirstOrDefault();
        if (next is null || next.Alignment < _profile.Shoulder.MinimumMovementAlignment)
            return false;
        _session.SelectTile(next.Tile);
        _session.SetCameraStatus(
            $"{(_firstPersonMode ? "FIRST-PERSON" : "THIRD-PERSON")} HEX STEP " +
            $"{_session.PlayerTile} → {next.Tile} • one AP • C cycle view");
        return true;
    }

    private float PlayerBehindYaw()
    {
        var behind = _session.PlayerToken.GlobalBasis.Z;
        behind.Y = 0.0f;
        if (behind.LengthSquared() <= Fo1TacticalCameraNumericContracts.PresentationFloat0Point001f)
            return _targetYaw;
        behind = behind.Normalized();
        return MathF.Atan2(behind.X, behind.Z);
    }

    internal void FrameCombatPair(int firstTile, int secondTile)
    {
        FramePair(firstTile, secondTile, _profile.Tactical.CombatFraming);
    }

    internal void FrameEntryPair(int firstTile, int secondTile)
    {
        FramePair(firstTile, secondTile, _profile.Tactical.EntryFraming);
    }

    private void FramePair(
        int firstTile,
        int secondTile,
        Fo1PairFramingProfile framing)
    {
        var first = Fo1HexMath.Center(firstTile);
        var second = Fo1HexMath.Center(secondTile);
        Position = (first + second) / 2.0f;
        Position = new Vector3(Position.X, framing.FocusHeightMeters, Position.Z);
        _targetSize = Math.Clamp(
            Fo1HexMath.Distance(firstTile, secondTile) + framing.PaddingMeters,
            framing.MinimumSizeMeters,
            framing.MaximumSizeMeters);
        _camera.Size = _targetSize;
        var screenUp = _camera.GlobalBasis.Y;
        screenUp.Y = 0.0f;
        var viewportHeight = MathF.Max(1.0f, GetViewport().GetVisibleRect().Size.Y);
        if (screenUp.LengthSquared() > Fo1TacticalCameraNumericContracts.PresentationFloat0Point0001f)
        {
            var viewOffset = _targetSize * framing.ReservedHudPixels * Fo1TacticalCameraNumericContracts.PresentationFloat0Point5f / viewportHeight;
            Position -= screenUp.Normalized() * (viewOffset / screenUp.Length());
        }
    }

    internal void FocusTile(int tile, float sizeMeters)
    {
        FocusPoint(Fo1HexMath.Center(tile), sizeMeters);
        _camera.Size = _targetSize;
    }

    internal void FocusTileAtHeight(int tile, float sizeMeters, float heightMeters)
    {
        FocusPoint(Fo1HexMath.Center(tile) + Vector3.Up * heightMeters, sizeMeters);
        _camera.Size = _targetSize;
    }

    internal void FocusWorldPoint(
        Vector3 point,
        float sizeMeters,
        float reservedHudPixels = 0.0f)
    {
        FocusPoint(point, sizeMeters);
        _camera.Size = _targetSize;
        var screenUp = _camera.GlobalBasis.Y;
        screenUp.Y = 0.0f;
        if (reservedHudPixels > 0.0f && screenUp.LengthSquared() > Fo1TacticalCameraNumericContracts.PresentationFloat0Point0001f)
        {
            var viewportHeight = MathF.Max(1.0f, GetViewport().GetVisibleRect().Size.Y);
            var viewOffset = _targetSize * reservedHudPixels * Fo1TacticalCameraNumericContracts.PresentationFloat0Point5f / viewportHeight;
            Position -= screenUp.Normalized() * (viewOffset / screenUp.Length());
        }
    }

    internal void SetOrbitDegrees(float yawDegrees, float pitchDegrees)
    {
        _targetYaw = Mathf.DegToRad(yawDegrees);
        _targetPitch = Math.Clamp(
            Mathf.DegToRad(pitchDegrees),
            _firstPersonMode
                ? Mathf.DegToRad(_profile.FirstPerson.MinimumPitchDegrees)
                : _explorationMode
                    ? Mathf.DegToRad(_profile.Shoulder.MinimumPitchDegrees)
                    : Mathf.DegToRad(_profile.Tactical.MinimumPitchDegrees),
            _firstPersonMode
                ? Mathf.DegToRad(_profile.FirstPerson.MaximumPitchDegrees)
                : _explorationMode
                    ? Mathf.DegToRad(_profile.Shoulder.MaximumPitchDegrees)
                    : Mathf.DegToRad(_profile.Tactical.MaximumPitchDegrees));
        _yawPivot.Rotation = new Vector3(0.0f, _targetYaw, 0.0f);
        _pitchPivot.Rotation = new Vector3(_targetPitch, 0.0f, 0.0f);
    }

    private void FocusPoint(Vector3 point, float size)
    {
        if (_explorationMode)
            return;
        Position = point;
        _targetSize = Math.Clamp(
            size,
            _profile.Tactical.MinimumSizeMeters,
            _profile.Tactical.MaximumSizeMeters);
    }

    private void ZoomAt(Vector2 screenPosition, float factor)
    {
        var beforeValid = TryProjectToGround(screenPosition, out var before);
        var oldSize = _camera.Size;
        var newSize = Math.Clamp(
            _targetSize * factor,
            _profile.Tactical.MinimumSizeMeters,
            _profile.Tactical.MaximumSizeMeters);
        _camera.Size = newSize;
        var afterValid = TryProjectToGround(screenPosition, out var after);
        _camera.Size = oldSize;
        if (beforeValid && afterValid)
            Position += before - after;
        _targetSize = newSize;
    }

    private void PanByPixels(Vector2 relative)
    {
        var viewportHeight = MathF.Max(1.0f, GetViewport().GetVisibleRect().Size.Y);
        var metersPerPixel = _camera.Size / viewportHeight;
        var right = _camera.GlobalBasis.X;
        var screenUp = _camera.GlobalBasis.Y;
        right.Y = 0.0f;
        screenUp.Y = 0.0f;
        right = right.Normalized();
        screenUp = screenUp.Normalized();
        Position += (-right * relative.X + screenUp * relative.Y) * metersPerPixel;
    }

    private void UpdateKeyboardAndEdgePan(float delta)
    {
        var input = Vector2.Zero;
        if (Input.IsPhysicalKeyPressed(Key.A) || Input.IsPhysicalKeyPressed(Key.Left))
            input.X -= 1.0f;
        if (Input.IsPhysicalKeyPressed(Key.D) || Input.IsPhysicalKeyPressed(Key.Right))
            input.X += 1.0f;
        if (Input.IsPhysicalKeyPressed(Key.W) || Input.IsPhysicalKeyPressed(Key.Up))
            input.Y += 1.0f;
        if (Input.IsPhysicalKeyPressed(Key.S) || Input.IsPhysicalKeyPressed(Key.Down))
            input.Y -= 1.0f;
        input += EdgePan();
        if (input.LengthSquared() <= 0.0f)
            return;
        input = input.Normalized();
        var forward = -_camera.GlobalBasis.Z;
        var right = _camera.GlobalBasis.X;
        forward.Y = 0.0f;
        right.Y = 0.0f;
        forward = forward.Normalized();
        right = right.Normalized();
        var speedScale = _targetSize / _profile.Tactical.PanReferenceSizeMeters;
        if (Input.IsPhysicalKeyPressed(Key.Shift))
            speedScale *= _profile.Tactical.FastPanMultiplier;
        Position += (right * input.X + forward * input.Y).Normalized() *
            _profile.Tactical.KeyboardPanMetersPerSecond * speedScale * delta;
    }

    private Vector2 EdgePan()
    {
        if (DisplayServer.GetName() == "headless" || !DisplayServer.WindowIsFocused())
            return Vector2.Zero;
        var viewport = GetViewport().GetVisibleRect();
        var mouse = GetViewport().GetMousePosition();
        if (!viewport.HasPoint(mouse) ||
            mouse.X < _profile.Tactical.GuiExclusionMinimumX &&
            mouse.Y > viewport.Size.Y - _profile.Tactical.GuiExclusionBottomPixels ||
            _orbitDragging || _panDragging)
            return Vector2.Zero;
        var result = Vector2.Zero;
        if (mouse.X <= _profile.Tactical.EdgeMarginPixels)
            result.X -= 1.0f;
        else if (mouse.X >= viewport.Size.X - _profile.Tactical.EdgeMarginPixels)
            result.X += 1.0f;
        if (mouse.Y <= _profile.Tactical.EdgeMarginPixels)
            result.Y += 1.0f;
        else if (mouse.Y >= viewport.Size.Y - _profile.Tactical.EdgeMarginPixels)
            result.Y -= 1.0f;
        return result;
    }

    private static void ValidateProfile(Fo1CameraProfile profile)
    {
        if (profile.Tactical.MinimumSizeMeters >= profile.Tactical.MaximumSizeMeters ||
            profile.Tactical.MinimumPitchDegrees >= profile.Tactical.MaximumPitchDegrees ||
            profile.Tactical.NearClipMeters >= profile.Tactical.FarClipMeters ||
            profile.Shoulder.MinimumPitchDegrees >= profile.Shoulder.MaximumPitchDegrees ||
            profile.Shoulder.MinimumDistanceMeters > profile.Shoulder.DefaultDistanceMeters ||
            profile.Shoulder.DefaultDistanceMeters > profile.Shoulder.MaximumDistanceMeters ||
            profile.FirstPerson.MinimumPitchDegrees >= profile.FirstPerson.MaximumPitchDegrees)
            throw new InvalidOperationException("Fallout camera runtime-profile ranges are inconsistent.");
    }

    private bool TryProjectToGround(Vector2 screenPosition, out Vector3 point)
    {
        var origin = _camera.ProjectRayOrigin(screenPosition);
        var direction = _camera.ProjectRayNormal(screenPosition);
        if (MathF.Abs(direction.Y) <= Fo1TacticalCameraNumericContracts.PresentationFloat1Point0ENEgativE5f)
        {
            point = default;
            return false;
        }
        var distance = -origin.Y / direction.Y;
        if (distance <= 0.0f)
        {
            point = default;
            return false;
        }
        point = origin + direction * distance;
        return Fo1HexMath.NearestTile(point) >= 0;
    }
}
