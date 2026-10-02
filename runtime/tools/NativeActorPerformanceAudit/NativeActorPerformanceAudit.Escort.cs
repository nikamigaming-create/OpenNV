using System.Text.Json;
using Godot;
using OpenNV.Runtime;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;

public partial class NativeActorPerformanceAudit
{
    private async Task EscortPackage(string baseRoot, string mod, string root, string actorId, string questId,
        short stage, string[] dependencies)
    {
        var fixture = new Node3D(); AddChild(fixture);
        RuntimeNativeNpc? actor = null;
        try
        {
            var installation = new FalloutModStackSelection([new(mod, root, dependencies)]).Resolve(baseRoot);
            RuntimeLiveContentSource.Configure(baseRoot, RuntimeLiveContentSource.FalloutNewVegasGame,
                installation.ContentRoots.Skip(1).ToArray(), installation.ActivePlugins, installation.Settings);
            var content = RuntimeLiveContentSource.Current!;
            using var records = FalloutPluginStack.Load(content.PluginSources);
            using var world = new FalloutReferenceWorld(records);
            var caller = FalloutDialogueTopic.Find(records, "ACHR", actorId).FormKey;
            var quest = FalloutDialogueTopic.Find(records, "QUST", questId).FormKey;
            var quests = new FalloutQuestState(records); quests.EnterStage(quest, stage);
            var globals = FalloutGlobalState.Read(records);
            var cell = FalloutCellSceneReader.Read(records, world.Get(caller).Cell); world.LoadCell(cell);
            // The stage fixture explicitly enables its source actor; the
            // ordinary source quest program is exercised separately in play.
            world.Get(caller).Enabled = true;
            var placed = cell.References.Single(value => value.FormKey == caller);
            var units = RuntimeConfiguration.Load().World.GameUnitsToMeters;
            var templates = world.InitializeActorTemplates(caller, 1, globals);
            var navigation = CellNavigationGraph.LoadOwned(records, cell.Cell.FormKey);
            Vector3 Source(Vector3 value) => new Vector3(value.X, -value.Z, value.Y) / units;
            Vector3 World(Vector3 value) => new Vector3(value.X, value.Z, -value.Y) * units;
            Transform3D Placement(FalloutPlacedReference reference) => new(GamebryoCoordinate.ConvertReferenceEuler(
                new(reference.RotationRadians[0], reference.RotationRadians[1], reference.RotationRadians[2]), reference.Scale),
                GamebryoCoordinate.ConvertVector(new(reference.Position[0], reference.Position[1], reference.Position[2])) * units);
            var origin = World(navigation.FindNearestPoint(Source(Placement(placed).Origin)));
            // Deliberately isolated native-collision fixture. The selected
            // owned NAVM/KF and package remain the route/clock inputs; these
            // diagnostic bodies do not establish the live cell's collision.
            var floor = new StaticBody3D { Position = origin - Vector3.Up * .05f };
            floor.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new(100, .1f, 100) } });
            fixture.AddChild(floor);
            var player = new RuntimeNativePlayer(); fixture.AddChild(player);
            player.Configure(RuntimeConfiguration.Load(), new(Basis.Identity, origin + Vector3.Right));
            player.SetProcess(false); player.SetPhysicsProcess(false); player.SetProcessUnhandledInput(false);
            var context = new NativeActorCombatContext(() => player, () => new(1, 200, 200, 70, 70, 0, 100),
                (_, _) => throw new InvalidOperationException("Escort fixture attacked the player."),
                (from, to) => navigation.FindPath(Source(from), Source(to)).Select(World).ToArray(),
                _ => true, () => 1, globals, .4f, 9.81f, PlayerCell: () => cell.Cell.FormKey);

            RuntimeNativeNpc CreateActor()
            {
                var created = RuntimeNativeNpc.Create(records, content, placed, units, (_, _, _, _) => new StandardMaterial3D(),
                    world.EquippedArmor(caller, 1, globals), templates);
                created.Transform = new(Placement(placed).Basis, origin + Vector3.Up * .05f);
                fixture.AddChild(created); created.SetProcess(false); created.SetPhysicsProcess(false);
                created.ConfigureAi(records, quests, cell, Placement, world: world);
                created.Combat = RuntimeNativeActorCombat.Attach(created, created.Skeleton, created.Appearance.SkeletonPath,
                    world, world.Get(caller), records, content, 2, 3, context);
                created.Combat.SetPhysicsProcess(false);
                if (created.AiError is not null) throw new InvalidDataException(created.AiError);
                return created;
            }
            actor = CreateActor();
            try { world.Get(caller).Capture(); throw new InvalidDataException("Uninitialized escort snapshot was saveable."); }
            catch (NotSupportedException) { }
            var package = records.GetEffective(actor.CurrentPackage ?? throw new InvalidDataException("No selected escort package."));
            var definition = FalloutEscortPackage.Read(package);
            var destination = World(navigation.FindNearestPoint(Source(Placement(cell.References.Single(value => value.FormKey == definition.Destination)).Origin)));
            if (Math.Abs(destination.Y - origin.Y) > .05f)
                throw new NotSupportedException("This isolated native floor fixture requires a level source corridor.");
            var direction = (destination - origin).Normalized();
            var side = direction.Cross(Vector3.Up).Normalized();
            var distance = definition.Distance * units;
            player.GlobalPosition = origin + side * Math.Min(1, distance * .5f);

            async Task Frame()
            {
                await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                actor!._PhysicsProcess(1d / 60); actor._Process(1d / 60);
                if (actor.AiError is not null || actor.AnimationError is not null)
                    throw new InvalidDataException(actor.AiError ?? actor.AnimationError);
            }
            for (var frame = 0; frame < 600 && actor.GlobalPosition.DistanceTo(origin) < .35f; frame++) await Frame();
            if (actor.GlobalPosition.DistanceTo(origin) < .35f || world.Get(caller).PackageMotion?.Escort?.TargetAcquired != true)
                throw new InvalidDataException("Owned escort did not acquire its target and advance native motion: " +
                    JsonSerializer.Serialize(actor.AiState) + " " + JsonSerializer.Serialize(actor.Combat!.Observation));
            player.GlobalPosition = actor.GlobalPosition - direction * distance * 2.2f;
            await Frame();
            var waitingPosition = actor.GlobalPosition;
            for (var frame = 0; frame < 45; frame++) await Frame();
            RequireWait(actor, waitingPosition);

            var saved = JsonSerializer.Deserialize<FalloutActorPackageMotion>(JsonSerializer.Serialize(world.Get(caller).PackageMotion))!;
            saved.Validate();
            actor.Free(); actor = null;
            world.Get(caller).PackageMotion = saved;
            actor = CreateActor();
            for (var frame = 0; frame < 12; frame++) await Frame();
            RequireWait(actor, waitingPosition);
            var restoredEvents = JsonSerializer.SerializeToElement(actor.AiState).GetProperty("packageEvents");
            if (restoredEvents.GetProperty("Revision").GetInt64() != 0)
                throw new InvalidDataException("Cold escort replayed its consumed package-start event.");

            var wall = new StaticBody3D
            {
                Position = actor.GlobalPosition + direction + Vector3.Up * 2.5f,
                Basis = Basis.LookingAt(direction, Vector3.Up)
            };
            wall.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new(100, 5, .15f) } });
            fixture.AddChild(wall);
            var beforeWall = actor.GlobalPosition;
            player.GlobalPosition = destination + side * Math.Min(1, distance * .5f);
            for (var frame = 0; frame < 90; frame++) await Frame();
            if ((actor.GlobalPosition - beforeWall).Dot(direction) > .9f || world.Get(caller).PackageMotion?.Escort?.Complete != false)
                throw new InvalidDataException("Native escort crossed its blocking wall or invented arrival.");
            wall.Free();
            for (var frame = 0; frame < 1800 && world.Get(caller).PackageMotion?.Escort?.Complete != true; frame++) await Frame();
            if (world.Get(caller).PackageMotion?.Escort?.Complete != true || !actor.IsOnFloor())
                throw new InvalidDataException("Owned escort did not resume through native collision to its supported destination: " +
                    JsonSerializer.Serialize(actor.AiState) + " " + JsonSerializer.Serialize(actor.Combat!.Observation));
            var events = JsonSerializer.SerializeToElement(actor.AiState).GetProperty("packageEvents");
            var revision = events.GetProperty("Revision").GetInt64();
            if (!events.GetProperty("Done").GetBoolean()) throw new InvalidDataException("Escort arrival did not publish package completion.");
            for (var frame = 0; frame < 12; frame++) await Frame();
            if (JsonSerializer.SerializeToElement(actor.AiState).GetProperty("packageEvents").GetProperty("Revision").GetInt64() != revision)
                throw new InvalidDataException("Escort completion replayed on later frames.");
            GD.Print($"OPENNV_NATIVE_ESCORT_PASS actor={caller} package={package.FormKey} distance={definition.Distance} " +
                "ownedNavm=true ownedKf=true nativeCapsule=true wait=true coldWait=true targetAhead=true blockedWall=true " +
                "arrival=true eventOnce=true recording=false fixture=synthetic-floor-and-target-observations campaign=unverified parity=unverified");
        }
        finally { fixture.Free(); RuntimeLiveContentSource.Clear(); }
    }

    private static void RequireWait(RuntimeNativeNpc actor, Vector3 before)
    {
        var escort = JsonSerializer.SerializeToElement(actor.AiState).GetProperty("escort");
        if (actor.GlobalPosition.DistanceTo(before) > .03f || escort.GetProperty("status").GetString() != "WaitForTarget")
            throw new InvalidDataException("Escort did not preserve its stationary wait for a trailing target: " + escort);
    }
}
