using System.Text.Json;
using Godot;
using OpenNV.Runtime.World.Cells;

internal static partial class NativeNavigationContracts
{
    private static void CheckActorVerticalCandidate()
    {
        using var data = JsonDocument.Parse("""
            {"schema":"opennv-owned-cell-navigation/v1","navmeshes":[
             {"formId":"slope","cellFormId":"cell","version":11,
              "verticesGameUnits":[[-200,-200,0],[200,-200,200],[0,200,100]],
              "triangles":[{"vertexIndices":[0,1,2],"adjacentTriangles":[-1,-1,-1],"flags":0}],"externalConnections":[]},
             {"formId":"near-edge","cellFormId":"cell","version":11,
              "verticesGameUnits":[[10,-1,0],[12,-1,0],[10,1,0]],
              "triangles":[{"vertexIndices":[0,1,2],"adjacentTriangles":[-1,-1,-1],"flags":0}],"externalConnections":[]}]}
            """);
        var graph = CellNavigationGraph.Load(data.RootElement, new HashSet<string> { "cell" });
        var path = graph.FindPath(new(-1, -1, 0), Vector3.Zero, out _, out var floorFactory,
            destinationRadiusGameUnits: 1, actorHeightProjection: true);
        if (path[^1] != new Vector3(0, 0, 100) || !floorFactory(.01f, .6f)(Vector3.Zero))
            throw new InvalidOperationException("Actor vertical projection shifted target XY on a slope or chose a nearby unrelated edge.");
        var meshes = data.RootElement.GetProperty("navmeshes").EnumerateArray().Select(value => value.Clone()).ToList();
        for (var index = 0; index < 65; index++)
            meshes.Add(JsonSerializer.SerializeToElement(new
            {
                formId = "far-" + index,
                cellFormId = "cell",
                version = 11,
                verticesGameUnits = new[] { new[] { 1000 + index * 10, 0, 0 }, new[] { 1001 + index * 10, 0, 0 }, new[] { 1000 + index * 10, 1, 0 } },
                triangles = new[] { new { vertexIndices = new[] { 0, 1, 2 }, adjacentTriangles = new[] { -1, -1, -1 }, flags = 0 } },
                externalConnections = Array.Empty<object>()
            }));
        using var indexedData = JsonDocument.Parse(JsonSerializer.Serialize(new { schema = "opennv-owned-cell-navigation/v1", navmeshes = meshes }));
        var indexed = CellNavigationGraph.Load(indexedData.RootElement, new HashSet<string> { "cell" });
        var indexedPath = indexed.FindPath(new(-1, -1, 0), Vector3.Zero, out _, out var indexedFloor,
            destinationRadiusGameUnits: 1, actorHeightProjection: true);
        if (indexedPath[^1] != path[^1] || !indexedFloor(.01f, .6f)(Vector3.Zero))
            throw new InvalidOperationException("Indexed actor vertical projection lost the containing triangle or source floor.");
        var badStart = false;
        try { _ = graph.FindPath(new(-1, -1, -300), Vector3.Zero, out _, out _, actorHeightProjection: true); }
        catch (InvalidOperationException) { badStart = true; }
        if (!badStart) throw new InvalidOperationException("Actor route admitted an unqualified starting source-height projection.");
    }

    private static void CheckActorProjectedStandoff()
    {
        using var data = JsonDocument.Parse("""
            {"schema":"opennv-owned-cell-navigation/v1","navmeshes":[
             {"formId":"walk","cellFormId":"cell","version":11,
              "verticesGameUnits":[[-3,-3,100],[3,-3,100],[-3,3,100],[3,3,100]],
              "triangles":[{"vertexIndices":[0,1,2],"adjacentTriangles":[-1,1,-1],"flags":0},
                           {"vertexIndices":[1,3,2],"adjacentTriangles":[-1,-1,0],"flags":0}],"externalConnections":[]},
             {"formId":"island","cellFormId":"cell","version":11,
              "verticesGameUnits":[[4,-3,100],[6,-3,100],[4,3,100],[6,3,100]],
              "triangles":[{"vertexIndices":[0,1,2],"adjacentTriangles":[-1,1,-1],"flags":0},
                           {"vertexIndices":[1,3,2],"adjacentTriangles":[-1,-1,0],"flags":0}],"externalConnections":[]}]}
            """);
        var graph = CellNavigationGraph.Load(data.RootElement, new HashSet<string> { "cell" });
        var start = new Vector3(-2, -2, 0); var target = new Vector3(4.5f, 0, 0);
        var path = graph.FindPath(start, target, out _, out _, destinationRadiusGameUnits: 2, actorHeightProjection: true);
        var region = graph.ArrivalRegion(start, target, path[^1], 2, .01f, .6f, actorHeightProjection: true);
        if (path[^1] != new Vector3(3, 0, 100) || !region(new(2.9f, 0, 0)) || region(new(2.9f, 0, 100)))
            throw new InvalidOperationException("Actor radius planning required a disconnected destination instead of its reachable projected standoff region.");
    }

