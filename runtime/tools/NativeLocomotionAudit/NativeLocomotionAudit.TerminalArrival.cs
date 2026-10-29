using Godot;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;
using System.Text.Json;

public partial class NativeLocomotionAudit
{
    private async Task CheckNativeTerminalArrivalRegion()
    {
        await CheckNativePackageRouteArrival();
        var scene = new Node3D(); AddChild(scene);
        StaticBody3D Box(Vector3 position, Vector3 size)
        {
            var box = new StaticBody3D { Position = position };
            box.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
            scene.AddChild(box); return box;
        }
        Box(new(0, -.5f, 0), new(16, 1, 16));
        var wall = Box(new(0, 1, -2.322f), new(4, 2, .2f));
        var body = new CharacterBody3D { Position = new(0, .05f, 1), FloorSnapLength = .33f, FloorMaxAngle = FloorAngle };
        body.AddChild(new CollisionShape3D
        {
            Position = Vector3.Up * .95f,
            Shape = new CapsuleShape3D { Radius = .33f, Height = 1.9f }
        });
        scene.AddChild(body);
        try
        {
            await SettleCorridorBody(body);
            var original = body.GlobalTransform;
            var geometry = scene.GetChildren().OfType<StaticBody3D>().Select(node => (node, node.GlobalTransform)).ToArray();
            var portal = Vector3.Zero;
            var goal = new Vector3(0, 0, -2);
            const float radius = .25f;
            var prefix = NativeCapsuleNavigation.Intent(original.Origin, [portal, goal], goal, radius);
            if (prefix.ReferenceApproach || prefix.ArrivalRadius != 0 || prefix.Target != portal || prefix.Resume != 1)
                throw new InvalidOperationException("A long final goal region skipped its mandatory source portal.");
            var prefixRoute = NativeCapsuleNavigation.FindRefined(body, original.Origin, prefix.Target, .4f, .66f, .165f,
                _ => true, 512, corridor: prefix.Corridor);
            if (body.GlobalTransform != original) throw new InvalidOperationException("Terminal prefix query moved its controller.");
            await WalkTerminalArrivalRoute(body, prefixRoute.Path);
            if (!body.IsOnFloor() || body.GlobalPosition.DistanceTo(portal) > .01f)
                throw new InvalidOperationException("Ordinary movement did not reach the required source portal.");
            var pose = body.GlobalTransform;
            var tail = NativeCapsuleNavigation.Intent(pose.Origin, [goal], goal, radius);
            if (!tail.ReferenceApproach || tail.ArrivalRadius != radius || tail.Target != goal || tail.Corridor.Count != 0)
                throw new InvalidOperationException("Replanning at the source portal discarded the existing long terminal region.");
            using var placement = new NativeCapsulePlacementQuery(body);
            var supported = new Vector3(goal.X, pose.Origin.Y, goal.Z + .245f);
            if (!placement.CanStand(supported) || placement.CanStand(goal) || supported.DistanceTo(goal) > radius)
                throw new InvalidOperationException("Terminal fixture lost its supported 0.245m endpoint or admitted the occupied exact goal.");
            var route = NativeCapsuleNavigation.FindRefined(body, pose.Origin, tail.Target, .4f, .66f, .165f,
                _ => true, 512, targetRadius: tail.ArrivalRadius, corridor: tail.Corridor);
            if (body.GlobalTransform != pose || route.Path[^1].DistanceTo(goal) > radius || !placement.CanStand(route.Path[^1]))
                throw new InvalidOperationException("Long terminal query changed its radius, moved its body or selected an unsupported endpoint.");
            var occupiedPortal = goal + Vector3.Back * .1f;
            var regionalCorridor = NativeCapsuleNavigation.TerminalRegionCorridor([portal, occupiedPortal, goal],
                point => point.DistanceTo(goal) <= radius);
            var projected = NativeCapsuleNavigation.Intent(pose.Origin, regionalCorridor,
                projection: new(goal, regionalCorridor[^1], radius));
            var projectedRoute = NativeCapsuleNavigation.FindRefined(body, pose.Origin, projected.Target, .4f, .66f, .165f,
                _ => true, 512, corridor: projected.Corridor, arrival: point => point.DistanceTo(goal) <= radius);
            if (body.GlobalTransform != pose || !placement.CanStand(projectedRoute.Path[^1]) || projectedRoute.Path[^1].DistanceTo(goal) > radius)
                throw new InvalidOperationException("Projected pursuit required a portal midpoint inside the occupied attack region.");
            var outside = new Vector3(2, 0, -2);
            var folded = NativeCapsuleNavigation.TerminalRegionCorridor([portal, occupiedPortal, outside, occupiedPortal, goal],
                point => point.DistanceTo(goal) <= radius);
            if (!folded.Contains(outside)) throw new InvalidOperationException("Projected pursuit trimmed an untraversed source prefix outside its region.");
            void Refuse(NativeNavigationIntent intent, Func<Vector3, bool> resident, string boundary)
            {
                var refused = false;
                try
                {
                    _ = NativeCapsuleNavigation.FindRefined(body, pose.Origin, intent.Target, .4f, .66f, .165f,
                        resident, 512, targetRadius: intent.ArrivalRadius, corridor: intent.Corridor);
                }
                catch (InvalidOperationException) { refused = true; }
                if (!refused || body.GlobalTransform != pose)
                    throw new InvalidOperationException("Terminal arrival bypassed " + boundary + " or moved its query body.");
            }
            Refuse(NativeCapsuleNavigation.Intent(pose.Origin, [goal], goal, 0), _ => true, "the declared exact goal");
            Refuse(NativeCapsuleNavigation.Intent(pose.Origin, [goal], goal + Vector3.Right * .05f, radius),
                _ => true, "a mismatched projected/reference goal");
            var occupied = new Vector3(wall.Position.X, 0, wall.Position.Z);
            Refuse(NativeCapsuleNavigation.Intent(pose.Origin, [occupied], occupied, radius), _ => true, "an occupied final region");
            var near = new Vector3(0, 0, .1f);
            var blockedPrefix = NativeCapsuleNavigation.Intent(pose.Origin, [occupied, near], near, radius);
            var upperPrefix = NativeCapsuleNavigation.Intent(pose.Origin, [new(0, 3, 0), near], near, radius);
            if (blockedPrefix.ReferenceApproach || blockedPrefix.ArrivalRadius != 0 || blockedPrefix.Target != occupied ||
                upperPrefix.ReferenceApproach || upperPrefix.ArrivalRadius != 0 || upperPrefix.Target != new Vector3(0, 3, 0))
                throw new InvalidOperationException("A nearby final region erased a blocked or elevated intermediate source waypoint.");
            Refuse(blockedPrefix, _ => true, "a blocked intermediate source portal");
            Refuse(upperPrefix, _ => true, "an unsupported intermediate source floor");
            Refuse(tail, _ => false, "unknown native collision residency");
            await WalkTerminalArrivalRoute(body, route.Path);
            if (!body.IsOnFloor() || body.GlobalPosition.DistanceTo(goal) > radius)
                throw new InvalidOperationException("Ordinary controller movement did not reach the existing terminal region.");
            var returned = body.GlobalTransform;
            var reverse = NativeCapsuleNavigation.FindRefined(body, returned.Origin, original.Origin, .4f, .66f, .165f,
                _ => true, 512, corridor: [portal, original.Origin]);
            if (body.GlobalTransform != returned) throw new InvalidOperationException("Reverse terminal query moved its body.");
            await WalkTerminalArrivalRoute(body, reverse.Path);
            if (!body.IsOnFloor() || body.GlobalPosition.DistanceTo(original.Origin) > .01f ||
                geometry.Any(value => value.node.GlobalTransform != value.GlobalTransform))
                throw new InvalidOperationException("Reverse terminal movement lost support or changed the original collision geometry.");
            GD.Print($"OPENNV_NATIVE_TERMINAL_ARRIVAL_REGION_PASS radius={radius} supportedDistance={supported.DistanceTo(goal)} acceptedDistance={route.Path[^1].DistanceTo(goal)} " +
                "portalThenRegion=true originalRadius=true exactGoalRefused=true mismatchedGoalRefused=true occupiedRegionRefused=true blockedPrefixRefused=true " +
                "otherFloorPrefixRefused=true unknownGeometryRefused=true controllerBothDirections=true geometryUnchanged=true queryBodyUnmoved=true " +
                "maxNodesPerSearch=512 fixture=synthetic gameplay=separate parity=unverified");
        }
        finally { scene.Free(); }
    }

