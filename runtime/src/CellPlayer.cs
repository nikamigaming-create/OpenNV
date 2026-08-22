using Godot;

namespace OpenNV.Runtime;

internal partial class CellPlayer : CharacterBody3D
{
    private const float MoveSpeed = 3.6f;
    private const float MouseSensitivity = 0.0022f;
    private const float Gravity = 9.8f;

    private readonly Camera3D _camera = new()
    {
        Name = "DataDerivedEntryCamera",
        Position = new Vector3(0.0f, 0.72f, 0.0f),
        Near = 0.02f,
        Far = 200.0f,
        Current = true,
    };
    private GameplaySession? _session;

    internal Camera3D Camera => _camera;

    internal void Configure(float yaw, GameplaySession session)
    {
        _session = session;
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
        AddChild(_camera);
    }

    public override void _Ready()
    {
        if (DisplayServer.GetName() != "headless")
            Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    public override void _PhysicsProcess(double delta)
    {
        var left = Input.IsPhysicalKeyPressed(Key.A) ? 1.0f : 0.0f;
        var right = Input.IsPhysicalKeyPressed(Key.D) ? 1.0f : 0.0f;
        var forward = Input.IsPhysicalKeyPressed(Key.W) ? 1.0f : 0.0f;
        var backward = Input.IsPhysicalKeyPressed(Key.S) ? 1.0f : 0.0f;
        var direction = GlobalBasis.X * (right - left) - GlobalBasis.Z * (forward - backward);
        direction.Y = 0.0f;
        direction = direction.Normalized();

        var velocity = Velocity;
        velocity.X = direction.X * MoveSpeed;
        velocity.Z = direction.Z * MoveSpeed;
        velocity.Y = IsOnFloor() ? MathF.Min(velocity.Y, 0.0f) : velocity.Y - Gravity * (float)delta;
        Velocity = velocity;
        MoveAndSlide();
    }

    public override void _UnhandledInput(InputEvent inputEvent)
    {
        if (inputEvent is InputEventKey key && key.Pressed && !key.Echo)
        {
            if (key.PhysicalKeycode == Key.E)
                Activate();
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

    private void Activate()
    {
        var collider = Cast(2.5f);
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

    private Node? Cast(float distance)
    {
        var from = _camera.GlobalPosition;
        var to = from - _camera.GlobalBasis.Z * distance;
        var query = PhysicsRayQueryParameters3D.Create(from, to, 1);
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
        return hit.Count == 0 ? null : hit["collider"].AsGodotObject() as Node;
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