    private static void CheckActorReachableRadius()
    {
        // The Euclidean-nearest source triangle is reachable but outside the
        // horizontal attack radius. A later connected source triangle supplies
        // a valid source-height standoff; reaching the nearest seed is not arrival.
        using var data = JsonDocument.Parse("""
            {"schema":"opennv-owned-cell-navigation/v1","navmeshes":[
             {"formId":"radius-component","cellFormId":"cell","version":11,
              "verticesGameUnits":[[20,-10,0],[20,10,0],[40,0,0],[5,-10,100],[5,10,100],[15,0,100]],
              "triangles":[
               {"vertexIndices":[0,1,2],"adjacentTriangles":[1,-1,-1],"flags":0},
               {"vertexIndices":[1,0,3],"adjacentTriangles":[0,-1,2],"flags":0},
               {"vertexIndices":[1,3,4],"adjacentTriangles":[1,3,-1],"flags":0},
               {"vertexIndices":[3,4,5],"adjacentTriangles":[2,-1,-1],"flags":0}],"externalConnections":[]}]}
            """);
        var graph = CellNavigationGraph.Load(data.RootElement, new HashSet<string> { "cell" });
        var path = graph.FindPath(new(30, 0, 0), Vector3.Zero, out _, out _,
            destinationRadiusGameUnits: 10, actorHeightProjection: true);
        if (path[^1] != new Vector3(5, 0, 100) || path.Count < 2)
            throw new InvalidOperationException("Radius search stopped at a reachable but unadmitted nearest triangle.");
        void NoCandidate(CellNavigationGraph candidate)
        {
            var refused = false; var portalQueries = 0;
            try
            {
                _ = candidate.FindPath(new(30, 0, 0), Vector3.Zero, out _, out _,
                    permittedPortal: _ => { portalQueries++; return true; }, destinationRadiusGameUnits: 4, actorHeightProjection: true);
            }
            catch (InvalidOperationException) { refused = true; }
            if (!refused || portalQueries != 0)
                throw new InvalidOperationException("An impossible radius fabricated a source endpoint or scanned connectivity before proving an empty spatial region.");
        }
        NoCandidate(graph);
        var meshes = data.RootElement.GetProperty("navmeshes").EnumerateArray().Select(value => value.Clone()).ToList();
        for (var index = 0; index < 65; index++)
            meshes.Add(JsonSerializer.SerializeToElement(new
            {
                formId = "radius-far-" + index,
                cellFormId = "cell",
                version = 11,
                verticesGameUnits = new[] { new[] { 1000 + index * 10, 0, 0 }, new[] { 1001 + index * 10, 0, 0 }, new[] { 1000 + index * 10, 1, 0 } },
                triangles = new[] { new { vertexIndices = new[] { 0, 1, 2 }, adjacentTriangles = new[] { -1, -1, -1 }, flags = 0 } },
                externalConnections = Array.Empty<object>()
            }));
        using var indexedData = JsonDocument.Parse(JsonSerializer.Serialize(new { schema = "opennv-owned-cell-navigation/v1", navmeshes = meshes }));
        var indexed = CellNavigationGraph.Load(indexedData.RootElement, new HashSet<string> { "cell" });
        NoCandidate(indexed);
        var indexedPath = indexed.FindPath(new(30, 0, 0), Vector3.Zero, out _, out _, destinationRadiusGameUnits: 10, actorHeightProjection: true);
        if (indexedPath[^1] != path[^1]) throw new InvalidOperationException("Spatial radius preflight lost an admitted connected source endpoint.");
    }

