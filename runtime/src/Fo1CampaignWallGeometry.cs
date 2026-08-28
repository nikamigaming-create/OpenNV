using Godot;

namespace OpenNV.Runtime;

internal static class Fo1CampaignWallGeometry
{
    internal static Coverage Build(
        Node3D root,
        Fo1CampaignPresentationCatalog catalog,
        Fo1CampaignElevationPresentation elevation)
    {
        var profile = catalog.Viewer.WallGeometry;
        if (profile.Mode != "source-wall-hex-union-v1" ||
            profile.CollisionMode != "blocking-wall-hex-union-v1")
            throw new InvalidOperationException("Fallout campaign wall geometry mode drifted.");
        var topology = elevation.WallTopology;
        if (topology.Cells.Count == 0)
            return new Coverage(0, 0, 0, 0, 0);

        var sourceColors = elevation.Placements
            .Where(row => row.ObjectType == profile.SourceObjectType)
            .Select(row => new
            {
                row.Serial,
                Color = catalog.SpriteArtifacts[row.ArtifactId].AverageOpaqueColor,
            })
            .Where(row => row.Color.HasValue)
            .ToDictionary(row => row.Serial, row => row.Color!.Value);
        var cellColors = ResolveConnectedColors(
            topology.Cells,
            sourceColors,
            profile.UnresolvedSourceAlbedo);
        var renderMesh = BuildUnionMesh(
            topology.Cells,
            profile,
            cell => cellColors[cell.Tile]);
        var expectedTriangles = topology.Cells.Count * Fo1HexMath.DirectionCount * 2 +
            topology.Coverage.BoundaryEdges * 2;
        var triangles = renderMesh.GetFaces().Length / 3;
        if (triangles != expectedTriangles)
            throw new InvalidOperationException(
                $"Fallout connected-wall render coverage drifted: {triangles} != {expectedTriangles}");
        var material = new StandardMaterial3D
        {
            AlbedoColor = Colors.White,
            VertexColorUseAsAlbedo = true,
            Roughness = profile.Roughness,
            Metallic = profile.Metallic,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        var meshInstance = new MeshInstance3D
        {
            Name = "ConnectedSourceWallHexUnion",
            Mesh = renderMesh,
            MaterialOverride = material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.On,
        };
        meshInstance.SetMeta("fo1_geometry_contract", profile.Mode);
        meshInstance.SetMeta("fo1_source_cards_are_geometry", false);
        root.AddChild(meshInstance);

        var blockingCells = topology.Cells
            .Where(row => row.SourceObjects.Any(source => source.Blocking))
            .ToArray();
        if (blockingCells.Length > 0)
        {
            var collisionMesh = BuildUnionMesh(
                blockingCells,
                profile,
                _ => Colors.White);
            var shape = collisionMesh.CreateTrimeshShape();
            if (shape is null)
                throw new InvalidOperationException("Could not build Fallout connected-wall collision.");
            var body = new StaticBody3D { Name = "SourceBlockingWallHexUnion" };
            body.SetMeta("fo1_collision_contract", profile.CollisionMode);
            body.AddChild(new CollisionShape3D
            {
                Name = "BlockingWallCollision",
                Shape = shape,
            });
            root.AddChild(body);
        }
        return new Coverage(
            topology.Cells.Count,
            topology.Coverage.ConnectedComponents,
            topology.Coverage.BoundaryEdges,
            triangles,
            blockingCells.Length);
    }

    private static Dictionary<int, Color> ResolveConnectedColors(
        IReadOnlyCollection<Fo1CampaignWallCell> cells,
        IReadOnlyDictionary<int, Color> sourceColors,
        Color unresolvedColor)
    {
        var byTile = cells.ToDictionary(row => row.Tile);
        var direct = cells.ToDictionary(
            row => row.Tile,
            row => Average(
                row.SourceObjects
                    .Where(source => sourceColors.ContainsKey(source.Serial))
                    .Select(source => sourceColors[source.Serial])));
        var result = new Dictionary<int, Color>();
        var visited = new HashSet<int>();
        var queue = new Queue<int>();
        foreach (var start in cells)
        {
            if (!visited.Add(start.Tile))
                continue;
            var component = new List<int>();
            queue.Enqueue(start.Tile);
            while (queue.Count > 0)
            {
                var tile = queue.Dequeue();
                component.Add(tile);
                for (var edge = 0; edge < Fo1HexMath.DirectionCount; edge++)
                {
                    var neighbor = Fo1HexMath.NeighborAcrossEdge(tile, edge);
                    if (neighbor >= 0 && byTile.ContainsKey(neighbor) && visited.Add(neighbor))
                        queue.Enqueue(neighbor);
                }
            }
            var componentColor = Average(
                component.Where(tile => direct[tile].HasValue)
                    .Select(tile => direct[tile]!.Value)) ?? unresolvedColor;
            foreach (var tile in component)
                result.Add(tile, direct[tile] ?? componentColor);
        }
        return result;
    }

    private static Color? Average(IEnumerable<Color> colors)
    {
        var count = 0;
        var red = 0.0f;
        var green = 0.0f;
        var blue = 0.0f;
        foreach (var color in colors)
        {
            red += color.R;
            green += color.G;
            blue += color.B;
            count++;
        }
        return count == 0
            ? null
            : new Color(red / count, green / count, blue / count, 1.0f);
    }

    private static ArrayMesh BuildUnionMesh(
        IReadOnlyCollection<Fo1CampaignWallCell> cells,
        Fo1CampaignWallGeometryProfile profile,
        Func<Fo1CampaignWallCell, Color> sourceColor)
    {
        var occupied = cells.Select(row => row.Tile).ToHashSet();
        var tool = new SurfaceTool();
        tool.Begin(Mesh.PrimitiveType.Triangles);
        foreach (var cell in cells)
        {
            var color = sourceColor(cell);
            var sideColor = Multiply(color, profile.SideColorMultiplier);
            var topColor = Multiply(color, profile.TopColorMultiplier);
            var center = Fo1HexMath.Center(cell.Tile);
            var bottomCenter = center - Vector3.Up * profile.GroundSinkMeters;
            var topCenter = center + Vector3.Up * profile.HeightMeters;
            var corners = Fo1HexMath.Corners(cell.Tile, profile.CellRadiusScale);
            for (var edge = 0; edge < Fo1HexMath.DirectionCount; edge++)
            {
                var next = (edge + 1) % Fo1HexMath.DirectionCount;
                var firstBottom = corners[edge] - Vector3.Up * profile.GroundSinkMeters;
                var secondBottom = corners[next] - Vector3.Up * profile.GroundSinkMeters;
                var firstTop = corners[edge] + Vector3.Up * profile.HeightMeters;
                var secondTop = corners[next] + Vector3.Up * profile.HeightMeters;
                AddTriangle(tool, topCenter, secondTop, firstTop, topColor);
                AddTriangle(tool, bottomCenter, firstBottom, secondBottom, sideColor);
                var neighbor = Fo1HexMath.NeighborAcrossEdge(cell.Tile, edge);
                if (neighbor >= 0 && occupied.Contains(neighbor))
                    continue;
                AddTriangle(tool, firstBottom, firstTop, secondTop, sideColor);
                AddTriangle(tool, firstBottom, secondTop, secondBottom, sideColor);
            }
        }
        return tool.Commit() ??
            throw new InvalidOperationException("Could not build Fallout connected-wall union mesh.");
    }

    private static Color Multiply(Color first, Color second) => new(
        first.R * second.R,
        first.G * second.G,
        first.B * second.B,
        first.A * second.A);

    private static void AddTriangle(
        SurfaceTool tool,
        Vector3 first,
        Vector3 second,
        Vector3 third,
        Color color)
    {
        var normal = (second - first).Cross(third - first).Normalized();
        foreach (var vertex in new[] { first, second, third })
        {
            tool.SetNormal(normal);
            tool.SetColor(color);
            tool.AddVertex(vertex);
        }
    }

    internal readonly record struct Coverage(
        int RenderedWallHexes,
        int ConnectedComponents,
        int BoundaryEdges,
        int Triangles,
        int BlockingCollisionHexes);
}
