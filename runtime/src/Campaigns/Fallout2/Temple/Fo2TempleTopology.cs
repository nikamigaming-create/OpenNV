using Godot;

namespace OpenNV.Runtime.Campaigns.Fallout2.Temple;

internal sealed record Fo2TempleTopologyCoverage(
    string ProfilePath,
    string ProfileSha256,
    string FloorSupportMode,
    int FloorSupportPatches,
    int FloorSupportHexes,
    int FloorCollisionTriangles,
    string FloorCollisionPath,
    int SourceBlockingObjects,
    int SourceBlockingHexes,
    int MultihexCentralOnlyBlockers,
    string WalkMaskMode,
    string WalkMaskSha256,
    int WalkableHexes,
    int EntryReachableHexes,
    int WallSourceObjects,
    int WallHexes,
    int WallComponents,
    int LargestWallComponentHexes,
    int WallBoundaryEdges,
    int WallTriangles,
    int WallMeshInstances,
    int WallCollisionBodies,
    int WallCollisionHexes,
    string WallCollisionMode,
    string WallProbeCollisionPath,
    Vector3 FloorProbeFrom,
    Vector3 FloorProbeTo,
    Vector3 WallProbeFrom,
    Vector3 WallProbeTo,
    Fo2TempleMovementConsumer Movement);

internal static class Fo2TempleTopology
{
    private const int TileCount = Fo1HexMath.Width * Fo1HexMath.Height;

    internal static Fo2TempleTopologyCoverage Build(
        Node3D root,
        Fo2TemplePresentationCatalog catalog)
    {
        var profile = Fo2TempleTopologyProfile.Load(catalog);
        var floorIds = catalog.TileEntries
            .Select(entry => (int)(entry & 0x0fff))
            .ToArray();
        var floorPatches = Enumerable.Range(0, floorIds.Length)
            .Where(index => floorIds[index] != profile.DefaultFloorTileId)
            .ToArray();
        var floorSupported = Enumerable.Range(0, TileCount)
            .Select(tile => floorIds[Fo1HexMath.FloorIndex(tile)] != profile.DefaultFloorTileId)
            .ToArray();
        var blockingObjects = catalog.ObjectPlacements
            .Where(row => row.Tile >= 0 && row.Blocking(profile.ObjectNoBlockFlag))
            .ToArray();
        var blocked = blockingObjects.Select(row => row.Tile).ToHashSet();
        var walkable = Enumerable.Range(0, TileCount)
            .Select(tile => floorSupported[tile] && !blocked.Contains(tile))
            .ToArray();
        if (!walkable[catalog.EntryTile])
            throw new InvalidOperationException(
                "Fallout 2 Temple MAP header entry is not source-walkable.");

        var physicsRoot = new Node3D { Name = "MAP_126_SOURCE_PHYSICS" };
        root.AddChild(physicsRoot);
        var floorMesh = BuildFloorCollisionMesh(floorPatches, profile.FloorSupport.SurfaceMeters);
        var floorShape = floorMesh.CreateTrimeshShape() ??
            throw new InvalidOperationException("Could not build Fallout 2 Temple floor support.");
        if (floorShape is ConcavePolygonShape3D floorConcave)
            floorConcave.BackfaceCollision = true;
        var floorBody = new StaticBody3D { Name = "NON_DEFAULT_SOURCE_FLOOR_SUPPORT" };
        floorBody.SetMeta("fo2_floor_support_contract", profile.FloorSupport.Mode);
        floorBody.AddChild(new CollisionShape3D
        {
            Name = "SOURCE_FLOOR_PATCH_COLLISION",
            Shape = floorShape,
        });
        physicsRoot.AddChild(floorBody);

        var wallPlacements = catalog.ObjectPlacements
            .Where(row => row.ObjectType == profile.Wall.SourceObjectType)
            .ToArray();
        var wallTiles = wallPlacements.Select(row => row.Tile).ToHashSet();
        if (wallTiles.Count == 0 || wallTiles.Count > wallPlacements.Length)
            throw new InvalidOperationException("Fallout 2 Temple source wall coverage is invalid.");
        var components = ConnectedComponents(wallTiles);
        var wallRoot = new Node3D { Name = "MAP_126_SOURCE_WALL_HEX_UNION" };
        root.AddChild(wallRoot);
        var boundaryEdges = 0;
        var wallTriangles = 0;
        var wallMeshes = 0;
        var wallBodies = 0;
        var collisionHexes = 0;
        string? probeCollisionPath = null;
        var wallProbeFrom = Vector3.Zero;
        var wallProbeTo = Vector3.Zero;
        foreach (var (component, index) in components.Select((value, index) => (value, index)))
        {
            var componentRoot = new Node3D { Name = $"WALL_COMPONENT_{index:D3}" };
            wallRoot.AddChild(componentRoot);
            var (componentBoundary, componentTriangles) = WallGeometryMetrics(component);
            boundaryEdges += componentBoundary;
            wallTriangles += componentTriangles;
            componentRoot.SetMeta(
                "fo2_wall_presentation_contract",
                profile.Wall.PresentationMode);

            var blockingComponent = component
                .Where(tile => wallPlacements.Any(row =>
                    row.Tile == tile && row.Blocking(profile.ObjectNoBlockFlag)))
                .ToHashSet();
            if (blockingComponent.Count == 0)
                continue;
            var (collisionMesh, _, _) = BuildWallMesh(blockingComponent, profile.Wall);
            var collisionShape = collisionMesh.CreateTrimeshShape() ??
                throw new InvalidOperationException(
                    "Could not build Fallout 2 Temple wall collision.");
            if (collisionShape is ConcavePolygonShape3D wallConcave)
                wallConcave.BackfaceCollision = true;
            var body = new StaticBody3D { Name = "BLOCKING_SOURCE_WALL_HEX_UNION" };
            body.SetMeta("fo2_wall_collision_contract", profile.Wall.CollisionMode);
            body.AddChild(new CollisionShape3D
            {
                Name = "MOLDED_WALL_COLLISION",
                Shape = collisionShape,
            });
            componentRoot.AddChild(body);
            wallBodies++;
            collisionHexes += blockingComponent.Count;
            if (probeCollisionPath is null)
            {
                probeCollisionPath = body.GetPath().ToString();
                (wallProbeFrom, wallProbeTo) = BoundaryProbe(blockingComponent, profile.Wall);
            }
        }
        if (probeCollisionPath is null)
            throw new InvalidOperationException(
                "Fallout 2 Temple has no source-blocking wall collision to prove.");

        var entry = Fo1HexMath.Center(catalog.EntryTile);
        var walkMaskSha256 = Fo2TempleMovementConsumer.MaskSha256(walkable);
        var entryReachableHexes = ReachableCount(catalog.EntryTile, walkable);
        var movement = Fo2TempleMovementConsumer.Build(
            root,
            catalog,
            profile,
            walkable,
            walkMaskSha256,
            entryReachableHexes);
        return new Fo2TempleTopologyCoverage(
            profile.ResourcePath,
            profile.Sha256,
            profile.FloorSupport.Mode,
            floorPatches.Length,
            floorSupported.Count(value => value),
            floorPatches.Length * 2,
            floorBody.GetPath().ToString(),
            blockingObjects.Length,
            blocked.Count,
            blockingObjects.Count(row => (row.Flags & profile.ObjectMultihexFlag) != 0),
            profile.WalkMask.Mode,
            walkMaskSha256,
            walkable.Count(value => value),
            entryReachableHexes,
            wallPlacements.Length,
            wallTiles.Count,
            components.Count,
            components.Max(row => row.Count),
            boundaryEdges,
            wallTriangles,
            wallMeshes,
            wallBodies,
            collisionHexes,
            profile.Wall.CollisionMode,
            probeCollisionPath,
            entry + Vector3.Up * 2.0f,
            entry - Vector3.Up,
            wallProbeFrom,
            wallProbeTo,
            movement);
    }

