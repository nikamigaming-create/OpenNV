using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Formats.Gamebryo;

// A dynamic Havok attachment drives its source visual target. The physics body
// stays in world space so publishing its pose cannot feed it back through the
// visual parent's transform a second time.
internal partial class RuntimeNifRigidBody : RigidBody3D
{
    private Node3D _visual = null!;
    private Transform3D _bodyToVisual;
    private FalloutSoundRandomState? _windRandom;
    internal bool RespondsToWind => (GetMeta("opennv_nif_body_flags", 0).AsUInt32() & FalloutWindForce.ResponsiveBodyFlag) != 0;

    internal void ApplyWind(float speed, float heading, double seconds, float unitsToMetres)
    {
        if (!RespondsToWind || Freeze || !CanProcess() || speed == 0) return;
        _windRandom ??= new(BitConverter.ToUInt64(System.Security.Cryptography.RandomNumberGenerator.GetBytes(sizeof(ulong))));
        var force = FalloutWindForce.Sample(speed, heading, seconds, _windRandom.NextUnitFloat(), _windRandom.NextUnitFloat());
        // Force has mass * distance / time² units. Convert Havok distance to
        // metres; Godot integrates time and mass, so neither is applied here.
        Sleeping = false;
        ApplyCentralForce(GamebryoCoordinate.ConvertVector(new(force.X, force.Y, force.Z)) * (7 * unitsToMetres));
    }

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
