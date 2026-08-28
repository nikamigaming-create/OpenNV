using Godot;

namespace OpenNV.Runtime;

internal static class Fo1CaveGeometry
{
    internal static Coverage Build(
        Node3D root,
        bool[] floorBacked,
        IReadOnlyList<Obstacle> obstacles,
        float boundaryHeightMeters)
    {
        if (floorBacked.Length != Fo1HexMath.Width * Fo1HexMath.Height || boundaryHeightMeters <= 0.0f)
            throw new ArgumentException("Fallout 3D cave geometry received an invalid topology contract.");
        var tool = new SurfaceTool();
        tool.Begin(Mesh.PrimitiveType.Triangles);
        var boundaryEdges = 0;
        for (var tile = 0; tile < floorBacked.Length; tile++)
        {
            if (!floorBacked[tile])
                continue;
            var center = Fo1HexMath.Center(tile);
            var corners = Fo1HexMath.Corners(tile, 1.02f);
            for (var edge = 0; edge < 6; edge++)
            {
                var first = corners[edge];
                var second = corners[(edge + 1) % 6];
                var midpoint = (first + second) / 2.0f;
                var outward = midpoint - center;
                outward.Y = 0.0f;
                outward = outward.Normalized();
                var neighbor = Fo1HexMath.NearestTile(center + outward * 0.72f);
                if (neighbor >= 0 && floorBacked[neighbor])
                    continue;
                AddWallQuad(
                    tool,
                    first,
                    second,
                    boundaryHeightMeters,
                    new Color(0.16f, 0.14f, 0.11f));
                boundaryEdges++;
            }
        }

        var grouped = obstacles
            .GroupBy(obstacle => obstacle.Tile)
            .Select(group => new Obstacle(
                group.Key,
                group.Max(value => value.HeightMeters),
                group.Max(value => value.RadiusMeters),
                group.First().ObjectType,
                group.First().Rotation))
            .ToArray();

        var mesh = tool.Commit() ?? throw new InvalidOperationException("Could not build Fallout 3D cave mesh.");
        var material = new StandardMaterial3D
        {
            AlbedoColor = Colors.White,
            VertexColorUseAsAlbedo = true,
            Roughness = 0.96f,
            Metallic = 0.0f,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        root.AddChild(new MeshInstance3D
        {
            Name = "V13ENT_FIXED_3D_CAVE_GEOMETRY",
            Mesh = mesh,
            MaterialOverride = material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.On,
            Visible = false,
        });
        var obstacleTriangles = BuildObstacleInstances(root, grouped);
        return new Coverage(boundaryEdges, grouped.Length, mesh.GetFaces().Length / 3 + obstacleTriangles);
    }

    private static void AddWallQuad(
        SurfaceTool tool,
        Vector3 first,
        Vector3 second,
        float height,
        Color color)
    {
        var firstTop = first + Vector3.Up * height;
        var secondTop = second + Vector3.Up * height;
        AddTriangle(tool, first, second, secondTop, color);
        AddTriangle(tool, first, secondTop, firstTop, color.Lightened(0.08f));
    }

    private static int BuildObstacleInstances(Node3D root, Obstacle[] obstacles)
    {
        var walls = obstacles.Where(obstacle => obstacle.ObjectType == 3).ToArray();
        var rocks = obstacles.Where(obstacle => obstacle.ObjectType != 3).ToArray();
        if (walls.Length > 0)
        {
            var box = new BoxMesh { Size = Vector3.One };
            var multiMesh = BuildObstacleMultiMesh(walls, box, true);
            root.AddChild(new MultiMeshInstance3D
            {
                Name = "V13ENT_3D_WALL_BLOCKERS",
                Multimesh = multiMesh,
                MaterialOverride = ObstacleMaterial(new Color(0.075f, 0.070f, 0.062f)),
                Visible = false,
            });
        }
        if (rocks.Length > 0)
        {
            var rock = new SphereMesh
            {
                Radius = 0.5f,
                Height = 1.0f,
                RadialSegments = 8,
                Rings = 4,
            };
            var multiMesh = BuildObstacleMultiMesh(rocks, rock, false);
            root.AddChild(new MultiMeshInstance3D
            {
                Name = "V13ENT_3D_ROCK_BLOCKERS",
                Multimesh = multiMesh,
                MaterialOverride = ObstacleMaterial(new Color(0.070f, 0.060f, 0.050f)),
                Visible = false,
            });
        }
        return walls.Length * 12 + rocks.Length * 48;
    }

    private static MultiMesh BuildObstacleMultiMesh(
        Obstacle[] obstacles,
        PrimitiveMesh mesh,
        bool wall)
    {
        var multiMesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            Mesh = mesh,
            InstanceCount = obstacles.Length,
        };
        for (var index = 0; index < obstacles.Length; index++)
        {
            var obstacle = obstacles[index];
            var deterministic = 0.86f + (obstacle.Tile * 17 % 23) / 100.0f;
            var scale = wall
                ? new Vector3(
                    obstacle.RadiusMeters * 2.0f,
                    obstacle.HeightMeters * 0.76f,
                    MathF.Max(0.18f, obstacle.RadiusMeters * 0.38f))
                : new Vector3(
                    obstacle.RadiusMeters * 1.25f * deterministic,
                    obstacle.HeightMeters * 0.46f,
                    obstacle.RadiusMeters * 1.12f / deterministic);
            var yaw = -obstacle.Rotation * MathF.PI / 3.0f;
            var basis = new Basis(Vector3.Up, yaw).Scaled(scale);
            var position = Fo1HexMath.Center(obstacle.Tile) + Vector3.Up * (scale.Y / 2.0f);
            multiMesh.SetInstanceTransform(index, new Transform3D(basis, position));
            var shade = 0.78f + (obstacle.Tile * 31 % 19) / 100.0f;
            multiMesh.SetInstanceColor(index, new Color(shade, shade * 0.94f, shade * 0.82f, 1.0f));
        }
        return multiMesh;
    }

    private static StandardMaterial3D ObstacleMaterial(Color color) => new()
    {
        AlbedoColor = color,
        VertexColorUseAsAlbedo = true,
        Roughness = 0.94f,
        Metallic = 0.0f,
    };

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

    internal readonly record struct Obstacle(
        int Tile,
        float HeightMeters,
        float RadiusMeters,
        int ObjectType,
        int Rotation);

    internal readonly record struct Coverage(
        int BoundaryEdges,
        int Obstacles,
        int Triangles);
}
