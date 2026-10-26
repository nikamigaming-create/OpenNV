using System.Reflection;
using System.Text.Json;
using Godot;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;

public partial class NativeActorPerformanceAudit
{
    // A native query/intent component fixture, not campaign or animation
    // acceptance. No source actor, package, arrival or collision rule is faked.
    private async Task CheckPursuitWaypoint()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var type = typeof(RuntimeNativeActorCombat);
        FieldInfo Field(string name) => type.GetField(name, flags) ?? throw new MissingFieldException(name);
        var pursuit = type.GetMethod("PursuitTarget", flags)!;
        var fixture = new Node3D(); AddChild(fixture);
        var floorShape = new BoxShape3D { Size = new(12, .5f, 12) };
        var capsule = new CapsuleShape3D { Radius = .3f, Height = 1.8f };
        var floor = new StaticBody3D { Position = new(0, -.25f, 0) };
        floor.AddChild(new CollisionShape3D { Shape = floorShape }); fixture.AddChild(floor);
        var body = new CharacterBody3D { Position = new(0, .002f, 0), SafeMargin = .001f, FloorSnapLength = .32f };
        body.AddChild(new CollisionShape3D { Position = new(0, .9f, 0), Shape = capsule }); fixture.AddChild(body);
        // Keep the unconfigured combat component out of the scene tree; only
        // its real body/query/waypoint owner participates in this fixture.
        var combat = new RuntimeNativeActorCombat();
        Field("_actor").SetValue(combat, body); Field("_mover").SetValue(combat, body);
        Field("_radius").SetValue(combat, .3f);
        IEnumerator<IReadOnlyList<Vector3>?>? search = null;
        var baseline = NativeCapsuleNavigation.RegisteredSearches;
        try
        {
            for (var frame = 0; frame < 3; frame++)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                body.Velocity = Vector3.Down; body.MoveAndSlide();
            }
            if (!body.IsOnFloor()) throw new InvalidDataException("Pursuit fixture has no actual native floor.");
            // A supported root can be above the eventual queried landing by a
            // small vertical margin. The resulting route is nonempty while
            // its complete native endpoint has exactly zero X/Z displacement.
            body.GlobalPosition += Vector3.Up * .02f;
            var position = body.GlobalPosition;
            async Task<Vector3?> Complete(Vector3 target, bool observeStall)
            {
                Field("_pursuitPath").SetValue(combat, new[] { position + Vector3.Right * 2 });
                Field("_pursuitCursor").SetValue(combat, 0);
                Field("_waypointDistance").SetValue(combat, 2f);
                Field("_routeStall").SetValue(combat, .2d);
                Field("_routeClock").SetValue(combat, 100d);
                search = NativeCapsuleNavigation.Search(body, position, target, .4f, .3f,
                    _ => true, 512, corridor: [target]).GetEnumerator();
                Field("_routeSearch").SetValue(combat, search);
                for (var frame = 0; frame < 120; frame++)
                {
                    await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                    var before = (double)Field("_routeStall").GetValue(combat)!;
                    var destination = (Vector3?)pursuit.Invoke(combat, [target, .001d, 0f]);
                    if (observeStall && Math.Abs((double)Field("_routeStall").GetValue(combat)! - before - .001d) > 1e-9)
                        throw new InvalidDataException("Pursuit charged a stalled waypoint more or less than once per tick.");
                    if (Field("_routeSearch").GetValue(combat) is not null) continue;
                    var work = NativeCapsuleNavigation.Work(search)!;
                    if (work.State != "completed" || work.GuidedSamples == 0)
                        throw new InvalidDataException("Pursuit fixture did not complete its actual supported native route.");
                    search.Dispose(); search = null;
                    return destination;
                }
                throw new InvalidDataException("Pursuit fixture did not complete within its native work bound.");
            }
            var zero = await Complete(position, observeStall: false);
            var zeroPath = (Vector3[])Field("_pursuitPath").GetValue(combat)!;
            if (zero is not null || zeroPath.Length == 0 || zeroPath.Any(point => point.X != position.X || point.Z != position.Z) ||
                (int)Field("_pursuitCursor").GetValue(combat)! != zeroPath.Length ||
                (double)Field("_routeStall").GetValue(combat)! != 0 || body.GlobalPosition != position)
                throw new InvalidDataException("A newly completed reached waypoint published false movement intent.");
            var target = position + Vector3.Right * 2;
            var moving = await Complete(target, observeStall: true);
            var movingPath = (Vector3[])Field("_pursuitPath").GetValue(combat)!;
            var cursor = (int)Field("_pursuitCursor").GetValue(combat)!;
            var horizontal = moving is { } next ? new Vector2(next.X - position.X, next.Z - position.Z) : Vector2.Zero;
            var remaining = moving is { } candidateDestination ? new Vector2(target.X - candidateDestination.X, target.Z - candidateDestination.Z).Length() : 2;
            // Smoothing retains the first supported height transition. That
            // valid next waypoint need not be the final two-metre endpoint.
            if (moving is not { } waypoint || cursor >= movingPath.Length || waypoint != movingPath[cursor] ||
                !waypoint.IsFinite() || horizontal.Length() <= body.SafeMargin * 8 || horizontal.Length() > 2 + body.SafeMargin * 8 ||
                remaining >= 2 || movingPath.Length == 0 || new Vector2(movingPath[^1].X - target.X, movingPath[^1].Z - target.Z).Length() > body.SafeMargin * 8)
                throw new InvalidDataException("Reached-waypoint consumption discarded a genuine native movement destination: " + JsonSerializer.Serialize(new
                {
                    position = position.ToString(),
                    target = target.ToString(),
                    destination = moving?.ToString(),
                    cursor,
                    path = movingPath.Select(point => new[] { point.X, point.Y, point.Z }).ToArray(),
                    horizontalProgress = horizontal.Length(),
                    remaining
                }));
            var motion = NativeCapsuleNavigation.RouteMotion(position, Vector3.Right * .05f, waypoint);
            body.Velocity = motion * 60 + Vector3.Down; body.MoveAndSlide();
            if (body.GlobalPosition.X - position.X < .01f)
                throw new InvalidDataException("The genuine native destination did not permit actual capsule displacement.");
            if (NativeCapsuleNavigation.RegisteredSearches != baseline)
                throw new InvalidDataException("Pursuit waypoint completion retained a native work registration.");
            GD.Print("OPENNV_PURSUIT_WAYPOINT_PASS nativeZeroEndpoint=true sameTickConsumed=true falseMovement=false " +
                $"genuineDestination=true nextWaypointDistance={horizontal.Length():R} nativeDisplacement=true stallDeltaExactlyOnce=true fixture=synthetic-native-query-component gameplay=separate");
        }
        finally
        {
            search?.Dispose(); combat.Free(); fixture.Free(); floorShape.Dispose(); capsule.Dispose();
        }
    }
}
