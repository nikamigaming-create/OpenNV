using Godot;

namespace OpenNV.Runtime.World.Actors;

// Standard non-creature character-controller branch. Its cached hull is fixed
// in world units; visual skeleton and reference scales do not enlarge it.
internal sealed class NativeHumanoidMovementEnvelope
{
    private const float GameToHavok = .142875f;
    private const float ConvexRadiusHavok = .1f;
    internal float Radius { get; }
    internal float Height { get; }
    internal float Margin { get; }
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
        coreVertices = CorePoints.Length,
        sourcePlanes = CorePlanes.Length,
        rootContactOffset = 0f
    };

    internal NativeHumanoidMovementEnvelope(float unitsToMetres)
    {
        if (!float.IsFinite(unitsToMetres) || unitsToMetres <= 0)
            throw new InvalidDataException("Humanoid controller units are invalid.");
        var toMetres = unitsToMetres / GameToHavok;
        var radius = (23f + 17.5f) * .5f * GameToHavok;
        var halfHeight = 64f * GameToHavok;
        var step = Math.Max(31f * GameToHavok, .75f * radius);
        Radius = radius * toMetres; Height = halfHeight * 2 * toMetres;
        Margin = ConvexRadiusHavok * toMetres;
        // Convert the constructor's centered core plus controller-center offset.
        // The radius outside the bottom core pole places actual support at zero.
        var center = halfHeight + ConvexRadiusHavok;
        var lower = -halfHeight + step + ConvexRadiusHavok;
        var upper = halfHeight - radius * .5f;
        const float diagonal = .7071f;
        Vector2[] ring = [new(1, 0), new(diagonal, diagonal), new(0, 1), new(-diagonal, diagonal),
            new(-1, 0), new(-diagonal, -diagonal), new(0, -1), new(diagonal, -diagonal)];
        CorePoints = new Vector3[18];
        CorePoints[0] = Vector3.Up * ((-halfHeight + center) * toMetres);
        CorePoints[17] = Vector3.Up * ((halfHeight + center) * toMetres);
        for (var i = 0; i < ring.Length; ++i)
        {
            CorePoints[1 + i] = new(ring[i].X * Radius, (lower + center) * toMetres, -ring[i].Y * Radius);
            CorePoints[9 + i] = new(ring[i].X * Radius, (upper + center) * toMetres, -ring[i].Y * Radius);
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

    internal void Publish(CollisionShape3D node, float actorScale)
    {
        if (!float.IsFinite(actorScale) || actorScale <= 0)
            throw new InvalidDataException("Humanoid controller reference scale is invalid.");
        // Jolt limits its radius by local hull error and the configured fraction.
        // Refuse an unsupported backend/scale instead of silently changing support.
        if (ProjectSettings.GetSetting("physics/3d/physics_engine").AsString() != "Jolt Physics" ||
            ProjectSettings.GetSetting("physics/jolt_physics_3d/collisions/collision_margin_fraction", .08f).AsDouble() < Margin / Radius ||
            _roundingError / actorScale > .05f)
            throw new NotSupportedException("Humanoid source convex radius cannot be retained by the configured physics backend/scale.");
        node.Position = Vector3.Zero;
        node.Quaternion = Quaternion.Identity;
        node.Shape = new ConvexPolygonShape3D
        {
            Points = _outerPoints.Select(point => point / actorScale).ToArray(),
            Margin = Margin / actorScale,
        };
    }
}
