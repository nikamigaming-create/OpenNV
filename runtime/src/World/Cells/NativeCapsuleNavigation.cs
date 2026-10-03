using System.Diagnostics;
using Godot;

namespace OpenNV.Runtime.World.Cells;

// A short-lived refinement of source NAVM intent against the same native body
// and collision mask used by movement. Queries never move that body. Height is
// part of a node identity, so a bridge cannot join the floor beneath it.
internal static partial class NativeCapsuleNavigation
{
    private static ulong _workFrame;
    private static double _workMilliseconds;

    internal static Vector3 RouteMotion(Vector3 position, Vector3 accumulatedMotion, Vector3 waypoint)
    {
        if (!position.IsFinite() || !accumulatedMotion.IsFinite() || !waypoint.IsFinite())
            throw new InvalidDataException("Native route motion requires finite source accumulation and a finite waypoint.");
        var direction = waypoint - position; direction.Y = 0;
        accumulatedMotion.Y = 0;
        var distance = direction.Length();
        return distance == 0 ? Vector3.Zero : direction / distance * Math.Min(distance, accumulatedMotion.Length());
    }

    // All runtime actor searches share this physics-thread budget. Queries
    // stay with their native owner and yield between node expansions; source
    // reads can use content workers, but physics is not safe worker-pool work.
    internal static bool Advance(IEnumerator<IReadOnlyList<Vector3>?> search, out IReadOnlyList<Vector3>? result)
    {
        var frame = Engine.GetPhysicsFrames();
        if (_workFrame != frame) { _workFrame = frame; _workMilliseconds = 0; }
        result = null;
        if (_workMilliseconds >= 2) return false;
        var started = Stopwatch.GetTimestamp();
        try
        {
            for (var nodes = 0; nodes < 16 && _workMilliseconds + Stopwatch.GetElapsedTime(started).TotalMilliseconds < 2; nodes++)
            {
                if (!search.MoveNext()) throw new InvalidOperationException("Capsule search ended without a route.");
                if (search.Current is not { } path) continue;
                result = path;
                return true;
            }
            return false;
        }
        finally { _workMilliseconds += Stopwatch.GetElapsedTime(started).TotalMilliseconds; }
    }

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
        float stepHeight, float spacing, Func<Vector3, bool> resident, int maximumNodes = 1200, float targetRadius = 0)
    {
        foreach (var result in Search(body, start, target, stepHeight, spacing, resident, maximumNodes, targetRadius: targetRadius))
            if (result is not null) return result;
        throw new InvalidOperationException("Capsule search ended without a route.");
    }

    internal static (IReadOnlyList<Vector3> Path, float Spacing, string? CoarseError) FindRefined(
        CharacterBody3D body, Vector3 start, Vector3 target, float stepHeight,
        float coarseSpacing, float refinedSpacing, Func<Vector3, bool> resident, int maximumNodes = 1200, float targetRadius = 0)
    {
        if (!float.IsFinite(coarseSpacing) || !float.IsFinite(refinedSpacing) ||
            coarseSpacing <= 0 || refinedSpacing <= 0 || refinedSpacing > coarseSpacing)
            throw new ArgumentOutOfRangeException(nameof(refinedSpacing));
        try { return (Find(body, start, target, stepHeight, coarseSpacing, resident, maximumNodes, targetRadius), coarseSpacing, null); }
        catch (InvalidOperationException coarse) when (refinedSpacing < coarseSpacing)
        {
            // A lattice can miss a supported passage narrower than its node
            // spacing. One finer search keeps the same body, sweep/floor rules,
            // residency predicate and node bound. It cannot create clearance.
            try { return (Find(body, start, target, stepHeight, refinedSpacing, resident, maximumNodes, targetRadius), refinedSpacing, coarse.Message); }
            catch (InvalidOperationException refined)
            {
                throw new InvalidOperationException($"Coarse capsule query: {coarse.Message} Refined capsule query: {refined.Message}", refined);
            }
        }
    }

    internal static IEnumerable<IReadOnlyList<Vector3>?> Search(CharacterBody3D body, Vector3 start, Vector3 target,
        float stepHeight, float spacing, Func<Vector3, bool> resident, int maximumNodes = 1200,
        NativeNavigationProbe? probe = null, float targetRadius = 0)
    {
        if (!start.IsFinite() || !target.IsFinite() || stepHeight <= 0 || spacing <= 0 || maximumNodes <= 0 ||
            !float.IsFinite(targetRadius) || targetRadius < 0)
            throw new ArgumentOutOfRangeException(nameof(stepHeight));
        using var query = new PhysicsTestMotionParameters3D { Margin = body.SafeMargin, MaxCollisions = 4 };
        using var hit = new PhysicsTestMotionResult3D();
        var rid = body.GetRid();
        using var supportRay = PhysicsRayQueryParameters3D.Create(Vector3.Zero, Vector3.Zero, body.CollisionMask, [rid]);
        var basis = body.GlobalBasis;
        var floorCosine = MathF.Cos(body.FloorMaxAngle);
        // A short descent includes the controller's supported step interval
        // below the raised sweep. Using the ascent limit in both directions
        // strands a capsule that ordinary sliding has carried onto a bevel.
        // The landing still requires a complete native sweep and floor support;
        // movement follows it with gravity rather than writing the query pose.
        var maximumDrop = stepHeight + Math.Max(stepHeight, body.FloorSnapLength);
        bool Sweep(Vector3 from, Vector3 motion)
        {
            query.From = new(basis, from); query.Motion = motion;
            return PhysicsServer3D.BodyTestMotion(rid, query, hit);
        }
        NativeNavigationContact? rejected = null;
        bool Edge(Vector3 from, Vector3 desired, out Vector3 landing, bool allowStep = true)
        {
            rejected = null;
            landing = default;
            if (!resident(from) || !resident(desired)) return false;
            var motion = desired - from; motion.Y = 0;
            var supportedFrom = from + motion;
            var drop = maximumDrop + body.SafeMargin * 8;
            // Ordinary flat motion needs no step headroom. Only test a raised
            // sweep when the direct capsule motion actually meets an obstacle.
            if (Sweep(from, motion))
            {
                var obstacle = Contact(hit, from, desired, floorCosine);
                if (!allowStep) { rejected = obstacle; return false; }
                var lift = Vector3.Up * (stepHeight + body.SafeMargin * 4);
                if (Sweep(from, lift) || Sweep(from + lift, motion)) { rejected = obstacle; return false; }
                supportedFrom += lift;
                drop += lift.Y;
            }
            if (!Sweep(supportedFrom, Vector3.Down * drop) || !Enumerable.Range(0, hit.GetCollisionCount())
                .Any(index => hit.GetCollisionNormal(index).Dot(Vector3.Up) >= floorCosine)) return false;
            landing = supportedFrom + hit.GetTravel();
            // A rounded capsule can touch a walkable normal on a ledge while
            // its feet remain over empty space. Sliding then pushes that body
            // off the edge instead of executing the planned waypoint. Require
            // the same native floor beneath the controller root as well.
            supportRay.From = landing + Vector3.Up * body.SafeMargin * 8;
            supportRay.To = landing - Vector3.Up * Math.Max(stepHeight, body.FloorSnapLength);
            using var support = body.GetWorld3D().DirectSpaceState.IntersectRay(supportRay);
            if (support.Count == 0 || support["normal"].AsVector3().Dot(Vector3.Up) < floorCosine) return false;
            var height = landing.Y - from.Y;
            return height <= stepHeight + body.SafeMargin * 8 &&
                height >= -maximumDrop - body.SafeMargin * 8 && resident(landing);
        }
        static float Flat(Vector3 a, Vector3 b) => new Vector2(a.X - b.X, a.Z - b.Z).Length();
        bool FlatEdge(Vector3 from, Vector3 desired, out Vector3 landing) => Edge(from, desired, out landing, allowStep: false);
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
        Vector3[] Approach((int X, int Z, int Y) node)
        {
            var path = new List<Vector3> { positions[node] };
            while (node != first) { node = parents[node]; path.Add(positions[node]); }
            path.Reverse();
            return path.ToArray();
        }
        void Record((int X, int Z, int Y) node)
        {
            if (rejected is { } contact) probe?.Record(contact, () => Approach(node));
        }
        while (open.TryDequeue(out var current, out _) && closed.Count < maximumNodes)
        {
            if (!closed.Add(current)) continue;
            var from = positions[current];
            rejected = null;
            nearest = Math.Min(nearest, from.DistanceTo(target));
            // A source approach radius describes a reachable region. Every
            // discovered point already passed the complete capsule sweep and
            // support query; it need not enter the target's collision body.
            // Three-dimensional distance keeps other floors outside the region.
            if (targetRadius > 0 && current != first && from.DistanceTo(target) <= targetRadius)
            {
                foreach (var result in SmoothRoute(Approach(current), start, spacing, body.SafeMargin * 8, FlatEdge))
                    yield return result;
                yield break;
            }
            if (Flat(from, target) <= spacing * 1.5f && Edge(from, target, out var goal) && Math.Abs(goal.Y - target.Y) < .6f)
            {
                var path = new List<Vector3> { goal };
                while (current != first) { path.Add(positions[current]); current = parents[current]; }
                path.Reverse();
                foreach (var result in SmoothRoute(path, start, spacing, body.SafeMargin * 8, FlatEdge))
                    yield return result;
                yield break;
            }
            Record(current);
            for (var z = -1; z <= 1; z++)
                for (var x = -1; x <= 1; x++)
                {
                    if (x == 0 && z == 0) continue;
                    var candidate = new Vector3(start.X + (current.X + x) * spacing, from.Y,
                        start.Z + (current.Z + z) * spacing);
                    if (Flat(start, candidate) > bound) continue;
                    if (!Edge(from, candidate, out var next)) { Record(current); continue; }
                    var key = Key(next);
                    var cost = costs[current] + from.DistanceTo(next);
                    if (closed.Contains(key) || costs.TryGetValue(key, out var old) && old <= cost) continue;
                    costs[key] = cost; positions[key] = next; parents[key] = current;
                    open.Enqueue(key, cost + Flat(next, target) + Math.Abs(next.Y - target.Y));
                }
            yield return null;
        }
        throw new InvalidOperationException($"No supported capsule route within {closed.Count} native collision nodes; nearest target distance={nearest:F3}m.");
    }
}
