using System.Text.Json;
using Godot;
using OpenNV.Runtime.World.Cells;

public partial class NativeLocomotionAudit
{
    private async Task CheckNativeSourceArrivalRegion()
    {
        var scene = new Node3D(); AddChild(scene);
        void Box(Vector3 position, Vector3 size)
        {
            var box = new StaticBody3D { Position = position };
            box.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } }); scene.AddChild(box);
        }
        Box(new(0, -.5f, -2), new(16, 1, 16));
        Box(new(0, 1, -2), new(4, 2, 2));
        var body = CorridorBody(scene, new(0, .05f, 1));
        static Vector3 Source(Vector3 point) => new(point.X, -point.Z, point.Y);
        static CellNavigationGraph Floor(float height)
        {
            using var data = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                schema = "opennv-owned-cell-navigation/v1",
                navmeshes = new[] { new
                {
                    formId = "floor", cellFormId = "cell", version = 11,
                    verticesGameUnits = new float[][] { [-8, -6, height], [8, -6, height], [-8, 2, height], [8, 2, height] },
                    triangles = new[]
                    {
                        new { vertexIndices = new[] { 0, 1, 2 }, adjacentTriangles = new[] { -1, 1, -1 }, flags = 0 },
                        new { vertexIndices = new[] { 1, 3, 2 }, adjacentTriangles = new[] { -1, -1, 0 }, flags = 0 }
                    }, externalConnections = Array.Empty<object>()
                } }
            }));
            return CellNavigationGraph.Load(data.RootElement, new HashSet<string> { "cell" });
        }
        try
        {
            await SettleCorridorBody(body);
            var pose = body.GlobalTransform;
            var requested = new Vector3(0, 0, -2.1f);
            var graph = Floor(0);
            var sourcePath = graph.FindPath(Source(pose.Origin), Source(requested), destinationRadiusGameUnits: 1.6f);
            var sourceSelected = sourcePath[^1];
            var selected = new Vector3(sourceSelected.X, sourceSelected.Z, -sourceSelected.Y);
            if (selected == requested) throw new InvalidOperationException("Synthetic source projection did not preserve an independent requested point.");
            var sourceRegion = graph.ArrivalRegion(Source(pose.Origin), Source(requested), sourceSelected, 1.6f,
                body.SafeMargin * 8, NativeCapsuleNavigation.SourceHeightTolerance);
            bool Arrival(Vector3 point) => sourceRegion(Source(point));
            var exactRefused = false;
            try { _ = NativeCapsuleNavigation.FindRefined(body, pose.Origin, selected, .4f, .32f, .16f, _ => true); }
            catch (InvalidOperationException) { exactRefused = true; }
            if (!exactRefused || body.GlobalTransform != pose)
                throw new InvalidOperationException("The original selected source point was not a real capsule obstruction.");
            var portal = new Vector3(0, 0, -.3f);
            var projection = new NativeNavigationProjectionRegion(requested, selected, 1.6f);
            var prefix = NativeCapsuleNavigation.Intent(pose.Origin, [portal, selected], length: 2.1f, projection: projection);
            if (prefix.Target != portal || prefix.Resume != 1 || prefix.Projection is not null)
                throw new InvalidOperationException("Intermediate source slice did not retain its authored portal and untraversed tail.");
            var prefixRoute = NativeCapsuleNavigation.FindRefined(body, pose.Origin, prefix.Target, .4f, .32f, .16f,
                _ => true, corridor: prefix.Corridor);
            if (body.GlobalTransform != pose) throw new InvalidOperationException("Intermediate source query moved its body.");
            await WalkCorridorBody(body, prefixRoute.Path, .04f);
            if (!body.IsOnFloor() || body.GlobalPosition.DistanceTo(portal) > .06f)
                throw new InvalidOperationException("Ordinary controller did not reach its real supported source portal.");
            pose = body.GlobalTransform;
            var tail = NativeCapsuleNavigation.Intent(pose.Origin, [selected], length: 2.1f, projection: projection);
            if (tail.Projection != projection || tail.Resume != 1 || tail.ReferenceApproach)
                throw new InvalidOperationException("Replanning from the actual portal lost the final source projection region.");
            var route = NativeCapsuleNavigation.FindRefined(body, pose.Origin, selected, .4f, .32f, .16f, _ => true,
                corridor: tail.Corridor, arrival: Arrival);
            using var placement = new NativeCapsulePlacementQuery(body);
            if (body.GlobalTransform != pose || !Arrival(route.Path[^1]) || !placement.CanStand(route.Path[^1]) ||
                route.Path[^1].DistanceTo(selected) <= .5f || placement.CanStand(selected))
                throw new InvalidOperationException("Source region lost its requested radius, real supported endpoint or unchanged query body.");
            var originalGeometry = scene.GetChildren().OfType<StaticBody3D>().Select(node => (node, node.GlobalTransform)).ToArray();
            var occupiedRegion = graph.ArrivalRegion(Source(pose.Origin), Source(selected), Source(selected), .1f,
                body.SafeMargin * 8, NativeCapsuleNavigation.SourceHeightTolerance);
            var occupiedRefused = false;
            try
            {
                _ = NativeCapsuleNavigation.FindRefined(body, pose.Origin, selected, .4f, .32f, .16f, _ => true,
                    arrival: point => occupiedRegion(Source(point)));
            }
            catch (InvalidOperationException) { occupiedRefused = true; }
            var upper = selected + Vector3.Up;
            var upperRegion = Floor(1).ArrivalRegion(Source(pose.Origin), Source(requested + Vector3.Up), Source(upper), 1.6f,
                body.SafeMargin * 8, NativeCapsuleNavigation.SourceHeightTolerance);
            var upperRefused = false;
            try
            {
                _ = NativeCapsuleNavigation.FindRefined(body, pose.Origin, upper, .4f, .32f, .16f, _ => true,
                    arrival: point => upperRegion(Source(point)));
            }
            catch (InvalidOperationException) { upperRefused = true; }
            var unloadedRefused = false;
            try
            {
                _ = NativeCapsuleNavigation.FindRefined(body, pose.Origin, selected, .4f, .32f, .16f, _ => false,
                    arrival: Arrival);
            }
            catch (InvalidOperationException) { unloadedRefused = true; }
            if (!occupiedRefused || !upperRefused || !unloadedRefused || body.GlobalTransform != pose)
                throw new InvalidOperationException("Source region bypassed an occupied destination, authored floor or native residency.");
            await WalkCorridorBody(body, route.Path, .04f);
            if (!body.IsOnFloor() || !Arrival(body.GlobalPosition) ||
                originalGeometry.Any(value => value.node.GlobalTransform != value.GlobalTransform))
                throw new InvalidOperationException("Ordinary controller did not execute the supported source region with its real wall intact.");
            GD.Print($"OPENNV_NATIVE_SOURCE_ARRIVAL_REGION_PASS requested={requested} selected={selected} radius=1.6 accepted={route.Path[^1]} sourceSha256={graph.SourceSha256} portalThenRegion=true exactPointRefused=true occupiedRegionRefused=true otherFloorRefused=true unloadedRefused=true controllerArrival=true wallUnchanged=true queryBodyUnmoved=true maxNodesPerSearch=1200 fixture=synthetic gameplay=separate parity=unverified");
        }
        finally { scene.Free(); }
    }
}
