using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using OpenNV.Runtime;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.World.Cells;

// Selected source placement and its actual NIF collision, with a disposable
// player controller or read-only checkpoint-bound actor. No ordinary campaign
// progress, substitute floor or changed source geometry is used.
internal static partial class NativeOwnedNavigationAudit
{
    internal static async Task Run(Node3D owner, string[] arguments)
    {
        var floorOnly = arguments[0] == "--owned-floor-query";
        var actorRoute = arguments[0] == "--owned-actor-route";
        var wholeRoute = actorRoute || arguments[0] == "--owned-route";
        var maximumNodes = actorRoute ? 512 : wholeRoute ? 1200 : 1;
        var offset = floorOnly ? 10 : actorRoute ? 14 : 12;
        if (arguments.Length < offset)
            throw new ArgumentException("Owned navigation needs game/mod/root, source plugin/reference, from metres, then to metres or floor-ray reach, followed by dependency roots.");
        Vector3 Point(int index) => new(float.Parse(arguments[index], CultureInfo.InvariantCulture),
            float.Parse(arguments[index + 1], CultureInfo.InvariantCulture), float.Parse(arguments[index + 2], CultureInfo.InvariantCulture));
        var start = Point(6); var target = floorOnly ? start : Point(9);
        var reach = floorOnly ? float.Parse(arguments[9], CultureInfo.InvariantCulture) : 0;
        if (!start.IsFinite() || !target.IsFinite() || floorOnly && (!float.IsFinite(reach) || reach <= 0))
            throw new InvalidDataException("Owned navigation requires finite points and a positive finite floor-ray reach.");
        Node3D? fixture = null; RuntimeNativeNifPrototype? prototype = null;
        try
        {
            var installation = new FalloutModStackSelection([new(arguments[2], arguments[3], arguments[offset..])]).Resolve(arguments[1]);
            RuntimeLiveContentSource.Configure(arguments[1], RuntimeLiveContentSource.FalloutNewVegasGame,
                installation.ContentRoots.Skip(1).ToArray(), installation.ActivePlugins, installation.Settings);
            var content = RuntimeLiveContentSource.Current!;
            using var records = FalloutPluginStack.Load(content.PluginSources);
            using var world = new FalloutReferenceWorld(records);
            var key = new FalloutFormKey(arguments[4], Convert.ToUInt32(arguments[5], 16));
            var record = records.GetEffective(key);
            if (record.Signature != "REFR" || record.IsDeleted)
                throw new InvalidDataException("Owned navigation needs an existing source static reference.");
            var cell = FalloutCellSceneReader.Read(records, FalloutCellSceneReader.ParentCell(record)!.Value);
            if (!actorRoute)
            {
                world.LoadCell(cell);
                if (!world.IsEnabled(key)) throw new InvalidDataException("Owned navigation cannot activate a source-disabled reference.");
            }
            var reference = cell.References.Single(value => value.FormKey == key);
            var source = records.GetEffective(reference.Base);
            if (source.Signature != "STAT") throw new InvalidDataException("Owned navigation requires a source STAT collision model.");
            var path = cell.BaseObjects[reference.Base].ModelPath ?? throw new InvalidDataException("Source static has no model.");
            if (!content.TryRead(path, null, out var bytes, out var resource)) throw new FileNotFoundException(path);
            var recordHash = SHA256.HashData(record.ReadData()); var baseHash = SHA256.HashData(source.ReadData());
            var modelHash = SHA256.HashData(bytes);
            var configuration = RuntimeConfiguration.Load(); var units = configuration.World.GameUnitsToMeters;
            prototype = new(bytes, units);
            if (prototype.Scene.CollisionShapes == 0) throw new InvalidDataException("Source model has no native collision shapes.");
            var placement = new Transform3D(GamebryoCoordinate.ConvertReferenceEuler(
                new(reference.RotationRadians[0], reference.RotationRadians[1], reference.RotationRadians[2]), reference.Scale),
                GamebryoCoordinate.ConvertVector(new(reference.Position[0], reference.Position[1], reference.Position[2])) * units);
            var instance = prototype.InstantiatePlaced(placement);
            fixture = new(); owner.AddChild(fixture); fixture.AddChild(instance);
            OwnedActorRoute? actorOwner = null;
            CharacterBody3D body;
            if (actorRoute)
            {
                actorOwner = PrepareOwnedActor(fixture, records, world, content, configuration, start, arguments[12], arguments[13], cell.Cell.FormKey);
                body = actorOwner.Body;
                if (!world.IsEnabled(key)) throw new InvalidDataException("The actual source checkpoint disables the selected collision reference.");
            }
            else
            {
                body = new CharacterBody3D
                {
                    Position = start,
                    CollisionLayer = configuration.Player.CollisionLayer,
                    CollisionMask = configuration.Player.CollisionMask,
                    FloorSnapLength = configuration.Player.CapsuleRadiusMeters,
                    FloorMaxAngle = Mathf.DegToRad(configuration.Player.MaximumWalkableSlopeDegrees)
                };
                body.AddChild(new CollisionShape3D
                {
                    Position = Vector3.Up * configuration.Player.SpawnCenterHeightMeters,
                    Shape = new CapsuleShape3D { Height = configuration.Player.CapsuleHeightMeters, Radius = configuration.Player.CapsuleRadiusMeters }
                });
                fixture.AddChild(body);
            }
            FalloutFormKey? Identity(ulong id) => GodotObject.InstanceFromId(id) is Node node && instance.IsAncestorOf(node) ? key : null;
            float[] Coordinates(Vector3 value) => [value.X, value.Y, value.Z];
            object? Floor(Vector3 point, float verticalReach)
            {
                using var request = PhysicsRayQueryParameters3D.Create(point + Vector3.Up * verticalReach,
                    point - Vector3.Up * verticalReach, body.CollisionMask, [body.GetRid()]);
                using var hit = body.GetWorld3D().DirectSpaceState.IntersectRay(request);
                var collider = hit.Count == 0 ? 0 : checked((ulong)hit["collider_id"].AsInt64());
                return hit.Count == 0 ? null : new
                {
                    position = Coordinates(hit["position"].AsVector3()),
                    normal = Coordinates(hit["normal"].AsVector3()),
                    collider,
                    source = Identity(collider)?.ToString(),
                    shape = hit["shape"].AsInt32(),
                    rayFrom = Coordinates(request.From),
                    rayTo = Coordinates(request.To)
                };
            }
            bool Supported(Vector3 point) => NativeCharacterStep.TrySupport(body, point, configuration.Player.StepHeightMeters, out _);
            await owner.ToSignal(owner.GetTree(), SceneTree.SignalName.PhysicsFrame);
            await owner.ToSignal(owner.GetTree(), SceneTree.SignalName.PhysicsFrame);
            var geometry = instance.FindChildren("*", "", true, false).OfType<CollisionObject3D>()
                .Select(node => (node, node.GlobalTransform)).ToArray();
            GD.Print(JsonSerializer.Serialize(new
            {
                kind = "owned-native-navigation-source",
                ownerBuild = typeof(NativeCapsuleNavigation).Module.ModuleVersionId,
                reference = key.ToString(),
                cell = cell.Cell.FormKey.ToString(),
                recordWinner = record.Plugin.Name,
                recordSha256 = Convert.ToHexString(recordHash),
                baseWinner = source.Plugin.Name,
                baseSha256 = Convert.ToHexString(baseHash),
                path,
                resource,
                modelSha256 = Convert.ToHexString(modelHash),
                sourcePlacement = Coordinates(placement.Origin),
                from = Coordinates(start),
                to = Coordinates(target),
                units,
                controllerOwner = actorRoute ? "source-NPC-BBX" : "configured-player-capsule",
                CapsuleHeightMeters = actorRoute ? (float?)null : configuration.Player.CapsuleHeightMeters,
                CapsuleRadiusMeters = actorOwner?.Radius ?? configuration.Player.CapsuleRadiusMeters,
                configuration.Player.StepHeightMeters,
                configuration.Player.MaximumWalkableSlopeDegrees,
                body.SafeMargin,
                body.FloorSnapLength,
                floor = Floor(start, floorOnly ? reach : configuration.Player.StepHeightMeters),
                recording = false,
                campaign = false,
                parity = false
            }));
            if (!floorOnly)
            {
                void RequireSourceFloor(string stage, Vector3 point)
                {
                    var supported = NativeCharacterStep.TrySupport(body, point, configuration.Player.StepHeightMeters, out var support);
                    GD.Print(JsonSerializer.Serialize(new
                    {
                        kind = "owned-native-route-endpoint",
                        stage,
                        point = Coordinates(point),
                        supported,
                        floor = support is null ? null : new
                        {
                            support.Collider,
                            support.Shape,
                            reference = Identity(support.Collider)?.ToString(),
                            point = Coordinates(support.Point),
                            normal = Coordinates(support.Normal),
                            rayFrom = Coordinates(support.From),
                            rayTo = Coordinates(support.Desired)
                        }
                    }));
                    if (!supported || support is null || Identity(support.Collider) != key)
                        throw new InvalidDataException("The selected source model does not own the route endpoint's walkable root floor.");
                }
                if (wholeRoute)
                {
                    RequireSourceFloor("requested-start", start);
                    RequireSourceFloor("requested-target", target);
                }
                object? Sweep(NativeNavigationSweep? sweep) => sweep is null ? null : new
                {
                    from = Coordinates(sweep.From),
                    to = Coordinates(sweep.Desired),
                    travel = Coordinates(sweep.Travel),
                    sweep.Collided,
                    contacts = sweep.Contacts.Select(contact => new
                    {
                        contact.Collider,
                        contact.Shape,
                        reference = contact.Reference?.ToString(),
                        point = Coordinates(contact.Point),
                        normal = Coordinates(contact.Normal)
                    })
                };
                object? Support(NativeNavigationLandingSupport? support) => support is null ? null : new
                {
                    capsule = Sweep(support.Capsule),
                    floor = new
                    {
                        support.Floor.Collider,
                        support.Floor.Shape,
                        reference = support.Floor.Reference?.ToString(),
                        point = Coordinates(support.Floor.Point),
                        normal = Coordinates(support.Floor.Normal),
                        rayFrom = Coordinates(support.Floor.From),
                        rayTo = Coordinates(support.Floor.Desired)
                    }
                };
                IReadOnlyList<Vector3>? Query(Vector3 from, Vector3 to)
                {
                    var pose = body.GlobalTransform;
                    NativeNavigationIntent? intent = null;
                    IReadOnlyList<Vector3> corridor = [to];
                    if (actorOwner is not null)
                    {
                        Vector3 Source(Vector3 point) => new Vector3(point.X, -point.Z, point.Y) / units;
                        var source = actorOwner.Navigation.FindPath(Source(from), Source(to))
                            .Select(point => GamebryoCoordinate.ConvertVector(point) * units).ToArray();
                        intent = NativeCapsuleNavigation.Intent(from, source, to, 0);
                        if (intent.Resume < source.Length) throw new InvalidDataException("Selected actor corridor proof exceeds one complete bounded source prefix.");
                        corridor = intent.Corridor;
                    }
                    var probe = new NativeNavigationProbe(NativeCapsuleNavigation.FirstCorridorContact(body, from,
                        intent?.ReferenceApproach == true ? [to] : corridor), Identity);
                    IReadOnlyList<Vector3>? route = null; string? error = null;
                    string? coarseError = null; var refinements = 0;
                    var spacing = actorOwner is null ? .32f : Math.Max(.3f, actorOwner.Radius * 2);
                    var destination = intent?.Target ?? to;
                    try { route = NativeCapsuleNavigation.Find(body, from, destination, configuration.Player.StepHeightMeters, spacing, _ => true, maximumNodes, probe: probe, corridor: corridor); }
                    catch (InvalidOperationException failure) { error = failure.Message; }
                    if (route is null && actorOwner is not null && Math.Max(.15f, actorOwner.Radius) < spacing)
                    {
                        coarseError = error; refinements = 1; spacing = Math.Max(.15f, actorOwner.Radius);
                        try { route = NativeCapsuleNavigation.Find(body, from, destination, configuration.Player.StepHeightMeters, spacing, _ => true, maximumNodes, probe: probe, corridor: corridor); error = null; }
                        catch (InvalidOperationException failure) { error = failure.Message; }
                    }
                    if (body.GlobalTransform != pose) throw new InvalidDataException("Owned navigation query moved its controller.");
                    var downward = probe.GuideDownwardSweep ?? probe.FirstDownwardSweep;
                    GD.Print(JsonSerializer.Serialize(new
                    {
                        kind = wholeRoute ? "owned-native-route-query" : "owned-native-step-query",
                        from = Coordinates(from),
                        to = Coordinates(to),
                        maximumNodes,
                        spacing,
                        refinements,
                        coarseError,
                        sourceCorridor = actorOwner is null ? null : corridor.Select(Coordinates),
                        sourceIntent = intent is null ? null : new { intent.Resume, intent.ArrivalRadius, intent.ReferenceApproach, target = Coordinates(intent.Target) },
                        route = route?.Select(Coordinates),
                        error,
                        downward = Sweep(downward),
                        rootFloor = downward is null ? null : Floor(downward.From + downward.Travel, Math.Min(configuration.Player.StepHeightMeters, body.FloorSnapLength)),
                        probe.ReconciledLandings,
                        firstReconciledLanding = Support(probe.FirstReconciledLanding),
                        noQueryMovement = true
                    }));
                    return route;
                }
                var forward = Query(start, target);
                if (arguments[0] == "--owned-step" || wholeRoute)
                {
                    if (forward is null) throw new InvalidDataException("Owned source navigation query refused its forward fixture.");
                    object? lastMove = null;
                    void ObserveController(string stage, Vector3 expected)
                    {
                        var supported = NativeCharacterStep.TrySupport(body, body.GlobalPosition, configuration.Player.StepHeightMeters, out var support);
                        var interval = Math.Min(configuration.Player.StepHeightMeters, body.FloorSnapLength);
                        GD.Print(JsonSerializer.Serialize(new
                        {
                            kind = "owned-native-step-controller",
                            stage,
                            position = Coordinates(body.GlobalPosition),
                            expected = Coordinates(expected),
                            velocity = Coordinates(body.Velocity),
                            onFloor = body.IsOnFloor(),
                            supported,
                            flatError = new Vector2(body.GlobalPosition.X - expected.X, body.GlobalPosition.Z - expected.Z).Length(),
                            supportRayFrom = Coordinates(body.GlobalPosition + Vector3.Up * body.SafeMargin),
                            supportRayTo = Coordinates(body.GlobalPosition - Vector3.Up * interval),
                            support = support is null ? null : new
                            {
                                support.Collider,
                                support.Shape,
                                reference = Identity(support.Collider)?.ToString(),
                                point = Coordinates(support.Point),
                                normal = Coordinates(support.Normal)
                            },
                            nearbyFloor = Floor(body.GlobalPosition, configuration.Player.StepHeightMeters),
                            lastMove
                        }));
                    }
                    async Task Settle()
                    {
                        for (var frame = 0; frame < 30; frame++)
                        {
                            await owner.ToSignal(owner.GetTree(), SceneTree.SignalName.PhysicsFrame);
                            var delta = (float)owner.GetPhysicsProcessDeltaTime();
                            body.Velocity = new(0, body.IsOnFloor() ? Math.Min(body.Velocity.Y, 0) :
                                body.Velocity.Y - configuration.Simulation.GravityMetersPerSecondSquared * delta, 0);
                            body.MoveAndSlide();
                            if (body.IsOnFloor()) break;
                        }
                    }
                    async Task Walk(IReadOnlyList<Vector3> route)
                    {
                        foreach (var waypoint in route)
                            for (var frame = 0; frame < 240; frame++)
                            {
                                await owner.ToSignal(owner.GetTree(), SceneTree.SignalName.PhysicsFrame);
                                var offsetToPoint = waypoint - body.GlobalPosition; offsetToPoint.Y = 0;
                                if (offsetToPoint.Length() <= .03f) break;
                                if (frame == 239) throw new InvalidDataException("Ordinary controller missed a source stair waypoint.");
                                var delta = (float)owner.GetPhysicsProcessDeltaTime();
                                var velocity = offsetToPoint.Normalized() * Math.Min(configuration.Player.MoveSpeedMetersPerSecond, offsetToPoint.Length() / delta);
                                velocity.Y = body.IsOnFloor() ? Math.Min(body.Velocity.Y, 0) : body.Velocity.Y - configuration.Simulation.GravityMetersPerSecondSquared * delta;
                                body.Velocity = velocity;
                                var from = body.GlobalPosition; var motion = new Vector3(velocity.X, 0, velocity.Z) * delta;
                                var stepped = NativeCharacterStep.TryStep(body, motion, configuration.Player.StepHeightMeters, out var blocked);
                                var afterStep = body.GlobalPosition;
                                if (stepped) body.Velocity = Vector3.Down * .01f;
                                body.MoveAndSlide();
                                lastMove = new
                                {
                                    from = Coordinates(from),
                                    motion = Coordinates(motion),
                                    stepped,
                                    blocked,
                                    afterStep = Coordinates(afterStep),
                                    afterSlide = Coordinates(body.GlobalPosition)
                                };
                            }
                    }
                    await Walk(forward);
                    ObserveController("forward-before-settle", forward[^1]);
                    await Settle();
                    ObserveController("forward-after-settle", forward[^1]);
                    var forwardPosition = body.GlobalPosition;
                    if (!Supported(forwardPosition)) throw new InvalidDataException("Ordinary forward traversal lost the mover's actual root floor support.");
                    if (new Vector2(forwardPosition.X - target.X, forwardPosition.Z - target.Z).Length() > .04f)
                        throw new InvalidDataException("Ordinary forward traversal did not retain its source target after gravity settling.");
                    if (wholeRoute)
                    {
                        RequireSourceFloor("actual-forward", forwardPosition);
                        if (!body.IsOnFloor() || Math.Abs(forwardPosition.Y - target.Y) > configuration.Player.StepHeightMeters)
                            throw new InvalidDataException("Ordinary forward route did not settle on its actual source floor at the target height.");
                    }
                    var reverse = Query(forwardPosition, start) ?? throw new InvalidDataException("Owned source navigation query refused its reverse fixture.");
                    await Walk(reverse);
                    ObserveController("reverse-before-settle", reverse[^1]);
                    await Settle();
                    ObserveController("reverse-after-settle", reverse[^1]);
                    var flatError = new Vector2(body.GlobalPosition.X - start.X, body.GlobalPosition.Z - start.Z).Length();
                    if (flatError > .04f || Math.Abs(body.GlobalPosition.Y - start.Y) > configuration.Player.StepHeightMeters ||
                        !Supported(body.GlobalPosition) || instance.GlobalTransform != placement ||
                        geometry.Any(value => value.node.GlobalTransform != value.GlobalTransform))
                        throw new InvalidDataException("Source stair traversal diverged or changed its owned geometry.");
                    if (wholeRoute)
                    {
                        RequireSourceFloor("actual-reverse", body.GlobalPosition);
                        if (!body.IsOnFloor()) throw new InvalidDataException("Ordinary reverse route did not settle on its actual source floor.");
                    }
                    GD.Print($"{(wholeRoute ? "OPENNV_OWNED_ROUTE_PASS" : "OPENNV_OWNED_STEP_PASS")} forward={forwardPosition} reverse={body.GlobalPosition} " +
                        $"forwardWaypoints={forward.Count} reverseWaypoints={reverse.Count} selectedSourceOnly=true controllerBothDirections=true " +
                        "noQueryMovement=true geometryUnchanged=true recording=false campaign=false parity=false");
                }
            }
            if (!recordHash.AsSpan().SequenceEqual(SHA256.HashData(record.ReadData())) ||
                !baseHash.AsSpan().SequenceEqual(SHA256.HashData(source.ReadData())) ||
                !content.TryRead(path, null, out var after, out _) || !modelHash.AsSpan().SequenceEqual(SHA256.HashData(after)))
                throw new InvalidDataException("Owned navigation changed its source records or model.");
            GD.Print("OPENNV_OWNED_NAVIGATION_SOURCE_UNCHANGED recording=false campaign=false parity=false");
            actorOwner?.RequireUnchanged();
        }
        finally { fixture?.Free(); prototype?.Scene.Root.Free(); RuntimeLiveContentSource.Clear(); }
    }
}
