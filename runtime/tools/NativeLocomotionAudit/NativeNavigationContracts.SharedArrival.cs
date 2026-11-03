using System.Text.Json;
using Godot;
using OpenNV.Runtime.World.Cells;

internal static partial class NativeNavigationContracts
{
    private static void CheckSharedArrivalReachability()
    {
        CellNavigationGraph Graph(object[] meshes)
        {
            using var data = JsonDocument.Parse(JsonSerializer.Serialize(new
            { schema = "opennv-owned-cell-navigation/v1", navmeshes = meshes }));
            return CellNavigationGraph.Load(data.RootElement, new HashSet<string> { "cell" });
        }
        CellNavigationGraph Bridge(bool disabled) => Graph([new
        {
            formId = "bridge", cellFormId = "cell", version = 11,
            verticesGameUnits = new[] { new[] { 0, 0, 0 }, new[] { 2, 0, 0 }, new[] { 0, 2, 0 },
                new[] { 2, 2, 0 }, new[] { 4, 0, 0 } },
            triangles = new[]
            {
                new { vertexIndices = new[] { 0, 1, 2 }, adjacentTriangles = new[] { -1, 1, -1 }, flags = 0 },
                new { vertexIndices = new[] { 1, 3, 2 }, adjacentTriangles = new[] { 2, -1, 0 }, flags = disabled ? 8 : 0 },
                new { vertexIndices = new[] { 1, 4, 3 }, adjacentTriangles = new[] { -1, -1, 1 }, flags = 0 }
            },
            externalConnections = Array.Empty<object>()
        }]);
        var origin = new Vector3(.25f, .25f, 0); var far = new Vector3(2.75f, .25f, 0);
        foreach (var disabled in new[] { false, true })
        {
            var graph = Bridge(disabled);
            var expected = disabled ? origin : far;
            if (graph.FindReachablePoint(origin, far) != expected)
                throw new InvalidOperationException("Internal source connectivity traversed a disabled bridge or lost an enabled route.");
            _ = graph.FindPath(origin, origin, out _, out var factory);
            var corridor = factory(.01f, .6f);
            if (corridor(far) == disabled)
                throw new InvalidOperationException("Source corridor membership disagrees with enabled internal connectivity.");
        }
        object Mesh(string name, int x, int y, string[] targets) => new
        {
            formId = name,
            cellFormId = "cell",
            version = 11,
            verticesGameUnits = new[] { new[] { x, y, 0 }, new[] { x + 4, y, 0 }, new[] { x, y + 4, 0 } },
            triangles = new[] { new { vertexIndices = new[] { 0, 1, 2 },
                adjacentTriangles = new[] { targets.Length > 0 ? 0 : -1, targets.Length > 1 ? 1 : -1, -1 },
                flags = targets.Length == 2 ? 3 : targets.Length == 1 ? 1 : 0 } },
            externalConnections = targets.Select(target => new { navmeshFormId = target, triangleIndex = 0 }).ToArray()
        };
        var directed = Graph([Mesh("a", 0, 0, ["b", "c"]), Mesh("b", 10, 0, []),
            Mesh("c", 0, 10, ["e"]), Mesh("d", 20, 0, ["b"]),
            Mesh("e", 0, 20, ["f"]), Mesh("f", 0, 30, [])]);
        var a = new Vector3(1, 1, 0); var b = new Vector3(11, 1, 0);
        var c = new Vector3(1, 11, 0); var d = new Vector3(21, 1, 0);
        var e = new Vector3(1, 21, 0); var f = new Vector3(1, 31, 0);
        _ = directed.FindPath(a, b, out _, out var floors);
        var orderedFloor = floors(.01f, .6f);
        if (!orderedFloor(c) || !orderedFloor(e) || !orderedFloor(f) || orderedFloor(d) ||
            !orderedFloor(e) || !orderedFloor(a) || orderedFloor(d))
            throw new InvalidOperationException("Early source reachability lost a pending branch, reversed a link or changed after exhaustion.");
        var exhaustedFirst = floors(.01f, .6f);
        if (exhaustedFirst(d) || !exhaustedFirst(f) || !exhaustedFirst(c))
            throw new InvalidOperationException("Source membership changed with reachable-first versus unreachable-first query order.");
        var sourceFloor = floors(.01f, .6f);
        var original = directed.ArrivalRegion(a, b, b, 50, .01f, .6f);
        var shared = directed.ArrivalRegion(a, b, b, 50, .01f, .6f, reachableFloor: sourceFloor);
        foreach (var point in new[] { a, b, c, d, e, f, b + Vector3.Up * 100, b + Vector3.Back * 2 })
            if (shared(point) != original(point))
                throw new InvalidOperationException("Shared arrival changed the default directed region, source floor or radius.");
        if (!shared(a) || !shared(b) || !shared(c) || shared(d))
            throw new InvalidOperationException("Arrival forgot an upstream source anchor, lost an outgoing branch or reversed an incoming-only link.");
        _ = directed.FindPath(a, b, out _, out var filteredFloors, permittedPortal: point => point.Y < 1);
        var filtered = directed.ArrivalRegion(a, b, b, 50, .01f, .6f,
            reachableFloor: filteredFloors(.01f, .6f));
        if (!filtered(a) || !filtered(b) || filtered(c) || filtered(d))
            throw new InvalidOperationException("Shared arrival escaped its matching permitted-portal scope.");
        var small = directed.ArrivalRegion(a, b, b, .25f, .01f, .6f, reachableFloor: sourceFloor);
        if (!small(b) || small(a)) throw new InvalidOperationException("Shared arrival changed the authored radius.");
        _ = directed.FindPath(b, b, out _, out var reverseFloors);
        var refused = false;
        try { _ = directed.ArrivalRegion(b, a, a, 50, .01f, .6f, reachableFloor: reverseFloors(.01f, .6f)); }
        catch (InvalidDataException) { refused = true; }
        if (!refused) throw new InvalidOperationException("Shared arrival admitted an unreachable selected source floor.");
        GD.Print("OPENNV_SHARED_ARRIVAL_REACHABILITY_PASS enabledOnly=true directed=true upstreamAnchor=true " +
            "offPathBranch=true retainedFrontier=true queryOrder=true incomingOnlyRefused=true filteredPortal=true defaultEquivalent=true selectedFloor=true radius=true");
    }
}