    private static void CheckActorPreflightFloatBoundary()
    {
        var first = new Vector3(23470.94140625f, 65736.0078125f, 21.1014461517334f);
        var second = new Vector3(23493.91796875f, 66731.71875f, 20.319746017456055f);
        var third = new Vector3(23759.87890625f, 64773.48828125f, .04241270199418068f);
        var target = new Vector3(third.X, third.Y, -179.9575958251953f);
        var normal = (second - first).Cross(third - first);
        var projected = (float)(first.Z - ((double)normal.X * (target.X - first.X) +
            (double)normal.Y * (target.Y - first.Y)) / normal.Z);
        if (!NativeActorNavigationHeight.Admits(target.Z, projected) ||
            NativeActorNavigationHeight.Admits(target.Z, third.Z))
            throw new InvalidOperationException("Synthetic Float32 plane boundary does not distinguish source plane from vertex Z bounds.");
        var meshes = new List<object>
        {
            new
            {
                formId = "rounded-plane", cellFormId = "cell", version = 11,
                verticesGameUnits = new[] { new[] { first.X, first.Y, first.Z }, new[] { second.X, second.Y, second.Z }, new[] { third.X, third.Y, third.Z } },
                triangles = new[] { new { vertexIndices = new[] { 0, 1, 2 }, adjacentTriangles = new[] { -1, -1, -1 }, flags = 0 } },
                externalConnections = Array.Empty<object>()
            },
            new
            {
                formId = "near-low", cellFormId = "cell", version = 11,
                verticesGameUnits = new[] { new[] { target.X + 10, target.Y - 2, target.Z }, new[] { target.X + 20, target.Y - 2, target.Z },
                    new[] { target.X + 10, target.Y + 2, target.Z }, new[] { target.X + 20, target.Y + 2, target.Z } },
                triangles = new[] { new { vertexIndices = new[] { 0, 1, 2 }, adjacentTriangles = new[] { -1, 1, -1 }, flags = 0 },
                    new { vertexIndices = new[] { 1, 3, 2 }, adjacentTriangles = new[] { -1, -1, 0 }, flags = 0 } },
                externalConnections = Array.Empty<object>()
            }
        };
        void CheckCandidate()
        {
            using var data = JsonDocument.Parse(JsonSerializer.Serialize(new { schema = "opennv-owned-cell-navigation/v1", navmeshes = meshes }));
            var graph = CellNavigationGraph.Load(data.RootElement, new HashSet<string> { "cell" });
            var portals = 0; var disconnected = false;
            try
            {
                _ = graph.FindPath(new(target.X + 15, target.Y, target.Z), target, out _, out _,
                    permittedPortal: _ => { portals++; return true; }, destinationRadiusGameUnits: 1, actorHeightProjection: true);
            }
            catch (InvalidOperationException) { disconnected = true; }
            // The candidate is source-valid but disconnected. Connectivity must
            // run and refuse; an invalid bounds prune would skip every portal.
            if (!disconnected || portals == 0)
                throw new InvalidOperationException("Actor radius preflight rejected an exact admitted plane before connectivity.");
        }
        CheckCandidate();
        for (var index = 0; index < 65; index++)
            meshes.Add(new
            {
                formId = "rounded-far-" + index,
                cellFormId = "cell",
                version = 11,
                verticesGameUnits = new[] { new[] { target.X + 1000 + index * 10, target.Y, 0f }, new[] { target.X + 1001 + index * 10, target.Y, 0f }, new[] { target.X + 1000 + index * 10, target.Y + 1, 0f } },
                triangles = new[] { new { vertexIndices = new[] { 0, 1, 2 }, adjacentTriangles = new[] { -1, -1, -1 }, flags = 0 } },
                externalConnections = Array.Empty<object>()
            });
        CheckCandidate();
    }

