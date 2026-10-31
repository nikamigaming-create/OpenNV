using Godot;
using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.World.Actors;

// Ordinary BBX-driven horizontal character controllers. Positions/radii are
// world metres; the actor adapter removes its reference scale when publishing
// child shapes. Vertical/custom controllers remain a separate capability.
internal sealed record NativeActorMovementEnvelope(float Radius, float AuthoredHeight, string Kind,
    IReadOnlyList<NativeActorMovementCapsule> Capsules)
{
    internal float RootContactOffset => Math.Max(0, Capsules.Min(capsule =>
        Math.Min(capsule.One.Y, capsule.Two.Y) - capsule.Radius));

    internal static NativeActorMovementEnvelope? Horizontal(FalloutNifVector3 sourceCenter,
        FalloutNifVector3 sourceHalfExtents, float worldScale, float unitsToMetres)
    {
        var center = new Vector3(sourceCenter.X, sourceCenter.Y, sourceCenter.Z);
        var half = new Vector3(sourceHalfExtents.X, sourceHalfExtents.Y, sourceHalfExtents.Z);
        if (!center.IsFinite() || !half.IsFinite() || half.X <= 0 || half.Y <= 0 || half.Z <= 0 ||
            !float.IsFinite(worldScale) || worldScale <= 0 || !float.IsFinite(unitsToMetres) || unitsToMetres <= 0)
            throw new InvalidDataException("Source character bounds require finite positive extents and scale.");
        if (half.Z >= Math.Max(half.X, half.Y)) return null;
        half *= worldScale * unitsToMetres;
        center *= worldScale * unitsToMetres;
        if (!half.IsFinite() || !center.IsFinite()) throw new InvalidDataException("Source character bounds overflowed their world scale.");
        var alongX = half.X > half.Y;
        var major = alongX ? half.X : half.Y;
        var cross = alongX ? half.Y : half.X;
        var radius = Math.Min(cross, half.Z);
        var other = Math.Max(cross, half.Z);
        // The source controller mirrors the positive long-axis endpoint; its
        // authored center participates before that mirroring operation.
        var end = major - radius + (alongX ? center.X : center.Y);
        var controllerCenter = new Vector3(center.X, center.Y, 0);
        Vector3 Point(float longitudinal, float z) => alongX
            ? new(longitudinal, center.Y, z) : new(center.X, longitudinal, z);
        static Vector3 Convert(Vector3 value) => new(value.X, value.Z, -value.Y);
        var capsules = new List<NativeActorMovementCapsule>();
        void Add(float r, Vector3 a, Vector3 b)
        {
            if (!float.IsFinite(r) || r <= 0 || !a.IsFinite() || !b.IsFinite())
                throw new InvalidDataException("Source character capsule dimensions are invalid.");
            capsules.Add(new(r, Convert(a + controllerCenter), Convert(b + controllerCenter)));
        }
        // The default controller contact separation is world-space, independent
        // of reference/model scaling (two 0.005m keep-distance intervals).
        const float clearance = .01f;
        if (other * .75f <= radius)
        {
            controllerCenter.Z = radius + clearance;
            Add(radius, Point(end, 0), Point(-end, 0));
        }
        else
        {
            controllerCenter.Z = clearance;
            var lower = other - radius;
            var lowerRadius = lower - unitsToMetres;
            var inset = 8 * unitsToMetres;
            Add(radius, Point(end, other * 2 - radius), Point(-end, other * 2 - radius));
            // The two lower supports offset source Y, including when X is
            // the selected long axis. Do not rotate this fixed source rule.
            var positive = Point(end, lower); positive.Y -= inset;
            var negative = Point(-end, lower); negative.Y += inset;
            Add(lowerRadius, positive + Vector3.Back * unitsToMetres, positive - Vector3.Back * unitsToMetres);
            Add(lowerRadius, negative + Vector3.Back * unitsToMetres, negative - Vector3.Back * unitsToMetres);
        }
        return new(radius, half.Z * 2, capsules.Count == 1 ? "horizontal-capsule" : "horizontal-three-capsule",
            capsules.ToArray());
    }
}

internal sealed record NativeActorMovementCapsule(float Radius, Vector3 One, Vector3 Two)
{
    internal void Publish(CollisionShape3D node, float actorScale)
    {
        if (!float.IsFinite(actorScale) || actorScale <= 0) throw new InvalidDataException("Actor scale is invalid.");
        var axis = Two - One;
        var length = axis.Length();
        node.Position = (One + Two) * (.5f / actorScale);
        node.Quaternion = length > 0 ? new Quaternion(Vector3.Up, axis / length) : Quaternion.Identity;
        node.Shape = new CapsuleShape3D { Radius = Radius / actorScale, Height = (length + Radius * 2) / actorScale };
    }
}
