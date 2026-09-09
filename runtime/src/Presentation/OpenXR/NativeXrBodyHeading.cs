using Godot;

namespace OpenNV.Runtime.Presentation.OpenXR;

// Head yaw can move inside the neck's envelope without dragging the torso.
// The body catches up during locomotion or a turn beyond that envelope.
internal sealed class NativeXrBodyHeading
{
    private float? _yaw;
    internal Basis Advance(Transform3D head, bool moving, double seconds)
    {
        var forward = -head.Basis.Z;
        if (new Vector2(forward.X, forward.Z).LengthSquared() < .0001f)
            return new Basis(Vector3.Up, _yaw ?? 0);
        var target = MathF.Atan2(-forward.X, -forward.Z);
        _yaw ??= target;
        var difference = MathF.IEEERemainder(target - _yaw.Value, MathF.Tau);
        if (moving || MathF.Abs(difference) > Mathf.DegToRad(50))
        {
            var requested = moving ? difference : difference - MathF.CopySign(Mathf.DegToRad(30), difference);
            var amount = Math.Min((float)seconds, .05f) * 2.5f;
            _yaw += Math.Clamp(requested, -amount, amount);
        }
        return new Basis(Vector3.Up, _yaw.Value);
    }
}
