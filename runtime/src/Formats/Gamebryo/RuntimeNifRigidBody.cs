using Godot;

namespace OpenNV.Runtime.Formats.Gamebryo;

// A dynamic Havok attachment drives its source visual target. The physics body
// stays in world space so publishing its pose cannot feed it back through the
// visual parent's transform a second time.
internal partial class RuntimeNifRigidBody : RigidBody3D
{
    private Node3D _visual = null!;
    private Transform3D _bodyToVisual;

    public override void _Ready()
    {
        if (Freeze) return;
        _visual = GetParent<Node3D>();
        _bodyToVisual = Transform.AffineInverse();
        var world = GlobalTransform;
        TopLevel = true;
        GlobalTransform = world;
    }

    public override void _IntegrateForces(PhysicsDirectBodyState3D state)
    {
        if (_visual is not null) _visual.GlobalTransform = state.Transform * _bodyToVisual;
    }
}
