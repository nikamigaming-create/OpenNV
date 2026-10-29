using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;

internal static partial class NativeNavigationContracts
{
    internal static NativeActorCombatContext PackageReceiptContext() => new(() => null,
        () => throw new InvalidOperationException("Receipt contract queried player vitals."), (_, _) => { },
        (_, _) => [], _ => true, () => 1, null!, .4f, 9.81f);

    private static void CheckPackageRouteReceipts()
    {
        using var shape = new CapsuleShape3D();
        using var otherShape = new CapsuleShape3D();
        var context = PackageReceiptContext();
        var target = new Vector3(2, -.3f, 0);
        var projected = target + Vector3.Right * .00001f;
        var endpoint = new Vector3(projected.X, .001f, 0);
        var identity = new NativePackageRouteIdentity(new("fixture.esm", 1), target, .0714375f,
            new('a', 64), context, 1, 2, shape, Vector3.One);
        bool Floor(Vector3 point) => Math.Abs(point.Y + .3f) < NativeCapsuleNavigation.SourceHeightTolerance;
        var source = new NativeActorNavigationRoute([projected], SourceSha256: identity.SourceSha256, TerminalFloor: Floor);
        var prefix = new NativeNavigationIntent(projected, 1, 0, false, [projected]);
        var request = new NativePackageRouteRequest(identity, source, prefix, 1);
        Vector3[] prefixPath = [endpoint];
        var receipt = new NativePackageRouteReceipt(request, prefixPath);
        var approach = receipt.Approach(identity, prefixPath, endpoint, true, 1);
        if (approach is null || !approach.ReferenceApproach || approach.Target != target ||
            approach.ArrivalRadius != identity.Radius || receipt.Arrived(identity, prefixPath, endpoint, true, 1))
            throw new InvalidOperationException("Observed terminal prefix lost its original target/radius or became final arrival.");
        if (receipt.Approach(identity, prefixPath, endpoint, true, 0) is not null ||
            receipt.Approach(identity, prefixPath, endpoint, false, 1) is not null ||
            receipt.Approach(identity, prefixPath, endpoint + Vector3.Up, true, 1) is not null ||
            receipt.Approach(identity, prefixPath.ToArray(), endpoint, true, 1) is not null ||
            (receipt with { Request = request with { SourcePoints = 2 } }).Approach(identity, prefixPath, endpoint, true, 1) is not null ||
            (receipt with { Request = request with { Source = source with { TerminalFloor = _ => false } } })
                .Approach(identity, prefixPath, endpoint, true, 1) is not null ||
            (receipt with { Request = request with { Intent = prefix with { Projection = new(target, projected, 1) } } })
                .Approach(identity, prefixPath, endpoint, true, 1) is not null)
            throw new InvalidOperationException("Terminal handoff accepted an unwalked prefix, wrong floor, replaced path or projected region.");

        Vector3[] finalPath = [endpoint];
        var final = new NativePackageRouteReceipt(request with { Intent = approach }, finalPath);
        var motion = new FalloutActorPackageMotion(identity.Package, new('c', 64), "fixture.kf", new('d', 64), 0, false,
            [endpoint.X, endpoint.Y, endpoint.Z], [0, 0, 0, 1]);
        if (!NativePackageRouteReceipt.ContinuesPose(motion, identity.Package, endpoint, Quaternion.Identity) ||
            NativePackageRouteReceipt.ContinuesPose(null, identity.Package, endpoint, Quaternion.Identity) ||
            NativePackageRouteReceipt.ContinuesPose(motion, new("fixture.esm", 2), endpoint, Quaternion.Identity) ||
            NativePackageRouteReceipt.ContinuesPose(motion, identity.Package, endpoint + Vector3.Right * .00001f, Quaternion.Identity) ||
            NativePackageRouteReceipt.ContinuesPose(motion, identity.Package, endpoint, new Quaternion(Vector3.Up, .1f)))
            throw new InvalidOperationException("A replaced package or externally changed pose retained a package route receipt.");
        if (!final.Arrived(identity, finalPath, endpoint, true, 1) ||
            final.Arrived(identity, prefixPath, endpoint, true, 1) || final.Arrived(identity, finalPath, endpoint, true, 0) ||
            final.Arrived(identity, finalPath, endpoint, false, 1) ||
            final.Arrived(identity, finalPath, endpoint + Vector3.Up, true, 1) ||
            final.Arrived(identity, finalPath, endpoint + Vector3.Right * (identity.Radius + .001f), true, 1))
            throw new InvalidOperationException("Final arrival lost its exact native query, grounded endpoint or original 3D tolerance.");
        foreach (var stale in new[]
        {
            identity with { Package = new("fixture.esm", 2) }, identity with { Target = target + Vector3.Right * .00001f },
            identity with { Radius = identity.Radius + .001f }, identity with { SourceSha256 = new('b', 64) },
            identity with { Context = context with { } }, identity with { Body = 3 }, identity with { PhysicsWorld = 3 },
            identity with { Envelope = otherShape }, identity with { Scale = Vector3.One * 2 }
        })
            if (final.Arrived(stale, finalPath, endpoint, true, 1) ||
                receipt.Approach(stale, prefixPath, endpoint, true, 1) is not null)
                throw new InvalidOperationException("Package arrival reused a stale package, target, source, body or context.");
        foreach (var invalid in new[]
        {
            approach with { Target = projected }, approach with { ArrivalRadius = 1 },
            approach with { Corridor = new[] { target } }, approach with { ReferenceApproach = false },
            approach with { Projection = new(target, projected, 1) }
        })
            if ((final with { Request = request with { Intent = invalid } }).Arrived(identity, finalPath, endpoint, true, 1))
                throw new InvalidOperationException("A mismatched final request or source region became package arrival.");

        CheckTerminalFloorIndex();
        Console.WriteLine("OPENNV_PACKAGE_ROUTE_RECEIPT_PASS terminalPrefixThenReference=true originalTargetAndRadius=true " +
            "exactSuccessfulPath=true walkedAndGrounded=true wrongFloorRefused=true staleOwnersRefused=true sourceHash=true terminalTriangle=true");
    }

