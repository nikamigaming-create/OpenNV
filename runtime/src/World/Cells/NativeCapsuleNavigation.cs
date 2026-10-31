using System.Diagnostics;
using Godot;

namespace OpenNV.Runtime.World.Cells;

// A short-lived refinement of source NAVM intent against the same native body
// and collision mask used by movement. Queries never move that body. Height is
// part of a node identity, so a bridge cannot join the floor beneath it.
internal static partial class NativeCapsuleNavigation
{
    internal const float SourceHeightTolerance = .6f;
    private static NativeNavigationWorkSchedule? _workSchedule;
    private static NativeNavigationWorkSchedule WorkSchedule
    {
        get
        {
            if (OS.GetThreadCallerId() != OS.GetMainThreadId())
                throw new InvalidOperationException("Native capsule navigation requires the Godot main thread.");
            return _workSchedule ??= new();
        }
    }
    internal static int RegisteredSearches => WorkSchedule.Count;
    internal static NativeNavigationWorkSnapshot? Work(IEnumerator<IReadOnlyList<Vector3>?>? search) =>
        (search as NativeNavigationScheduledSearch<IReadOnlyList<Vector3>?>)?.State;

    internal static float RefinementSpacing(float coarse, float capsuleRadius, float horizontalScale)
    {
        if (!float.IsFinite(coarse) || !float.IsFinite(capsuleRadius) || !float.IsFinite(horizontalScale) ||
            coarse <= 0 || capsuleRadius <= 0 || horizontalScale <= 0)
            throw new ArgumentOutOfRangeException(nameof(coarse));
        return Math.Min(coarse * .5f, Math.Max(.15f, capsuleRadius * horizontalScale * .5f));
    }

    internal static Vector3 RouteMotion(Vector3 position, Vector3 accumulatedMotion, Vector3 waypoint)
    {
        if (!position.IsFinite() || !accumulatedMotion.IsFinite() || !waypoint.IsFinite())
            throw new InvalidDataException("Native route motion requires finite source accumulation and a finite waypoint.");
        var remaining = waypoint - position; remaining.Y = 0;
        accumulatedMotion.Y = 0;
        var distance = remaining.Length();
        var length = accumulatedMotion.Length();
        // A waypoint bounds overshoot; it never changes the signed authored
        // displacement already rotated through the actor's actual heading.
        return length == 0 || distance == 0 ? Vector3.Zero : accumulatedMotion * Math.Min(1, distance / length);
    }

    // Actor and player searches share the original global time/node budget.
    // Round-robin leases reserve progress for late Process callers even when
    // earlier PhysicsProcess callbacks overrun an indivisible collision query.
    internal static bool Advance(IEnumerator<IReadOnlyList<Vector3>?> search, out IReadOnlyList<Vector3>? result)
        => AdvanceScheduled(search, out result);

    internal static bool AdvanceAirborne(IEnumerator<NativeAirContact?> search, out NativeAirContact? result)
        => AdvanceScheduled(search, out result);

    internal static bool AdvanceDeparture(IEnumerator<NativeGroundDeparture?> search, out NativeGroundDeparture? result)
        => AdvanceScheduled(search, out result);

    private static bool AdvanceScheduled<T>(IEnumerator<T?> search, out T? result) where T : class
    {
        var schedule = WorkSchedule;
        if (search is not NativeNavigationScheduledSearch<T?> owned)
            throw new InvalidOperationException("Capsule search lacks its native work registration.");
        result = null;
        if (!owned.TryBegin(Engine.GetPhysicsFrames(), Engine.GetProcessFrames())) return false;
        var started = Stopwatch.GetTimestamp();
        try
        {
            for (var steps = 0; steps < NativeNavigationWorkSchedule.MaximumIteratorSteps &&
                schedule.UsedMilliseconds + Stopwatch.GetElapsedTime(started).TotalMilliseconds < NativeNavigationWorkSchedule.BudgetMilliseconds; steps++)
            {
                if (!owned.MoveNext()) throw new InvalidOperationException("Capsule search ended without a route.");
                if (owned.Current is not { } path) continue;
                result = path;
                return true;
            }
            return false;
        }
        finally { owned.Finish(Stopwatch.GetElapsedTime(started).TotalMilliseconds); }
    }

