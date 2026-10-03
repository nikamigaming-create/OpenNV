using System.Security.Cryptography;
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
    private async Task DialoguePackage(string baseRoot, string mod, string root, string actorId, string questId,
        short stage, string[] dependencies)
    {
        var fixture = new Node3D(); AddChild(fixture);
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
            var clock = new FalloutGameTime(globals, FalloutGameTimeBindings.Read(records), FalloutCalendar.Read(Path.Combine(baseRoot, "FalloutNV.exe")));
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
            var goal = new[] { Vector3.Right, Vector3.Left, Vector3.Back, Vector3.Forward }
                .Select(direction => World(navigation.FindNearestPoint(Source(origin + direction * 4))))
                .First(point => point.DistanceTo(origin) > 3 && Math.Abs(point.Y - origin.Y) < .1f);
            // Isolated collision fixture, using the owned actor, NAVM, KF,
            // materials and audio. This floor/wall is not campaign evidence.
            var floor = new StaticBody3D { Position = origin - Vector3.Up * .05f };
            floor.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new(100, .1f, 100) } }); fixture.AddChild(floor);
            var directionToGoal = (goal - origin).Normalized();
            var wall = new StaticBody3D
            {
                Position = origin + directionToGoal * .8f + Vector3.Up * 2.5f,
                Basis = Basis.LookingAt(directionToGoal, Vector3.Up)
            };
            wall.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new(100, 5, .15f) } }); fixture.AddChild(wall);
            var player = new RuntimeNativePlayer(); fixture.AddChild(player);
            player.Configure(configuration, new(Basis.Identity, goal)); player.SetPhysicsProcess(false); player.SetProcess(false);
            var context = new NativeActorCombatContext(() => player, () => new(1, 200, 200, 70, 70, 0, 100),
                (_, _) => throw new InvalidDataException("Dialogue package fixture attacked its listener."),
                (from, to) => navigation.FindPath(Source(from), Source(to)).Select(World).ToArray(),
                _ => true, () => 1, globals, .4f, 9.81f, PlayerCell: () => cell.Cell.FormKey);
            var actor = RuntimeNativeNpc.Create(records, content, placed, units,
                (appearance, part, nif, geometry) => NativeNpcMaterial.Resolve(appearance, part, nif, geometry, records, new Color(.4f, .4f, .4f), content),
                world.EquippedArmor(caller, 1, globals), templates);
            actor.Transform = new(Placement(placed).Basis, origin + Vector3.Up * .05f);
            fixture.AddChild(actor); actor.SetProcess(false); actor.SetPhysicsProcess(false);
            actor.ConfigureAi(records, quests, cell, Placement, clock: clock, globals: globals, world: world);
            actor.Combat = RuntimeNativeActorCombat.Attach(actor, actor.Skeleton, actor.Appearance.SkeletonPath,
                world, world.Get(caller), records, content, 2, 3, context);
            actor.Combat.SetPhysicsProcess(false);
            var package = records.GetEffective(actor.CurrentPackage ?? throw new InvalidDataException(actor.AiError ?? "No dialogue package selected."));
            var source = FalloutDialoguePackage.Read(package);
            var sourceHash = SHA256.HashData(package.ReadData());
            if (source.Type != 1 || source.Target != records.RuntimeFormKey(0x14) || FalloutScriptPackage.Read(package).LocationType is not null)
                throw new InvalidDataException("Fixture needs an owned SayTo package without a wait location.");
            var speech = new RuntimeNativeSpeech();
            speech.Configure(records, configuration.ActorCompiler.FaceGenAnimation.Lip, key => quests.Stage(key),
                condition => quests.Evaluate(condition), templates: reference => world.Get(reference).Templates,
                quests: quests, playerFemale: () => false, references: world);
            speech.PrepareSubtitle = _ => { };
            speech.ExecuteResults = (_, _, _) => throw new NotSupportedException("This isolated audio fixture does not execute campaign results.");
            speech.SayToCompleted += (_, _) => throw new InvalidDataException("Package speech invented a script SayToDone event.");
            fixture.AddChild(speech);
            var requests = 0; var completions = 0; Action? retired = null;
            bool Done() => JsonSerializer.SerializeToElement(actor.AiState).GetProperty("packageEvents").GetProperty("Done").GetBoolean();
            actor.BeginPackageDialogue = (request, completed) =>
            {
                if (request != source || !actor.IsOnFloor() || actor.GlobalPosition.DistanceTo(goal) > source.ActivationDistance * units || Done())
                    throw new InvalidDataException("Dialogue began before supported target-range arrival.");
                requests++; retired = completed;
                speech.StartPackageSpeech(caller, request.Target, request.Topic!.Value, () => { completed(); ++completions; });
            };
            async Task Frame()
            {
                await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                actor._PhysicsProcess(1d / 60); actor._Process(1d / 60);
                if (actor.AiError is not null || actor.AnimationError is not null || speech.Error is not null)
                    throw new InvalidDataException(actor.AiError ?? actor.AnimationError ?? speech.Error);
            }
            for (var frame = 0; frame < 90; frame++) await Frame();
            if (requests != 0 || Done() || (actor.GlobalPosition - origin).Dot(directionToGoal) > .75f)
                throw new InvalidDataException("Dialogue crossed its wall or invented a location/range completion.");
            try { world.Get(caller).Capture(); throw new InvalidDataException("Pending dialogue continuation was saveable."); }
            catch (NotSupportedException) { }
            wall.Free();
            for (var frame = 0; frame < 900 && requests == 0; frame++) await Frame();
            var voice = JsonSerializer.SerializeToElement(speech.State);
            if (requests != 1 || completions != 0 || Done() || !speech.IsTalking(caller) ||
                voice.GetProperty("audioSha256").ValueKind != JsonValueKind.String || voice.GetProperty("lipSha256").ValueKind != JsonValueKind.String)
                throw new InvalidDataException("Package did not retain its real owned voice/lip continuation.");
            var deadline = Time.GetTicksMsec() + 20000;
            while (speech.Active && speech.Error is null && Time.GetTicksMsec() < deadline) await Frame();
            if (speech.Active || speech.Error is not null || requests != 1 || completions != 1 || !Done())
                throw new InvalidDataException(speech.Error ?? "Package did not complete once after actual audio finished.");
            var revision = JsonSerializer.SerializeToElement(actor.AiState).GetProperty("packageEvents").GetProperty("Revision").GetInt64();
            retired!();
            if (JsonSerializer.SerializeToElement(actor.AiState).GetProperty("packageEvents").GetProperty("Revision").GetInt64() != revision)
                throw new InvalidDataException("Completed package callback replayed its source end.");
            _ = world.Get(caller).Capture();
            // Source quest advancement replaces the dialogue procedure.
            quests.EnterStage(quest, 80); actor.EvaluatePackages(false);
            if (actor.CurrentPackage == package.FormKey || actor.AiError is not null) throw new InvalidDataException("Package replacement fixture failed source selection.");
            var pendingRevision = JsonSerializer.SerializeToElement(actor.AiState).GetProperty("packageEvents").GetProperty("Revision").GetInt64();
            retired();
            if (JsonSerializer.SerializeToElement(actor.AiState).GetProperty("packageEvents").GetProperty("Revision").GetInt64() != pendingRevision)
                throw new InvalidDataException("Old audio completed a pending source replacement.");
            actor._Process(0);
            var replacedRevision = JsonSerializer.SerializeToElement(actor.AiState).GetProperty("packageEvents").GetProperty("Revision").GetInt64();
            retired();
            if (JsonSerializer.SerializeToElement(actor.AiState).GetProperty("packageEvents").GetProperty("Revision").GetInt64() != replacedRevision)
                throw new InvalidDataException("Old audio completed a replacement package.");
            if (!sourceHash.AsSpan().SequenceEqual(SHA256.HashData(package.ReadData())))
                throw new InvalidDataException("Dialogue audit modified its owned package.");
            GD.Print($"OPENNV_NATIVE_DIALOGUE_PACKAGE_PASS actor={caller} package={package.FormKey} optionalLocation=true ownedNavm=true ownedKf=true " +
                "nativeCapsule=true blockedWall=true targetRange=true ownedAudio=true ownedLip=true completionAfterAudio=true eventOnce=true replacementGuard=true " +
                "pendingSaveRefused=true scriptEvent=false recording=false fixture=synthetic-floor-wall-player campaign=unverified parity=unverified");
        }
        finally { fixture.Free(); RuntimeLiveContentSource.Clear(); }
    }
}
