using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

internal static class NativeNavigationContracts
{
    internal static void Run()
    {
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
}
