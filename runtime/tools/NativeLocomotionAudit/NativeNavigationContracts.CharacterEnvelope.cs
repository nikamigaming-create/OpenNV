using Godot;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;

internal static partial class NativeNavigationContracts
{
    private static void CheckSourceCharacterEnvelopes()
    {
        const float units = .0142875f;
        var dog = NativeActorMovementEnvelope.Horizontal(new(0, 0, 35), new(30, 63, 35), 1, units)!;
        if (dog.Capsules.Count != 1 || Math.Abs(dog.Radius - 30 * units) > .000001f ||
            dog.Capsules[0].One.DistanceTo(new(0, 30 * units + .01f, -33 * units)) > .000001f ||
            dog.Capsules[0].Two.DistanceTo(new(0, 30 * units + .01f, 33 * units)) > .000001f)
            throw new InvalidOperationException("Horizontal source bounds lost capsule axis, radius, endpoints or contact separation.");
        var shape = new CollisionShape3D();
        try
        {
            dog.Capsules[0].Publish(shape, .7f);
            var capsule = (CapsuleShape3D)shape.Shape!;
            if (Math.Abs(capsule.Height * .7f - 126 * units) > .000001f ||
                Math.Abs(capsule.Radius * .7f - dog.Radius) > .000001f ||
                Math.Abs(shape.Basis.Y.Dot(Vector3.Forward)) < .99999f)
                throw new InvalidOperationException("Published horizontal capsule lost its full length, reference scale or axis.");
        }
        finally { shape.Free(); }
        var centeredY = NativeActorMovementEnvelope.Horizontal(new(4, 7, 9), new(30, 60, 35), 1, units)!;
        var centeredX = NativeActorMovementEnvelope.Horizontal(new(4, 7, 9), new(60, 30, 35), 1, units)!;
        var z = 30 * units + .01f;
        if (centeredY.Capsules[0].One.DistanceTo(new(8 * units, z, -44 * units)) > .000001f ||
            centeredY.Capsules[0].Two.DistanceTo(new(8 * units, z, 30 * units)) > .000001f ||
            centeredX.Capsules[0].One.DistanceTo(new(38 * units, z, -14 * units)) > .000001f ||
            centeredX.Capsules[0].Two.DistanceTo(new(-30 * units, z, -14 * units)) > .000001f)
            throw new InvalidOperationException("Source character center participation changed before/after endpoint mirroring.");
        var wide = NativeActorMovementEnvelope.Horizontal(new(4, 7, 9), new(60, 30, 50), 1, units)!;
        if (wide.Capsules.Count != 3 ||
            wide.Capsules[1].One.DistanceTo(new(38 * units, 21 * units + .01f, -6 * units)) > .000001f ||
            wide.Capsules[2].Two.DistanceTo(new(-30 * units, 19 * units + .01f, -22 * units)) > .000001f)
            throw new InvalidOperationException("Source X-axis compound rotated the fixed source-Y lower-support inset.");
        var compound = NativeActorMovementEnvelope.Horizontal(new(0, 23.5f, 55), new(35, 56, 55), 1.05f, units)!;
        if (compound.Capsules.Count != 3 || Math.Abs(compound.Capsules[0].Radius - 35 * 1.05f * units) > .000001f ||
            Math.Abs(compound.Capsules[1].Radius - 20 * units) > .000001f ||
            Math.Abs(compound.Capsules[1].Two.Y - (20 * units + .01f)) > .000001f ||
            Math.Abs(compound.Capsules[0].One.Y - (75 * 1.05f * units + .01f)) > .000001f)
            throw new InvalidOperationException("Horizontal compound source bounds lost upper/lower capsule composition or fixed-unit offsets.");
        if (NativeActorMovementEnvelope.Horizontal(new(0, 0, 60), new(20, 20, 60), 1, units) is not null)
            throw new InvalidOperationException("Horizontal source controller replaced an unqualified vertical/custom controller.");
        var humanoid = new NativeHumanoidMovementEnvelope(units);
        if (humanoid.CorePoints.Length != 18 || humanoid.CorePlanes.Length != 24 ||
            Math.Abs(humanoid.Radius - .289321875f) > .000001f ||
            Math.Abs(humanoid.Height - 1.8288f) > .000001f ||
            Math.Abs(humanoid.CorePoints[0].Y - humanoid.Margin) > .000001f ||
            Math.Abs(humanoid.CorePoints[1].Y - .4629125f) > .000001f ||
            Math.Abs(humanoid.CorePoints[9].Y - 1.6941391f) > .000001f)
            throw new InvalidOperationException("Standard humanoid hull lost its source dimensions, taper, center or floor support.");
        var humanoidShape = new CollisionShape3D();
        try
        {
            Vector3[]? worldPoints = null;
            foreach (var referenceScale in new[] { .7f, 1.3f })
            {
                humanoid.Publish(humanoidShape, referenceScale);
                var convex = (ConvexPolygonShape3D)humanoidShape.Shape!;
                var points = convex.Points.Select(point => point * referenceScale).ToArray();
                if (Math.Abs(convex.Margin * referenceScale - humanoid.Margin) > .000001f ||
                    humanoidShape.Position != Vector3.Zero || worldPoints is not null &&
                    points.Where((point, index) => point.DistanceTo(worldPoints[index]) > .000001f).Any())
                    throw new InvalidOperationException("Visual actor scale changed the standard controller's world envelope.");
                foreach (var plane in humanoid.CorePlanes)
                    if (Math.Abs(points.Max(point => plane.Normal.Dot(point)) - plane.D - humanoid.Margin) > .00001f)
                        throw new InvalidOperationException("Jolt hull rounding changed a source supporting plane.");
                worldPoints = points;
            }
        }
        finally { humanoidShape.Free(); }
        var basis = NativeCapsuleNavigation.PlanningBasis(Basis.Identity.Scaled(new(2, 2, 2)), Vector3.Zero, Vector3.Right);
        if ((basis * Vector3.Forward).Normalized().Dot(Vector3.Right) < .99999f ||
            basis.Scale.DistanceTo(new(2, 2, 2)) > .000001f)
            throw new InvalidOperationException("Directional source capsule planning lost intended heading or body scale.");
        Console.WriteLine("OPENNV_CHARACTER_ENVELOPE_CONTRACT_PASS simple=true compound=true fullLength=true scale=true clearance=true planningHeading=true standardHumanoid=true creatureVerticalCustomSeparate=true gameplay=unverified");
    }
}
