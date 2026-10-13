using System.Runtime.CompilerServices;

namespace OpenNV.Runtime.World.Physics;

internal static class FalloutPenetrationImpulse
{
    internal const uint ProjectionLengthEpsilonBits = 0x34000000;
    internal static void Apply(IFalloutPenetrationBody body, int bone,
        IReadOnlyList<FalloutPenetrationContact> contacts, FalloutPenetrationEffectiveSettings settings,
        IFalloutPenetrationBackend backend)
    {
        var point = default(FalloutPhysicalVector3);
        var direction = backend.InitialImpulseDirection();
        foreach (var contact in contacts)
            if (contact.Bone == bone) { point = contact.Point; direction = contact.Direction; }
        var arm = Subtract(point, body.ReadCenterOfMass());
        var velocity = Add(body.ReadLinearVelocity(), Cross(body.ReadAngularVelocity(), arm));
        var normalized = backend.Normalize(velocity);
        var length = BitConverter.UInt32BitsToSingle(normalized.LengthBits);
        var projected = length > BitConverter.UInt32BitsToSingle(ProjectionLengthEpsilonBits)
            ? FalloutPhysicalFloat32.Multiply(
                BitConverter.UInt32BitsToSingle(backend.DotBits(direction, normalized.Direction)), length)
            : 0f;
        var inverseMass = BitConverter.UInt32BitsToSingle(body.ReadInverseMassBits());
        var mass = inverseMass == 0f ? 0f : Divide(1f, inverseMass);
        var scalar = FalloutPhysicalFloat32.Multiply(mass,
            FalloutPhysicalFloat32.Subtract(BitConverter.UInt32BitsToSingle(settings.DesiredVelocityBits), projected));
        var limit = BitConverter.UInt32BitsToSingle(settings.ImpulseLimitBits);
        if (scalar > limit) scalar = limit;
        if (scalar < -limit) scalar = -limit;
        var impulse = Multiply(direction, scalar);
        body.Activate();
        body.ApplyPointImpulse(impulse, point);
    }

    internal static FalloutPhysicalVector3 Add(FalloutPhysicalVector3 left, FalloutPhysicalVector3 right) =>
        FalloutPhysicalVector3.FromValues(FalloutPhysicalFloat32.Add(left.XValue, right.XValue),
            FalloutPhysicalFloat32.Add(left.YValue, right.YValue), FalloutPhysicalFloat32.Add(left.ZValue, right.ZValue));

    internal static FalloutPhysicalVector3 Subtract(FalloutPhysicalVector3 left, FalloutPhysicalVector3 right) =>
        FalloutPhysicalVector3.FromValues(FalloutPhysicalFloat32.Subtract(left.XValue, right.XValue),
            FalloutPhysicalFloat32.Subtract(left.YValue, right.YValue), FalloutPhysicalFloat32.Subtract(left.ZValue, right.ZValue));

    internal static FalloutPhysicalVector3 Multiply(FalloutPhysicalVector3 vector, float scalar) =>
        FalloutPhysicalVector3.FromValues(FalloutPhysicalFloat32.Multiply(vector.XValue, scalar),
            FalloutPhysicalFloat32.Multiply(vector.YValue, scalar), FalloutPhysicalFloat32.Multiply(vector.ZValue, scalar));

    private static FalloutPhysicalVector3 Cross(FalloutPhysicalVector3 left, FalloutPhysicalVector3 right) =>
        FalloutPhysicalVector3.FromValues(Component(left.YValue, right.ZValue, left.ZValue, right.YValue),
            Component(left.ZValue, right.XValue, left.XValue, right.ZValue),
            Component(left.XValue, right.YValue, left.YValue, right.XValue));

    private static float Component(float a, float b, float c, float d) => FalloutPhysicalFloat32.Subtract(
        FalloutPhysicalFloat32.Multiply(a, b), FalloutPhysicalFloat32.Multiply(c, d));

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static float Divide(float numerator, float denominator) => numerator / denominator;
}