    private static ArrayMesh BuildFloorCollisionMesh(
        IReadOnlyCollection<int> floorPatches,
        float surfaceMeters)
    {
        var builder = new IndexedMeshBuilder();
        var halfX = Fo1HexMath.ColumnSpacingMeters;
        var halfZ = Fo1HexMath.FlatToFlatMeters;
        foreach (var index in floorPatches)
        {
            var center = Fo1HexMath.FloorPatchCenter(index) + Vector3.Up * surfaceMeters;
            var first = center + new Vector3(-halfX, 0.0f, -halfZ);
            var second = center + new Vector3(-halfX, 0.0f, halfZ);
            var third = center + new Vector3(halfX, 0.0f, halfZ);
            var fourth = center + new Vector3(halfX, 0.0f, -halfZ);
            builder.AddTriangle(first, second, third);
            builder.AddTriangle(first, third, fourth);
        }
        return builder.Commit("Fallout 2 Temple floor support");
    }

    private static (ArrayMesh Mesh, int BoundaryEdges, int Triangles) BuildWallMesh(
        IReadOnlySet<int> occupied,
        Fo2TempleWallProfile profile)
    {
        var builder = new IndexedMeshBuilder();
        var boundary = 0;
        foreach (var tile in occupied.Order())
        {
            var center = Fo1HexMath.Center(tile);
            var bottomCenter = center - Vector3.Up * profile.GroundSinkMeters;
            var topCenter = center + Vector3.Up * profile.HeightMeters;
            var corners = Fo1HexMath.Corners(tile, profile.CellRadiusScale);
            for (var edge = 0; edge < Fo1HexMath.DirectionCount; edge++)
            {
                var next = (edge + 1) % Fo1HexMath.DirectionCount;
                var firstBottom = corners[edge] - Vector3.Up * profile.GroundSinkMeters;
                var secondBottom = corners[next] - Vector3.Up * profile.GroundSinkMeters;
                var firstTop = corners[edge] + Vector3.Up * profile.HeightMeters;
                var secondTop = corners[next] + Vector3.Up * profile.HeightMeters;
                builder.AddTriangle(topCenter, secondTop, firstTop);
                builder.AddTriangle(bottomCenter, firstBottom, secondBottom);
                var neighbor = Fo1HexMath.NeighborAcrossEdge(tile, edge);
                if (neighbor >= 0 && occupied.Contains(neighbor))
                    continue;
                boundary++;
                builder.AddTriangle(firstBottom, firstTop, secondTop);
                builder.AddTriangle(firstBottom, secondTop, secondBottom);
            }
        }
        return (
            builder.Commit("Fallout 2 Temple molded wall shell"),
            boundary,
            occupied.Count * Fo1HexMath.DirectionCount * 2 + boundary * 2);
    }

