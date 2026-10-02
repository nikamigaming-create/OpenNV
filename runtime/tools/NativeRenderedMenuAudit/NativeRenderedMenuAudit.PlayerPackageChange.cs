using System.Text.Json;
using Godot;
using OpenNV.Runtime;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.World.Cells;

public partial class NativeRenderedMenuAudit
{
    private void PlayerPackageChange(string baseRoot, string mod, string root,
        string initialId, string nextId, string replacementId, string[] dependencies)
    {
        var setup = new FalloutModStackSelection([new(mod, root, dependencies)]).Resolve(baseRoot);
        RuntimeLiveContentSource.Configure(baseRoot, RuntimeLiveContentSource.FalloutNewVegasGame,
            setup.ContentRoots.Skip(1).ToArray(), setup.ActivePlugins, setup.Settings);
        RuntimeNativePlayer? player = null, coldPlayer = null, removalPlayer = null;
        try
        {
            var content = RuntimeLiveContentSource.Current!;
            using var records = FalloutPluginStack.Load(content.PluginSources);
            using var world = new FalloutReferenceWorld(records);
            var initial = FalloutDialogueTopic.Find(records, "PACK", initialId);
            var next = FalloutDialogueTopic.Find(records, "PACK", nextId);
            var replacement = FalloutDialogueTopic.Find(records, "PACK", replacementId);
            var definition = FalloutScriptPackage.Read(initial);
            var nextDefinition = FalloutScriptPackage.Read(next);
            var replacementDefinition = FalloutScriptPackage.Read(replacement);
            var change = definition.Events.GetValueOrDefault("POCA") ?? throw new InvalidDataException("Owned fixture requires a change idle.");
            if (initial.FormKey == next.FormKey || initial.FormKey == replacement.FormKey || next.FormKey == replacement.FormKey)
                throw new InvalidDataException("Owned change fixture requires three distinct packages.");
            var marker = world.Placement(definition.LocationReference ?? throw new InvalidDataException("Owned fixture requires an explicit reference location."));
            var configuration = RuntimeConfiguration.Load();
            var transform = new Transform3D(GamebryoCoordinate.ConvertReferenceEuler(
                new(marker.RotationRadians[0], marker.RotationRadians[1], marker.RotationRadians[2]), 1),
                GamebryoCoordinate.ConvertVector(new(marker.Position[0], marker.Position[1], marker.Position[2])) * configuration.World.GameUnitsToMeters);
            RuntimeNativePlayer Create()
            {
                var actor = new RuntimeNativePlayer(); actor.Configure(configuration, transform);
                AddChild(actor); actor.SetProcess(false); actor.SetPhysicsProcess(false); actor.SetProcessUnhandledInput(false);
                return actor;
            }
            Transform3D Camera(RuntimeNativePlayer actor) => actor.GetChildren().OfType<Camera3D>().Single().Transform;
            void CheckRemoval(FalloutScriptSessionSnapshot snapshot)
            {
                removalPlayer?.Free(); removalPlayer = Create();
                var freeCamera = Camera(removalPlayer);
                var removalSession = new FalloutScriptSession();
                removalSession.Restore(JsonSerializer.Deserialize<FalloutScriptSessionSnapshot>(JsonSerializer.Serialize(snapshot))!);
                var removal = new RuntimeNativePlayerPackage(records, removalPlayer, removalSession, world, () => marker.Cell);
                removal.Apply(null);
                if (removalSession.PlayerPackage is not null || Camera(removalPlayer) != freeCamera)
                    throw new InvalidDataException("Source removal retained its assignment or authored camera.");
                removal.Advance(60); removal.Apply(null);
                if (removalSession.PlayerPackage is not null || Camera(removalPlayer) != freeCamera)
                    throw new InvalidDataException("Cancelled change reinstalled its pending package or camera on later frames.");
                var cleared = new FalloutScriptSession(); cleared.Restore(JsonSerializer.Deserialize<FalloutScriptSessionSnapshot>(
                    JsonSerializer.Serialize(removalSession.Capture()))!);
                _ = new RuntimeNativePlayerPackage(records, removalPlayer, cleared, world, () => marker.Cell);
                if (cleared.PlayerPackage is not null || Camera(removalPlayer) != freeCamera)
                    throw new InvalidDataException("Cold cancellation resurrected a pending assignment or camera.");
                removalPlayer.Free(); removalPlayer = null;
            }
            double Duration(FalloutFormKey idle)
            {
                var selected = FalloutActorIdleSource.Resolve(records, records.GetEffective(idle));
                if (!content.TryRead(selected.AnimationPath, null, out var bytes, out _)) throw new FileNotFoundException(selected.AnimationPath);
                var nif = FalloutNifFile.Read(bytes); var sequence = nif.Roots.Select(nif.ReadControllerSequence).Single();
                return (double)(sequence.StopTime - sequence.StartTime) / sequence.Frequency;
            }
            if (FalloutIdleAnimationData.Read(records.GetEffective(change)).AdmitsAdditionalLoops(byte.MaxValue))
            {
                RepeatedPlayerPackageChange(records, world, definition, nextDefinition, replacementDefinition,
                    marker.Cell, Create, Camera, CheckRemoval);
                return;
            }
            player = Create(); coldPlayer = Create();
            var session = new FalloutScriptSession(); var cold = new FalloutScriptSession();
            var owner = new RuntimeNativePlayerPackage(records, player, session, world, () => marker.Cell);
            owner.Apply(initial.FormKey); owner.Advance(.375);
            owner.Apply(initial.FormKey); owner.Advance(.375);
            var same = session.Capture();
            if (same.PlayerPackage is not { EventKind: "POCA", Elapsed: .375 } || same.PlayerPackage.PendingPackage != initial.FormKey ||
                same.PlayerPackage.Idle != change) throw new InvalidDataException("Same-package request did not reach its source change event.");
            CheckRemoval(same);
            cold.Restore(JsonSerializer.Deserialize<FalloutScriptSessionSnapshot>(JsonSerializer.Serialize(same))!);
            var restored = new RuntimeNativePlayerPackage(records, coldPlayer, cold, world, () => marker.Cell);
            if (session.PlayerPackage != cold.PlayerPackage || Camera(player) != Camera(coldPlayer))
                throw new InvalidDataException("Cold change event restarted or changed the sampled camera.");
            var remainder = Duration(change) - same.PlayerPackage.Elapsed + .5;
            owner.Advance(remainder); restored.Advance(remainder);
            if (session.PlayerPackage != cold.PlayerPackage || Camera(player) != Camera(coldPlayer) ||
                session.PlayerPackage!.PendingPackage is not null || session.PlayerPackage.EventKind is not null || session.PlayerPackage.Elapsed != .5)
                throw new InvalidDataException("Same-package change lost its idle handoff or frame remainder.");

            owner.Apply(next.FormKey); owner.Advance(.375); var firstPending = session.PlayerPackage!;
            owner.Apply(replacement.FormKey); var newest = session.Capture();
            if (newest.PlayerPackage!.PendingPackage != replacement.FormKey || newest.PlayerPackage.Elapsed != firstPending.Elapsed ||
                newest.PlayerPackage.Idle != firstPending.Idle)
                throw new InvalidDataException("A later package request restarted the outgoing event or failed to replace its pending assignment.");
            coldPlayer.Free(); coldPlayer = Create(); cold = new();
            cold.Restore(JsonSerializer.Deserialize<FalloutScriptSessionSnapshot>(JsonSerializer.Serialize(newest))!);
            restored = new(records, coldPlayer, cold, world, () => marker.Cell);
            world.QueuePlayerMoveTo(initial.FormKey, definition.LocationReference!.Value, 0, 0, 0);
            owner.Advance(3); restored.Advance(3); world.PlayerMoves.Clear();
            if (session.PlayerPackage != newest.PlayerPackage || cold.PlayerPackage != newest.PlayerPackage)
                throw new InvalidDataException("Pending player movement advanced the change clock.");
            var rejected = false;
            try { owner.Apply(change); } catch (InvalidDataException) { rejected = true; }
            if (!rejected || session.PlayerPackage != newest.PlayerPackage)
                throw new InvalidDataException("Invalid package request changed the active event or pending assignment.");
            CheckRemoval(newest);
            if (session.PlayerPackage != newest.PlayerPackage || cold.PlayerPackage != newest.PlayerPackage)
                throw new InvalidDataException("Removing a separate source assignment changed other package owners.");
            var forged = new FalloutScriptSession(); forged.Restore(newest with
            { PlayerPackage = newest.PlayerPackage with { PendingPackageSha256 = new string('0', 64) } });
            rejected = false;
            try { _ = new RuntimeNativePlayerPackage(records, coldPlayer, forged, world, () => marker.Cell); }
            catch (InvalidDataException) { rejected = true; }
            if (!rejected) throw new InvalidDataException("Cold change accepted a changed pending source package.");
            remainder = Duration(change) - newest.PlayerPackage.Elapsed + .5;
            owner.Advance(remainder); restored.Advance(remainder);
            if (session.PlayerPackage != cold.PlayerPackage || Camera(player) != Camera(coldPlayer) ||
                session.PlayerPackage is not { EventKind: "POBA", Elapsed: .5 } || session.PlayerPackage.Package != replacement.FormKey ||
                session.PlayerPackage.Idle != replacementDefinition.Events.GetValueOrDefault("POBA"))
                throw new InvalidDataException("Deferred replacement lost its source begin event, clock or cold camera handoff.");
            GD.Print($"OPENNV_NATIVE_PLAYER_PACKAGE_CHANGE_PASS source={initial.FormKey} next={next.FormKey} newest={replacement.FormKey} " +
                $"nextIdles={nextDefinition.Idles.Count} newestIdles={replacementDefinition.Idles.Count} samePackageChange=true " +
                "latestPending=true coldClock=true coldCamera=true remainder=true pendingMovePaused=true invalidAtomic=true sourceDriftRejected=true " +
                "cancellation=true sameAndDifferentPending=true cameraReleased=true noLateReinstall=true coldCancellation=true " +
                "recording=false boundary=isolated-owned-package-fixture endAnimationsAndScripts=unbound nonCameraTargets=unbound parity=unverified");
        }
        finally { player?.Free(); coldPlayer?.Free(); removalPlayer?.Free(); RuntimeLiveContentSource.Clear(); }
    }
}
