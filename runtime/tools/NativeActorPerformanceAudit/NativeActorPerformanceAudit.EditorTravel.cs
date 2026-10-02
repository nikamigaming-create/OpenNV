using System.Text.Json;
using Godot;
using OpenNV.Runtime;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.Presentation.Rendering;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;

public partial class NativeActorPerformanceAudit
{
    private async Task EditorTravel(string baseRoot, string mod, string root, string actorId, string questId,
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
            var quests = new FalloutQuestState(records); quests.EnterStage(FalloutDialogueTopic.Find(records, "QUST", questId).FormKey, stage);
            var globals = FalloutGlobalState.Read(records);
            var cell = FalloutCellSceneReader.Read(records, world.Get(caller).Cell); world.LoadCell(cell);
            world.SetEnabled(caller, true); world.AdvanceEnableChanges(0, new(1, 1), _ => false);
            var placed = cell.References.Single(value => value.FormKey == caller);
            var configuration = RuntimeConfiguration.Load(); var units = configuration.World.GameUnitsToMeters;
            var templates = world.InitializeActorTemplates(caller, 1, globals);
            var navigation = CellNavigationGraph.LoadOwned(records, cell.Cell.FormKey);
            Vector3 Source(Vector3 value) => new Vector3(value.X, -value.Z, value.Y) / units;
            Vector3 World(Vector3 value) => new Vector3(value.X, value.Z, -value.Y) * units;
            Transform3D Placement(FalloutPlacedReference reference) => new(GamebryoCoordinate.ConvertReferenceEuler(
                new(reference.RotationRadians[0], reference.RotationRadians[1], reference.RotationRadians[2]), reference.Scale),
                GamebryoCoordinate.ConvertVector(new(reference.Position[0], reference.Position[1], reference.Position[2])) * units);
            var origin = World(navigation.FindNearestPoint(Source(Placement(placed).Origin)));
            var start = new[] { Vector3.Right, Vector3.Left, Vector3.Back, Vector3.Forward }
                .Select(direction => World(navigation.FindNearestPoint(Source(origin + direction * 3))))
                .First(point => point.DistanceTo(origin) > 1.5f && Math.Abs(point.Y - origin.Y) < .1f);
            // Isolated movement fixture: retain source editor placement while
            // setting a distinct current pose in authoritative diagnostic state.
            // These test floor/wall bodies do not establish live-cell collision.
            var moved = Source(start);
            world.SetPlacement(caller, new(cell.Cell.FormKey, [moved.X, moved.Y, moved.Z], placed.RotationRadians.ToArray()));
            var floor = new StaticBody3D { Position = origin - Vector3.Up * .05f };
            floor.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new(100, .1f, 100) } }); fixture.AddChild(floor);
            var directionToGoal = (origin - start).Normalized();
            var wall = new StaticBody3D
            {
                Position = start + directionToGoal * .8f + Vector3.Up * 2.5f,
                Basis = Basis.LookingAt(directionToGoal, Vector3.Up)
            };
            wall.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new(100, 5, .15f) } }); fixture.AddChild(wall);
            var context = new NativeActorCombatContext(() => null, () => new(1, 200, 200, 70, 70, 0, 100),
                (_, _) => throw new InvalidOperationException("Editor travel fixture attacked the player."),
                (from, to) => navigation.FindPath(Source(from), Source(to)).Select(World).ToArray(),
                _ => true, () => 1, globals, .4f, 9.81f);
            RuntimeNativeNpc Create()
            {
                var created = RuntimeNativeNpc.Create(records, content, placed, units,
                    (appearance, part, nif, geometry) => NativeNpcMaterial.Resolve(appearance, part, nif, geometry, records, new Color(.4f, .4f, .4f), content),
                    world.EquippedArmor(caller, 1, globals), templates);
                created.Transform = new(Placement(placed).Basis, start + Vector3.Up * .05f);
                fixture.AddChild(created); created.SetProcess(false); created.SetPhysicsProcess(false);
                created.ConfigureAi(records, quests, cell, Placement, world: world);
                created.Combat = RuntimeNativeActorCombat.Attach(created, created.Skeleton, created.Appearance.SkeletonPath,
                    world, world.Get(caller), records, content, 2, 3, context);
                created.Combat.SetPhysicsProcess(false);
                if (created.AiError is not null) throw new InvalidDataException(created.AiError);
                return created;
            }
            actor = Create();
            var package = records.GetEffective(actor.CurrentPackage ?? throw new InvalidDataException("No editor travel selected."));
            _ = FalloutEditorTravelPackage.Read(package);
            try { world.Get(caller).Capture(); throw new InvalidDataException("Uninitialized editor travel was saveable."); }
            catch (NotSupportedException) { }
            async Task Frame()
            {
                await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                actor!._PhysicsProcess(1d / 60); actor._Process(1d / 60);
                if (actor.AiError is not null || actor.AnimationError is not null) throw new InvalidDataException(actor.AiError ?? actor.AnimationError);
            }
            for (var frame = 0; frame < 90; frame++) await Frame();
            if ((actor.GlobalPosition - start).Dot(directionToGoal) > .75f || world.Get(caller).PackageMotion?.EditorTravel?.Complete != false)
                throw new InvalidDataException("Editor travel crossed its wall or invented arrival.");
            var saved = JsonSerializer.Deserialize<FalloutActorPackageMotion>(JsonSerializer.Serialize(world.Get(caller).PackageMotion))!;
            saved.Validate(); actor.Free(); actor = null; world.Get(caller).PackageMotion = saved;
            actor = Create();
            for (var frame = 0; frame < 12; frame++) await Frame();
            if (JsonSerializer.SerializeToElement(actor.AiState).GetProperty("packageEvents").GetProperty("Revision").GetInt64() != 0)
                throw new InvalidDataException("Cold editor travel replayed package start.");
            wall.Free();
            for (var frame = 0; frame < 900 && world.Get(caller).PackageMotion?.EditorTravel?.Complete != true; frame++) await Frame();
            if (world.Get(caller).PackageMotion?.EditorTravel?.Complete != true || !actor.IsOnFloor() || actor.GlobalPosition.DistanceTo(origin) > .4f)
                throw new InvalidDataException("Editor travel failed supported native arrival: " + JsonSerializer.Serialize(actor.AiState) +
                    " " + JsonSerializer.Serialize(actor.Combat!.Observation));
            var events = JsonSerializer.SerializeToElement(actor.AiState).GetProperty("packageEvents");
            var revision = events.GetProperty("Revision").GetInt64();
            for (var frame = 0; frame < 12; frame++) await Frame();
            if (!events.GetProperty("Done").GetBoolean() ||
                JsonSerializer.SerializeToElement(actor.AiState).GetProperty("packageEvents").GetProperty("Revision").GetInt64() != revision)
                throw new InvalidDataException("Editor travel lost or repeated arrival publication.");
            GD.Print($"OPENNV_NATIVE_EDITOR_TRAVEL_PASS actor={caller} package={package.FormKey} ownedNavm=true ownedKf=true sourceMaterials=true " +
                "nativeCapsule=true blockedWall=true coldBlocked=true arrival=true eventOnce=true recording=false fixture=synthetic-floor-wall-current-pose campaign=unverified parity=unverified");
        }
        finally { fixture.Free(); RuntimeLiveContentSource.Clear(); }
    }
}