    private static (int BoundaryEdges, int Triangles) WallGeometryMetrics(
        IReadOnlySet<int> occupied)
    {
        var boundary = occupied.Sum(tile => Enumerable.Range(0, Fo1HexMath.DirectionCount)
            .Count(edge => !occupied.Contains(Fo1HexMath.NeighborAcrossEdge(tile, edge))));
        return (
            boundary,
            occupied.Count * Fo1HexMath.DirectionCount * 2 + boundary * 2);
    }

    private static List<HashSet<int>> ConnectedComponents(IReadOnlySet<int> occupied)
    {
        var result = new List<HashSet<int>>();
        var visited = new HashSet<int>();
        foreach (var start in occupied.Order())
        {
            if (!visited.Add(start))
                continue;
            var component = new HashSet<int> { start };
            var queue = new Queue<int>();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                var tile = queue.Dequeue();
                foreach (var neighbor in Fo1HexMath.Neighbors(tile))
                    if (occupied.Contains(neighbor) && visited.Add(neighbor))
                    {
                        component.Add(neighbor);
                        queue.Enqueue(neighbor);
                    }
            }
            result.Add(component);
        }
        return result;
    }

    private static int ReachableCount(int start, IReadOnlyList<bool> walkable)
    {
        var visited = new HashSet<int> { start };
        var queue = new Queue<int>();
        queue.Enqueue(start);
        while (queue.Count > 0)
            foreach (var neighbor in Fo1HexMath.Neighbors(queue.Dequeue()))
                if (walkable[neighbor] && visited.Add(neighbor))
                    queue.Enqueue(neighbor);
        return visited.Count;
    }

    private static (Vector3 From, Vector3 To) BoundaryProbe(
        IReadOnlySet<int> occupied,
        Fo2TempleWallProfile profile)
    {
        foreach (var tile in occupied.Order())
            for (var edge = 0; edge < Fo1HexMath.DirectionCount; edge++)
                if (!occupied.Contains(Fo1HexMath.NeighborAcrossEdge(tile, edge)))
                {
                    var corners = Fo1HexMath.Corners(tile, profile.CellRadiusScale);
                    var midpoint = (corners[edge] + corners[(edge + 1) % Fo1HexMath.DirectionCount]) /
                        2.0f + Vector3.Up * (profile.HeightMeters / 2.0f);
                    var center = Fo1HexMath.Center(tile) + Vector3.Up * (profile.HeightMeters / 2.0f);
                    var outward = (midpoint - center).Normalized();
                    return (midpoint + outward * 0.5f, center);
                }
        throw new InvalidOperationException("Fallout 2 Temple wall has no boundary edge.");
    }

    private sealed class IndexedMeshBuilder
    {
        private const float VertexPrecision = 100000.0f;
        private readonly Dictionary<Vector3I, int> _lookup = [];
        private readonly List<Vector3> _vertices = [];
        private readonly List<Vector3> _normalSums = [];
        private readonly List<int> _indices = [];

        internal void AddTriangle(Vector3 first, Vector3 second, Vector3 third)
        {
            var normal = (second - first).Cross(third - first);
            if (normal.IsZeroApprox())
                throw new InvalidOperationException(
                    "Fallout 2 Temple topology produced a degenerate triangle.");
            foreach (var position in new[] { first, second, third })
            {
                var index = AddVertex(position);
                _indices.Add(index);
                _normalSums[index] += normal;
            }
        }

        internal ArrayMesh Commit(string label)
        {
            if (_indices.Count == 0 || _indices.Count % 3 != 0)
                throw new InvalidOperationException($"{label} has no complete triangles.");
            var normals = _normalSums.Select(value => value.Normalized()).ToArray();
            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = _vertices.ToArray();
            arrays[(int)Mesh.ArrayType.Normal] = normals;
            arrays[(int)Mesh.ArrayType.Index] = _indices.ToArray();
            var mesh = new ArrayMesh();
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
            if (mesh.GetFaces().Length != _indices.Count)
                throw new InvalidOperationException($"{label} indexed coverage drifted.");
            return mesh;
        }

        private int AddVertex(Vector3 position)
        {
            var key = new Vector3I(
                Mathf.RoundToInt(position.X * VertexPrecision),
                Mathf.RoundToInt(position.Y * VertexPrecision),
                Mathf.RoundToInt(position.Z * VertexPrecision));
            if (_lookup.TryGetValue(key, out var index))
                return index;
            index = _vertices.Count;
            _lookup.Add(key, index);
            _vertices.Add(position);
            _normalSums.Add(Vector3.Zero);
            return index;
        }
    }
}