    internal static (Vector3 Target, int Resume) CorridorPrefix(Vector3 start, IReadOnlyList<Vector3> path, float length)
    {
        if (path.Count == 0 || !float.IsFinite(length) || length <= 0)
            throw new ArgumentOutOfRangeException(nameof(length));
        var previous = start;
        for (var index = 0; index < path.Count; index++)
        {
            var distance = previous.DistanceTo(path[index]);
            if (distance > length)
            {
                // Keep the bounded slice on its preceding authored portal.
                // An arbitrary cut inside the final segment can be occupied,
                // while its source arrival region belongs to the next slice.
                if (index > 0 && start.DistanceTo(previous) > .2f) return (previous, index);
                return (previous.Lerp(path[index], length / distance), index);
            }
            length -= distance;
            previous = path[index];
        }
        return (path[^1], path.Count);
    }

    internal static IReadOnlyList<Vector3> Find(CharacterBody3D body, Vector3 start, Vector3 target,
        float stepHeight, float spacing, Func<Vector3, bool> resident, int maximumNodes = 1200, float targetRadius = 0,
        NativeNavigationProbe? probe = null, IReadOnlyList<Vector3>? corridor = null, Func<Vector3, bool>? arrival = null)
    {
        foreach (var result in Search(body, start, target, stepHeight, spacing, resident, maximumNodes, probe, targetRadius, corridor, arrival))
            if (result is not null) return result;
        throw new InvalidOperationException("Capsule search ended without a route.");
    }

    internal static (IReadOnlyList<Vector3> Path, float Spacing, string? CoarseError) FindRefined(
        CharacterBody3D body, Vector3 start, Vector3 target, float stepHeight,
        float coarseSpacing, float refinedSpacing, Func<Vector3, bool> resident, int maximumNodes = 1200, float targetRadius = 0,
        NativeNavigationProbe? probe = null, IReadOnlyList<Vector3>? corridor = null, Func<Vector3, bool>? arrival = null)
    {
        if (!float.IsFinite(coarseSpacing) || !float.IsFinite(refinedSpacing) ||
            coarseSpacing <= 0 || refinedSpacing <= 0 || refinedSpacing > coarseSpacing)
            throw new ArgumentOutOfRangeException(nameof(refinedSpacing));
        try { return (Find(body, start, target, stepHeight, coarseSpacing, resident, maximumNodes, targetRadius, probe, corridor, arrival), coarseSpacing, null); }
        catch (InvalidOperationException coarse) when (refinedSpacing < coarseSpacing)
        {
            // A lattice can miss a supported passage narrower than its node
            // spacing. One finer search keeps the same body, sweep/floor rules
            // and residency predicate, scaling the node bound to match the finer lattice.
            try { return (Find(body, start, target, stepHeight, refinedSpacing, resident, Math.Max(maximumNodes * 2, 2400), targetRadius, probe, corridor, arrival), refinedSpacing, coarse.Message); }
            catch (InvalidOperationException refined)
            {
                throw new InvalidOperationException($"Coarse capsule query: {coarse.Message} Refined capsule query: {refined.Message}", refined);
            }
        }
    }

    internal static IEnumerable<IReadOnlyList<Vector3>?> Search(CharacterBody3D body, Vector3 start, Vector3 target,
        float stepHeight, float spacing, Func<Vector3, bool> resident, int maximumNodes = 1200,
        NativeNavigationProbe? probe = null, float targetRadius = 0, IReadOnlyList<Vector3>? corridor = null, Func<Vector3, bool>? arrival = null,
        Action? partialResult = null, float minimumSearchRadius = 0, Func<Vector3, bool>? partialCandidate = null,
        Func<Vector3, bool>? sourceFloor = null)
    {
        var schedule = WorkSchedule;
        if (!GodotObject.IsInstanceValid(body) || !body.IsInsideTree() || body.IsQueuedForDeletion())
            throw new InvalidOperationException("Capsule search requires its actual resident native body.");
        var identity = body.GetInstanceId(); var rid = body.GetRid(); var world = body.GetWorld3D().GetInstanceId();
        bool OwnerValid() => GodotObject.IsInstanceValid(body) && body.IsInsideTree() && !body.IsQueuedForDeletion() &&
            body.GetInstanceId() == identity && body.GetRid().Equals(rid) && body.GetWorld3D().GetInstanceId() == world;
        Action BindRetirement(Action retired)
        {
            body.TreeExiting += retired;
            return () => { if (GodotObject.IsInstanceValid(body)) body.TreeExiting -= retired; };
        }
        return new NativeNavigationScheduledEnumerable<IReadOnlyList<Vector3>?>(schedule,
            work => SearchCore(body, start, target, stepHeight, spacing, resident, maximumNodes, probe,
                targetRadius, corridor, arrival, partialResult, minimumSearchRadius, partialCandidate, sourceFloor, work).GetEnumerator(), OwnerValid, result => result is not null, BindRetirement);
    }

