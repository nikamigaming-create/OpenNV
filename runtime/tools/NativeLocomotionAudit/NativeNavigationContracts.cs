using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

internal static class NativeNavigationContracts
{
    internal static void Run()
    {
        CheckProjectionBounds();
        var prefix = NativeCapsuleNavigation.CorridorPrefix(Vector3.Zero,
            [new(0, 0, 6), new(2, 0, 6), new(2, 4, 0)], 8);
        if (prefix.Target != new Vector3(2, 0, 6) || prefix.Resume != 2)
            throw new InvalidOperationException("Local refinement cut across a folded source corridor onto another floor.");
        using var detourData = JsonDocument.Parse("""
            {"schema":"opennv-owned-cell-navigation/v1","navmeshes":[
              {"formId":"detour","cellFormId":"cell","version":11,
               "verticesGameUnits":[[0,0,0],[-10,10,0],[10,10,0],[10,-10,0],[-10,-10,0]],
               "triangles":[{"vertexIndices":[0,1,2],"adjacentTriangles":[3,-1,1],"flags":0},
                            {"vertexIndices":[0,2,3],"adjacentTriangles":[0,-1,2],"flags":0},
                            {"vertexIndices":[0,3,4],"adjacentTriangles":[1,-1,3],"flags":0},
                            {"vertexIndices":[0,4,1],"adjacentTriangles":[2,-1,0],"flags":0}],
               "externalConnections":[]}]}
            """);
        var detourGraph = CellNavigationGraph.Load(detourData.RootElement, new HashSet<string> { "cell" });
        var alternate = detourGraph.FindPath(new(0, 6, 0), new(0, -6, 0), point => point.X <= 0);
        if (alternate.Count != 3 || alternate.Any(point => point.X > 0) || alternate[0].X != -5)
            throw new InvalidOperationException("A* did not choose the alternate authored corridor after a blocked portal.");
        var sealedRoute = false;
        try { _ = detourGraph.FindPath(new(0, 6, 0), new(0, -6, 0), _ => false); }
        catch (InvalidOperationException) { sealedRoute = true; }
        if (!sealedRoute) throw new InvalidOperationException("A* fabricated a route through rejected source portals.");
        using var data = JsonDocument.Parse("""
            {"schema":"opennv-owned-cell-navigation/v1","navmeshes":[
              {"formId":"a","cellFormId":"cell","version":11,
               "verticesGameUnits":[[0,0,0],[10,0,0],[0,10,0],[10,10,0]],
               "triangles":[{"vertexIndices":[0,1,2],"adjacentTriangles":[-1,1,-1],"flags":0},
                            {"vertexIndices":[1,3,2],"adjacentTriangles":[0,-1,0],"flags":1}],
               "externalConnections":[{"navmeshFormId":"b","triangleIndex":0}]},
              {"formId":"b","cellFormId":"cell","version":11,
               "verticesGameUnits":[[10,0,0],[20,0,0],[10,10,0]],
               "triangles":[{"vertexIndices":[0,1,2],"adjacentTriangles":[-1,-1,0],"flags":4}],
               "externalConnections":[{"navmeshFormId":"a","triangleIndex":1}]}
            ]}
            """);
        var graph = CellNavigationGraph.Load(data.RootElement, new HashSet<string> { "cell" });
        var route = graph.FindPath(new(1, 1, 0), new(15, 1, 0));
        if (route.Count != 3 || route[1].DistanceTo(new(10, 5, 0)) > .001f || route[^1].DistanceTo(new(15, 1, 0)) > .001f)
            throw new InvalidOperationException("An external-link table index aliasing an internal triangle changed the source corridor.");
        if (graph.FindPath(new(15, 1, 0), new(1, 1, 0)).Count != 3)
            throw new InvalidOperationException("A reciprocal source edge lost reverse traversal.");
        using var directed = JsonDocument.Parse("""
            {"schema":"opennv-owned-cell-navigation/v1","navmeshes":[
              {"formId":"a","cellFormId":"cell","version":11,
               "verticesGameUnits":[[0,0,0],[10,0,0],[0,10,0]],
               "triangles":[{"vertexIndices":[0,1,2],"adjacentTriangles":[-1,0,-1],"flags":2}],
               "externalConnections":[{"navmeshFormId":"b","triangleIndex":0}]},
              {"formId":"b","cellFormId":"cell","version":11,
               "verticesGameUnits":[[10,0,0],[10,10,0],[0,10,0]],
               "triangles":[{"vertexIndices":[0,1,2],"adjacentTriangles":[-1,-1,-1],"flags":0}],
               "externalConnections":[]}]}
            """);
        var directedGraph = CellNavigationGraph.Load(directed.RootElement, new HashSet<string> { "cell" });
        var directedPath = directedGraph.FindPath(new(1, 1, 0), new(9, 9, 0));
        if (directedPath.Count != 2 || directedPath[0] != new Vector3(5, 5, 0))
            throw new InvalidOperationException("A directed external link lost its authored source edge.");
        var reverseRefused = false;
        try { _ = directedGraph.FindPath(new(9, 9, 0), new(1, 1, 0)); }
        catch (InvalidOperationException) { reverseRefused = true; }
        if (!reverseRefused) throw new InvalidOperationException("A directed external link fabricated reverse traversal.");
        var approach = directedGraph.FindPath(new(9, 9, 0), new(4, 4, 0), destinationRadiusGameUnits: 2);
        if (approach.Count != 1 || approach[0] != new Vector3(5, 5, 0))
            throw new InvalidOperationException("Reference approach did not stay on its reachable authored floor.");
        var distantFloorRefused = false;
        try { _ = directedGraph.FindPath(new(9, 9, 0), new(1, 1, 0), destinationRadiusGameUnits: 2); }
        catch (InvalidOperationException) { distantFloorRefused = true; }
        if (!distantFloorRefused) throw new InvalidOperationException("Reference approach exceeded its bounded source floor distance.");
        using var selfLinked = JsonDocument.Parse("""
            {"schema":"opennv-owned-cell-navigation/v1","navmeshes":[
              {"formId":"self","cellFormId":"cell","version":11,
               "verticesGameUnits":[[0,0,0],[10,0,0],[0,10,0],[10,0,0],[10,10,0],[0,10,0]],
               "triangles":[{"vertexIndices":[0,1,2],"adjacentTriangles":[-1,0,-1],"flags":2},
                            {"vertexIndices":[3,4,5],"adjacentTriangles":[-1,-1,1],"flags":4}],
               "externalConnections":[{"navmeshFormId":"self","triangleIndex":1},{"navmeshFormId":"self","triangleIndex":0}]}
            ]}
            """);
        var selfGraph = CellNavigationGraph.Load(selfLinked.RootElement, new HashSet<string> { "cell" });
        var selfRoute = selfGraph.FindPath(new(1, 1, 0), new(9, 9, 0));
        if (selfRoute.Count != 2 || selfRoute[0].DistanceTo(new(5, 5, 0)) > .001f)
            throw new InvalidOperationException("A source external link within one NAVM was mistaken for shared vertex indices.");
        foreach (var point in new[] { new Vector3(100000, 100000, 2000), new(-100000, 30000, 0), new(5, 5, 4) })
        {
            var nearest = graph.FindNearestPoint(point);
            if (!nearest.IsFinite() || nearest.Z != 0 || nearest.X is < 0 or > 20 || nearest.Y is < 0 or > 10)
                throw new InvalidOperationException("A distant world-space floor projection became nonfinite or left the authored triangles.");
        }
        const float units = .0142875f;
        var width = 4096 * units;
        if (FalloutExteriorStreamTarget.Predict(width - 10, 10, 5, 0, units) != (1, 0) ||
            FalloutExteriorStreamTarget.Predict(-1, -1, 0, 0, units) != (-1, -1) ||
            FalloutExteriorStreamTarget.Predict(-1, -1, -1000, 1000, units) != (-2, 0))
            throw new InvalidOperationException("Exterior demand lost early loading, negative-grid ownership or bounded lookahead.");
        Console.WriteLine("OPENNV_WORLD_NAVIGATION_CONTRACT_PASS sourceEdgeFlags=true distantProjection=true boundedPrefetch=true");
    }

