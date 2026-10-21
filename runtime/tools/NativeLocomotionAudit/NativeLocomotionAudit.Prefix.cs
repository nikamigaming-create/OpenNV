using System.Text.Json;
using Godot;
using OpenNV.Runtime.World.Cells;

public partial class NativeLocomotionAudit
{
    private async Task CheckNativePrefixArrivalRegion()
    {
        var scene = new Node3D(); AddChild(scene);
        StaticBody3D Box(Vector3 position, Vector3 size)
        {
            var box = new StaticBody3D { Position = position };
            box.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
            scene.AddChild(box); return box;
        }
        Box(new(0, -.5f, 0), new(16, 1, 16));
        var obstacle = Box(new(0, 1, 0), new(1.5f, 2, 1.5f));
        var body = new CharacterBody3D { Position = new(-2, .05f, 2), FloorSnapLength = .33f, FloorMaxAngle = FloorAngle };
        body.AddChild(new CollisionShape3D
        {
            Position = Vector3.Up * .95f,
            Shape = new CapsuleShape3D { Radius = .33f, Height = 1.9f }
        });
        scene.AddChild(body);
        using var source = JsonDocument.Parse("""
            {"schema":"opennv-owned-cell-navigation/v1","navmeshes":[
              {"formId":"floor","cellFormId":"cell","version":11,
               "verticesGameUnits":[[-4,-4,0],[4,-4,0],[-4,4,0],[4,4,0]],
               "triangles":[{"vertexIndices":[0,1,2],"adjacentTriangles":[-1,1,-1],"flags":0},
                            {"vertexIndices":[1,3,2],"adjacentTriangles":[-1,-1,0],"flags":0}],"externalConnections":[]}]}
            """);
        var graph = CellNavigationGraph.Load(source.RootElement, new HashSet<string> { "cell" });
        static Vector3 Source(Vector3 point) => new(point.X, -point.Z, point.Y);
        static Vector3 World(Vector3 point) => new(point.X, point.Z, -point.Y);
        try
        {
            await SettleCorridorBody(body);
            var obstaclePose = obstacle.GlobalTransform;
            for (var direction = 0; direction < 2; direction++)
            {
                var pose = body.GlobalTransform;
                var destination = direction == 0 ? new Vector3(2, 2, 0) : new(-2, -2, 0);
                var points = graph.FindPath(Source(pose.Origin), destination, out var prefixFactory);
                var region = prefixFactory(points[0], 1, .01f, .6f);
                bool Arrival(Vector3 point) => region(Source(point));
                var route = NativeCapsuleNavigation.FindRefined(body, pose.Origin, World(points[0]), .4f, .66f, .165f,
                    _ => true, 512, corridor: [World(points[0])], arrival: Arrival);
                if (body.GlobalTransform != pose || obstacle.GlobalTransform != obstaclePose || !Arrival(route.Path[^1]) ||
                    route.Path[^1].DistanceTo(World(points[0])) < .75f)
                    throw new InvalidOperationException("Source prefix query moved geometry or required its occupied portal midpoint.");
                await WalkTerminalArrivalRoute(body, route.Path);
                if (!body.IsOnFloor() || !Arrival(body.GlobalPosition))
                    throw new InvalidOperationException("Native movement did not cross the source portal through its clear width.");
            }
            var retreat = NativeCapsuleNavigation.FindRefined(body, body.GlobalPosition, new(-2, 0, 2), .4f, .66f, .165f,
                _ => true, 512);
            await WalkTerminalArrivalRoute(body, retreat.Path);
            var wall = Box(new(0, 1, 0), new(30, 2, .25f)); wall.Rotation = new(0, -Mathf.Pi / 4, 0);
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            var stopped = body.GlobalTransform;
            var blockedPoints = graph.FindPath(Source(stopped.Origin), new(2, 2, 0), out var blockedFactory);
            var blockedRegion = blockedFactory(blockedPoints[0], 1, .01f, .6f);
            var refused = false;
            try
            {
                _ = NativeCapsuleNavigation.FindRefined(body, stopped.Origin, World(blockedPoints[0]), .4f, .66f, .165f,
                    _ => true, 512, arrival: point => blockedRegion(Source(point)));
            }
            catch (InvalidOperationException) { refused = true; }
            if (!refused || body.GlobalTransform != stopped)
                throw new InvalidOperationException("Source prefix bypassed a complete physical wall or moved its query body.");
            GD.Print("OPENNV_NATIVE_PREFIX_REGION_PASS occupiedMidpoint=true actualCapsule=true ordinaryMovement=true bothDirections=true wallRefused=true queryReadOnly=true");
        }
        finally { scene.Free(); }
    }
}