    private static void CheckTerminalFloorIndex()
    {
        using var json = JsonDocument.Parse("""
            {"schema":"opennv-owned-cell-navigation/v1","navmeshes":[
              {"formId":"floor","cellFormId":"cell","version":11,
               "verticesGameUnits":[[0,0,0],[-10,10,0],[10,10,0],[10,-10,0],[-10,-10,0]],
               "triangles":[{"vertexIndices":[0,1,2],"adjacentTriangles":[3,-1,1],"flags":0},
                            {"vertexIndices":[0,2,3],"adjacentTriangles":[0,-1,2],"flags":0},
                            {"vertexIndices":[0,3,4],"adjacentTriangles":[1,-1,3],"flags":0},
                            {"vertexIndices":[0,4,1],"adjacentTriangles":[2,-1,0],"flags":0}],
               "externalConnections":[]}]}
            """);
        var graph = CellNavigationGraph.Load(json.RootElement, new HashSet<string> { "cell" });
        var start = new Vector3(2, 6, 0);
        foreach (var destination in new[] { new Vector3(1, 7, 0), new Vector3(-2, -6, 0) })
        {
            var path = graph.FindPath(start, destination, out var prefix);
            var floor = NativeActorNavigationRoute.ReadTerminalFloor(path, (point, index) => prefix(point, index, .01f, .6f));
            if (!floor(destination + new Vector3(0, 0, .3f)) || floor(destination + new Vector3(0, 0, 1)) ||
                floor(new Vector3(20, 20, 0)) || path.Count > 1 && floor(start))
                throw new InvalidOperationException("Terminal source floor selected the wrong prefix index, triangle or elevation.");
        }
    }
}
