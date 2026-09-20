using Godot;

namespace OpenNV.Runtime.World.Cells;

// Source NAVM portals are intent, not clearance. Project onto the native floor,
// then test the complete existing body at that point without moving it.
internal sealed class NativeCapsulePlacementQuery(CharacterBody3D body) : IDisposable
{
    private readonly PhysicsTestMotionParameters3D _parameters = new()
    { Margin = body.SafeMargin, MaxCollisions = 8, RecoveryAsCollision = true };
    private readonly PhysicsTestMotionResult3D _result = new();
    private readonly Dictionary<Vector3, bool> _results = [];
    internal int Rejected { get; private set; }

    internal bool CanStand(Vector3 point)
    {
        if (_results.TryGetValue(point, out var cached)) return cached;
        var clear = Query(point);
        _results.Add(point, clear);
        if (!clear) Rejected++;
        return clear;
    }

    private bool Query(Vector3 point)
    {
        using var ray = PhysicsRayQueryParameters3D.Create(point + Vector3.Up * .6f,
            point - Vector3.Up * .6f, body.CollisionMask, [body.GetRid()]);
        using var floor = body.GetWorld3D().DirectSpaceState.IntersectRay(ray);
        var floorCosine = MathF.Cos(body.FloorMaxAngle);
        if (floor.Count == 0 || floor["normal"].AsVector3().Dot(Vector3.Up) < floorCosine) return false;
        _parameters.From = new(body.GlobalBasis, floor["position"].AsVector3() + Vector3.Up * body.SafeMargin * 4);
        _parameters.Motion = Vector3.Down * body.SafeMargin * 8;
        if (!PhysicsServer3D.BodyTestMotion(body.GetRid(), _parameters, _result)) return false;
        return _result.GetCollisionCount() != 0 && Enumerable.Range(0, _result.GetCollisionCount())
            .All(index => _result.GetCollisionNormal(index).Dot(Vector3.Up) >= floorCosine);
    }

    public void Dispose() { _parameters.Dispose(); _result.Dispose(); }
}
