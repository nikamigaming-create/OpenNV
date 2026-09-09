using Godot;

namespace OpenNV.Runtime.Campaigns.Classic.Native;

internal sealed partial class ClassicWorldCamera : Camera3D
{
    internal Vector3 Focus;
    internal float Distance = 26;
    internal float Pitch = 0.8f;
    internal float MaximumDistance = 320;
    internal float MinimumDistance = 1.25f;
    internal float FramePadding = 1.15f;
    internal float SubjectHeight = 1.875f;
    private float _yaw = MathF.PI;
    private (Vector3 Focus, float Distance, float Pitch, float Yaw, float SubjectHeight)? _savedView;
    private double _shotSeconds = -1;
    internal void SaveView() { _savedView = (Focus, Distance, Pitch, _yaw, SubjectHeight); _shotSeconds = -1; }
    internal void RestoreView()
    {
        if (_savedView is not { } view) return;
        (Focus, Distance, Pitch, _yaw, SubjectHeight) = view; _shotSeconds = -1; UpdateCamera();
    }
    internal void CinematicPass() { if (_savedView is null) SaveView(); RestoreView(); _shotSeconds = 0; }
    internal void StopShot() => _shotSeconds = -1;
    internal event Action? ToggleGrid;
    internal event Action? ToggleRoofs;
    internal void SwitchGrid() => ToggleGrid?.Invoke();

    public override void _Ready() => UpdateCamera();

    public override void _Process(double delta)
    {
        if (_shotSeconds >= 0 && _savedView is { } view)
        {
            _shotSeconds = Math.Min(22, _shotSeconds + delta);
            var t = (float)_shotSeconds;
            static float Smooth(float value) { value = Math.Clamp(value, 0, 1); return value * value * (3 - 2 * value); }
            var close = Math.Max(MinimumDistance, Math.Max(Math.Min(view.Distance, view.SubjectHeight * 2), view.Distance * 0.32f));
            var pull = t < 7 ? Smooth(t / 7) : t > 16 ? 1 - Smooth((t - 16) / 6) : 1;
            Focus = view.Focus; Distance = Mathf.Lerp(view.Distance, close, pull);
            Pitch = Mathf.Lerp(view.Pitch, Math.Min(view.Pitch, 0.43f), pull);
            _yaw = view.Yaw + Smooth((t - 5) / 15) * 1.1f;
            UpdateCamera(); if (_shotSeconds == 22) _shotSeconds = -1;
            return;
        }
        var movement = Vector3.Zero;
        if (Input.IsPhysicalKeyPressed(Key.W)) movement.Z--;
        if (Input.IsPhysicalKeyPressed(Key.S)) movement.Z++;
        if (Input.IsPhysicalKeyPressed(Key.A)) movement.X--;
        if (Input.IsPhysicalKeyPressed(Key.D)) movement.X++;
        if (Input.IsPhysicalKeyPressed(Key.Q)) _yaw -= (float)delta;
        if (Input.IsPhysicalKeyPressed(Key.E)) _yaw += (float)delta;
        Focus += movement.Rotated(Vector3.Up, _yaw) * (float)delta * Distance * 0.45f;
        UpdateCamera();
    }

    public override void _UnhandledInput(InputEvent input)
    {
        if (input is InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.G }) ToggleGrid?.Invoke();
        if (input is InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.R }) ToggleRoofs?.Invoke();
        if (input is InputEventMouseButton { Pressed: true } button)
        {
            if (button.ButtonIndex == MouseButton.WheelUp) Distance = Math.Clamp(Distance * 0.86f, MinimumDistance, MaximumDistance);
            if (button.ButtonIndex == MouseButton.WheelDown) Distance = Math.Clamp(Distance / 0.86f, MinimumDistance, MaximumDistance);
        }
        if (input is InputEventMouseMotion motion)
        {
            if ((motion.ButtonMask & MouseButtonMask.Right) != 0)
            {
                _yaw -= motion.Relative.X * 0.005f;
                Pitch = Math.Clamp(Pitch + motion.Relative.Y * 0.004f, 0.28f, 1.45f);
            }
            if ((motion.ButtonMask & MouseButtonMask.Middle) != 0)
                Focus += new Vector3(-motion.Relative.X, 0, -motion.Relative.Y).Rotated(Vector3.Up, _yaw) * Distance * 0.0015f;
        }
    }

    internal void FrameLevel(Aabb bounds)
    {
        Focus = bounds.GetCenter();
        UpdateCamera();
        // Fit the selected elevation in the current orbit, including its
        // height. A different elevation need not share the MAP entry hex.
        var size = GetViewport().GetVisibleRect().Size;
        var aspect = size.X / Math.Max(1, size.Y);
        var tangent = Mathf.Tan(Mathf.DegToRad(Fov) / 2);
        var vertical = KeepAspect == KeepAspectEnum.Height ? tangent : tangent / aspect;
        var horizontal = vertical * aspect;
        var required = MinimumDistance;
        for (var corner = 0; corner < 8; corner++)
        {
            var point = Basis.Inverse() * (bounds.GetEndpoint(corner) - Focus);
            required = Math.Max(required, point.Z + FramePadding * Math.Max(Math.Abs(point.Y) / vertical, Math.Abs(point.X) / horizontal));
        }
        Distance = Math.Clamp(required, MinimumDistance, MaximumDistance);
        UpdateCamera();
    }

    private void UpdateCamera()
    {
        Position = Focus + new Vector3(0, MathF.Sin(Pitch), MathF.Cos(Pitch)).Rotated(Vector3.Up, _yaw) * Distance;
        LookAt(Focus, Vector3.Up);
    }
}
