using System.Reflection;
using Godot;
using OpenNV.Runtime;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;

public partial class NativeActorPerformanceAudit
{
    private void OwnedCreaturePendingSandbox(string game, string actorText)
    {
        RuntimeLiveContentSource.Configure(game, RuntimeLiveContentSource.FalloutNewVegasGame);
        using var content = RuntimeLiveContentSource.Current!;
        using var records = FalloutPluginStack.Load(content.PluginSources);
        using var world = new FalloutReferenceWorld(records);
        var parts = actorText.Split(':');
        if (parts.Length != 2) throw new InvalidDataException("Expected actor plugin and object ID.");
        var key = new FalloutFormKey(parts[0], Convert.ToUInt32(parts[1], 16));
        var cell = FalloutCellSceneReader.Read(records, world.Get(key).Cell);
        world.LoadCell(cell);
        var placed = cell.References.Single(value => value.FormKey == key);
        var globals = FalloutGlobalState.Read(records);
        var quests = new FalloutQuestState(records);
        var clock = new FalloutGameTime(globals, FalloutGameTimeBindings.Read(records), FalloutCalendar.Read(Path.Combine(game, "FalloutNV.exe")));
        var units = RuntimeConfiguration.Load().World.GameUnitsToMeters;
        world.InitializeActorTemplates(key, 1, globals);
        var state = world.Get(key);
        // The observed mantis is quest-enabled. Model that already-applied
        // precondition in this isolated component fixture, including XESP.
        var enableOwner = state;
        var opposite = false;
        while (enableOwner.EnableParent is { } enableParent)
        {
            opposite ^= enableParent.Opposite;
            enableOwner = world.Get(enableParent.Reference);
        }
        enableOwner.Enabled = !opposite;
        var busy = true;
        var context = new NativeActorCombatContext(() => null, () => throw new InvalidOperationException("Fixture has no player vitals."),
            (_, _) => throw new InvalidOperationException("Fixture cannot attack."), (_, to) => [to], _ => true, () => 1,
            globals, .4f, 9.81f, DialogueBusy: _ => busy);
        var parent = new Node3D { Transform = new(new Basis(Vector3.Up, .37f), new(3, 1, -2)) };
        AddChild(parent);
        RuntimeNativeCreature? actor = null;
        RuntimeNativeCreature? restored = null;
        try
        {
            actor = RuntimeNativeCreature.Create(records, content, placed, state, units);
            actor.Transform = new(GamebryoCoordinate.ConvertReferenceEuler(new(placed.RotationRadians[0], placed.RotationRadians[1], placed.RotationRadians[2]), placed.Scale),
                GamebryoCoordinate.ConvertVector(new(placed.Position[0], placed.Position[1], placed.Position[2])) * units);
            actor.Combat = RuntimeNativeActorCombat.Attach(actor, actor.Skeleton, actor.Appearance.SkeletonPath, world, state, records, content, 2, 3, context);
            actor.ConfigureAi(records, quests, world, clock, globals);
            parent.AddChild(actor);
            actor.SetProcess(false); actor.SetPhysicsProcess(false); actor.Combat.SetPhysicsProcess(false);
            actor._PhysicsProcess(0);
            var expected = new FalloutFormKey("FalloutNV.esm", 0x0410af);
            var events = (FalloutPackageEvents)(typeof(RuntimeNativeCreature).GetField("_packageEvents", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(actor)
                ?? throw new InvalidDataException("Creature lifecycle is absent."));
            if (events.Active?.Form != expected || events.Revision != 1 || events.LastEvent != "POBA")
                throw new InvalidDataException($"Creature did not begin the expected Sandbox once: enabled={world.IsEnabled(key)} ready={actor.Combat.PackageMovementReady} combat={System.Text.Json.JsonSerializer.Serialize(actor.Combat.Observation)} ai={System.Text.Json.JsonSerializer.Serialize(actor.AiState)}");
            var snapshot = state.Capture();
            var pending = snapshot.PendingSandboxStart ?? throw new InvalidDataException("Creature pending Sandbox was not captured.");
            var pose = actor.Transform;
            actor.Free(); actor = null;
            using var coldWorld = new FalloutReferenceWorld(records);
            coldWorld.Restore([snapshot]);
            coldWorld.LoadCell(cell);
            var cold = coldWorld.Get(key);
            var coldEnableOwner = cold;
            var coldOpposite = false;
            while (coldEnableOwner.EnableParent is { } coldParent)
            {
                coldOpposite ^= coldParent.Opposite;
                coldEnableOwner = coldWorld.Get(coldParent.Reference);
            }
            coldEnableOwner.Enabled = !coldOpposite;
            var originalClock = cold.Animation.Capture();
            var interrupted = RuntimeNativeCreature.Create(records, content, placed, cold, units);
            try
            {
                interrupted.Combat = RuntimeNativeActorCombat.Attach(interrupted, interrupted.Skeleton, interrupted.Appearance.SkeletonPath, coldWorld, cold, records, content, 2, 3, context);
                interrupted.ConfigureAi(records, quests, coldWorld, clock, globals);
                var originalReceipt = cold.PendingSandboxStart;
                var advancedClock = originalClock! with { ElapsedSeconds = originalClock.ElapsedSeconds + .25 };
                cold.Animation.Restore(advancedClock);
                parent.AddChild(interrupted);
                interrupted.SetProcess(false); interrupted.SetPhysicsProcess(false); interrupted.Combat.SetPhysicsProcess(false);
                var rejected = false;
                try { interrupted.RequireSourcePublication(); }
                catch (InvalidDataException) { rejected = true; }
                if (!rejected || !ReferenceEquals(cold.PendingSandboxStart, originalReceipt) || cold.Animation.Capture() != advancedClock)
                    throw new InvalidDataException("Interrupted creature publication changed the newer clock or receipt.");
                cold.Animation.Restore(originalClock);
            }
            finally { if (GodotObject.IsInstanceValid(interrupted)) interrupted.Free(); }
            restored = RuntimeNativeCreature.Create(records, content, placed, cold, units);
            restored.Combat = RuntimeNativeActorCombat.Attach(restored, restored.Skeleton, restored.Appearance.SkeletonPath, coldWorld, cold, records, content, 2, 3, context);
            restored.ConfigureAi(records, quests, coldWorld, clock, globals);
            if (cold.Animation.Capture() != originalClock || cold.PendingSandboxStart is null)
                throw new InvalidDataException("Detached creature replaced its retained clock or receipt.");
            parent.AddChild(restored);
            restored.SetProcess(false); restored.SetPhysicsProcess(false); restored.Combat.SetPhysicsProcess(false);
            var publicationReceipt = cold.PendingSandboxStart;
            RuntimeCoordinator.RequireNativeActorPublication(restored);
            RuntimeCoordinator.RequireNativeActorPublication(parent);
            var restoredEvents = (FalloutPackageEvents)(typeof(RuntimeNativeCreature).GetField("_packageEvents", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(restored)!);
            if (!restored.Transform.IsEqualApprox(pose) || restoredEvents.Revision != pending.EventRevision ||
                restoredEvents.LastEvent != "POBA" || cold.SoundRandom.State != pending.RandomState ||
                !ReferenceEquals(cold.PendingSandboxStart, publicationReceipt) ||
                cold.Capture().PendingSandboxStart?.SelectionAttempts != pending.SelectionAttempts)
                throw new InvalidDataException("Cold creature lost its pose, lifecycle, random state or selection count.");
            restored._PhysicsProcess(0);
            if (!restored.Combat.PackageMovementReady || restoredEvents.Revision != pending.EventRevision || cold.Capture().PendingSandboxStart is null)
                throw new InvalidDataException("Busy cold creature replayed its begin event or lost the pending owner.");
            busy = false;
            restored._PhysicsProcess(0);
            if (cold.PendingSandboxStart is not null || cold.PackageMotion is not { Sandbox: not null } motion ||
                motion.Package != expected || restoredEvents.Revision != pending.EventRevision)
                throw new InvalidDataException("Cold creature did not publish its first Sandbox activity: " + System.Text.Json.JsonSerializer.Serialize(restored.AiState));
            _ = cold.Capture();
            GD.Print("OPENNV_OWNED_CREATURE_PENDING_SANDBOX_PASS retainedClock=true transformedParent=true coldPose=true beginOnce=true selectionCount=true interruptedPublication=true sharedPublicationIdempotent=true firstActivity=true gameplay=unverified retail=unverified");
            restored.Free(); restored = null;
        }
        finally
        {
            if (GodotObject.IsInstanceValid(restored)) restored!.Free();
            if (GodotObject.IsInstanceValid(actor)) actor!.Free();
            if (GodotObject.IsInstanceValid(parent)) parent.Free();
        }
    }

    private void OwnedCurrentSandboxStart(string game, string actorText)
    {
        RuntimeLiveContentSource.Configure(game, RuntimeLiveContentSource.FalloutNewVegasGame);
        using var content = RuntimeLiveContentSource.Current!;
        using var records = FalloutPluginStack.Load(content.PluginSources);
        using var world = new FalloutReferenceWorld(records);
        var parts = actorText.Split(':');
        if (parts.Length != 2) throw new InvalidDataException("Expected actor plugin and object ID.");
        var key = new FalloutFormKey(parts[0], Convert.ToUInt32(parts[1], 16));
        var cell = FalloutCellSceneReader.Read(records, world.Get(key).Cell);
        world.LoadCell(cell);
        var placed = cell.References.Single(value => value.FormKey == key);
        var globals = FalloutGlobalState.Read(records);
        var quests = new FalloutQuestState(records);
        var clock = new FalloutGameTime(globals, FalloutGameTimeBindings.Read(records), FalloutCalendar.Read(Path.Combine(game, "FalloutNV.exe")));
        var units = RuntimeConfiguration.Load().World.GameUnitsToMeters;
        var templates = world.InitializeActorTemplates(key, 1, globals);
        var location = world.Placement(key);
        Transform3D Placement(FalloutPlacedReference reference)
        {
            var pose = world.Placement(reference.FormKey);
            return new(GamebryoCoordinate.ConvertReferenceEuler(new(pose.RotationRadians[0], pose.RotationRadians[1], pose.RotationRadians[2]), reference.Scale),
                GamebryoCoordinate.ConvertVector(new(pose.Position[0], pose.Position[1], pose.Position[2])) * units);
        }
        var context = new NativeActorCombatContext(() => null, () => throw new InvalidOperationException("Fixture has no player vitals."),
            (_, _) => throw new InvalidOperationException("Fixture cannot attack."), (_, to) => [to], _ => true, () => 1,
            globals, .4f, 9.81f, DialogueBusy: _ => true);
        var actor = RuntimeNativeNpc.Create(records, content, placed, units, (_, _, _, _) => new StandardMaterial3D(),
            world.EquippedArmor(key, 1, globals), templates);
        var parent = new Node3D { Transform = new(new Basis(Vector3.Up, .37f), new(3, 1, -2)) };
        AddChild(parent);
        try
        {
            actor.Transform = Placement(placed);
            actor.Combat = RuntimeNativeActorCombat.Attach(actor, actor.Skeleton, actor.Appearance.SkeletonPath,
                world, world.Get(key), records, content, 2, 3, context);
            actor.ConfigureAi(records, quests, cell, Placement, () => world.ActorFactions(key), clock, globals, world);
            var expected = new FalloutFormKey("FalloutNV.esm", 0x0410af);
            if (actor.AiError is not null || actor.CurrentPackage != expected || actor.IsInsideTree() || world.ActorPackageStartLocation(key) is not null)
                throw new InvalidDataException("Detached actor did not retain the expected unstarted source package: " + actor.AiError);
            var state = world.Get(key);
            try { _ = state.Capture(); throw new InvalidDataException("Detached start incorrectly admitted capture."); }
            catch (NotSupportedException error) when (error.Message.Contains("awaiting live native start pose", StringComparison.Ordinal)) { }
            parent.AddChild(actor);
            actor.SetProcess(false); actor.SetPhysicsProcess(false); actor.Combat.SetPhysicsProcess(false);
            var expectedPlacement = world.SpatialPlacement(key, null, units);
            actor._PhysicsProcess(0);
            var start = world.ActorPackageStartLocation(key) ?? throw new InvalidDataException("Attached actor did not publish its start location: " + actor.AiError);
            var events = (FalloutPackageEvents)(typeof(RuntimeNativeNpc).GetField("_packageEvents", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(actor)
                ?? throw new InvalidDataException("Native lifecycle is absent."));
            if (actor.AiError is not null || start.Cell != location.Cell || !start.Position.SequenceEqual(expectedPlacement.Position) ||
                events.Active?.Form != expected || events.LastEvent != "POBA" || events.Revision != 1)
                throw new InvalidDataException("Attached actor did not capture its source point and begin once: " + actor.AiError);
            actor.Position += new Vector3(units * 20, 0, 0);
            actor._PhysicsProcess(0);
            if (!world.ActorPackageStartLocation(key)!.Position.SequenceEqual(start.Position) || events.Revision != 1)
                throw new InvalidDataException("Active Sandbox recaptured a moved pose or repeated its begin event.");
            GD.Print("OPENNV_OWNED_CURRENT_SANDBOX_NATIVE_START_PASS detached=true captureBlocked=true transformedParent=true sharedSpatialPose=true retainedAfterMove=true beginOnce=true ordinaryMovement=unverified retail=unverified");
        }
        finally { if (GodotObject.IsInstanceValid(actor)) actor.Free(); if (GodotObject.IsInstanceValid(parent)) parent.Free(); }
    }
}
