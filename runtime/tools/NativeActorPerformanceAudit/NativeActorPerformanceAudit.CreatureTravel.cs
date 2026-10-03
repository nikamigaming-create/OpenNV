using System.Security.Cryptography;
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
    private async Task CreatureTravel(string game, string mod, string root, string actorIdentity, string questId,
        short stage, short expected, string[] dependencies, bool arrivalOnly = false)
    {
        var fixture = new Node3D(); AddChild(fixture);
        FalloutReferenceWorld? world = null;
        try
        {
            var installation = new FalloutModStackSelection([new(mod, root, dependencies)]).Resolve(game);
            RuntimeLiveContentSource.Configure(game, RuntimeLiveContentSource.FalloutNewVegasGame,
                installation.ContentRoots.Skip(1).ToArray(), installation.ActivePlugins, installation.Settings);
            var content = RuntimeLiveContentSource.Current!;
            using var records = FalloutPluginStack.Load(content.PluginSources);
            var parts = actorIdentity.Split(':');
            var caller = new FalloutFormKey(parts[0], Convert.ToUInt32(parts[1], 16));
            var quest = FalloutDialogueTopic.Find(records, "QUST", questId).FormKey;
            var quests = new FalloutQuestState(records); quests.EnterStage(quest, stage);
            var globals = FalloutGlobalState.Read(records);
            var clock = new FalloutGameTime(globals, FalloutGameTimeBindings.Read(records), FalloutCalendar.Read(Path.Combine(game, "FalloutNV.exe")));
            world = new(records);
            var cell = FalloutCellSceneReader.Read(records, world.Get(caller).Cell); world.LoadCell(cell);
            var enableOwner = world.Get(caller); var desiredEnable = true;
            var parents = new HashSet<FalloutFormKey>();
            while (enableOwner.EnableParent is { } parent)
            {
                if (!parents.Add(enableOwner.Reference)) throw new InvalidDataException("Fixture enable-parent cycle.");
                desiredEnable ^= parent.Opposite; enableOwner = world.Get(parent.Reference);
            }
            // This explicit stage fixture activates the source parent chain;
            // ordinary campaign enabling is exercised through quest results.
            enableOwner.Enabled = desiredEnable; world.InitializeActorTemplates(caller, 1, globals);
            var placed = cell.References.Single(value => value.FormKey == caller);
            var units = RuntimeConfiguration.Load().World.GameUnitsToMeters;
            var navigation = CellNavigationGraph.LoadOwned(records, cell.Cell.FormKey);
            Vector3 Source(Vector3 point) => new Vector3(point.X, -point.Z, point.Y) / units;
            Vector3 Native(Vector3 point) => new Vector3(point.X, point.Z, -point.Y) * units;
            Transform3D Placement(FalloutPlacedReference value) => new(GamebryoCoordinate.ConvertReferenceEuler(
                new(value.RotationRadians[0], value.RotationRadians[1], value.RotationRadians[2]), value.Scale),
                GamebryoCoordinate.ConvertVector(new(value.Position[0], value.Position[1], value.Position[2])) * units);
            var origin = Native(navigation.FindNearestPoint(Source(Placement(placed).Origin)));
            // An isolated native floor tests the real source procedure, model,
            // NAVM and KF. It does not replace the ordinary cell's collision.
            var floor = new StaticBody3D { Position = origin - Vector3.Up * .05f };
            floor.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new(100, .1f, 100) } }); fixture.AddChild(floor);
            var context = new NativeActorCombatContext(() => null, () => throw new InvalidOperationException("Fixture queried player vitals."),
                (_, _) => throw new InvalidOperationException("Fixture attacked the player."),
                (from, to) => navigation.FindPath(Source(from), Source(to)).Select(Native).ToArray(),
                _ => true, () => 1, globals, .4f, 9.81f, PlayerCell: () => cell.Cell.FormKey);
            var effects = new List<FalloutReferenceScriptEffect>();
            var groups = new List<(FalloutFormKey Target, string Group)>();
            var presentation = new RuntimeNativeReferencePresentation(world, cell.References, reference =>
            {
                var baseObject = cell.BaseObjects[reference.Base];
                if (baseObject.ModelPath is not { } path || !content.TryRead(path, null, out var bytes, out _))
                    throw new FileNotFoundException("Source animation target model is absent.");
                var scene = RuntimeNativeNifMeshBuilder.Build(FalloutNifFile.Read(bytes), units, contentSource: content);
                scene.Root.Transform = Placement(reference); fixture.AddChild(scene.Root);
                foreach (var controller in scene.Root.FindChildren("*", "", true, false).OfType<RuntimeNifControllerPlayer>()) controller.SetProcess(false);
                return scene.Root;
            });
            fixture.AddChild(presentation);
            var scripts = new FalloutReferenceScripts(records, world, quests, new((_, _) => false, effect =>
            {
                if (effect.Kind != FalloutReferenceEffectKind.SetStage || effect.Source != caller || effect.Target != quest)
                    throw new NotSupportedException("The isolated creature fixture reached another result owner.");
                effects.Add(effect); quests.EnterStage(quest, effect.Stage);
            }, Globals: globals, PlayGroup: (target, group, initialization) =>
            {
                presentation.PlayGroup(target, group, initialization); groups.Add((target, group));
            }));
            RuntimeNativeCreature CreateActor()
            {
                var state = world.Get(caller);
                var actor = RuntimeNativeCreature.Create(records, content, placed, state, units);
                actor.Transform = new(Placement(placed).Basis, origin + Vector3.Up * .05f);
                fixture.AddChild(actor); actor.SetProcess(false); actor.SetPhysicsProcess(false);
                actor.Combat = RuntimeNativeActorCombat.Attach(actor, actor.Skeleton, actor.Appearance.SkeletonPath,
                    world, state, records, content, 2, 3, context);
                actor.Combat.SetPhysicsProcess(false);
                actor.ExecutePackageEvent = scripts.ExecutePackageEvent;
                actor.ConfigureAi(records, quests, world, clock, globals);
                return actor;
            }
            var actor = CreateActor();
            async Task Frame()
            {
                await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                actor._PhysicsProcess(1d / 60); actor._Process(1d / 60);
                if (world.PackageEvents.HasPending(caller))
                {
                    var batch = world.PackageEvents.SnapshotPending(caller);
                    _ = scripts.DispatchFrame(caller, batch.Events, 0);
                    world.PackageEvents.Consume(batch);
                }
                foreach (var node in presentation.Nodes.Values)
                    foreach (var controller in node.FindChildren("*", "", true, false).OfType<RuntimeNifControllerPlayer>()) controller._Process(1d / 60);
                var ai = JsonSerializer.SerializeToElement(actor.AiState);
                if (actor.Error is not null || ai.GetProperty("error").ValueKind != JsonValueKind.Null)
                    throw new InvalidDataException("Owned creature Travel diverged: " + JsonSerializer.Serialize(actor.Observation));
            }
            string? retainedFailure = null;
            try
            {
                for (var frame = 0; frame < 1800 && world.Get(caller).PackageIdle is null; frame++) await Frame();
            }
            catch (InvalidDataException) when (arrivalOnly && world.Get(caller).PackageMotion?.Travel?.Complete == true &&
                world.Get(caller).ScriptError is not null)
            { retainedFailure = world.Get(caller).ScriptError; }
            var arrived = world.Get(caller).PackageMotion ?? throw new InvalidDataException("Creature produced no package motion.");
            var package = records.GetEffective(arrived.Package); var sourceHash = SHA256.HashData(package.ReadData());
            if (arrivalOnly)
            {
                var ai = JsonSerializer.SerializeToElement(actor.AiState);
                var endpoint = ai.GetProperty("travel").GetProperty("endpoint").EnumerateArray().Select(value => value.GetSingle()).ToArray();
                var radius = Math.Max(FalloutTravelPackage.Read(package).Radius * units, actor.SafeMargin * 8);
                if (arrived.Travel?.Complete != true || !actor.IsOnFloor() || actor.GlobalPosition.DistanceTo(new(endpoint[0], endpoint[1], endpoint[2])) > radius)
                    throw new InvalidDataException("Owned creature did not reach its bounded native Travel endpoint.");
                var revision = ai.GetProperty("packageEvents").GetProperty("Revision").GetInt64();
                for (var frame = 0; frame < 12; frame++) { actor._PhysicsProcess(1d / 60); actor._Process(1d / 60); }
                if (JsonSerializer.SerializeToElement(actor.AiState).GetProperty("packageEvents").GetProperty("Revision").GetInt64() != revision ||
                    world.Get(caller).ScriptError != retainedFailure)
                    throw new InvalidDataException("Failed source end result replayed its consumed prefix.");
                var snapshots = JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(world.Capture()))!;
                using var cold = new FalloutReferenceWorld(records); cold.Restore(snapshots);
                if (cold.Get(caller).PackageMotion?.Travel?.Complete != true || cold.Get(caller).ScriptError != retainedFailure ||
                    !sourceHash.SequenceEqual(SHA256.HashData(package.ReadData())))
                    throw new InvalidDataException("Cold Travel lost arrival or cleared its reached source failure.");
                GD.Print($"OPENNV_NATIVE_CREATURE_TRAVEL_ARRIVAL_PASS actor={caller} package={package.FormKey} " +
                    "sourceNavm=true sourceKf=true nativeCapsule=true endpointBounded=true supportedFloor=true arrivalRetained=true " +
                    "endResultFailureRetained=true prefixNotReplayed=true cold=true sourceReadonly=true " +
                    "endAnimationAndCampaign=unverified fixture=isolated-floor-and-stage recording=false");
                return;
            }
            if (arrived.Travel?.Complete != true || world.Get(caller).PackageIdle?.Kind != "POEA" || !actor.IsOnFloor() || groups.Count != 1)
                throw new InvalidDataException("Owned Travel did not reach its source end result and idle.");
            for (var frame = 0; frame < 30; frame++) await Frame();
            var idlePhase = world.Get(caller).PackageIdle!;
            var savedMotion = JsonSerializer.Deserialize<FalloutActorPackageMotion>(JsonSerializer.Serialize(world.Get(caller).PackageMotion))!;
            var savedIdle = JsonSerializer.Deserialize<FalloutPackageEventIdle>(JsonSerializer.Serialize(idlePhase))!;
            actor.Free();
            world.Get(caller).PackageMotion = savedMotion; world.Get(caller).PackageIdle = savedIdle;
            actor = CreateActor();
            if (world.Get(caller).PackageIdle!.Clock.SourceSeconds != idlePhase.Clock.SourceSeconds)
                throw new InvalidDataException("Creature reassembly reset its retained event idle.");
            for (var frame = 0; frame < 1800 && quests.Stage(quest) != expected; frame++) await Frame();
            if (quests.Stage(quest) != expected || effects.Count != 1 || groups.Count != 1 ||
                !sourceHash.SequenceEqual(SHA256.HashData(package.ReadData())))
                throw new InvalidDataException("Owned creature continuation lost its source result or replayed the consumed end event.");
            GD.Print($"OPENNV_NATIVE_CREATURE_TRAVEL_PASS actor={caller} package={package.FormKey} " +
                "sourceNavm=true sourceKf=true nativeCapsule=true arrival=true resultPlayGroup=true eventIdle=true " +
                "retainedIdleReassembly=true endEventOnce=true changeResult=true sourceReadonly=true " +
                "fixture=isolated-floor-and-stage stageProgramAndCampaignCold=unverified parity=unverified recording=false");
        }
        finally { fixture.Free(); world?.Dispose(); RuntimeLiveContentSource.Clear(); }
    }
}
