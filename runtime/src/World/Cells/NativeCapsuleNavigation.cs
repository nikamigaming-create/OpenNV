using Godot;

namespace OpenNV.Runtime.World.Cells;

// A short-lived refinement of source NAVM intent against the same native body
// and collision mask used by movement. Queries never move that body. Height is
// part of a node identity, so a bridge cannot join the floor beneath it.
internal static class NativeCapsuleNavigation
{
    internal static (Vector3 Target, int Resume) CorridorPrefix(Vector3 start, IReadOnlyList<Vector3> path, float length)
    {
        if (path.Count == 0 || !float.IsFinite(length) || length <= 0)
            throw new ArgumentOutOfRangeException(nameof(length));
        var previous = start;
        for (var index = 0; index < path.Count; index++)
        {
            var distance = previous.DistanceTo(path[index]);
            if (distance > length) return (previous.Lerp(path[index], length / distance), index);
            length -= distance;
            previous = path[index];
        }
        return (path[^1], path.Count);
    }

    internal static IReadOnlyList<Vector3> Find(CharacterBody3D body, Vector3 start, Vector3 target,
        float stepHeight, float spacing, Func<Vector3, bool> resident, int maximumNodes = 1200)
    {
        if (!start.IsFinite() || !target.IsFinite() || stepHeight <= 0 || spacing <= 0 || maximumNodes <= 0)
            throw new ArgumentOutOfRangeException(nameof(stepHeight));
        using var query = new PhysicsTestMotionParameters3D { Margin = body.SafeMargin, MaxCollisions = 4 };
        using var hit = new PhysicsTestMotionResult3D();
        var rid = body.GetRid();
        var basis = body.GlobalBasis;
        var floorCosine = MathF.Cos(body.FloorMaxAngle);
        bool Sweep(Vector3 from, Vector3 motion)
        {
            query.From = new(basis, from); query.Motion = motion;
            return PhysicsServer3D.BodyTestMotion(rid, query, hit);
        }
        bool Edge(Vector3 from, Vector3 desired, out Vector3 landing)
        {
            landing = default;
            if (!resident(from) || !resident(desired)) return false;
            var motion = desired - from; motion.Y = 0;
            var supportedFrom = from + motion;
            var drop = stepHeight + body.SafeMargin * 8;
            // Ordinary flat motion needs no step headroom. Only test a raised
            // sweep when the direct capsule motion actually meets an obstacle.
            if (Sweep(from, motion))
            {
                var lift = Vector3.Up * (stepHeight + body.SafeMargin * 4);
                if (Sweep(from, lift) || Sweep(from + lift, motion)) return false;
                supportedFrom += lift;
                drop += lift.Y;
            }
            if (!Sweep(supportedFrom, Vector3.Down * drop) || !Enumerable.Range(0, hit.GetCollisionCount())
                .Any(index => hit.GetCollisionNormal(index).Dot(Vector3.Up) >= floorCosine)) return false;
            landing = supportedFrom + hit.GetTravel();
            return Math.Abs(landing.Y - from.Y) <= stepHeight + body.SafeMargin * 8 && resident(landing);
        }
        static float Flat(Vector3 a, Vector3 b) => new Vector2(a.X - b.X, a.Z - b.Z).Length();
        (int X, int Z, int Y) Key(Vector3 p) => ((int)MathF.Round((p.X - start.X) / spacing),
            (int)MathF.Round((p.Z - start.Z) / spacing), (int)MathF.Round(p.Y / .2f));
        var positions = new Dictionary<(int X, int Z, int Y), Vector3>();
        var costs = new Dictionary<(int X, int Z, int Y), float>();
        var parents = new Dictionary<(int X, int Z, int Y), (int X, int Z, int Y)>();
        var open = new PriorityQueue<(int X, int Z, int Y), float>();
        var closed = new HashSet<(int X, int Z, int Y)>();
        var first = Key(start); positions.Add(first, start); costs.Add(first, 0); open.Enqueue(first, Flat(start, target));
        var bound = Math.Max(8, Flat(start, target) + 8);
        var nearest = float.PositiveInfinity;
        while (open.TryDequeue(out var current, out _) && closed.Count < maximumNodes)
        {
            if (!closed.Add(current)) continue;
            var from = positions[current];
            nearest = Math.Min(nearest, from.DistanceTo(target));
            if (Flat(from, target) <= spacing * 1.5f && Edge(from, target, out var goal) && Math.Abs(goal.Y - target.Y) < .6f)
            {
                var path = new List<Vector3> { goal };
                while (current != first) { path.Add(positions[current]); current = parents[current]; }
                path.Reverse();
                return path;
            }
            for (var z = -1; z <= 1; z++)
                for (var x = -1; x <= 1; x++)
                {
                    if (x == 0 && z == 0) continue;
                    var candidate = new Vector3(start.X + (current.X + x) * spacing, from.Y,
                        start.Z + (current.Z + z) * spacing);
                    if (Flat(start, candidate) > bound || !Edge(from, candidate, out var next)) continue;
                    var key = Key(next);
                    var cost = costs[current] + from.DistanceTo(next);
                    if (closed.Contains(key) || costs.TryGetValue(key, out var old) && old <= cost) continue;
                    costs[key] = cost; positions[key] = next; parents[key] = current;
                    open.Enqueue(key, cost + Flat(next, target) + Math.Abs(next.Y - target.Y));
                }
        }
        throw new InvalidOperationException($"No supported capsule route within {closed.Count} native collision nodes; nearest target distance={nearest:F3}m.");
    }
}
