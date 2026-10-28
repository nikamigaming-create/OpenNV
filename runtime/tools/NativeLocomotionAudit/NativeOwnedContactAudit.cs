using System.Globalization;
using System.Text.Json;
using Godot;
using OpenNV.Runtime;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.World.Cells;

// A private, source-geometry collision fixture. It never advances a campaign or
// supplies footage; ordinary traversal must separately pass the same contact.
internal static class NativeOwnedContactAudit
{
    internal static async Task Run(Node3D owner, string[] arguments)
    {
        if (arguments.Length is not (5 or 9 or 10 or 12 or 15 or 16) || arguments.Length == 16 && arguments[15] != "land")
            throw new ArgumentException("Owned contact needs installation, reference, three metre coordinates, optional quaternion, nearby geometry, capsule height/radius, optional target coordinates and optional land.");
        RuntimeLiveContentSource.Configure(arguments[0], RuntimeLiveContentSource.FalloutNewVegasGame);
        GD.Print($"OPENNV_CONTACT_ENGINE {ProjectSettings.GetSetting("physics/3d/physics_engine")} server={PhysicsServer3D.Singleton.GetClass()}");
        using var content = RuntimeLiveContentSource.Current!;
        using var records = FalloutPluginStack.Load(content.PluginSources);
        var key = records.RuntimeFormKey(Convert.ToUInt32(arguments[1], 16));
        var cell = FalloutCellSceneReader.Read(records, FalloutCellSceneReader.ParentCell(records.GetEffective(key))!.Value);
        var reference = cell.References.Single(value => value.FormKey == key);
        var path = cell.BaseObjects[reference.Base].ModelPath!;
        if (!content.TryRead(path, null, out var bytes, out _)) throw new FileNotFoundException(path);
        var prototype = new RuntimeNativeNifPrototype(bytes, .0142875f);
        var placement = new Transform3D(GamebryoCoordinate.ConvertReferenceEuler(
            new(reference.RotationRadians[0], reference.RotationRadians[1], reference.RotationRadians[2]), reference.Scale),
            GamebryoCoordinate.ConvertVector(new(reference.Position[0], reference.Position[1], reference.Position[2])) * .0142875f);
        var instance = prototype.InstantiatePlaced(placement);
        var body = new CharacterBody3D
        {
            Position = new(float.Parse(arguments[2], CultureInfo.InvariantCulture),
            float.Parse(arguments[3], CultureInfo.InvariantCulture), float.Parse(arguments[4], CultureInfo.InvariantCulture)),
            CollisionLayer = 2,
            CollisionMask = 1,
            FloorSnapLength = .32f,
            FloorMaxAngle = Mathf.DegToRad(RuntimeConfiguration.Load().Player.MaximumWalkableSlopeDegrees)
        };
        if (arguments.Length >= 9)
        {
            var rotation = arguments.Skip(5).Take(4).Select(value => float.Parse(value, CultureInfo.InvariantCulture)).ToArray();
            body.Basis = new(new Quaternion(rotation[0], rotation[1], rotation[2], rotation[3]).Normalized());
        }
        var height = arguments.Length >= 12 ? float.Parse(arguments[10], CultureInfo.InvariantCulture) : 1.8f;
        var radius = arguments.Length >= 12 ? float.Parse(arguments[11], CultureInfo.InvariantCulture) : .32f;
        body.FloorSnapLength = radius;
        body.AddChild(new CollisionShape3D
        {
            Position = Vector3.Up * height / 2,
            Shape = new CapsuleShape3D { Height = height, Radius = radius }
        });
        owner.AddChild(instance); owner.AddChild(body);
        var nearby = new List<Node3D>();
        var terrains = new List<StaticBody3D>();
        if (arguments.Length >= 10)
        {
            var selected = arguments[9].StartsWith("references=", StringComparison.Ordinal)
                ? arguments[9][11..].Split(',').Select(hex => records.RuntimeFormKey(Convert.ToUInt32(hex, 16))).ToHashSet() : null;
            foreach (var other in cell.References.Where(value => value.FormKey != key && (value.Flags & 0x800) == 0))
            {
                var point = GamebryoCoordinate.ConvertVector(new(other.Position[0], other.Position[1], other.Position[2])) * .0142875f;
                if ((selected is null ? point.DistanceTo(body.Position) > 8 : !selected.Contains(other.FormKey)) ||
                    cell.BaseObjects[other.Base].ModelPath is not { } model || !model.EndsWith(".nif", StringComparison.OrdinalIgnoreCase)) continue;
                if (!content.TryRead(model, null, out var payload, out _)) throw new FileNotFoundException(model);
                var candidate = new RuntimeNativeNifPrototype(payload, .0142875f);
                var node = candidate.InstantiatePlaced(new(GamebryoCoordinate.ConvertReferenceEuler(
                    new(other.RotationRadians[0], other.RotationRadians[1], other.RotationRadians[2]), other.Scale), point));
                node.Name = "Reference_" + other.FormKey;
                GD.Print($"OPENNV_OWNED_CONTACT_NEARBY reference={other.FormKey} model={model} position={point}");
                owner.AddChild(node); nearby.Add(node); candidate.Scene.Root.Free();
            }
        }
        if (arguments.Length == 16)
        {
            var world = cell.Cell.Worldspace ?? throw new InvalidDataException("Owned contact LAND requires an exterior worldspace.");
            var units = RuntimeConfiguration.Load().World.GameUnitsToMeters;
            var exterior = new FalloutExteriorGrid(records);
            var persistent = exterior.PersistentCell(world);
            var grid = exterior.Resolve(world, persistent, body.Position.X / units, -body.Position.Z / units, 3);
            foreach (var landCell in grid.Cells)
            {
                var source = FalloutLandscapeTransportResolver.ResolveCell(records, landCell, persistent);
                var prepared = RuntimeNativeLandscapeTransportBuilder.Prepare(source, units);
                var terrain = new StaticBody3D { Name = "LAND_" + source.Landscape };
                terrain.AddChild(new CollisionShape3D { Shape = new ConcavePolygonShape3D { Data = prepared.Faces } });
                owner.AddChild(terrain); terrains.Add(terrain);
                GD.Print($"OPENNV_OWNED_CONTACT_LAND cell={source.ActiveCell} land={source.Landscape} faces={prepared.Faces.Length / 3}");
            }
        }
        try
        {
            await owner.ToSignal(owner.GetTree(), SceneTree.SignalName.PhysicsFrame);
            await owner.ToSignal(owner.GetTree(), SceneTree.SignalName.PhysicsFrame);
            if (arguments.Length >= 15)
            {
                var target = new Vector3(float.Parse(arguments[12], CultureInfo.InvariantCulture),
                    float.Parse(arguments[13], CultureInfo.InvariantCulture), float.Parse(arguments[14], CultureInfo.InvariantCulture));
                foreach (var (spacing, nodes) in new[] { (.32f, 1200), (.16f, 3600) })
                {
                    var probe = new NativeNavigationProbe(null);
                    try
                    {
                        var partial = false;
                        IReadOnlyList<Vector3>? route = null;
                        foreach (var result in NativeCapsuleNavigation.Search(body, body.GlobalPosition, target, .4f, spacing, _ => true,
                            maximumNodes: nodes, probe: probe,
                            partialResult: spacing < .32f ? () => partial = true : null,
                            partialCandidate: point => RuntimeCoordinator.NativeBotPartialFloorEligible(body.GlobalPosition, target, point)))
                            if (result is not null) { route = result; break; }
                        if (route is null) throw new InvalidOperationException("Owned contact search returned no route.");
                        GD.Print($"OPENNV_OWNED_CONTACT_ROUTE result={(partial ? "partial" : "found")} spacing={spacing} target={target} endpoint={route[^1]} points={route.Count} " +
                            $"sourceRegion=absent terrainCells={terrains.Count} otherCellModels=absent gameplay=unverified parity=unverified");
                    }
                    catch (InvalidOperationException error)
                    {
                        GD.Print($"OPENNV_OWNED_CONTACT_ROUTE result=refused spacing={spacing} target={target} reason={error.Message} " +
                            $"sourceRegion=absent terrainCells={terrains.Count} otherCellModels=absent gameplay=unverified parity=unverified");
                    }
                }
            }
            foreach (var direction in new[] { Vector3.Right, Vector3.Left, Vector3.Forward, Vector3.Back, Vector3.Up, Vector3.Down })
            {
                using var request = new PhysicsTestMotionParameters3D
                {
                    From = body.GlobalTransform,
                    Motion = direction * .06f,
                    Margin = body.SafeMargin,
                    MaxCollisions = 6
                };
                using var result = new PhysicsTestMotionResult3D();
                var collided = PhysicsServer3D.BodyTestMotion(body.GetRid(), request, result);
                static float[] Point(Vector3 value) => [value.X, value.Y, value.Z];
                GD.Print(JsonSerializer.Serialize(new
                {
                    kind = "owned-contact-sweep",
                    reference = key.ToString(),
                    path,
                    direction = Point(direction),
                    collided,
                    travel = Point(result.GetTravel()),
                    contacts = Enumerable.Range(0, result.GetCollisionCount()).Select(index => new
                    {
                        point = Point(result.GetCollisionPoint(index)),
                        normal = Point(result.GetCollisionNormal(index)),
                        depth = result.GetCollisionDepth(index),
                        collider = (result.GetCollider(index) as Node)?.GetPath().ToString(),
                        priority = (result.GetCollider(index) as CollisionObject3D)?.CollisionPriority
                    })
                }));
            }
            var before = body.GlobalPosition;
            var maximumEscape = 0f;
            foreach (var direction in new[] { Vector3.Right, Vector3.Left, Vector3.Forward, Vector3.Back })
            {
                body.GlobalPosition = before;
                var blocks = new Dictionary<string, int>();
                for (var frame = 0; frame < 60; frame++)
                {
                    await owner.ToSignal(owner.GetTree(), SceneTree.SignalName.PhysicsFrame);
                    body.Velocity = direction * 3.6f + Vector3.Down * .16f;
                    if (NativeCharacterStep.TryStep(body, direction * .06f, .4f, out var blocked)) body.Velocity = Vector3.Down * .01f;
                    blocks[blocked ?? "climbed"] = blocks.GetValueOrDefault(blocked ?? "climbed") + 1;
                    body.MoveAndSlide();
                }
                GD.Print($"OPENNV_OWNED_CONTACT_ESCAPE direction={direction} from={before} to={body.GlobalPosition} distance={before.DistanceTo(body.GlobalPosition):R} steps={JsonSerializer.Serialize(blocks)}");
                maximumEscape = Math.Max(maximumEscape, before.DistanceTo(body.GlobalPosition));
            }
            if (arguments.Length >= 15)
            {
                var target = new Vector3(float.Parse(arguments[12], CultureInfo.InvariantCulture),
                    float.Parse(arguments[13], CultureInfo.InvariantCulture), float.Parse(arguments[14], CultureInfo.InvariantCulture));
                var direction = target - before; direction.Y = 0;
                if (direction.LengthSquared() > 0)
                {
                    var configuration = RuntimeConfiguration.Load();
                    Engine.PhysicsTicksPerSecond = configuration.Simulation.PhysicsTicksPerSecond;
                    var delta = 1f / configuration.Simulation.PhysicsTicksPerSecond;
                    direction = direction.Normalized(); body.GlobalPosition = before; body.Velocity = Vector3.Zero;
                    var airborneFrames = 0; var landings = 0; var unstepped = 0; var collided = 0;
                    var unsupportedGroundedFrames = 0; bool? previousRoot = null;
                    var recoveryOnlyFrames = 0;
                    var snapChecks = 0; var snapMatches = 0; var snapMismatches = 0; var snapUnknown = 0; var maximumSnapDifference = 0f;
                    var maximumClearDifference = 0f; var maximumCollisionDifference = 0f;
                    var airborneClearFrames = 0; var maximumAirborneClearDifference = 0f;
                    (int Frame, Vector3 Contact)? predictedContact = null;
                    (int Frame, Vector3 Position)? predictedDeparture = null;
                    (int Frame, Vector3 Position)? predictedChainedContact = null;
                    var departureAttempted = false;
                    var supportObserved = false; var predictionMade = false;
                    using var motion = new PhysicsTestMotionParameters3D { Margin = body.SafeMargin, MaxCollisions = 4 };
                    using var sweep = new PhysicsTestMotionResult3D();
                    using var recovery = new PhysicsTestMotionParameters3D { Margin = body.SafeMargin, MaxCollisions = 6, RecoveryAsCollision = true };
                    using var recoveryResult = new PhysicsTestMotionResult3D();
                    for (var frame = 1; frame <= 120; frame++)
                    {
                        await owner.ToSignal(owner.GetTree(), SceneTree.SignalName.PhysicsFrame);
                        var floorBefore = body.IsOnFloor();
                        var velocity = direction * 3.6f;
                        velocity.Y = floorBefore ? MathF.Min(body.Velocity.Y, 0) :
                            NativeCharacterGravity.AirVelocity(body.Velocity.Y, configuration.Simulation.GravityMetersPerSecondSquared, delta);
                        var beforeMotion = body.GlobalPosition;
                        motion.From = body.GlobalTransform; motion.Motion = velocity * delta;
                        var hasCollision = PhysicsServer3D.BodyTestMotion(body.GetRid(), motion, sweep);
                        recovery.From = motion.From; recovery.Motion = motion.Motion;
                        var hasRecoveryCollision = PhysicsServer3D.BodyTestMotion(body.GetRid(), recovery, recoveryResult);
                        if (hasRecoveryCollision && !hasCollision) recoveryOnlyFrames++;
                        var sweepTravel = hasCollision ? sweep.GetTravel() : motion.Motion;
                        body.Velocity = velocity;
                        var stepped = NativeCharacterStep.TryStep(body, direction * 3.6f * delta, configuration.Player.StepHeightMeters, out _);
                        if (stepped)
                        {
                            body.Velocity = Vector3.Down * .01f; body.MoveAndSlide();
                            body.Velocity = new(velocity.X, 0, velocity.Z);
                        }
                        else body.MoveAndSlide();
                        if (!stepped)
                        {
                            unstepped++; if (hasCollision) collided++;
                            var difference = (body.GlobalPosition - beforeMotion).DistanceTo(sweepTravel);
                            if (hasCollision) maximumCollisionDifference = Math.Max(maximumCollisionDifference, difference);
                            else maximumClearDifference = Math.Max(maximumClearDifference, difference);
                            if (!floorBefore && !body.IsOnFloor() && !hasCollision)
                            {
                                airborneClearFrames++;
                                maximumAirborneClearDifference = Math.Max(maximumAirborneClearDifference, difference);
                            }
                            if (floorBefore && !hasCollision && velocity.Y <= 0 && !body.FloorConstantSpeed)
                            {
                                snapChecks++;
                                var snapOrigin = beforeMotion + sweepTravel;
                                var snap = NativeCharacterFloorSnap.Query(body, snapOrigin);
                                if (snap.OnFloor is null) snapUnknown++;
                                else if (snap.OnFloor == body.IsOnFloor()) snapMatches++; else snapMismatches++;
                                if (snap.OnFloor == true)
                                    maximumSnapDifference = Math.Max(maximumSnapDifference, snap.Position.DistanceTo(body.GlobalPosition));
                            }
                        }
                        if (!body.IsOnFloor()) airborneFrames++;
                        if (!floorBefore && body.IsOnFloor()) landings++;
                        var rootAfter = NativeCharacterStep.TrySupport(body, body.GlobalPosition, configuration.Player.StepHeightMeters, out _);
                        if (body.IsOnFloor() && !rootAfter) unsupportedGroundedFrames++;
                        if (!departureAttempted && body.IsOnFloor())
                        {
                            departureAttempted = true;
                            try
                            {
                                using var candidate = new NativeFallCandidateSearch(body, direction * 3.6f,
                                    configuration.Simulation.GravityMetersPerSecondSquared, delta, 120, 120,
                                    configuration.Player.StepHeightMeters, _ => true);
                                NativeFallCandidate? predictedCandidate = null;
                                for (var wait = 0; wait < 120 && predictedCandidate is null; wait++)
                                    if (!candidate.Advance(out predictedCandidate))
                                        await owner.ToSignal(owner.GetTree(), SceneTree.SignalName.PhysicsFrame);
                                if (predictedCandidate is null) throw new InvalidOperationException("Scheduled fall candidate did not complete.");
                                var predicted = predictedCandidate.Departure;
                                predictedDeparture = (frame + predicted.Frames, predicted.Position);
                                GD.Print($"OPENNV_OWNED_CONTACT_DEPARTURE_SEED frame={frame} predictedFrame={predictedDeparture.Value.Frame} " +
                                    $"position={predicted.Position} gameplay=unverified");
                                var predictedLanding = predictedCandidate.Contact;
                                predictedChainedContact = (predictedDeparture.Value.Frame + predictedLanding.Frames, predictedLanding.Position);
                                GD.Print($"OPENNV_OWNED_CONTACT_CHAINED_SEED predictedFrame={predictedChainedContact.Value.Frame} " +
                                    $"walkable={predictedLanding.WalkableContact} rootSupported={predictedLanding.RootSupported} gameplay=unverified");
                            }
                            catch (Exception error) when (error is InvalidOperationException or NotSupportedException)
                            {
                                GD.Print($"OPENNV_OWNED_CONTACT_DEPARTURE_SEED frame={frame} refused={error.Message} gameplay=unverified");
                            }
                        }
                        if (predictedDeparture is { } departurePrediction && departurePrediction.Frame == frame)
                            GD.Print($"OPENNV_OWNED_CONTACT_DEPARTURE_OBSERVED frame={frame} onFloor={body.IsOnFloor()} " +
                                $"difference={body.GlobalPosition.DistanceTo(departurePrediction.Position):R} gameplay=unverified");
                        if (predictedChainedContact is { } chainedPrediction && chainedPrediction.Frame == frame)
                            GD.Print($"OPENNV_OWNED_CONTACT_CHAINED_OBSERVED frame={frame} onFloor={body.IsOnFloor()} " +
                                $"difference={body.GlobalPosition.DistanceTo(chainedPrediction.Position):R} gameplay=unverified");
                        if (previousRoot != rootAfter)
                        {
                            GD.Print($"OPENNV_OWNED_CONTACT_ROOT_TRANSITION frame={frame} rootSupported={rootAfter} " +
                                $"onFloor={body.IsOnFloor()} position={body.GlobalPosition} gameplay=unverified");
                            previousRoot = rootAfter;
                        }
                        if (hasCollision || !floorBefore && body.IsOnFloor())
                        {
                            var normal = hasCollision && sweep.GetCollisionCount() > 0 ? sweep.GetCollisionNormal(0) : Vector3.Zero;
                            GD.Print($"OPENNV_OWNED_CONTACT_FALL_CONTACT frame={frame} floorBefore={floorBefore} floorAfter={body.IsOnFloor()} " +
                                $"sweepCollision={hasCollision} stepped={stepped} normal={normal} sweepTravel={sweepTravel} " +
                                $"actualTravel={body.GlobalPosition - beforeMotion} delta={(body.GlobalPosition - beforeMotion).DistanceTo(sweepTravel):R}");
                        }
                        if (body.Velocity.Y < 0 && body.GetSlideCollisionCount() > 0)
                        {
                            var actualVerticalSpeed = Math.Min(0, (body.GlobalPosition.Y - beforeMotion.Y) / delta);
                            if (actualVerticalSpeed > body.Velocity.Y + .01f)
                                body.Velocity = new(body.Velocity.X, actualVerticalSpeed, body.Velocity.Z);
                        }
                        if (predictedContact is { } prediction && prediction.Frame == frame)
                            GD.Print($"OPENNV_OWNED_CONTACT_FALL_PREDICTION observedFrame={frame} " +
                                $"predictedContact={prediction.Contact} actualPosition={body.GlobalPosition} " +
                                $"difference={body.GlobalPosition.DistanceTo(prediction.Contact):R} " +
                                $"actualFloor={body.IsOnFloor()} prediction=first-sweep-contact gameplay=unverified");
                        if (floorBefore) supportObserved = true;
                        if (!predictionMade && supportObserved && floorBefore && !body.IsOnFloor())
                        {
                            predictionMade = true;
                            var hadRootSupport = NativeCharacterStep.TrySupport(body, beforeMotion, configuration.Player.StepHeightMeters, out _);
                            var hasRootSupport = NativeCharacterStep.TrySupport(body, body.GlobalPosition, configuration.Player.StepHeightMeters, out _);
                            GD.Print($"OPENNV_OWNED_CONTACT_FALL_LAUNCH frame={frame} before={beforeMotion} after={body.GlobalPosition} " +
                                $"beforeRoot={hadRootSupport} afterRoot={hasRootSupport} sweepCollision={hasCollision} " +
                                $"stepped={stepped} velocity={body.Velocity} gameplay=unverified");
                            var initialPose = body.GlobalTransform;
                            var registrations = NativeCapsuleNavigation.RegisteredSearches;
                            NativeAirContact? forecast = null;
                            using var query = NativeCapsuleNavigation.AirborneContact(body, body.GlobalPosition,
                                direction * 3.6f, body.Velocity.Y, configuration.Simulation.GravityMetersPerSecondSquared,
                                delta, 120, configuration.Player.StepHeightMeters, _ => true).GetEnumerator();
                            for (var wait = 0; wait < 120 && forecast is null; wait++)
                            {
                                if (!NativeCapsuleNavigation.AdvanceAirborne(query, out forecast))
                                    await owner.ToSignal(owner.GetTree(), SceneTree.SignalName.PhysicsFrame);
                            }
                            if (forecast is null) throw new InvalidOperationException("Scheduled airborne prediction did not complete.");
                            var refusedMissing = false;
                            try
                            {
                                foreach (var ignored in NativeCapsuleNavigation.AirborneContact(body, body.GlobalPosition,
                                    direction * 3.6f, body.Velocity.Y, configuration.Simulation.GravityMetersPerSecondSquared,
                                    delta, 120, configuration.Player.StepHeightMeters, _ => false)) { }
                            }
                            catch (InvalidOperationException) { refusedMissing = true; }
                            if (!refusedMissing || body.GlobalTransform != initialPose || NativeCapsuleNavigation.RegisteredSearches != registrations)
                                throw new InvalidOperationException("Airborne contact mutated its body, retained its query or admitted missing residency.");
                            if (forecast is { } found)
                            {
                                predictedContact = (frame + found.Frames, found.Position);
                                GD.Print($"OPENNV_OWNED_CONTACT_FALL_SEED frame={frame} firstContactFrame={predictedContact.Value.Frame} " +
                                    $"position={body.GlobalPosition} contact={found.Position} walkable={found.WalkableContact} " +
                                    $"rootSupported={found.RootSupported} prediction=first-sweep-contact gameplay=unverified");
                            }
                            else GD.Print($"OPENNV_OWNED_CONTACT_FALL_SEED frame={frame} result=no-contact-within-bound gameplay=unverified");
                        }
                        if (frame % 20 == 0)
                            GD.Print($"OPENNV_OWNED_CONTACT_DIRECT frame={frame} position={body.GlobalPosition} " +
                                $"onFloor={body.IsOnFloor()} targetDistance={body.GlobalPosition.DistanceTo(target):R} " +
                                $"gravity=configured terrainCells={terrains.Count} otherCellModels=absent gameplay=unverified");
                    }
                    GD.Print($"OPENNV_OWNED_CONTACT_FALL_MODEL airborneFrames={airborneFrames} landings={landings} " +
                        $"unsteppedFrames={unstepped} sweptCollisions={collided} maxClearDifference={maximumClearDifference:R} " +
                        $"maxCollisionDifference={maximumCollisionDifference:R} airborneClearFrames={airborneClearFrames} " +
                        $"maxAirborneClearDifference={maximumAirborneClearDifference:R} unsupportedGroundedFrames={unsupportedGroundedFrames} " +
                        $"recoveryOnlyFrames={recoveryOnlyFrames} " +
                        $"snapChecks={snapChecks} snapMatches={snapMatches} snapMismatches={snapMismatches} snapUnknown={snapUnknown} maxSnapDifference={maximumSnapDifference:R} " +
                        "controller=fixture sourceRegion=absent parity=unverified");
                }
            }
            if (maximumEscape < 1) throw new InvalidDataException("The owned contact fixture has no one-metre escape through ordinary body motion.");
            GD.Print("OPENNV_OWNED_CONTACT_PASS ordinaryGameplay=separate");
        }
        finally { foreach (var node in nearby) node.QueueFree(); foreach (var terrain in terrains) terrain.QueueFree(); body.QueueFree(); instance.QueueFree(); prototype.Scene.Root.Free(); }
    }

}