    private static void CheckActorSourceHeight()
    {
        CheckActorVerticalCandidate();
        CheckVerticalProjectionCoefficients();
        CheckActorProjectedStandoff();
        CheckActorReachableRadius();
        CheckActorPreflightFloatBoundary();
        if (!NativeActorNavigationHeight.Admits(0, 180) || NativeActorNavigationHeight.Admits(0, 180.01f) ||
            !NativeActorNavigationHeight.Admits(0, -64) || NativeActorNavigationHeight.Admits(0, -64.01f) ||
            NativeActorNavigationHeight.Admits(float.NaN, 0))
            throw new InvalidOperationException("Actor source height lost its asymmetric world-unit bounds.");
        using var data = JsonDocument.Parse("""
            {"schema":"opennv-owned-cell-navigation/v1","navmeshes":[
             {"formId":"raised","cellFormId":"cell","version":11,
              "verticesGameUnits":[[-3,-3,100],[3,-3,100],[-3,3,100],[3,3,100]],
              "triangles":[{"vertexIndices":[0,1,2],"adjacentTriangles":[-1,1,-1],"flags":0},
                           {"vertexIndices":[1,3,2],"adjacentTriangles":[-1,-1,0],"flags":0}],"externalConnections":[]},
             {"formId":"other-floor","cellFormId":"cell","version":11,
              "verticesGameUnits":[[-3,-3,400],[3,-3,400],[-3,3,400]],
              "triangles":[{"vertexIndices":[0,1,2],"adjacentTriangles":[-1,-1,-1],"flags":0}],"externalConnections":[]}]}
            """);
        var graph = CellNavigationGraph.Load(data.RootElement, new HashSet<string> { "cell" });
        var start = new Vector3(-2, -2, 0); var target = new Vector3(2, 2, 0);
        var path = graph.FindPath(start, target, out _, out var floorFactory,
            destinationRadiusGameUnits: .5f, actorHeightProjection: true);
        var floor = floorFactory(.01f, .6f);
        if (path[^1].Z != 100 || !floor(target) || floor(new(4, 2, 0)) || floor(new(2, 2, -81)) ||
            floor(new(-2, -2, 400)))
            throw new InvalidOperationException("Actor source projection lost triangle identity, vertical bounds or horizontal admission.");
        var arrival = graph.ArrivalRegion(start, target, path[^1], .5f, .01f, .6f, actorHeightProjection: true);
        if (!arrival(new(1.8f, 2, 0)) || arrival(new(1.8f, 2, 100)) || arrival(new(1.8f, 2, 400)))
            throw new InvalidOperationException("Source NAVM altitude replaced the physical target's actual three-dimensional arrival.");
        var strict = false;
        try { _ = graph.FindPath(start, target, destinationRadiusGameUnits: .5f); }
        catch (InvalidOperationException) { strict = true; }
        if (!strict) throw new InvalidOperationException("Actor-specific source projection changed generic navigation admission.");
        GD.Print("OPENNV_ACTOR_SOURCE_HEIGHT source180Above64Below=true physicalGoal=true sourceLayer=true horizontalRefused=true genericUnchanged=true");
    }
    private static void CheckVerticalProjectionCoefficients()
    {
        var first = new Vector3(-71530.25f, 6000.5f, 8450.75f);
        var second = first + new Vector3(110.125f, 0, 37.625f);
        var third = first + new Vector3(20.25f, 170.5f, -14.25f);
        var vertices = new[] { first, second, third, first + Vector3.Right, first + Vector3.Right * 2, first + Vector3.Right * 3 };
        using var data = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            schema = "opennv-owned-cell-navigation/v1",
            navmeshes = new[] { new
            {
                formId = "coefficient-source", cellFormId = "cell", version = 11,
                verticesGameUnits = vertices.Select(value => new[] { value.X, value.Y, value.Z }).ToArray(),
                triangles = new[]
                {
                    new { vertexIndices = new[] { 3, 4, 5 }, adjacentTriangles = new[] { -1, -1, -1 }, flags = 0 },
                    new { vertexIndices = new[] { 0, 1, 2 }, adjacentTriangles = new[] { -1, -1, -1 }, flags = 0 },
                    new { vertexIndices = new[] { 0, 1, 2 }, adjacentTriangles = new[] { -1, -1, -1 }, flags = 8 }
                },
                externalConnections = Array.Empty<object>()
            } }
        }));
        var graph = CellNavigationGraph.Load(data.RootElement, new HashSet<string> { "cell" });
        for (var pass = 0; pass < 2; pass++)
            for (var x = 1; x <= 6; x++)
                for (var y = 1; y <= 6; y++)
                {
                    var point = first + (second - first) * (x / 16f) + (third - first) * (y / 16f);
                    var normal = (second - first).Cross(third - first);
                    var height = first.Z - ((double)normal.X * (point.X - first.X) + (double)normal.Y * (point.Y - first.Y)) / normal.Z;
                    var physical = new Vector3(point.X, point.Y, (float)height - 20);
                    var path = graph.FindPath(physical, physical, out _, out _, actorHeightProjection: true);
                    if (BitConverter.SingleToInt32Bits(path[^1].X) != BitConverter.SingleToInt32Bits(point.X) ||
                        BitConverter.SingleToInt32Bits(path[^1].Y) != BitConverter.SingleToInt32Bits(point.Y) ||
                        BitConverter.SingleToInt32Bits(path[^1].Z) != BitConverter.SingleToInt32Bits((float)height))
                        throw new InvalidOperationException("Immutable vertical projection changed source Float32 plane results across reuse.");
                }
    }

}
