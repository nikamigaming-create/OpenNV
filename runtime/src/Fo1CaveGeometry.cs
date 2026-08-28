using Godot;

namespace OpenNV.Runtime;

internal static class Fo1CaveGeometryNumericContracts
{
    // Immutable format, source-art, geometry, and acceptance contracts.
    // Runtime-tunable Fallout 1 behavior remains in the versioned runtime recipe.
    internal const float GeometryFloat0Point050f = 0.050f;
    internal const float GeometryFloat0Point060f = 0.060f;
    internal const float GeometryFloat0Point062f = 0.062f;
    internal const float GeometryFloat0Point070f = 0.070f;
    internal const float GeometryFloat0Point075f = 0.075f;
    internal const float GeometryFloat0Point08f = 0.08f;
    internal const float GeometryFloat0Point11f = 0.11f;
    internal const float GeometryFloat0Point14f = 0.14f;
    internal const float GeometryFloat0Point16f = 0.16f;
    internal const float GeometryFloat0Point18f = 0.18f;
    internal const float GeometryFloat0Point38f = 0.38f;
    internal const float GeometryFloat0Point46f = 0.46f;
    internal const float GeometryFloat0Point5f = 0.5f;
    internal const float GeometryFloat0Point72f = 0.72f;
    internal const float GeometryFloat0Point76f = 0.76f;
    internal const float GeometryFloat0Point78f = 0.78f;
    internal const float GeometryFloat0Point82f = 0.82f;
    internal const float GeometryFloat0Point86f = 0.86f;
    internal const float GeometryFloat0Point94f = 0.94f;
    internal const float GeometryFloat0Point96f = 0.96f;
    internal const float GeometryFloat1Point02f = 1.02f;
    internal const float GeometryFloat1Point12f = 1.12f;
    internal const float GeometryFloat1Point25f = 1.25f;
    internal const float GeometryFloat100Point0f = 100.0f;
    internal const int GeometryInt12 = 12;
    internal const int GeometryInt17 = 17;
    internal const int GeometryInt19 = 19;
    internal const int GeometryInt23 = 23;
    internal const int GeometryInt31 = 31;
    internal const int GeometryInt48 = 48;
    internal const int GeometryInt6 = 6;
    internal const int GeometryInt8 = 8;
}

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
            var corners = Fo1HexMath.Corners(tile, Fo1CaveGeometryNumericContracts.GeometryFloat1Point02f);
            for (var edge = 0; edge < Fo1CaveGeometryNumericContracts.GeometryInt6; edge++)
            {
                var first = corners[edge];
                var second = corners[(edge + 1) % Fo1CaveGeometryNumericContracts.GeometryInt6];
                var midpoint = (first + second) / 2.0f;
                var outward = midpoint - center;
                outward.Y = 0.0f;
                outward = outward.Normalized();
                var neighbor = Fo1HexMath.NearestTile(center + outward * Fo1CaveGeometryNumericContracts.GeometryFloat0Point72f);
                if (neighbor >= 0 && floorBacked[neighbor])
                    continue;
                AddWallQuad(
                    tool,
                    first,
                    second,
                    boundaryHeightMeters,
                    new Color(Fo1CaveGeometryNumericContracts.GeometryFloat0Point16f, Fo1CaveGeometryNumericContracts.GeometryFloat0Point14f, Fo1CaveGeometryNumericContracts.GeometryFloat0Point11f));
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
            Roughness = Fo1CaveGeometryNumericContracts.GeometryFloat0Point96f,
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
        AddTriangle(tool, first, secondTop, firstTop, color.Lightened(Fo1CaveGeometryNumericContracts.GeometryFloat0Point08f));
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
                MaterialOverride = ObstacleMaterial(new Color(Fo1CaveGeometryNumericContracts.GeometryFloat0Point075f, Fo1CaveGeometryNumericContracts.GeometryFloat0Point070f, Fo1CaveGeometryNumericContracts.GeometryFloat0Point062f)),
                Visible = false,
            });
        }
        if (rocks.Length > 0)
        {
            var rock = new SphereMesh
            {
                Radius = Fo1CaveGeometryNumericContracts.GeometryFloat0Point5f,
                Height = 1.0f,
                RadialSegments = Fo1CaveGeometryNumericContracts.GeometryInt8,
                Rings = 4,
            };
            var multiMesh = BuildObstacleMultiMesh(rocks, rock, false);
            root.AddChild(new MultiMeshInstance3D
            {
                Name = "V13ENT_3D_ROCK_BLOCKERS",
                Multimesh = multiMesh,
                MaterialOverride = ObstacleMaterial(new Color(Fo1CaveGeometryNumericContracts.GeometryFloat0Point070f, Fo1CaveGeometryNumericContracts.GeometryFloat0Point060f, Fo1CaveGeometryNumericContracts.GeometryFloat0Point050f)),
                Visible = false,
            });
        }
        return walls.Length * Fo1CaveGeometryNumericContracts.GeometryInt12 + rocks.Length * Fo1CaveGeometryNumericContracts.GeometryInt48;
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
            var deterministic = Fo1CaveGeometryNumericContracts.GeometryFloat0Point86f + (obstacle.Tile * Fo1CaveGeometryNumericContracts.GeometryInt17 % Fo1CaveGeometryNumericContracts.GeometryInt23) / Fo1CaveGeometryNumericContracts.GeometryFloat100Point0f;
            var scale = wall
                ? new Vector3(
                    obstacle.RadiusMeters * 2.0f,
                    obstacle.HeightMeters * Fo1CaveGeometryNumericContracts.GeometryFloat0Point76f,
                    MathF.Max(Fo1CaveGeometryNumericContracts.GeometryFloat0Point18f, obstacle.RadiusMeters * Fo1CaveGeometryNumericContracts.GeometryFloat0Point38f))
                : new Vector3(
                    obstacle.RadiusMeters * Fo1CaveGeometryNumericContracts.GeometryFloat1Point25f * deterministic,
                    obstacle.HeightMeters * Fo1CaveGeometryNumericContracts.GeometryFloat0Point46f,
                    obstacle.RadiusMeters * Fo1CaveGeometryNumericContracts.GeometryFloat1Point12f / deterministic);
            var yaw = -obstacle.Rotation * MathF.PI / 3.0f;
            var basis = new Basis(Vector3.Up, yaw).Scaled(scale);
            var position = Fo1HexMath.Center(obstacle.Tile) + Vector3.Up * (scale.Y / 2.0f);
            multiMesh.SetInstanceTransform(index, new Transform3D(basis, position));
            var shade = Fo1CaveGeometryNumericContracts.GeometryFloat0Point78f + (obstacle.Tile * Fo1CaveGeometryNumericContracts.GeometryInt31 % Fo1CaveGeometryNumericContracts.GeometryInt19) / Fo1CaveGeometryNumericContracts.GeometryFloat100Point0f;
            multiMesh.SetInstanceColor(index, new Color(shade, shade * Fo1CaveGeometryNumericContracts.GeometryFloat0Point94f, shade * Fo1CaveGeometryNumericContracts.GeometryFloat0Point82f, 1.0f));
        }
        return multiMesh;
    }

    private static StandardMaterial3D ObstacleMaterial(Color color) => new()
    {
        AlbedoColor = color,
        VertexColorUseAsAlbedo = true,
        Roughness = Fo1CaveGeometryNumericContracts.GeometryFloat0Point94f,
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
