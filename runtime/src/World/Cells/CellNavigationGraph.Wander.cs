using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed record FalloutWanderTriangle(string Mesh, int Triangle, uint Flags, Vector3 Center);
internal sealed record FalloutWanderDestination(Vector3 Point, FalloutWanderTriangle Triangle,
    bool ReachedDistance, IReadOnlyList<FalloutWanderTriangle> Path);

internal sealed partial class CellNavigationGraph
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<(string Mesh, int Triangle), bool> _wanderDestinationClearance = [];
    private const float PackageWanderClearanceRadius = 35;
    // Ambient package wandering creates a fresh close-point request. Its
    // clearance radius is the request default, not the actor's collision body;
    // it has no avoidance regions. Other navigation requests may differ.
    internal FalloutWanderDestination FindPackageWanderDestination(Vector3 start, Vector3 center,
        float radius, FalloutNumericGameSettings settings, Func<float> randomUnit,
        Func<uint, uint> randomBounded, Func<FalloutWanderTriangle, float?> doorTransitionCost,
        CancellationToken cancellation = default)
    {
        var largeActorRadius = settings.Float("fPathingLargeActorRadius");
        if (!float.IsFinite(largeActorRadius) || largeActorRadius < 0)
            throw new InvalidDataException("Wander large-actor radius setting is invalid.");
        return FindWanderDestination(start, center, Math.Min(32, .75f * radius), radius,
            randomUnit, randomBounded, triangle =>
            {
                if (PackageWanderClearanceRadius > largeActorRadius && (triangle.Flags & 0x10) != 0) return null;
                return (triangle.Flags & 0x1000) != 0 ? doorTransitionCost(triangle) : 0;
            }, _ => 0, HasPackageWanderClearance, cancellation);
    }

    internal bool HasPackageWanderClearance(FalloutWanderTriangle triangle)
    {
        // The graph and source triangle centers do not change during its
        // lifetime. Cache only the fixed default-radius package query; other
        // clearance requests continue to evaluate their own point and radius.
        var key = (triangle.Mesh, triangle.Triangle);
        if (!_wanderDestinationClearance.TryGetValue(key, out var clear))
        {
            clear = HasWanderBoundaryClearance(triangle.Center, PackageWanderClearanceRadius);
            _wanderDestinationClearance.TryAdd(key, clear);
        }
        return clear;
    }

    // Inputs and output use source coordinates (Z up). Admission, clearance and
    // avoidance belong to the actor's request; missing owners cannot be replaced
    // by silently discarding their triangles. Null cost means an ineligible edge.
    internal FalloutWanderDestination FindWanderDestination(Vector3 start, Vector3 center,
        float minimumDistance, float maximumDistance, Func<float> randomUnit, Func<uint, uint> randomBounded,
        Func<FalloutWanderTriangle, float?> transitionCost,
        Func<FalloutWanderTriangle, float> avoidanceCost,
        Func<FalloutWanderTriangle, bool> acceptsDestination,
        CancellationToken cancellation = default)
    {
        if (!start.IsFinite() || !center.IsFinite() || !float.IsFinite(minimumDistance) ||
            !float.IsFinite(maximumDistance) || minimumDistance < 0 || maximumDistance < minimumDistance)
            throw new InvalidDataException("Wander requires finite ordered source distances.");
        float Unit()
        {
            cancellation.ThrowIfCancellationRequested();
            var value = randomUnit();
            if (!float.IsFinite(value) || value < 0 || value > 1)
                throw new InvalidDataException("Wander random source returned a value outside the unit interval.");
            return value;
        }
        static FalloutWanderTriangle Describe(NavigationNode node) => new(node.NavMesh.FormId,
            node.TriangleIndex, node.NavMesh.Triangles[node.TriangleIndex].Flags, node.Centroid);
        static float Cost(float value)
        {
            if (!float.IsFinite(value) || value < 0)
                throw new InvalidDataException("Wander request returned an invalid navigation cost.");
            return value;
        }

        var distance = minimumDistance + Unit() * (maximumDistance - minimumDistance);
        var initial = NearestNode(start).Node;
        var initialAvoidance = Cost(avoidanceCost(Describe(initial)));
        var best = initial;
        var bestAvoidance = initialAvoidance;
        var costs = new Dictionary<NavigationNode, float> { [initial] = 0 };
        var previous = new Dictionary<NavigationNode, NavigationNode>();
        // Equal priorities are inserted before earlier entries. A FIFO queue
        // changes the destination even with the same random edge permutations.
        var pending = new PriorityQueue<(NavigationNode Node, float Cost), (float Cost, long Order)>();
        long order = 0;
        pending.Enqueue((initial, 0), (initialAvoidance, order));
        var reached = false;
        while (pending.TryDequeue(out var entry, out _))
        {
            cancellation.ThrowIfCancellationRequested();
            var node = entry.Node;
            if (costs[node] != entry.Cost) continue;
            var description = Describe(node);
            var offset = description.Center - center;
            if (new Vector2(offset.X, offset.Y).Length() >= distance && acceptsDestination(description))
            {
                best = node; reached = true; break;
            }
            var permutation = randomBounded(6);
            if (permutation >= 6) throw new InvalidDataException("Wander edge permutation is outside its six outcomes.");
            var first = (int)(permutation / 2);
            var step = (int)(permutation % 2) + 1;
            for (var index = 0; index < 3; index++)
            {
                if (WanderNeighbor(node, (first + index * step) % 3) is not { } neighbor) continue;
                var target = Describe(neighbor);
                if (transitionCost(target) is not { } requestedCost) continue;
                var cost = Cost(requestedCost);
                if (costs.TryGetValue(neighbor, out var known) && cost >= known) continue;
                var avoidance = Cost(avoidanceCost(target));
                costs[neighbor] = cost;
                previous[neighbor] = node;
                pending.Enqueue((neighbor, cost), (Cost(cost + avoidance), --order));
                if (avoidance < bestAvoidance) { best = neighbor; bestAvoidance = avoidance; }
            }
        }

        // An exhausted search retains its best avoidance candidate. In a request
        // with no avoidance field this is the initial triangle, not an invented
        // farthest point or a sample from a disconnected mesh.
        var path = new List<NavigationNode> { best };
        while (path[^1] != initial) path.Add(previous[path[^1]]);
        path.Reverse();
        var vertices = best.NavMesh.Triangles[best.TriangleIndex].VertexIndices;
        float u, v;
        do { u = Unit(); v = Unit(); } while (u + v > 1);
        var a = best.NavMesh.Vertices[vertices[0]];
        var point = a + u * (best.NavMesh.Vertices[vertices[1]] - a) +
            v * (best.NavMesh.Vertices[vertices[2]] - a);
        return new(point, Describe(best), reached, path.Select(Describe).ToArray());
    }

    private NavigationNode? WanderNeighbor(NavigationNode node, int edge)
    {
        var triangle = node.NavMesh.Triangles[node.TriangleIndex];
        var index = triangle.AdjacentTriangles[edge];
        if (index < 0) return null;
        NavigationNode target;
        if ((triangle.Flags & (1u << edge)) == 0)
        {
            if (!node.NavMesh.IsInternalNeighbor(node.TriangleIndex, index)) return null;
            target = new(node.NavMesh, index);
        }
        else
        {
            if (index >= node.NavMesh.ExternalConnections.Count)
                throw new InvalidDataException("Wander edge exceeds the source external connections.");
            var external = node.NavMesh.ExternalConnections[index];
            if (external.Type == 1) return null;
            if (!_navmeshesByFormId.TryGetValue(external.NavMeshFormId, out var mesh)) return null;
            if (external.TriangleIndex < 0 || external.TriangleIndex >= mesh.Triangles.Count)
                throw new InvalidDataException("Wander external edge exceeds its destination mesh.");
            target = new(mesh, external.TriangleIndex);
        }
        return (target.NavMesh.Triangles[target.TriangleIndex].Flags & 8) == 0 ? target : null;
    }
}
