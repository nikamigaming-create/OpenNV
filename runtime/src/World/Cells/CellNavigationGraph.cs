using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class CellNavigationGraph
{
    private const string ExpectedSchema = "opennv-owned-cell-navigation/v1";
    private const int TriangleVertexCount = 3;
    private const int TriangleEdgeCount = 3;
    private const int SharedEdgeVertexCount = 2;
    private const int NoAdjacentTriangle = -1;
    private const float TriangleCentroidDivisor = 3.0f;
    private const float BarycentricUnit = 1.0f;

    private readonly IReadOnlyList<NavigationMeshRecord> _navmeshes;
    private readonly NavigationMeshRecord[] _projectionMeshes;
    private readonly IReadOnlyDictionary<string, NavigationMeshRecord> _navmeshesByFormId;
    private readonly object _searchPoolGate = new();
    private readonly Stack<NavigationSearchWork> _searchPool = new();
    internal string SourceSha256 { get; private set; } = "";

    private CellNavigationGraph(IReadOnlyList<NavigationMeshRecord> navmeshes)
    {
        _navmeshes = navmeshes;
        _navmeshesByFormId = navmeshes.ToDictionary(
            value => value.FormId,
            StringComparer.OrdinalIgnoreCase);
        // Projection ties use FormID order; other graph operations retain the
        // original source order, including source-bound random selection.
        _projectionMeshes = navmeshes.OrderBy(value => value.FormId, StringComparer.OrdinalIgnoreCase).ToArray();
        // Large worlds reuse an immutable exact bounds index. Small scopes
        // retain their existing stack-only projection traversal.
        _projectionIndex = _projectionMeshes.Length > 64 ? new(_projectionMeshes) : null;
    }

    internal int NavMeshes => _navmeshes.Count;
    internal int Vertices => _navmeshes.Sum(value => value.Vertices.Count);
    internal int Triangles => _navmeshes.Sum(value => value.Triangles.Count);

    internal IReadOnlyList<Vector3> CandidatePoints => _navmeshes
        .SelectMany(value => Enumerable.Range(0, value.Triangles.Count)
            .Select(value.Centroid))
        .ToArray();

    internal static CellNavigationGraph Load(
        JsonElement source,
        IReadOnlySet<string> acceptedCellFormIds)
    {
        if (source.GetProperty("schema").GetString() != ExpectedSchema)
            throw new InvalidOperationException(
                "Owned CELL navigation has an unexpected contract.");
        var navmeshes = source.GetProperty("navmeshes").EnumerateArray()
            .Select(ParseNavMesh)
            .ToArray();
        if (navmeshes.Any(value => !acceptedCellFormIds.Contains(value.CellFormId)))
            throw new InvalidOperationException(
                "Owned navigation graph belongs to another CELL.");
        var graph = new CellNavigationGraph(navmeshes);
        graph.SourceSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source.GetRawText())));
        return graph;
    }

    internal IReadOnlyList<Vector3> FindPath(
        Vector3 startGameUnits,
        Vector3 destinationGameUnits,
        Func<Vector3, bool>? permittedPortal = null,
        float destinationRadiusGameUnits = 0, Func<Vector3, float>? portalPenalty = null)
        => FindPath(startGameUnits, destinationGameUnits, out _, permittedPortal, destinationRadiusGameUnits, portalPenalty);

    internal IReadOnlyList<Vector3> FindPath(
        Vector3 startGameUnits,
        Vector3 destinationGameUnits,
        out Func<Vector3, int, float, float, Func<Vector3, bool>> prefixRegion,
        Func<Vector3, bool>? permittedPortal = null,
        float destinationRadiusGameUnits = 0, Func<Vector3, float>? portalPenalty = null)
        => FindPath(startGameUnits, destinationGameUnits, out prefixRegion, out _, permittedPortal,
            destinationRadiusGameUnits, portalPenalty);

    internal IReadOnlyList<Vector3> FindPath(
        Vector3 startGameUnits,
        Vector3 destinationGameUnits,
        out Func<Vector3, int, float, float, Func<Vector3, bool>> prefixRegion,
        out Func<float, float, Func<Vector3, bool>> corridorFloor,
        Func<Vector3, bool>? permittedPortal = null,
        float destinationRadiusGameUnits = 0, Func<Vector3, float>? portalPenalty = null)
    {
        if (_navmeshes.Count == 0)
            throw new InvalidOperationException(
                "Owned CELL has no navigation mesh for actor travel.");
        var start = NearestNode(startGameUnits);
        var destination = NearestNode(destinationGameUnits);
        if (!float.IsFinite(destinationRadiusGameUnits) || destinationRadiusGameUnits < 0)
            throw new ArgumentOutOfRangeException(nameof(destinationRadiusGameUnits));
        var trianglePath = FindTrianglePath(start.Node, destination.Node, permittedPortal,
            destinationGameUnits, destinationRadiusGameUnits, portalPenalty);
        var result = new List<Vector3>();
        foreach (var pair in trianglePath.Zip(trianglePath.Skip(1)))
            result.Add(Portal(pair.First, pair.Second));
        var last = trianglePath[^1];
        var endpoint = last.NavMesh.ClosestPoint(last.TriangleIndex, destinationGameUnits);
        if (destinationRadiusGameUnits > 0 &&
            endpoint.DistanceSquaredTo(destinationGameUnits) > destinationRadiusGameUnits * destinationRadiusGameUnits)
            throw new InvalidOperationException("Owned NAVM endpoint lies outside the source destination radius.");
        result.Add(endpoint);
        prefixRegion = (target, resume, horizontal, height) =>
            PrefixRegion(startGameUnits, result, trianglePath, target, resume, horizontal, height);
        corridorFloor = (horizontal, height) => ReachableCorridorFloor(start.Node, trianglePath, permittedPortal, horizontal, height);
        return result;
    }

    private Vector3 Portal(NavigationNode from, NavigationNode to) =>
        from.NavMesh == to.NavMesh && from.NavMesh.IsInternalNeighbor(from.TriangleIndex, to.TriangleIndex)
            ? from.NavMesh.SharedEdgeMidpoint(from.TriangleIndex, to.TriangleIndex)
            : ExternalSharedEdgeMidpoint(from, to);

    private Vector3 ExternalSharedEdgeMidpoint(
        NavigationNode source,
        NavigationNode target)
    {
        // An authored external connection is directed. Plugins can change the
        // destination without publishing an inverse connection on that triangle.
        // Its source triangle edge, rather than guessed nearest vertex pairs,
        // owns the crossing point. Native clearance still gates traversal.
        var triangle = source.NavMesh.Triangles[source.TriangleIndex];
        var edges = Enumerable.Range(0, TriangleEdgeCount).Where(edge =>
        {
            if ((triangle.Flags & (1u << edge)) == 0) return false;
            var index = triangle.AdjacentTriangles[edge];
            if (index < 0 || index >= source.NavMesh.ExternalConnections.Count) return false;
            var link = source.NavMesh.ExternalConnections[index];
            return link.NavMeshFormId.Equals(target.NavMesh.FormId, StringComparison.OrdinalIgnoreCase) &&
                link.TriangleIndex == target.TriangleIndex;
        }).ToArray();
        if (edges.Length != 1) throw new InvalidDataException("External navigation transition has no unique authored source edge.");
        var edge = edges[0];
        return (source.NavMesh.Vertices[triangle.VertexIndices[edge]] +
            source.NavMesh.Vertices[triangle.VertexIndices[(edge + 1) % TriangleEdgeCount]]) / 2;
    }
    internal Vector3 FindNearestPoint(Vector3 pointGameUnits)
    {
        if (_navmeshes.Count == 0)
            throw new InvalidOperationException(
                "Owned CELL has no navigation mesh for actor placement.");
        return NearestNode(pointGameUnits).Point;
    }

    internal Vector3 FindReachablePoint(
        Vector3 startGameUnits,
        Vector3 destinationGameUnits)
    {
        if (_navmeshes.Count == 0)
            throw new InvalidOperationException(
                "Owned CELL has no navigation mesh for player movement.");
        var start = NearestNode(startGameUnits);
        var destination = NearestNode(destinationGameUnits);
        if (start.Node.NavMesh == destination.Node.NavMesh &&
            start.Node.NavMesh.CanReach(
                start.Node.TriangleIndex,
                destination.Node.TriangleIndex))
            return destination.Point;
        foreach (var neighbor in Neighbors(start.Node))
            if (neighbor == destination.Node) return destination.Point;
        return start.Point;
    }

    private NearestNodeResult NearestNodeSorted(Vector3 point)
    {
        if (_navmeshes.Count == 0)
            throw new InvalidOperationException(
                "Owned CELL has no navigation mesh for actor travel.");
        ProjectionCandidate[]? rented = null;
        Span<ProjectionCandidate> candidates = _projectionMeshes.Length <= 64
            ? stackalloc ProjectionCandidate[_projectionMeshes.Length]
            : (rented = ArrayPool<ProjectionCandidate>.Shared.Rent(_projectionMeshes.Length)).AsSpan(0, _projectionMeshes.Length);
        try
        {
            for (var index = 0; index < candidates.Length; index++)
                candidates[index] = new(_projectionMeshes[index].BoundsDistanceSquared(point), index);
            candidates.Sort();
            var hasBest = false;
            NearestNodeResult best = default;
            foreach (var candidate in candidates)
            {
                // Retain sorted visitation as well as strict pruning: a mesh
                // without a finite floor must still fail if actually visited.
                if (hasBest && candidate.Distance > best.DistanceSquared) break;
                var mesh = _projectionMeshes[candidate.MeshIndex];
                var nearest = mesh.NearestTriangle(point);
                if (!hasBest || nearest.DistanceSquared < best.DistanceSquared ||
                    nearest.DistanceSquared == best.DistanceSquared &&
                    StringComparer.OrdinalIgnoreCase.Compare(mesh.FormId, best.Node.NavMesh.FormId) < 0)
                {
                    best = new(new(mesh, nearest.Index), nearest.Point, nearest.DistanceSquared);
                    hasBest = true;
                }
            }
            return best;
        }
        finally
        {
            if (rented is not null) ArrayPool<ProjectionCandidate>.Shared.Return(rented);
        }
    }

    private IReadOnlyList<NavigationNode> FindTrianglePath(
        NavigationNode start,
        NavigationNode destination,
        Func<Vector3, bool>? permittedPortal, Vector3 approachTarget, float approachRadius,
        Func<Vector3, float>? portalPenalty)
    {
        if (start == destination)
            return new[] { start };
        var internalOnly = permittedPortal is null && portalPenalty is null && start.NavMesh == destination.NavMesh &&
            start.NavMesh.CanReach(start.TriangleIndex, destination.TriangleIndex);
        NavigationSearchWork work;
        lock (_searchPoolGate) work = _searchPool.Count == 0 ? new() : _searchPool.Pop();
        var frontier = work.Frontier;
        var previous = work.Previous;
        var costs = work.Costs;
        var closed = work.Closed;
        try
        {
            costs.Add(start, 0.0f);
            frontier.Enqueue(start, 0.0f);
            while (frontier.TryDequeue(out var current, out _))
            {
                if (!closed.Add(current)) continue;
                if (current == destination)
                    break;
                if (approachRadius > 0 && current.NavMesh.ClosestPoint(current.TriangleIndex, approachTarget)
                        .DistanceSquaredTo(approachTarget) <= approachRadius * approachRadius)
                {
                    destination = current;
                    break;
                }
                foreach (var adjacent in Neighbors(current))
                {
                    if (internalOnly && adjacent.NavMesh != start.NavMesh) continue;
                    if (closed.Contains(adjacent)) continue;
                    var portal = permittedPortal is not null || portalPenalty is not null ? Portal(current, adjacent) : default;
                    if (permittedPortal?.Invoke(portal) == false) continue;
                    var penalty = portalPenalty?.Invoke(portal) ?? 0;
                    if (!float.IsFinite(penalty) || penalty < 0)
                        throw new InvalidDataException("Navigation portal penalty must be finite and nonnegative.");
                    var cost = costs[current] + current.Centroid.DistanceTo(adjacent.Centroid) + penalty;
                    if (costs.TryGetValue(adjacent, out var known) && cost >= known)
                        continue;
                    costs[adjacent] = cost;
                    previous[adjacent] = current;
                    frontier.Enqueue(
                        adjacent,
                        cost + adjacent.Centroid.DistanceTo(destination.Centroid));
                }
            }
            if (destination == start) return [start];
            if (!previous.ContainsKey(destination))
                throw new InvalidOperationException(
                    $"Owned NAVM active set has no route between {start.NavMesh.FormId} " +
                    $"and {destination.NavMesh.FormId}.");
            var path = new List<NavigationNode> { destination };
            while (path[^1] != start)
                path.Add(previous[path[^1]]);
            path.Reverse();
            return path;
        }
        finally
        {
            // Never retain an unusually large frontier or a partial query's
            // source references across requests. Reentrant/concurrent calls
            // lease separate workspaces; pooling changes no graph admission.
            var retain = frontier.EnsureCapacity(0) <= 8192 && previous.EnsureCapacity(0) <= 8192 &&
                costs.EnsureCapacity(0) <= 8192 && closed.EnsureCapacity(0) <= 8192;
            frontier.Clear(); previous.Clear(); costs.Clear(); closed.Clear();
            if (retain)
                lock (_searchPoolGate)
                    if (_searchPool.Count < 4) _searchPool.Push(work);
        }
    }

    private sealed class NavigationSearchWork
    {
        internal PriorityQueue<NavigationNode, float> Frontier { get; } = new();
        internal Dictionary<NavigationNode, NavigationNode> Previous { get; } = [];
        internal Dictionary<NavigationNode, float> Costs { get; } = [];
        internal HashSet<NavigationNode> Closed { get; } = [];
    }

    private NavigationNeighbors Neighbors(NavigationNode node) => new(this, node);

    // Source neighbors are bounded by the three authored triangle edges.
    // A value enumerator avoids a heap iterator per expanded navigation node.
    private readonly record struct NavigationNeighbors(CellNavigationGraph Owner, NavigationNode Node)
    {
        public Enumerator GetEnumerator() => new(Owner, Node);

        internal struct Enumerator(CellNavigationGraph owner, NavigationNode node)
        {
            private readonly NavigationTriangle _triangle = node.NavMesh.Triangles[node.TriangleIndex];
            private int _edge;
            public NavigationNode Current { get; private set; }

            public bool MoveNext()
            {
                while (_edge < TriangleEdgeCount)
                {
                    var edge = _edge++;
                    var adjacent = _triangle.AdjacentTriangles[edge];
                    if (adjacent < 0) continue;
                    if ((_triangle.Flags & (1u << edge)) == 0)
                    {
                        if (!node.NavMesh.IsInternalNeighbor(node.TriangleIndex, adjacent) ||
                            (node.NavMesh.Triangles[adjacent].Flags & 8) != 0) continue;
                        Current = new(node.NavMesh, adjacent);
                        return true;
                    }
                    if (adjacent >= node.NavMesh.ExternalConnections.Count) continue;
                    var external = node.NavMesh.ExternalConnections[adjacent];
                    if (!owner._navmeshesByFormId.TryGetValue(external.NavMeshFormId, out var target)) continue;
                    if (external.TriangleIndex < 0 || external.TriangleIndex >= target.Triangles.Count ||
                        (target.Triangles[external.TriangleIndex].Flags & 8) != 0) continue;
                    Current = new(target, external.TriangleIndex);
                    return true;
                }
                return false;
            }
        }
    }

    private static NavigationMeshRecord ParseNavMesh(JsonElement source)
    {
        var vertices = source.GetProperty("verticesGameUnits").EnumerateArray()
            .Select(ReadVector)
            .ToArray();
        var triangles = source.GetProperty("triangles").EnumerateArray()
            .Select(value => new NavigationTriangle(
                ReadIntegers(value.GetProperty("vertexIndices"), TriangleVertexCount),
                ReadIntegers(value.GetProperty("adjacentTriangles"), TriangleEdgeCount),
                value.GetProperty("flags").GetUInt32()))
            .ToArray();
        var externalConnections = source.GetProperty("externalConnections")
            .EnumerateArray()
            .Select(value => new NavigationExternalConnection(
                value.GetProperty("navmeshFormId").GetString()!,
                value.GetProperty("triangleIndex").GetInt32(),
                value.TryGetProperty("type", out var type) ? type.GetUInt32() : 0))
            .ToArray();
        if (vertices.Length == 0 || triangles.Length == 0 ||
            vertices.Any(value => !value.IsFinite()) ||
            triangles.Any(value =>
                value.VertexIndices.Distinct().Count() != TriangleVertexCount ||
                value.VertexIndices.Any(index => index < 0 || index >= vertices.Length) ||
                value.AdjacentTriangles.Any(index => index < NoAdjacentTriangle)))
            throw new InvalidOperationException(
                "Owned CELL navigation geometry is malformed.");
        var result = new NavigationMeshRecord(
            source.GetProperty("formId").GetString()!,
            source.GetProperty("cellFormId").GetString()!,
            source.GetProperty("version").GetUInt32(),
            vertices,
            triangles,
            externalConnections);
        result.ValidateAdjacency();
        return result;
    }

    private static Vector3 ReadVector(JsonElement source)
    {
        var values = source.EnumerateArray().Select(value => value.GetSingle()).ToArray();
        if (values.Length != TriangleVertexCount)
            throw new InvalidOperationException(
                "Owned navigation vertex has an invalid component count.");
        return new Vector3(values[0], values[1], values[2]);
    }

    private static IReadOnlyList<int> ReadIntegers(JsonElement source, int count)
    {
        var values = source.EnumerateArray().Select(value => value.GetInt32()).ToArray();
        if (values.Length != count)
            throw new InvalidOperationException(
                "Owned navigation tuple has an invalid component count.");
        return values;
    }

    private sealed record NavigationTriangle(
        IReadOnlyList<int> VertexIndices,
        IReadOnlyList<int> AdjacentTriangles,
        uint Flags);

    private sealed record NavigationExternalConnection(
        string NavMeshFormId,
        int TriangleIndex,
        uint Type = 0);

    private sealed class NavigationMeshRecord
    {
        internal NavigationMeshRecord(
            string formId,
            string cellFormId,
            uint version,
            IReadOnlyList<Vector3> vertices,
            IReadOnlyList<NavigationTriangle> triangles,
            IReadOnlyList<NavigationExternalConnection> externalConnections)
        {
            FormId = formId;
            CellFormId = cellFormId;
            Version = version;
            Vertices = vertices;
            Triangles = triangles;
            ExternalConnections = externalConnections;
            _internalComponents = new(ReadInternalComponents);
            _minimum = _maximum = vertices[0];
            foreach (var vertex in vertices)
            {
                _minimum = new(Math.Min(_minimum.X, vertex.X), Math.Min(_minimum.Y, vertex.Y), Math.Min(_minimum.Z, vertex.Z));
                _maximum = new(Math.Max(_maximum.X, vertex.X), Math.Max(_maximum.Y, vertex.Y), Math.Max(_maximum.Z, vertex.Z));
            }
        }

        private readonly Vector3 _minimum, _maximum;
        private readonly Lazy<int[]> _internalComponents;
        internal (Vector3 Minimum, Vector3 Maximum) ProjectionBounds => (_minimum, _maximum);
        internal float BoundsDistanceSquared(Vector3 point)
        {
            var nearest = new Vector3(Math.Clamp(point.X, _minimum.X, _maximum.X),
                Math.Clamp(point.Y, _minimum.Y, _maximum.Y), Math.Clamp(point.Z, _minimum.Z, _maximum.Z));
            return point.DistanceSquaredTo(nearest);
        }

        internal string FormId { get; }
        internal string CellFormId { get; }
        internal uint Version { get; }
        internal IReadOnlyList<Vector3> Vertices { get; }
        internal IReadOnlyList<NavigationTriangle> Triangles { get; }
        internal IReadOnlyList<NavigationExternalConnection> ExternalConnections { get; }

        internal void ValidateAdjacency(CancellationToken cancellation = default)
        {
            for (var triangleIndex = 0; triangleIndex < Triangles.Count; triangleIndex++)
            {
                cancellation.ThrowIfCancellationRequested();
                var triangle = Triangles[triangleIndex];
                if ((triangle.Flags & 8) != 0) continue;
                for (var edge = 0; edge < TriangleEdgeCount; edge++)
                {
                    var adjacent = triangle.AdjacentTriangles[edge];
                    if (adjacent == NoAdjacentTriangle ||
                        ((triangle.Flags & (1u << edge)) != 0 ? adjacent < ExternalConnections.Count :
                            IsInternalNeighbor(triangleIndex, adjacent)))
                        continue;
                    else
                        throw new InvalidOperationException(
                            $"Owned NAVM {FormId} has unclassified adjacency.");
                }
            }
        }

        internal NearestTriangleResult NearestTriangle(Vector3 point)
        {
            var best = new NearestTriangleResult(-1, default, float.PositiveInfinity);
            for (var index = 0; index < Triangles.Count; index++)
            {
                if ((Triangles[index].Flags & 8) != 0) continue;
                var nearest = ClosestPoint(index, point);
                var distance = point.DistanceSquaredTo(nearest);
                if (float.IsFinite(distance) && distance < best.DistanceSquared)
                    best = new(index, nearest, distance);
            }
            return best.Index >= 0 ? best : throw new InvalidDataException($"Owned NAVM {FormId} has no finite floor projection.");
        }

        internal bool CanReach(int start, int destination) => start == destination ||
            _internalComponents.Value[start] == _internalComponents.Value[destination];

        private int[] ReadInternalComponents()
        {
            // IsInternalNeighbor requires reciprocal authored edges. Preserve
            // exactly that immutable relation; collision, source-floor and
            // permitted-portal checks remain live with their existing owners.
            var components = new int[Triangles.Count];
            Array.Fill(components, -1);
            var pending = new Queue<int>();
            for (var origin = 0; origin < components.Length; ++origin)
            {
                if (components[origin] >= 0) continue;
                components[origin] = origin;
                pending.Enqueue(origin);
                while (pending.TryDequeue(out var current))
                    foreach (var adjacent in Triangles[current].AdjacentTriangles)
                    {
                        if (!IsInternalNeighbor(current, adjacent) || components[adjacent] >= 0) continue;
                        components[adjacent] = origin;
                        pending.Enqueue(adjacent);
                    }
            }
            return components;
        }

        internal Vector3 SharedEdgeMidpoint(int first, int second)
        {
            var shared = SharedVertices(first, second);
            if (shared.Count != SharedEdgeVertexCount)
                throw new InvalidOperationException(
                    $"Owned NAVM {FormId} corridor has no shared edge.");
            return (Vertices[shared[0]] + Vertices[shared[1]]) / SharedEdgeVertexCount;
        }

        internal IReadOnlyList<Vector3> TriangleVertices(int triangleIndex) =>
            Triangles[triangleIndex].VertexIndices
                .Select(index => Vertices[index])
                .ToArray();

        private IReadOnlyList<int> SharedVertices(int first, int second) =>
            Triangles[first].VertexIndices.Intersect(Triangles[second].VertexIndices)
                .Order()
                .ToArray();

        internal bool IsInternalNeighbor(int source, int adjacent) =>
            adjacent >= 0 &&
            adjacent < Triangles.Count &&
            adjacent != source &&
            FalloutNavigationTopology.HasInternalEdge(Triangles[source].AdjacentTriangles, Triangles[source].Flags, adjacent) &&
            FalloutNavigationTopology.HasInternalEdge(Triangles[adjacent].AdjacentTriangles, Triangles[adjacent].Flags, source) &&
            FalloutNavigationTopology.SharedVertexCount(Triangles[source].VertexIndices, Triangles[adjacent].VertexIndices) == SharedEdgeVertexCount;

        internal Vector3 Centroid(int triangleIndex)
        {
            var triangle = Triangles[triangleIndex];
            // NAVM triangles always have three validated indices. Keep the
            // original zero-seeded accumulation order without constructing a
            // LINQ iterator and delegate on every path-search visit.
            var sum = Vector3.Zero;
            for (var index = 0; index < triangle.VertexIndices.Count; index++)
                sum += Vertices[triangle.VertexIndices[index]];
            return sum / TriangleCentroidDivisor;
        }

        internal Vector3? VerticalPlanePoint(int triangleIndex, Vector3 point)
        {
            var triangle = Triangles[triangleIndex];
            var first = Vertices[triangle.VertexIndices[0]];
            var second = Vertices[triangle.VertexIndices[1]];
            var third = Vertices[triangle.VertexIndices[2]];
            var normal = (second - first).Cross(third - first);
            if (normal.Z == 0) return null;
            var height = first.Z - ((double)normal.X * (point.X - first.X) + (double)normal.Y * (point.Y - first.Y)) / normal.Z;
            return double.IsFinite(height) ? new(point.X, point.Y, (float)height) : null;
        }

        internal Vector3 ClosestPoint(int triangleIndex, Vector3 point)
        {
            // World queries can be kilometres from a small source triangle.
            // Float dot-product determinants lose their differences there.
            static double Dot(Vector3 a, Vector3 b) => (double)a.X * b.X + (double)a.Y * b.Y + (double)a.Z * b.Z;
            var triangle = Triangles[triangleIndex];
            var first = Vertices[triangle.VertexIndices[0]];
            var second = Vertices[triangle.VertexIndices[1]];
            var third = Vertices[triangle.VertexIndices[2]];
            var firstToSecond = second - first;
            var firstToThird = third - first;
            var firstToPoint = point - first;
            var firstSecondProjection = Dot(firstToSecond, firstToPoint);
            var firstThirdProjection = Dot(firstToThird, firstToPoint);
            if (firstSecondProjection <= 0.0f && firstThirdProjection <= 0.0f)
                return first;

            var secondToPoint = point - second;
            var secondFirstProjection = Dot(firstToSecond, secondToPoint);
            var secondThirdProjection = Dot(firstToThird, secondToPoint);
            if (secondFirstProjection >= 0.0f &&
                secondThirdProjection <= secondFirstProjection)
                return second;

            var firstSecondRegion =
                firstSecondProjection * secondThirdProjection -
                secondFirstProjection * firstThirdProjection;
            if (firstSecondRegion <= 0.0f &&
                firstSecondProjection >= 0.0f &&
                secondFirstProjection <= 0.0f)
            {
                var weight = firstSecondProjection /
                    (firstSecondProjection - secondFirstProjection);
                return first + (float)weight * firstToSecond;
            }

            var thirdToPoint = point - third;
            var thirdSecondProjection = Dot(firstToSecond, thirdToPoint);
            var thirdFirstProjection = Dot(firstToThird, thirdToPoint);
            if (thirdFirstProjection >= 0.0f &&
                thirdSecondProjection <= thirdFirstProjection)
                return third;

            var firstThirdRegion =
                thirdSecondProjection * firstThirdProjection -
                firstSecondProjection * thirdFirstProjection;
            if (firstThirdRegion <= 0.0f &&
                firstThirdProjection >= 0.0f &&
                thirdFirstProjection <= 0.0f)
            {
                var weight = firstThirdProjection /
                    (firstThirdProjection - thirdFirstProjection);
                return first + (float)weight * firstToThird;
            }

            var secondThirdRegion =
                secondFirstProjection * thirdFirstProjection -
                thirdSecondProjection * secondThirdProjection;
            var secondThirdFirst =
                secondThirdProjection - secondFirstProjection;
            var secondThirdSecond =
                thirdSecondProjection - thirdFirstProjection;
            if (secondThirdRegion <= 0.0f &&
                secondThirdFirst >= 0.0f &&
                secondThirdSecond >= 0.0f)
            {
                var weight = secondThirdFirst /
                    (secondThirdFirst + secondThirdSecond);
                return second + (float)weight * (third - second);
            }

            var denominator = BarycentricUnit /
                (secondThirdRegion + firstThirdRegion + firstSecondRegion);
            var secondWeight = firstThirdRegion * denominator;
            var thirdWeight = firstSecondRegion * denominator;
            return first + firstToSecond * (float)secondWeight +
                firstToThird * (float)thirdWeight;
        }
    }

    private readonly record struct ProjectionCandidate(float Distance, int MeshIndex) : IComparable<ProjectionCandidate>
    {
        public int CompareTo(ProjectionCandidate other)
        {
            // float.CompareTo matches OrderBy's handling of NaN and infinity.
            var distance = Distance.CompareTo(other.Distance);
            return distance != 0 ? distance : MeshIndex.CompareTo(other.MeshIndex);
        }
    }

    private readonly record struct NearestTriangleResult(
        int Index,
        Vector3 Point,
        float DistanceSquared);

    private readonly record struct NavigationNode(
        NavigationMeshRecord NavMesh,
        int TriangleIndex)
    {
        internal Vector3 Centroid => NavMesh.Centroid(TriangleIndex);
    }

    private readonly record struct NearestNodeResult(
        NavigationNode Node,
        Vector3 Point,
        float DistanceSquared);

}