    private static IEnumerable<IReadOnlyList<Vector3>?> SearchCore(CharacterBody3D body, Vector3 start, Vector3 target,
        float stepHeight, float spacing, Func<Vector3, bool> resident, int maximumNodes,
        NativeNavigationProbe? probe, float targetRadius, IReadOnlyList<Vector3>? corridor, Func<Vector3, bool>? arrival, Action? partialResult,
        float minimumSearchRadius, Func<Vector3, bool>? partialCandidate, Func<Vector3, bool>? sourceFloor,
        NativeNavigationWorkSchedule.Registration work)
    {
        if (!start.IsFinite() || !target.IsFinite() || !float.IsFinite(stepHeight) || !float.IsFinite(spacing) ||
            stepHeight <= 0 || spacing <= 0 || maximumNodes <= 0 ||
            !float.IsFinite(targetRadius) || targetRadius < 0 || !float.IsFinite(minimumSearchRadius) || minimumSearchRadius < 0)
            throw new ArgumentOutOfRangeException(nameof(stepHeight));
        if (arrival is not null && targetRadius != 0)
            throw new InvalidDataException("Native route has competing source and reference arrival regions.");
        using var edgeQueries = new EdgeQuery(body, stepHeight, spacing, resident, probe);
        var basis = body.GlobalBasis;
        var directional = body.HasMeta(DirectionalEnvelope) && body.GetMeta(DirectionalEnvelope).AsBool();
        var sampleLength = edgeQueries.SampleLength;
        NativeNavigationContact? rejected = null;
        bool SingleEdge(Vector3 from, Vector3 desired, out Vector3 landing, bool allowStep)
        {
            var accepted = edgeQueries.TrySingleEdge(from, desired, directional ? PlanningBasis(basis, from, desired) : basis, out landing, allowStep);
            rejected = edgeQueries.RejectedContact;
            if (accepted && sourceFloor?.Invoke(landing) == false)
            {
                probe?.Reject("source-corridor-floor", from, desired, null);
                return false;
            }
            return accepted;
        }
        static float Flat(Vector3 a, Vector3 b) => new Vector2(a.X - b.X, a.Z - b.Z).Length();
        IEnumerable<Vector3?> Edge(Vector3 from, Vector3 desired, bool allowStep = true)
        {
            var landing = from;
            var samples = Math.Max(1, (int)Math.Ceiling(Flat(from, desired) / sampleLength));
            var origin = from;
            for (var index = 1; index <= samples; index++)
            {
                var accepted = SingleEdge(landing, origin.Lerp(desired, (float)index / samples), out var next, allowStep);
                // A lattice neighbor or smoothing segment can contain many
                // complete capsule samples. Give the shared scheduler a budget
                // boundary after each one, retaining the exact query order.
                yield return null;
                if (!accepted) yield break;
                landing = next;
            }
            yield return landing;
        }
        IEnumerable<Vector3?> FlatEdge(Vector3 from, Vector3 desired) => Edge(from, desired, allowStep: false);

        // Authored portal midpoints are useful seeds, not a collision bypass.
        // Validate every sampled capsule edge and root support first. A failed
        // seed falls back to the same bounded native lattice; it never proves
        // the full width of a NAVM portal is blocked.
        if (corridor is { Count: > 0 })
        {
            var guided = new List<Vector3>();
            var from = start;
            var clear = true;
            foreach (var destination in corridor)
            {
                if (!destination.IsFinite()) throw new InvalidDataException("Native source corridor has a nonfinite waypoint.");
                var origin = from;
                var samples = Math.Max(1, (int)Math.Ceiling(Flat(origin, destination) / sampleLength));
                for (var index = 1; index <= samples; index++)
                {
                    var desired = origin.Lerp(destination, (float)index / samples);
                    work.GuidedSamples++;
                    if (guided.Count >= maximumNodes)
                    {
                        probe?.Reject("source-corridor-node-bound", from, desired, null); probe?.RejectGuide(); clear = false;
                    }
                    else if (!SingleEdge(from, desired, out var landing, true))
                    {
                        probe?.RejectGuide();
                        if (rejected is { } contact) probe?.Record(contact, () => guided.Prepend(start).ToArray());
                        clear = false;
                    }
                    else if (Math.Abs(landing.Y - desired.Y) >= SourceHeightTolerance)
                    {
                        probe?.Reject("source-corridor-height", from, desired, null); probe?.RejectGuide(); clear = false;
                    }
                    else { guided.Add(landing); from = landing; }
                    yield return null;
                    if (!clear) break;
                }
                if (!clear) break;
            }
            if (clear && (arrival?.Invoke(from) ?? (targetRadius > 0 ? from.DistanceTo(target) <= targetRadius :
                Flat(from, target) <= body.SafeMargin * 8 && Math.Abs(from.Y - target.Y) < SourceHeightTolerance)))
            {
                foreach (var result in SmoothRoute(guided, start, spacing, body.SafeMargin * 8, FlatEdge))
                { if (result is null) work.SmoothingSteps++; yield return result; }
                yield break;
            }
        }
        (int X, int Z, int Y) Key(Vector3 p) => ((int)MathF.Round((p.X - start.X) / spacing),
            (int)MathF.Round((p.Z - start.Z) / spacing), (int)MathF.Round(p.Y / .2f));
        var positions = new Dictionary<(int X, int Z, int Y), Vector3>();
        var costs = new Dictionary<(int X, int Z, int Y), float>();
        var parents = new Dictionary<(int X, int Z, int Y), (int X, int Z, int Y)>();
        var open = new PriorityQueue<(int X, int Z, int Y), float>();
        var closed = new HashSet<(int X, int Z, int Y)>();
        var first = Key(start); positions.Add(first, start); costs.Add(first, 0); open.Enqueue(first, Flat(start, target));
        var bound = Math.Max(minimumSearchRadius, Math.Max(8, Flat(start, target) + 8));
        var nearest = float.PositiveInfinity;
        var partialNearest = float.PositiveInfinity;
        var partialBest = first;
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
            var targetDistance = from.DistanceTo(target);
            nearest = Math.Min(nearest, targetDistance);
            if (partialResult is not null && current != first && (partialCandidate?.Invoke(from) ?? true) &&
                targetDistance < partialNearest)
            { partialNearest = targetDistance; partialBest = current; }
            work.ObserveNode(targetDistance);
            // A source approach radius describes a reachable region. Every
            // discovered point already passed the complete capsule sweep and
            // support query; it need not enter the target's collision body.
            // Three-dimensional distance keeps other floors outside the region.
            if (current != first && (arrival?.Invoke(from) ?? (targetRadius > 0 && from.DistanceTo(target) <= targetRadius)))
            {
                foreach (var result in SmoothRoute(Approach(current), start, spacing, body.SafeMargin * 8, FlatEdge))
                { if (result is null) work.SmoothingSteps++; yield return result; }
                yield break;
            }
            Vector3? goal = null;
            if (Flat(from, target) <= spacing * 1.5f)
                foreach (var sample in Edge(from, target))
                {
                    if (sample is { } landing) goal = landing;
                    else yield return null;
                }
            if (goal is { } endpoint && Math.Abs(endpoint.Y - target.Y) < SourceHeightTolerance &&
                (arrival is null || arrival(endpoint)))
            {
                var path = new List<Vector3> { endpoint };
                while (current != first) { path.Add(positions[current]); current = parents[current]; }
                path.Reverse();
                foreach (var result in SmoothRoute(path, start, spacing, body.SafeMargin * 8, FlatEdge))
                { if (result is null) work.SmoothingSteps++; yield return result; }
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
                    Vector3? destination = null;
                    foreach (var sample in Edge(from, candidate))
                    {
                        if (sample is { } landing) destination = landing;
                        else yield return null;
                    }
                    if (destination is not { } next) { Record(current); continue; }
                    var key = Key(next);
                    var cost = costs[current] + from.DistanceTo(next);
                    if (closed.Contains(key) || costs.TryGetValue(key, out var old) && old <= cost) continue;
                    costs[key] = cost; positions[key] = next; parents[key] = current;
                    open.Enqueue(key, cost + Flat(next, target) + Math.Abs(next.Y - target.Y));
                }
            yield return null;
        }
        // Only explicitly requested diagnostic routes may use a supported
        // frontier as an intermediate waypoint. It must make material progress
        // and is never reported as arrival at the requested source region.
        if (partialResult is not null && partialBest != first && start.DistanceTo(target) - partialNearest >= 1 &&
            start.DistanceTo(positions[partialBest]) >= 1)
        {
            foreach (var result in SmoothRoute(Approach(partialBest), start, spacing, body.SafeMargin * 8, FlatEdge))
            {
                if (result is null) work.SmoothingSteps++;
                else partialResult();
                yield return result;
            }
            yield break;
        }
        throw new InvalidOperationException($"No supported capsule route within {closed.Count}/{maximumNodes} native collision nodes; " +
            $"nearest target distance={nearest:F3}m; search={(open.Count > 0 ? "node-bound" : "frontier-exhausted")}. {probe?.DescribeFailure()}");
    }
}
