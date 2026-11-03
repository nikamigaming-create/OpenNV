using System.Text.Json;
using Godot;
using OpenNV.Runtime.World.Cells;

internal static partial class NativeNavigationContracts
{
    private static void CheckVerticalTriangleIndex()
    {
        var vertices = new List<float[]>();
        var triangles = new List<object>();
        var separate = new List<object>();
        for (var index = 0; index < 96; index++)
        {
            var column = index % 48;
            var x = column % 8 * 8f; var y = column / 8 * 8f;
            var z = index < 48 ? 0f : 20f;
            float[][] points = [[x, y, z], [x + 4, y, z], [x, y + 4, z]];
            var flags = index == 0 ? 8 : 0;
            triangles.Add(new
            {
                vertexIndices = new[] { vertices.Count, vertices.Count + 1, vertices.Count + 2 },
                adjacentTriangles = new[] { -1, -1, -1 },
                flags
            });
            vertices.AddRange(points);
            if (flags == 0)
                separate.Add(new
                {
                    formId = $"source-{index:D3}",
                    cellFormId = "cell",
                    version = 11,
                    verticesGameUnits = points,
                    triangles = new[] { new { vertexIndices = new[] { 0, 1, 2 },
                        adjacentTriangles = new[] { -1, -1, -1 }, flags } },
                    externalConnections = Array.Empty<object>()
                });
        }
        CellNavigationGraph Graph(object[] meshes)
        {
            using var data = JsonDocument.Parse(JsonSerializer.Serialize(new { schema = "opennv-owned-cell-navigation/v1", navmeshes = meshes }));
            return CellNavigationGraph.Load(data.RootElement, new HashSet<string> { "cell" });
        }
        var indexed = Graph([new { formId = "indexed", cellFormId = "cell", version = 11,
            verticesGameUnits = vertices, triangles, externalConnections = Array.Empty<object>() }]);
        // Each reference mesh takes the original linear triangle lane. Padded
        // names preserve source triangle order for overlapping-floor ties.
        var reference = Graph(separate.ToArray());
        for (var index = 0; index < 48; index++)
        {
            var x = index % 8 * 8f; var y = index / 8 * 8f;
            foreach (var offset in new[] { Vector2.Zero, new Vector2(.75f, .75f), new Vector2(2, 2),
                         new Vector2(MathF.BitIncrement(4), 0), new Vector2(MathF.BitDecrement(4), 0) })
            {
                var point = new Vector3(x + offset.X, y + offset.Y, 10);
                var actual = indexed.FindPath(point, point, out _, out _, actorHeightProjection: true)[^1];
                var expected = reference.FindPath(point, point, out _, out _, actorHeightProjection: true)[^1];
                if (actual != expected)
                    throw new InvalidOperationException("Indexed actor floor differs from linear projection at a source edge, disabled triangle or layer tie.");
            }
        }
        GD.Print("OPENNV_VERTICAL_TRIANGLE_INDEX_PASS linearEquivalent=true edgeUlps=true sourceTies=true disabledTriangle=true layeredFloors=true");
    }
}
