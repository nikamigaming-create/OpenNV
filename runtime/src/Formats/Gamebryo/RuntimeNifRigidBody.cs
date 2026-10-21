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
    private readonly record struct GrabControl(object Owner, Vector3 LocalPoint, Vector3 Target,
        float AngularFrequency, float MaximumAcceleration, bool PreviousContinuousCd);
    private GrabControl? _grab;

    internal bool TryBeginGrab(object owner, Vector3 point, float responseHertz, float maximumAcceleration)
    {
        if (_grab is not null || Freeze) return false;
        var frequency = responseHertz * MathF.Tau;
        if (!point.IsFinite() || !float.IsFinite(responseHertz) || responseHertz <= 0 ||
            !float.IsFinite(frequency * frequency) || frequency * frequency <= 0 ||
            !float.IsFinite(maximumAcceleration) || maximumAcceleration <= 0)
            throw new InvalidDataException("Grab force configuration is invalid.");
        _grab = new(owner, ToLocal(point), point, frequency, maximumAcceleration, ContinuousCd);
        ContinuousCd = true;
        Sleeping = false;
        return true;
    }

    internal void SetGrabTarget(object owner, Vector3 target)
    {
        if (_grab is not { } grab || !ReferenceEquals(grab.Owner, owner))
            throw new InvalidOperationException("Grab target belongs to another owner.");
        if (!target.IsFinite()) throw new InvalidDataException("Grab target is not finite.");
        _grab = grab with { Target = target };
        Sleeping = false;
    }

    internal void ReleaseGrab(object owner)
    {
        if (_grab is not { } grab || !ReferenceEquals(grab.Owner, owner)) return;
        ContinuousCd = grab.PreviousContinuousCd;
        _grab = null;
        Sleeping = false;
    }
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
        if (_grab is { } grab)
        {
            var error = grab.Target - state.Transform * grab.LocalPoint;
            // A critically damped translational spring, capped before mass
            // and time conversion. Apply no pose or collision-mask changes.
            // Central force permits collision-driven rotation without an
            // unbounded point-joint torque. This is explicit adapter policy.
            var acceleration = error * (grab.AngularFrequency * grab.AngularFrequency) -
                state.LinearVelocity * (2 * grab.AngularFrequency) - state.TotalGravity;
            state.ApplyCentralImpulse(acceleration.LimitLength(grab.MaximumAcceleration) * Mass * state.Step);
        }
        if (_visual is not null) _visual.GlobalTransform = state.Transform * _bodyToVisual;
    }
}
