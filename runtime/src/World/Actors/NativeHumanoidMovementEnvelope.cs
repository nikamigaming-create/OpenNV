using Godot;

namespace OpenNV.Runtime.World.Actors;

// Standard non-creature character-controller branch. Its cached hull is fixed
// in world units; visual skeleton and reference scales do not enlarge it.
internal sealed class NativeHumanoidMovementEnvelope
{
    private const float HavokToGame = 6.9991254806518555f;
    private const float GameToHavok = 1f / HavokToGame;
    private const float ConvexRadiusHavok = .1f;
    internal float Radius { get; }
    internal float Height { get; }
    internal float Margin { get; }
    internal float CenterHeight { get; }
    internal float StepHeight { get; }
    internal float OuterRadius => Radius + Margin;
    internal float OuterHeight => Height + 2 * Margin;
    internal Vector3[] CorePoints { get; }
    internal Plane[] CorePlanes { get; }
    private readonly Vector3[] _outerPoints;
    private readonly float _roundingError;
    internal object Observation => new
    {
        kind = "standard-humanoid-convex",
        Radius,
        Height,
        Margin,
        CenterHeight,
        StepHeight,
        OuterRadius,
        OuterHeight,
        coreVertices = CorePoints.Length,
        sourcePlanes = CorePlanes.Length,
        rootContactOffset = 0f
    };

    internal NativeHumanoidMovementEnvelope(float unitsToMetres)
    {
        if (!float.IsFinite(unitsToMetres) || unitsToMetres <= 0)
            throw new InvalidDataException("Humanoid controller units are invalid.");
        var toMetres = unitsToMetres * HavokToGame;
        var radius = (23f + 17.5f) * .5f * GameToHavok;
        var halfHeight = 64f * GameToHavok;
        var step = Math.Min(31f * GameToHavok, .75f * halfHeight);
        StepHeight = step * toMetres;
        Radius = radius * toMetres; Height = halfHeight * 2 * toMetres;
        Margin = ConvexRadiusHavok * toMetres;
        // Convert the constructor's centered core plus controller-center offset.
        // The radius outside the bottom core pole places actual support at zero.
        var center = halfHeight + ConvexRadiusHavok;
        // Source endpoint conversion and final stores precede unit conversion.
        var endpoint = (64f - (23f + 17.5f) * .5f) * GameToHavok;
        var bottom = -endpoint - radius;
        var lower = (float)((double)bottom + step + ConvexRadiusHavok);
        var upper = (float)((double)endpoint + radius * .5);
        CenterHeight = center * toMetres;
        const float diagonal = .7071f;
        Vector2[] ring = [new(1, 0), new(diagonal, diagonal), new(0, 1), new(-diagonal, diagonal),
            new(-1, 0), new(-diagonal, -diagonal), new(0, -1), new(diagonal, -diagonal)];
        CorePoints = new Vector3[18];
        CorePoints[0] = Vector3.Up * ((bottom + center) * toMetres);
        CorePoints[17] = Vector3.Up * (((endpoint + radius) + center) * toMetres);
        for (var i = 0; i < ring.Length; ++i)
        {
            CorePoints[1 + i] = new((ring[i].X * radius) * toMetres, (lower + center) * toMetres, -(ring[i].Y * radius) * toMetres);
            CorePoints[9 + i] = new((ring[i].X * radius) * toMetres, (upper + center) * toMetres, -(ring[i].Y * radius) * toMetres);
        }
        var planes = new List<Plane>(24);
        var interior = Vector3.Up * (center * toMetres);
        void Face(int one, int two, int three)
        {
            var normal = (CorePoints[two] - CorePoints[one]).Cross(CorePoints[three] - CorePoints[one]).Normalized();
            if (normal.Dot(interior - CorePoints[one]) > 0) normal = -normal;
            planes.Add(new(normal, normal.Dot(CorePoints[one])));
        }
        for (var i = 0; i < ring.Length; ++i)
        {
            var next = (i + 1) % ring.Length;
            Face(0, 1 + i, 1 + next);
            Face(1 + i, 9 + i, 1 + next);
            Face(17, 9 + next, 9 + i);
        }
        CorePlanes = planes.ToArray();
        // Jolt rounds inward from its input hull. Offset its input planes first
        // so inward rounding recovers the source core and its external radius.
        // This is distinct from inflating a movement tolerance or a step limit.
        var outer = new Godot.Collections.Array<Plane>();
        foreach (var plane in CorePlanes) outer.Add(new(plane.Normal, plane.D + Margin));
        _outerPoints = Geometry3D.ComputeConvexMeshPoints(outer);
        if (_outerPoints.Length < 4 || _outerPoints.Any(point => !point.IsFinite()))
            throw new InvalidDataException("Humanoid controller hull could not be constructed.");
        _roundingError = _outerPoints.Max(point => CorePoints.Min(core => point.DistanceTo(core))) - Margin;
    }

    internal float SupportLift(Vector3 localNormal)
    {
        if (!localNormal.IsFinite() || Math.Abs(localNormal.LengthSquared() - 1) > .0001f || localNormal.Y <= 0)
            throw new InvalidDataException("Humanoid support normal is invalid.");
        return Math.Max(0, (Margin - CorePoints.Min(point => localNormal.Dot(point))) / localNormal.Y);
    }

    internal void Publish(CollisionShape3D node, float actorScale)
    {
        var shape = CreateShape(actorScale);
        node.Position = Vector3.Zero;
        node.Quaternion = Quaternion.Identity;
        node.Shape = shape;
    }

    internal ConvexPolygonShape3D CreateShape(float actorScale)
    {
        if (!float.IsFinite(actorScale) || actorScale <= 0)
            throw new InvalidDataException("Humanoid controller reference scale is invalid.");
        // Jolt limits its radius by local hull error and the configured fraction.
        // Refuse an unsupported backend/scale instead of silently changing support.
        if (ProjectSettings.GetSetting("physics/3d/physics_engine").AsString() != "Jolt Physics" ||
            ProjectSettings.GetSetting("physics/jolt_physics_3d/collisions/collision_margin_fraction", .08f).AsDouble() < Margin / Radius ||
            _roundingError / actorScale > .05f)
            throw new NotSupportedException("Humanoid source convex radius cannot be retained by the configured physics backend/scale.");
        return new ConvexPolygonShape3D
        {
            Points = _outerPoints.Select(point => point / actorScale).ToArray(),
            Margin = Margin / actorScale,
        };
    }
}