    private async Task CheckNativePackageRouteArrival()
    {
        var scene = new Node3D(); AddChild(scene);
        var floor = new StaticBody3D { Position = new(0, -.5f, 0) };
        floor.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new(16, 1, 16) } });
        scene.AddChild(floor);
        var body = new CharacterBody3D { Position = new(-2, .05f, 0), FloorSnapLength = .33f, FloorMaxAngle = FloorAngle };
        var envelope = new CapsuleShape3D { Radius = .33f, Height = 1.9f };
        body.AddChild(new CollisionShape3D { Position = Vector3.Up * .95f, Shape = envelope });
        scene.AddChild(body);
        try
        {
            await SettleCorridorBody(body);
            using var json = JsonDocument.Parse("""
                {"schema":"opennv-owned-cell-navigation/v1","navmeshes":[
                  {"formId":"floor","cellFormId":"cell","version":11,
                   "verticesGameUnits":[[-8,-8,-0.3],[8,-8,-0.3],[8,8,-0.3],[-8,8,-0.3]],
                   "triangles":[{"vertexIndices":[0,1,2],"adjacentTriangles":[-1,-1,1],"flags":0},
                                {"vertexIndices":[0,2,3],"adjacentTriangles":[0,-1,-1],"flags":0}],
                   "externalConnections":[]}]}
                """);
            var graph = CellNavigationGraph.Load(json.RootElement, new HashSet<string> { "cell" });
            Vector3 Source(Vector3 point) => new(point.X, -point.Z, point.Y);
            Vector3 World(Vector3 point) => new(point.X, point.Z, -point.Y);
            var sourcePath = graph.FindPath(Source(body.GlobalPosition), Source(new(2, -.3f, 0)), out var sourcePrefix);
            var points = sourcePath.Select(World).ToArray();
            Func<Vector3, bool> Prefix(Vector3 point, int index)
            {
                var predicate = sourcePrefix(Source(point), index, .01f, NativeCapsuleNavigation.SourceHeightTolerance);
                return current => predicate(Source(current));
            }
            var source = new NativeActorNavigationRoute(points, SourceSha256: graph.SourceSha256,
                TerminalFloor: NativeActorNavigationRoute.ReadTerminalFloor(points, Prefix));
            // Preserve the distinct requested point. Its terminal projection's
            // tiny round-trip difference cannot bypass the walked prefix.
            var target = points[^1] + Vector3.Right * .00001f;
            const float radius = .0714375f;
            var intent = NativeCapsuleNavigation.Intent(body.GlobalPosition, points, target, radius);
            if (intent.ReferenceApproach || intent.Resume != points.Length)
                throw new InvalidOperationException("Package fixture did not retain its complete terminal source prefix.");
            var pose = body.GlobalTransform;
            var query = NativeCapsuleNavigation.FindRefined(body, pose.Origin, intent.Target, .4f, .66f, .165f,
                _ => true, 512, corridor: intent.Corridor);
            var path = query.Path.ToArray();
            var identity = new NativePackageRouteIdentity(new("fixture.esm", 1), target, radius, graph.SourceSha256,
                NativeNavigationContracts.PackageReceiptContext(), body.GetRid().Id, body.GetWorld3D().GetInstanceId(), envelope, body.GlobalBasis.Scale);
            var request = new NativePackageRouteRequest(identity, source, intent, points.Length);
            var prefix = new NativePackageRouteReceipt(request, path);
            if (body.GlobalTransform != pose || path.Length == 0 ||
                prefix.Approach(identity, path, body.GlobalPosition, body.IsOnFloor(), 0) is not null)
                throw new InvalidOperationException("Package query moved its body, omitted its landing or consumed an unwalked prefix.");
            await WalkTerminalArrivalRoute(body, path);
            var approach = prefix.Approach(identity, path, body.GlobalPosition, body.IsOnFloor(), path.Length) ??
                throw new InvalidOperationException("Walked terminal native prefix did not admit its separate reference query.");
            if (prefix.Arrived(identity, path, body.GlobalPosition, body.IsOnFloor(), path.Length) ||
                body.GlobalPosition.DistanceTo(target) <= radius)
                throw new InvalidOperationException("Package fixture lost the source/native floor distinction or treated its prefix as arrival.");
            pose = body.GlobalTransform;
            var finalQuery = NativeCapsuleNavigation.FindRefined(body, pose.Origin, approach.Target, .4f, .66f, .165f,
                _ => true, 512, targetRadius: approach.ArrivalRadius, corridor: approach.Corridor);
            var finalPath = finalQuery.Path.ToArray();
            if (body.GlobalTransform != pose || finalPath.Length == 0)
                throw new InvalidOperationException("Final package query moved its controller or discarded its supported stationary endpoint.");
            var stationary = NativeCapsuleNavigation.FindRefined(body, finalPath[^1], finalPath[^1], .4f, .66f, .165f,
                _ => true, 512, corridor: [finalPath[^1]]);
            if (body.GlobalTransform != pose || stationary.Path.Count == 0 ||
                stationary.Path[^1].DistanceTo(finalPath[^1]) > radius)
                throw new InvalidOperationException("A supported stationary query lost its actual endpoint or moved the query body.");
            var final = new NativePackageRouteReceipt(request with { Intent = approach }, finalPath);
            await WalkTerminalArrivalRoute(body, finalPath);
            if (!final.Arrived(identity, finalPath, body.GlobalPosition, body.IsOnFloor(), finalPath.Length) ||
                final.Arrived(identity, path, body.GlobalPosition, body.IsOnFloor(), finalPath.Length) ||
                final.Arrived(identity, finalPath, body.GlobalPosition + Vector3.Up, true, finalPath.Length))
                throw new InvalidOperationException("Final native package arrival lost its physical endpoint, exact query or floor.");
            GD.Print("OPENNV_NATIVE_PACKAGE_ARRIVAL_PASS sourceFloorOffset=.3 originalRadius=.0714375 " +
                "walkedTerminalPrefix=true separateFinalQuery=true originalRequestedPoint=true groundedNativeArrival=true " +
                "prefixCannotArrive=true wrongFloorRefused=true stationaryEndpointRetained=true queryBodyUnmoved=true fixture=synthetic gameplay=separate parity=unverified");
        }
        finally { scene.Free(); }
    }

    private async Task WalkTerminalArrivalRoute(CharacterBody3D body, IReadOnlyList<Vector3> route)
    {
        foreach (var waypoint in route)
        {
            var maximumFrames = checked((int)Math.Ceiling(body.GlobalPosition.DistanceTo(waypoint) / GetPhysicsProcessDeltaTime()) + 90);
            for (var frame = 0; frame < maximumFrames; frame++)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                var offset = waypoint - body.GlobalPosition; offset.Y = 0;
                if (offset.Length() <= .001f) break;
                if (frame == maximumFrames - 1) throw new InvalidOperationException("Ordinary movement did not reach its checked terminal waypoint.");
                var delta = (float)GetPhysicsProcessDeltaTime();
                var motion = NativeCapsuleNavigation.RouteMotion(body.GlobalPosition, offset.Normalized() * delta, waypoint);
                body.Velocity = motion / delta + Vector3.Up *
                    (body.IsOnFloor() ? Math.Min(body.Velocity.Y, 0) : body.Velocity.Y - 9.81f * delta);
                if (NativeCharacterStep.TryStep(body, motion, .4f)) body.Velocity = Vector3.Down * .01f;
                body.MoveAndSlide();
            }
        }
    }
}