    private static void CheckProjectionBounds()
    {
        var meshes = Enumerable.Range(0, 32).Select(index =>
        {
            var x = index % 4 * 100f - 200;
            var y = index / 4 % 4 * 100f - 200;
            var z = index < 16 ? -10f : 10f;
            return new
            {
                formId = $"mesh-{index:D3}",
                cellFormId = "cell",
                version = 11,
                verticesGameUnits = new float[][] { [x, y, z], [x + 100, y, z], [x, y + 100, z] },
                triangles = new[] { new { vertexIndices = new[] { 0, 1, 2 }, adjacentTriangles = new[] { -1, -1, -1 }, flags = 0 } },
                externalConnections = Array.Empty<object>()
            };
        }).ToArray();
        CellNavigationGraph Graph(object[] values)
        {
            using var document = JsonDocument.Parse(JsonSerializer.Serialize(new { schema = "opennv-owned-cell-navigation/v1", navmeshes = values }));
            return CellNavigationGraph.Load(document.RootElement, new HashSet<string> { "cell" });
        }
        var graph = Graph(meshes.Cast<object>().Reverse().ToArray());
        var individual = meshes.Select(mesh => (mesh.formId, Graph: Graph([mesh]))).ToArray();
        var random = new Random(1701);
        for (var index = 0; index < 400; index++)
        {
            var extent = index < 200 ? 300f : 200000f;
            var point = new Vector3((random.NextSingle() * 2 - 1) * extent,
                (random.NextSingle() * 2 - 1) * extent, index % 3 == 0 ? 0 : (random.NextSingle() * 2 - 1) * 100);
            var expected = individual.Select(value => (value.formId, Point: value.Graph.FindNearestPoint(point)))
                .OrderBy(value => point.DistanceSquaredTo(value.Point)).ThenBy(value => value.formId, StringComparer.OrdinalIgnoreCase).First().Point;
            if (graph.FindNearestPoint(point) != expected)
                throw new InvalidOperationException("Bounded projection changed exhaustive triangle distance or deterministic floor ties.");
        }
        Console.WriteLine("OPENNV_NAVIGATION_PROJECTION_PASS exhaustiveAgreement=400 stackedFloors=true distantQueries=true formIdTies=true");
    }
}
