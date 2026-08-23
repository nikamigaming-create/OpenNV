using Godot;

namespace OpenNV.Runtime;

internal partial class Fo1TacticalCamera : Node3D
{
    internal const float MinimumSizeMeters = 5.0f;
    internal const float MaximumSizeMeters = 80.0f;
    internal const float MinimumPitchRadians = -1.309f;
    internal const float MaximumPitchRadians = -0.436f;
    internal const float KeyboardPanMetersPerSecond = 10.0f;
    internal const float OrbitRadiansPerPixel = 0.006f;
    internal const float KeyboardYawStepRadians = MathF.PI / 12.0f;
    private const float EdgeMarginPixels = 16.0f;

    private Fo1TacticalSession _session = null!;
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

    internal Camera3D Camera => _camera;
    internal Node3D YawPivot => _yawPivot;
    internal Node3D PitchPivot => _pitchPivot;
    internal float TargetSizeMeters => _targetSize;
    internal float TargetYawRadians => _targetYaw;
    internal float TargetPitchRadians => _targetPitch;
    internal bool OrbitDragging => _orbitDragging;
    internal bool PanDragging => _panDragging;

    internal void Configure(
        Fo1TacticalSession session,
        Vector3 homeFocus,
        float homeSize,
        float yawRadians,
        float pitchRadians)
    {
        _session = session;
        Name = "Fo1TacticalCameraRig";
        _homeFocus = homeFocus;
        _homeSize = Math.Clamp(homeSize, MinimumSizeMeters, MaximumSizeMeters);
        _homeYaw = yawRadians;
        _homePitch = Math.Clamp(pitchRadians, MinimumPitchRadians, MaximumPitchRadians);
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
            Position = new Vector3(0.0f, 0.0f, MathF.Max(24.0f, _homeSize * 1.35f)),
            Near = 0.05f,
            Far = 500.0f,
            Current = true,
        };
        _pitchPivot.AddChild(_camera);
    }

    public override void _Ready()
    {
        Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    public override void _Process(double delta)
    {
        var weight = Math.Clamp((float)delta * 12.0f, 0.0f, 1.0f);
        _yawPivot.Rotation = new Vector3(
            0.0f,
            Mathf.LerpAngle(_yawPivot.Rotation.Y, _targetYaw, weight),
            0.0f);
        _pitchPivot.Rotation = new Vector3(
            Mathf.LerpAngle(_pitchPivot.Rotation.X, _targetPitch, weight),
            0.0f,
            0.0f);
        _camera.Size = Mathf.Lerp(_camera.Size, _targetSize, weight);
        UpdateKeyboardAndEdgePan((float)delta);
        if (TryProjectToGround(GetViewport().GetMousePosition(), out var point))
            _session.SetHoveredTile(Fo1HexMath.NearestTile(point));
        else
            _session.SetHoveredTile(-1);
    }

    public override void _UnhandledInput(InputEvent inputEvent)
    {
        if (inputEvent is InputEventKey key && key.Pressed && !key.Echo)
        {
            if (key.PhysicalKeycode == Key.Q)
                _targetYaw += KeyboardYawStepRadians;
            else if (key.PhysicalKeycode == Key.E)
                _targetYaw -= KeyboardYawStepRadians;
            else if (key.PhysicalKeycode == Key.Home)
                ResetHome();
            else if (key.PhysicalKeycode == Key.F)
                FocusPlayer();
            else if (key.PhysicalKeycode == Key.Space)
                _session.EndTurn();
            else if (key.PhysicalKeycode == Key.F5)
                _session.SaveAndNotify();
            else if (key.PhysicalKeycode == Key.Escape)
            {
                _orbitDragging = false;
                _panDragging = false;
            }
            return;
        }

        if (inputEvent is InputEventMouseButton button)
        {
            if (button.ButtonIndex == MouseButton.Middle)
            {
                _orbitDragging = button.Pressed;
                if (button.Pressed)
                    _session.SetCameraStatus("MMB orbit: drag horizontally to rotate and vertically to tilt");
                return;
            }
            if (button.ButtonIndex == MouseButton.Right)
            {
                _panDragging = button.Pressed;
                if (button.Pressed)
                    _session.SetCameraStatus("RMB grab-pan: drag the map under the cursor");
                return;
            }
            if (!button.Pressed)
                return;
            if (button.ButtonIndex == MouseButton.WheelUp)
            {
                ZoomAt(button.Position, 0.86f);
                return;
            }
            if (button.ButtonIndex == MouseButton.WheelDown)
            {
                ZoomAt(button.Position, 1.0f / 0.86f);
                return;
            }
            if (button.ButtonIndex == MouseButton.Left && TryProjectToGround(button.Position, out var point))
            {
                var tile = Fo1HexMath.NearestTile(point);
                if (tile >= 0)
                {
                    _session.SelectTile(tile);
                    if (button.DoubleClick)
                        FocusPoint(Fo1HexMath.Center(tile), MathF.Min(_targetSize, 14.0f));
                }
            }
            return;
        }

        if (inputEvent is not InputEventMouseMotion motion)
            return;
        if (_orbitDragging || Input.IsPhysicalKeyPressed(Key.Ctrl))
        {
            _targetYaw -= motion.Relative.X * OrbitRadiansPerPixel;
            _targetPitch = Math.Clamp(
                _targetPitch + motion.Relative.Y * OrbitRadiansPerPixel,
                MinimumPitchRadians,
                MaximumPitchRadians);
        }
        else if (_panDragging)
        {
            PanByPixels(motion.Relative);
        }
    }

    internal void ResetHome()
    {
        Position = _homeFocus;
        _targetSize = _homeSize;
        _targetYaw = _homeYaw;
        _targetPitch = _homePitch;
        _session.SetCameraStatus("Route view reset: entry and Vault door framed");
    }

    internal void FocusPlayer()
    {
        FocusPoint(Fo1HexMath.Center(_session.PlayerTile), MathF.Min(_targetSize, 12.0f));
        _session.SetCameraStatus($"Focused Vault Dweller at hex {_session.PlayerTile}");
    }

    private void FocusPoint(Vector3 point, float size)
    {
        Position = new Vector3(point.X, 0.0f, point.Z);
        _targetSize = Math.Clamp(size, MinimumSizeMeters, MaximumSizeMeters);
    }

    private void ZoomAt(Vector2 screenPosition, float factor)
    {
        var beforeValid = TryProjectToGround(screenPosition, out var before);
        var oldSize = _camera.Size;
        var newSize = Math.Clamp(_targetSize * factor, MinimumSizeMeters, MaximumSizeMeters);
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
        var speedScale = _targetSize / 18.0f;
        if (Input.IsPhysicalKeyPressed(Key.Shift))
            speedScale *= 2.0f;
        Position += (right * input.X + forward * input.Y).Normalized() *
            KeyboardPanMetersPerSecond * speedScale * delta;
    }

    private Vector2 EdgePan()
    {
        if (DisplayServer.GetName() == "headless" || !DisplayServer.WindowIsFocused())
            return Vector2.Zero;
        var viewport = GetViewport().GetVisibleRect();
        var mouse = GetViewport().GetMousePosition();
        if (!viewport.HasPoint(mouse) || mouse.X < 940.0f && mouse.Y > viewport.Size.Y - 200.0f ||
            _orbitDragging || _panDragging)
            return Vector2.Zero;
        var result = Vector2.Zero;
        if (mouse.X <= EdgeMarginPixels)
            result.X -= 1.0f;
        else if (mouse.X >= viewport.Size.X - EdgeMarginPixels)
            result.X += 1.0f;
        if (mouse.Y <= EdgeMarginPixels)
            result.Y += 1.0f;
        else if (mouse.Y >= viewport.Size.Y - EdgeMarginPixels)
            result.Y -= 1.0f;
        return result;
    }

    private bool TryProjectToGround(Vector2 screenPosition, out Vector3 point)
    {
        var origin = _camera.ProjectRayOrigin(screenPosition);
        var direction = _camera.ProjectRayNormal(screenPosition);
        if (MathF.Abs(direction.Y) <= 1.0e-5f)
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
