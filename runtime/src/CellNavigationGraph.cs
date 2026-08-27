using System.Text.Json;
using Godot;

namespace OpenNV.Runtime;

internal sealed class CellNavigationGraph
{
    private const string ExpectedSchema = "opennv-owned-cell-navigation/v1";
    private const int TriangleVertexCount = 3;
    private const int TriangleEdgeCount = 3;
    private const int SharedEdgeVertexCount = 2;
    private const int NoAdjacentTriangle = -1;
    private const float TriangleCentroidDivisor = 3.0f;
    private const float BarycentricUnit = 1.0f;

    private readonly IReadOnlyList<NavigationMeshRecord> _navmeshes;

    private CellNavigationGraph(IReadOnlyList<NavigationMeshRecord> navmeshes)
    {
        _navmeshes = navmeshes;
    }

    internal int NavMeshes => _navmeshes.Count;
    internal int Vertices => _navmeshes.Sum(value => value.Vertices.Count);
    internal int Triangles => _navmeshes.Sum(value => value.Triangles.Count);

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
        return new CellNavigationGraph(navmeshes);
    }

    internal IReadOnlyList<Vector3> FindPath(
        Vector3 startGameUnits,
        Vector3 destinationGameUnits)
    {
        if (_navmeshes.Count == 0)
            throw new InvalidOperationException(
                "Owned CELL has no navigation mesh for actor travel.");
        var candidates = _navmeshes
            .Select(value => new
            {
                NavMesh = value,
                Start = value.NearestTriangle(startGameUnits),
                Destination = value.NearestTriangle(destinationGameUnits),
            })
            .OrderBy(value => value.Start.DistanceSquared + value.Destination.DistanceSquared)
            .ThenBy(value => value.NavMesh.FormId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var selected = candidates[0];
        var trianglePath = selected.NavMesh.FindTrianglePath(
            selected.Start.Index,
            selected.Destination.Index);
        var result = new List<Vector3>();
        foreach (var pair in trianglePath.Zip(trianglePath.Skip(1)))
            result.Add(selected.NavMesh.SharedEdgeMidpoint(pair.First, pair.Second));
        result.Add(destinationGameUnits);
        return result;
    }

    internal Vector3 FindNearestPoint(Vector3 pointGameUnits)
    {
        if (_navmeshes.Count == 0)
            throw new InvalidOperationException(
                "Owned CELL has no navigation mesh for actor placement.");
        return _navmeshes
            .Select(value => new
            {
                NavMesh = value,
                Nearest = value.NearestTriangle(pointGameUnits),
            })
            .OrderBy(value => value.Nearest.DistanceSquared)
            .ThenBy(value => value.NavMesh.FormId, StringComparer.OrdinalIgnoreCase)
            .First()
            .Nearest.Point;
    }

    internal Vector3 FindReachablePoint(
        Vector3 startGameUnits,
        Vector3 destinationGameUnits)
    {
        if (_navmeshes.Count == 0)
            throw new InvalidOperationException(
                "Owned CELL has no navigation mesh for player movement.");
        var candidates = _navmeshes
            .Select(value => new
            {
                NavMesh = value,
                Start = value.NearestTriangle(startGameUnits),
                Destination = value.NearestTriangle(destinationGameUnits),
            })
            .OrderBy(value => value.Start.DistanceSquared + value.Destination.DistanceSquared)
            .ThenBy(value => value.NavMesh.FormId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        foreach (var candidate in candidates)
        {
            if (candidate.NavMesh.CanReach(
                    candidate.Start.Index,
                    candidate.Destination.Index))
                return candidate.Destination.Point;
        }
        return candidates[0].Start.Point;
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
        var externalConnectionCount = source.GetProperty("externalConnections")
            .GetArrayLength();
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
            externalConnectionCount);
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

    private sealed class NavigationMeshRecord
    {
        internal NavigationMeshRecord(
            string formId,
            string cellFormId,
            uint version,
            IReadOnlyList<Vector3> vertices,
            IReadOnlyList<NavigationTriangle> triangles,
            int externalConnectionCount)
        {
            FormId = formId;
            CellFormId = cellFormId;
            Version = version;
            Vertices = vertices;
            Triangles = triangles;
            ExternalConnectionCount = externalConnectionCount;
        }

        internal string FormId { get; }
        internal string CellFormId { get; }
        internal uint Version { get; }
        internal IReadOnlyList<Vector3> Vertices { get; }
        internal IReadOnlyList<NavigationTriangle> Triangles { get; }
        internal int ExternalConnectionCount { get; }

        internal void ValidateAdjacency()
        {
            for (var triangleIndex = 0; triangleIndex < Triangles.Count; triangleIndex++)
            {
                foreach (var adjacent in Triangles[triangleIndex].AdjacentTriangles)
                {
                    if (adjacent == NoAdjacentTriangle ||
                        IsInternalNeighbor(triangleIndex, adjacent) ||
                        adjacent < ExternalConnectionCount)
                        continue;
                    else
                        throw new InvalidOperationException(
                            $"Owned NAVM {FormId} has unclassified adjacency.");
                }
            }
        }

        internal NearestTriangleResult NearestTriangle(Vector3 point)
        {
            var results = Triangles.Select((_, index) =>
                {
                    var nearest = ClosestPoint(index, point);
                    return new NearestTriangleResult(
                        index,
                        nearest,
                        point.DistanceSquaredTo(nearest));
                })
                .OrderBy(value => value.DistanceSquared)
                .ThenBy(value => value.Index)
                .ToArray();
            return results[0];
        }

        internal IReadOnlyList<int> FindTrianglePath(int start, int destination)
        {
            if (start == destination)
                return new[] { start };
            var frontier = new PriorityQueue<int, float>();
            var previous = new Dictionary<int, int>();
            var costs = new Dictionary<int, float> { [start] = 0.0f };
            frontier.Enqueue(start, 0.0f);
            while (frontier.TryDequeue(out var current, out _))
            {
                if (current == destination)
                    break;
                foreach (var adjacent in Triangles[current].AdjacentTriangles
                    .Where(value => IsInternalNeighbor(current, value))
                    .Distinct()
                    .Order())
                {
                    var cost = costs[current] + Centroid(current).DistanceTo(Centroid(adjacent));
                    if (costs.TryGetValue(adjacent, out var known) && cost >= known)
                        continue;
                    costs[adjacent] = cost;
                    previous[adjacent] = current;
                    var estimate = cost + Centroid(adjacent).DistanceTo(Centroid(destination));
                    frontier.Enqueue(adjacent, estimate);
                }
            }
            if (!previous.ContainsKey(destination))
                throw new InvalidOperationException(
                    $"Owned NAVM {FormId} has no route between package markers.");
            var path = new List<int> { destination };
            while (path[^1] != start)
                path.Add(previous[path[^1]]);
            path.Reverse();
            return path;
        }

        internal bool CanReach(int start, int destination)
        {
            if (start == destination)
                return true;
            var pending = new Queue<int>();
            var visited = new HashSet<int> { start };
            pending.Enqueue(start);
            while (pending.TryDequeue(out var current))
            {
                foreach (var adjacent in Triangles[current].AdjacentTriangles
                    .Where(value => IsInternalNeighbor(current, value))
                    .Distinct()
                    .Order())
                {
                    if (!visited.Add(adjacent))
                        continue;
                    if (adjacent == destination)
                        return true;
                    pending.Enqueue(adjacent);
                }
            }
            return false;
        }

        internal Vector3 SharedEdgeMidpoint(int first, int second)
        {
            var shared = SharedVertices(first, second);
            if (shared.Count != SharedEdgeVertexCount)
                throw new InvalidOperationException(
                    $"Owned NAVM {FormId} corridor has no shared edge.");
            return (Vertices[shared[0]] + Vertices[shared[1]]) / SharedEdgeVertexCount;
        }

        private IReadOnlyList<int> SharedVertices(int first, int second) =>
            Triangles[first].VertexIndices.Intersect(Triangles[second].VertexIndices)
                .Order()
                .ToArray();

        private bool IsInternalNeighbor(int source, int adjacent) =>
            adjacent >= 0 &&
            adjacent < Triangles.Count &&
            adjacent != source &&
            Triangles[adjacent].AdjacentTriangles.Contains(source) &&
            SharedVertices(source, adjacent).Count == SharedEdgeVertexCount;

        private Vector3 Centroid(int triangleIndex)
        {
            var triangle = Triangles[triangleIndex];
            return triangle.VertexIndices
                .Select(index => Vertices[index])
                .Aggregate(Vector3.Zero, (sum, value) => sum + value) /
                TriangleCentroidDivisor;
        }

        private Vector3 ClosestPoint(int triangleIndex, Vector3 point)
        {
            var triangle = Triangles[triangleIndex];
            var first = Vertices[triangle.VertexIndices[0]];
            var second = Vertices[triangle.VertexIndices[1]];
            var third = Vertices[triangle.VertexIndices[2]];
            var firstToSecond = second - first;
            var firstToThird = third - first;
            var firstToPoint = point - first;
            var firstSecondProjection = firstToSecond.Dot(firstToPoint);
            var firstThirdProjection = firstToThird.Dot(firstToPoint);
            if (firstSecondProjection <= 0.0f && firstThirdProjection <= 0.0f)
                return first;

            var secondToPoint = point - second;
            var secondFirstProjection = firstToSecond.Dot(secondToPoint);
            var secondThirdProjection = firstToThird.Dot(secondToPoint);
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
                return first + weight * firstToSecond;
            }

            var thirdToPoint = point - third;
            var thirdSecondProjection = firstToSecond.Dot(thirdToPoint);
            var thirdFirstProjection = firstToThird.Dot(thirdToPoint);
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
                return first + weight * firstToThird;
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
                return second + weight * (third - second);
            }

            var denominator = BarycentricUnit /
                (secondThirdRegion + firstThirdRegion + firstSecondRegion);
            var secondWeight = firstThirdRegion * denominator;
            var thirdWeight = firstSecondRegion * denominator;
            return first + firstToSecond * secondWeight +
                firstToThird * thirdWeight;
        }
    }

    private sealed record NearestTriangleResult(
        int Index,
        Vector3 Point,
        float DistanceSquared);
}
