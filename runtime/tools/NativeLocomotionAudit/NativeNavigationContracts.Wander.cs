using System.Text.Json;
using Godot;
using OpenNV.Runtime.World.Cells;

internal static partial class NativeNavigationContracts
{
    private static void CheckWanderSelection()
    {
        using var data = JsonDocument.Parse("""
            {"schema":"opennv-owned-cell-navigation/v1","navmeshes":[
              {"formId":"fan","cellFormId":"cell","version":11,
               "verticesGameUnits":[[0,0,0],[-10,10,0],[10,10,0],[10,-10,0],[-10,-10,0]],
               "triangles":[{"vertexIndices":[0,1,2],"adjacentTriangles":[3,-1,1],"flags":0},
                            {"vertexIndices":[0,2,3],"adjacentTriangles":[0,-1,2],"flags":0},
                            {"vertexIndices":[0,3,4],"adjacentTriangles":[1,-1,3],"flags":0},
                            {"vertexIndices":[0,4,1],"adjacentTriangles":[2,-1,0],"flags":0}],
               "externalConnections":[]},
              {"formId":"disconnected","cellFormId":"cell","version":11,
               "verticesGameUnits":[[20,0,0],[30,0,0],[20,10,0]],
               "triangles":[{"vertexIndices":[0,1,2],"adjacentTriangles":[-1,-1,-1],"flags":0}],
               "externalConnections":[]}]}
            """);
        var graph = CellNavigationGraph.Load(data.RootElement, new HashSet<string> { "cell" });
        if (!graph.HasWanderBoundaryClearance(Vector3.Zero, 9) ||
            !graph.HasWanderBoundaryClearance(new(0, 9, 0), 1) ||
            graph.HasWanderBoundaryClearance(new(0, 9, 0), 1.01f) ||
            !graph.HasWanderBoundaryClearance(new(0, 9, 2), 2) ||
            graph.HasWanderBoundaryClearance(new(10.5f, 10.5f, 0), 1))
            throw new InvalidOperationException("Wander clearance lost open edges, connected seams, tangency, height or endpoints.");
        using var externalData = JsonDocument.Parse("""
            {"schema":"opennv-owned-cell-navigation/v1","navmeshes":[
              {"formId":"edge","cellFormId":"cell","version":11,
               "verticesGameUnits":[[0,0,0],[10,0,0],[0,10,0]],
               "triangles":[{"vertexIndices":[0,1,2],"adjacentTriangles":[0,-1,-1],"flags":1}],
               "externalConnections":[{"navmeshFormId":"unloaded","triangleIndex":0}]}]}
            """);
        var externalGraph = CellNavigationGraph.Load(externalData.RootElement, new HashSet<string> { "cell" });
        if (!externalGraph.HasWanderBoundaryClearance(new(5, 0, 0), 1))
            throw new InvalidOperationException("Wander clearance converted an authored external connection into a wall.");
        var start = new Vector3(0, 6, 0);
        FalloutWanderDestination Select(uint permutation, float distance, Func<FalloutWanderTriangle, float?>? cost = null)
        {
            var values = new Queue<float>([0, .8f, .8f, .25f, .25f]);
            var result = graph.FindWanderDestination(start, start, distance, distance, values.Dequeue,
                _ => permutation, cost ?? (_ => 0), _ => 0, _ => true);
            if (values.Count != 0) throw new InvalidOperationException("Wander did not redraw both rejected barycentric samples.");
            return result;
        }
        var right = Select(0, 5);
        var left = Select(2, 5);
        if (!right.ReachedDistance || right.Triangle.Triangle != 1 || right.Point != new Vector3(5, 0, 0) ||
            right.Path.Count != 2 || left.Triangle.Triangle != 3 || left.Point != new Vector3(-5, 0, 0))
            throw new InvalidOperationException("Wander lost source edge order, equal-priority order or triangle sampling.");
        var blocked = Select(0, 5, value => value.Triangle == 1 ? null : 0);
        if (blocked.Triangle.Triangle != 3)
            throw new InvalidOperationException("Wander crossed a rejected transition.");
        var exhausted = Select(0, 100);
        if (exhausted.ReachedDistance || exhausted.Triangle.Mesh != "fan" || exhausted.Triangle.Triangle != 0 || exhausted.Path.Count != 1)
            throw new InvalidOperationException("Wander fabricated a distant or disconnected fallback destination.");
        Console.WriteLine("OPENNV_WANDER_NAVIGATION_PASS edgeOrder=true rejectedSampling=true unreachableFallback=true boundaryClearance=true gameplay=unverified");
    }
}
