using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using OpenNV.Runtime;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.World.Cells;

public partial class NativeReferenceTargetAudit : Node3D
{
    public override async void _Ready()
    {
        try
        {
            var arguments = OS.GetCmdlineUserArgs();
            if (arguments.Length == 0) await Synthetic();
            else if (arguments is ["--owned-door-geometry", var doorGame, var doorMod, var doorRoot, var doorIdentity,
                var doorX, var doorY, var doorZ, .. var doorDependencies])
                await Owned(doorGame, doorMod, doorRoot, doorIdentity, new(float.Parse(doorX, CultureInfo.InvariantCulture),
                    float.Parse(doorY, CultureInfo.InvariantCulture), float.Parse(doorZ, CultureInfo.InvariantCulture)), doorDependencies, true);
            else if (arguments is ["--owned-reference-geometry", var game, var mod, var root, var identity,
                var x, var y, var z, .. var dependencies])
                await Owned(game, mod, root, identity, new(float.Parse(x, CultureInfo.InvariantCulture),
                    float.Parse(y, CultureInfo.InvariantCulture), float.Parse(z, CultureInfo.InvariantCulture)), dependencies);
            else throw new ArgumentException("Expected --owned-reference-geometry game mod root plugin:reference fromX fromY fromZ [dependencies...].");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }

    private RuntimeNativePlayer Player(Node3D fixture, Vector3 from, FalloutCameraProjection projection)
    {
        var player = new RuntimeNativePlayer(); fixture.AddChild(player);
        player.Configure(RuntimeConfiguration.Load(), new(Basis.Identity, from), projection);
        player.SetProcess(false); player.SetPhysicsProcess(false);
        return player;
    }

    private async Task Sync()
    {
        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    }

    private NativeReferenceGeometryObservation Observe(Node3D target, RuntimeNativePlayer player, bool doorApproach = false)
    {
        var configuration = RuntimeConfiguration.Load();
        var original = player.GlobalTransform; var placed = target.GlobalTransform;
        NativeReferenceGeometryObservation result;
        if (doorApproach)
        {
            var surface = NativeReferenceGeometryTarget.ObserveSurface(target, player, player.Camera.GlobalPosition,
                player.CombatCollisionRids, collider => target.IsAncestorOf(collider), _ => true);
            result = new(target.GlobalPosition, surface.Aim, surface.Bounds, surface.Collider, surface.Shape, 0, -1);
        }
        else result = NativeReferenceGeometryTarget.Observe(target, player, player.Camera.GlobalPosition,
            player.CombatCollisionRids, collider => target.IsAncestorOf(collider), _ => true,
            configuration.Player.ActivationDistanceMeters + configuration.Player.CapsuleHeightMeters + configuration.Player.StepHeightMeters);
        Require(player.GlobalTransform == original && target.GlobalTransform == placed,
            "Geometry/floor queries moved the actual capsule or reference placement.");
        return result;
    }

    private async Task Approach(Node3D target, RuntimeNativePlayer player, NativeReferenceGeometryObservation geometry, bool doorApproach = false)
    {
        var configuration = RuntimeConfiguration.Load();
        var original = player.GlobalTransform;
        var path = NativeCapsuleNavigation.Find(player, player.GlobalPosition, geometry.Target,
            configuration.Player.StepHeightMeters, configuration.Player.CapsuleRadiusMeters, _ => true, targetRadius: 1.25f);
        Require(player.GlobalTransform == original && path.Count > 0, "Geometry target route changed its query body or had no supported path.");
        foreach (var waypoint in path)
            for (var frame = 0; frame < 240; frame++)
            {
                var offset = waypoint - player.GlobalPosition; offset.Y = 0;
                if (offset.Length() <= .08f) break;
                if (frame == 239) throw new InvalidDataException("Actual controller could not execute the geometry approach.");
                await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                var delta = (float)GetPhysicsProcessDeltaTime();
                var velocity = offset.Normalized() * Math.Min(configuration.Player.MoveSpeedMetersPerSecond, offset.Length() / delta);
                velocity.Y = player.IsOnFloor() ? Math.Min(player.Velocity.Y, 0) :
                    player.Velocity.Y - configuration.Simulation.GravityMetersPerSecondSquared * delta;
                player.Velocity = velocity;
                if (NativeCharacterStep.TryStep(player, new Vector3(velocity.X, 0, velocity.Z) * delta,
                    configuration.Player.StepHeightMeters)) player.Velocity = Vector3.Down * .01f;
                player.MoveAndSlide();
            }
        player.Velocity = Vector3.Zero;
        geometry = Observe(target, player, doorApproach);
        player.Camera.LookAt(geometry.Aim, Vector3.Up);
        Require(player.Camera.GlobalPosition.DistanceTo(geometry.Aim) <= configuration.Player.ActivationDistanceMeters &&
            player.AimedObject() is { } contact && target.IsAncestorOf(contact),
            "Actual ordinary player ray did not hit the selected native surface within its activation range.");
        Require(NativeCharacterStep.TrySupport(player, player.GlobalPosition, configuration.Player.StepHeightMeters, out _),
            "Observed approach has no actual native root floor support.");
    }

    private async Task Synthetic()
    {
        var fixture = new Node3D(); AddChild(fixture);
        try
        {
            var floor = Box(fixture, new(4, -.5f, -2), new(20, 1, 20));
            var target = new Node3D { Name = "AuthoredReferencePivot" }; fixture.AddChild(target);
            var geometry = new Node3D { Position = new(4.2f, 0, -2) }; target.AddChild(geometry);
            var mesh = new MeshInstance3D { Position = Vector3.Up * .8f, Mesh = new BoxMesh { Size = new(1, .8f, 1) } };
            geometry.AddChild(mesh);
            var collider = Box(geometry, mesh.Position, new(1, .8f, 1));
            var player = Player(fixture, new(4.2f, .05f, 4), new(70, 1));
            await Sync();
            var result = Observe(target, player);
            Require(result.Target.IsEqualApprox(new(4.2f, 0, -2)) && result.AimCollider == collider.GetInstanceId() &&
                result.FloorCollider == floor.GetInstanceId(), "Authored mesh offset was replaced by the REFR origin or an invented floor.");
            player.Camera.LookAt(result.Aim, Vector3.Up);
            Require(player.AimedObject() is not { } distant || !target.IsAncestorOf(distant),
                "A distant geometry observation granted an out-of-range ordinary activation ray.");
            await Approach(target, player, result);
            result = Observe(target, player);
            player.Camera.LookAt(result.Aim, Vector3.Up);
            var blocker = Box(fixture, player.Camera.GlobalPosition.Lerp(result.Aim, .5f), Vector3.One * .35f);
            await Sync();
            _ = Observe(target, player);
            Require(player.AimedObject() == blocker, "An occluded geometry observation bypassed the real activation obstruction.");
            blocker.Free(); await Sync();
            geometry.Position += Vector3.Right;
            await Sync();
            var moved = Observe(target, player);
            Require(MathF.Abs(moved.Target.X - result.Target.X - 1) < .001f && target.Position == Vector3.Zero,
                "Source child motion reused stale mesh bounds or rewrote its reference pivot.");
            var door = new Node3D { Position = new(1, 0, -2) }; fixture.AddChild(door);
            door.AddChild(new MeshInstance3D { Position = Vector3.Up * .8f, Mesh = new BoxMesh { Size = new(1, 3, 1) } });
            var doorCollider = Box(door, Vector3.Up * .8f, new(1, 3, 1));
            await Sync();
            var doorGoal = Observe(door, player, true);
            Require(doorGoal.Target == door.GlobalPosition && doorGoal.AimCollider == doorCollider.GetInstanceId() &&
                doorGoal.Bounds.Position.Y < 0 && doorGoal.FloorCollider == 0,
                "Door surface replaced its authored approach with its embedded model's floor projection.");
            await Approach(door, player, doorGoal, true);
            var embedded = new Node3D { Position = new(-2, 0, -4) }; fixture.AddChild(embedded);
            embedded.AddChild(new MeshInstance3D { Position = Vector3.Up * .8f, Mesh = new BoxMesh { Size = new(1, 3, 1) } });
            _ = Box(embedded, Vector3.Up * .8f, new(1, 3, 1));
            await Sync();
            var embeddedGoal = Observe(embedded, player);
            Require(embeddedGoal.Bounds.Position.Y < 0 && embeddedGoal.Target.IsEqualApprox(embedded.GlobalPosition) &&
                embeddedGoal.FloorCollider == floor.GetInstanceId(),
                "An embedded object skipped the real support above its mesh bottom or supplied a pivot as floor.");
            floor.Free(); await Sync();
            var refused = false;
            try { _ = Observe(target, player); }
            catch (NotSupportedException error) when (error.Message.Contains("native floor", StringComparison.Ordinal)) { refused = true; }
            Require(refused, "Missing native floor was replaced by an authored pivot or synthetic support.");
            refused = false;
            try
            {
                _ = NativeCapsuleNavigation.Find(player, player.GlobalPosition, doorGoal.Target,
                    RuntimeConfiguration.Load().Player.StepHeightMeters, RuntimeConfiguration.Load().Player.CapsuleRadiusMeters,
                    _ => true, targetRadius: 1.25f);
            }
            catch (InvalidOperationException) { refused = true; }
            Require(refused, "Authored door approach bypassed the independent native floor owner.");
            GD.Print($"OPENNV_NATIVE_REFERENCE_GEOMETRY_PASS runtimeMvid={typeof(RuntimeConfiguration).Assembly.ManifestModule.ModuleVersionId} " +
                "offsetMesh=true actualFloor=true actualCapsuleApproach=true ordinaryRayAndRange=true occlusionRefused=true childMotion=true " +
                "queryNoMutation=true missingFloorRefused=true embeddedObjectFloor=true embeddedDoorApproach=true doorFloorStillRequired=true fixture=synthetic campaign=false parity=false recording=false");
        }
        finally { fixture.Free(); }
    }

    private async Task Owned(string game, string mod, string root, string identity, Vector3 from, string[] dependencies, bool doorGeometry = false)
    {
        if (!from.IsFinite()) throw new InvalidDataException("Owned geometry query requires a finite observed start pose.");
        var setup = new FalloutModStackSelection([new(mod, root, dependencies)]).Resolve(game);
        RuntimeLiveContentSource.Configure(game, RuntimeLiveContentSource.FalloutNewVegasGame,
            setup.ContentRoots.Skip(1).ToArray(), setup.ActivePlugins, setup.Settings);
        var prototypes = new Dictionary<string, RuntimeNativeNifPrototype>(StringComparer.OrdinalIgnoreCase);
        var resources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var fixture = new Node3D(); AddChild(fixture);
        try
        {
            var content = RuntimeLiveContentSource.Current!;
            using var records = FalloutPluginStack.Load(content.PluginSources);
            using var world = new FalloutReferenceWorld(records);
            var parts = identity.Split(':');
            if (parts.Length != 2) throw new ArgumentException("Owned geometry needs an exact plugin:hex source reference.");
            var key = new FalloutFormKey(parts[0], Convert.ToUInt32(parts[1], 16));
            var source = records.GetEffective(key);
            if (source.Signature != "REFR" || source.IsDeleted) throw new InvalidDataException("Owned geometry requires a winning existing REFR.");
            var cell = FalloutCellSceneReader.Read(records, FalloutCellSceneReader.ParentCell(source)!.Value); world.LoadCell(cell);
            var selected = cell.References.Single(reference => reference.FormKey == key);
            if (records.GetEffective(selected.Base).Signature is "NPC_" or "CREA" || !world.IsEnabled(key))
                throw new InvalidDataException("Owned geometry query needs a source-enabled non-actor model.");
            var hashes = new Dictionary<FalloutFormKey, string>();
            Node3D Instance(FalloutPlacedReference reference)
            {
                var model = cell.BaseObjects[reference.Base].ModelPath ?? throw new InvalidDataException("Selected source model is absent.");
                if (!prototypes.TryGetValue(model, out var prototype))
                {
                    if (!content.TryRead(model, null, out var bytes, out _)) throw new FileNotFoundException(model);
                    resources.Add(model, Convert.ToHexString(SHA256.HashData(bytes)));
                    prototypes.Add(model, prototype = new(bytes, RuntimeConfiguration.Load().World.GameUnitsToMeters));
                }
                foreach (var form in new[] { reference.FormKey, reference.Base })
                    hashes.TryAdd(form, Convert.ToHexString(SHA256.HashData(records.GetEffective(form).ReadData())));
                var transform = new Transform3D(GamebryoCoordinate.ConvertReferenceEuler(
                    new(reference.RotationRadians[0], reference.RotationRadians[1], reference.RotationRadians[2]), reference.Scale),
                    GamebryoCoordinate.ConvertVector(new(reference.Position[0], reference.Position[1], reference.Position[2])) *
                        RuntimeConfiguration.Load().World.GameUnitsToMeters);
                var node = prototype.InstantiatePlaced(transform); fixture.AddChild(node); return node;
            }
            // Source declarations with no model add no geometry, as in ordinary
            // assembly. A declared model that cannot load remains a failure.
            var architecture = cell.References.Where(reference => reference.FormKey != key &&
                records.GetEffective(reference.Base).Signature is not ("NPC_" or "CREA") &&
                world.IsEnabled(reference.FormKey)).ToArray();
            var noModelDeclarations = architecture.Count(reference => cell.BaseObjects[reference.Base].ModelPath is null);
            foreach (var reference in architecture.Where(reference => cell.BaseObjects[reference.Base].ModelPath is not null))
                _ = Instance(reference);
            var target = Instance(selected); var player = Player(fixture, from, FalloutCameraProjection.Read(FalloutInstallationSettings.Read(content)));
            await Sync();
            var isDoor = records.GetEffective(selected.Base).Signature == "DOOR";
            RuntimeNativeDoorMotion? doorMotion = null;
            RuntimeNifControllerPlayer? doorController = null;
            if (doorGeometry)
            {
                Require(isDoor, "Owned door geometry requires an actual source DOOR.");
                var controllers = target.FindChildren("*", "", true, false).OfType<RuntimeNifControllerPlayer>().ToArray();
                doorController = controllers.Single(controller => controller.HasSequence("Open") && controller.HasSequence("Close"));
                doorMotion = new(world.Get(key), controllers, () => { }); target.AddChild(doorMotion);
                doorMotion.SetProcess(false); await Sync();
            }
            var doorApproach = isDoor && !doorGeometry;
            var geometry = Observe(target, player, doorApproach); var rootTransform = target.GlobalTransform;
            if (doorGeometry) DoorObservation("closed-before-approach", key, target, player, geometry, doorMotion!, doorController!);
            var beforeModels = target.FindChildren("*", nameof(MeshInstance3D), true, false).OfType<MeshInstance3D>()
                .Select(mesh => mesh.GlobalTransform).ToArray();
            await Approach(target, player, geometry, doorApproach);
            Require(target.GlobalTransform == rootTransform && beforeModels.SequenceEqual(target.FindChildren("*", nameof(MeshInstance3D), true, false)
                .OfType<MeshInstance3D>().Select(mesh => mesh.GlobalTransform)), "Geometry approach changed the original source placement or mesh transforms.");
            if (doorGeometry) await ExerciseDoorGeometry(key, target, player, doorMotion!, doorController!, beforeModels);
            Require(hashes.All(pair => pair.Value == Convert.ToHexString(SHA256.HashData(records.GetEffective(pair.Key).ReadData()))) &&
                resources.All(pair => content.TryRead(pair.Key, null, out var bytes, out _) && pair.Value == Convert.ToHexString(SHA256.HashData(bytes))),
                "Owned geometry audit changed a winning record or resource input.");
            GD.Print("OPENNV_OWNED_REFERENCE_GEOMETRY_PASS " + JsonSerializer.Serialize(new
            {
                reference = key.ToString(),
                runtimeMvid = typeof(RuntimeConfiguration).Assembly.ManifestModule.ModuleVersionId,
                referenceOrigin = Point(rootTransform.Origin),
                target = Point(geometry.Target),
                aim = Point(geometry.Aim),
                sourceGeometryOffsetMeters = rootTransform.Origin.DistanceTo(geometry.Bounds.GetCenter()),
                geometry.AimShape,
                geometry.FloorShape,
                authoredDoorApproach = doorApproach,
                architectureModels = prototypes.Count,
                noModelDeclarations,
                realNativeFloor = true,
                actualCapsuleApproach = true,
                ordinaryRayAndRange = true,
                sourceInputsAndPlacementUnchanged = true,
                initialSourceEnableState = true,
                campaign = false,
                pixels = false,
                recording = false
            }));
        }
        finally
        {
            fixture.Free();
            foreach (var prototype in prototypes.Values) prototype.Scene.Root.Free();
            RuntimeLiveContentSource.Clear();
        }
    }

    private static StaticBody3D Box(Node3D parent, Vector3 point, Vector3 size)
    {
        var result = new StaticBody3D { Position = point, CollisionLayer = 1 };
        result.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } }); parent.AddChild(result); return result;
    }
    private static float[] Point(Vector3 point) => [point.X, point.Y, point.Z];
    private static void Require(bool condition, string error) { if (!condition) throw new InvalidDataException(error); }
}
