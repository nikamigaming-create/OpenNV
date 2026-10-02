using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

public partial class NativeRenderedMenuAudit
{
    private static void RepeatedPlayerPackageChange(FalloutPluginStack records, FalloutReferenceWorld world,
        FalloutScriptPackage initial, FalloutScriptPackage next, FalloutScriptPackage replacement, FalloutFormKey cell,
        Func<RuntimeNativePlayer> create, Func<RuntimeNativePlayer, Transform3D> camera,
        Action<FalloutScriptSessionSnapshot> checkRemoval)
    {
        RuntimeNativePlayer? player = null, coldPlayer = null;
        try
        {
            player = create(); coldPlayer = create();
            var session = new FalloutScriptSession(); var cold = new FalloutScriptSession();
            var owner = new RuntimeNativePlayerPackage(records, player, session, world, () => cell);
            owner.Apply(initial.Form); owner.Advance(.375);
            owner.Apply(initial.Form); owner.Advance(.375);
            var same = session.Capture();
            var change = initial.Events.GetValueOrDefault("POCA");
            if (same.PlayerPackage is not
                {
                    EventKind: "POCA", PendingPackage: null, Elapsed: .375,
                    Playback.SelectedAdditionalLoops: byte.MaxValue
                } || same.PlayerPackage.Idle != change)
                throw new InvalidDataException("Infinite change pose imposed an assignment barrier or lost its source IDLE.");
            checkRemoval(same);
            var legacyPlayer = create();
            try
            {
                var legacy = new FalloutScriptSession(); legacy.Restore(same with
                {
                    PlayerPackage = same.PlayerPackage with
                    {
                        Playback = null,
                        IdleSha256 = null,
                        PendingPackage = initial.Form,
                        PendingPackageSha256 = same.PlayerPackage.PackageSha256
                    }
                });
                var legacyOwner = new RuntimeNativePlayerPackage(records, legacyPlayer, legacy, world, () => cell);
                if (camera(legacyPlayer) != camera(player) || legacyPlayer.GetChildren().OfType<NativeOwnedAnimationSoundPlayer>().Any())
                    throw new InvalidDataException("Legacy pending camera restarted its phase or replayed past sounds.");
                legacyOwner.Advance(0);
                if (legacy.PlayerPackage != same.PlayerPackage || camera(legacyPlayer) != camera(player))
                    throw new InvalidDataException("Legacy infinite change did not settle while retaining its restored pose.");
            }
            finally { legacyPlayer.Free(); }
            cold.Restore(JsonSerializer.Deserialize<FalloutScriptSessionSnapshot>(JsonSerializer.Serialize(same))!);
            var restored = new RuntimeNativePlayerPackage(records, coldPlayer, cold, world, () => cell);
            if (session.PlayerPackage != cold.PlayerPackage || camera(player) != camera(coldPlayer))
                throw new InvalidDataException("Cold infinite change restarted its source camera phase.");
            owner.Advance(75.125); restored.Advance(75.125);
            var held = session.PlayerPackage!;
            if (held.Playback is not { CompletedRepeats: > 0, RemainingAdditionalLoops: byte.MaxValue } ||
                session.PlayerPackage != cold.PlayerPackage || camera(player) != camera(coldPlayer))
                throw new InvalidDataException("Infinite camera replayed its intro or diverged across cold repeats.");
            owner.Apply(initial.Form);
            if (session.PlayerPackage != held) throw new InvalidDataException("Repeated assignment restarted the active identical change pose.");

            if (next.Events.GetValueOrDefault("POBA") != change)
                throw new InvalidDataException("Owned handoff fixture requires matching outgoing/change and incoming/begin IDLEs.");
            var before = camera(player);
            owner.Apply(next.Form);
            var handed = session.Capture();
            if (handed.PlayerPackage is not { EventKind: "POBA", PendingPackage: null } ||
                handed.PlayerPackage.Package != next.Form || handed.PlayerPackage.Idle != change ||
                handed.PlayerPackage.Playback != held.Playback || handed.PlayerPackage.Elapsed != held.Elapsed || camera(player) != before)
                throw new InvalidDataException("Incoming assignment restarted its identical outgoing camera travel.");
            checkRemoval(handed);
            coldPlayer.Free(); coldPlayer = create(); cold = new();
            cold.Restore(JsonSerializer.Deserialize<FalloutScriptSessionSnapshot>(JsonSerializer.Serialize(handed))!);
            restored = new(records, coldPlayer, cold, world, () => cell);
            owner.Advance(.625); restored.Advance(.625);
            if (session.PlayerPackage != cold.PlayerPackage || camera(player) != camera(coldPlayer))
                throw new InvalidDataException("Cold handoff lost the next repeat interval.");

            owner.Apply(replacement.Form);
            var newest = session.Capture();
            if (newest.PlayerPackage?.Package != replacement.Form || newest.PlayerPackage.PendingPackage is not null ||
                newest.PlayerPackage.Idle != replacement.Events.GetValueOrDefault("POBA"))
                throw new InvalidDataException("A later assignment did not select its winning begin pose.");
            coldPlayer.Free(); coldPlayer = create(); cold = new();
            cold.Restore(JsonSerializer.Deserialize<FalloutScriptSessionSnapshot>(JsonSerializer.Serialize(newest))!);
            restored = new(records, coldPlayer, cold, world, () => cell);
            world.QueuePlayerMoveTo(initial.Form, initial.LocationReference!.Value, 0, 0, 0);
            owner.Advance(3); restored.Advance(3); world.PlayerMoves.Clear();
            if (session.PlayerPackage != newest.PlayerPackage || cold.PlayerPackage != newest.PlayerPackage)
                throw new InvalidDataException("Pending player movement advanced the infinite camera.");
            var rejected = false;
            try { owner.Apply(change); } catch (InvalidDataException) { rejected = true; }
            if (!rejected || session.PlayerPackage != newest.PlayerPackage)
                throw new InvalidDataException("Malformed package request changed a settled infinite pose.");
            checkRemoval(newest);
            foreach (var bad in new[]
            {
                newest.PlayerPackage! with { AnimationSha256 = new string('0', 64) },
                newest.PlayerPackage! with { IdleSha256 = new string('0', 64) },
                newest.PlayerPackage! with { Elapsed = newest.PlayerPackage!.Elapsed + 1 },
                newest.PlayerPackage! with { Playback = newest.PlayerPackage!.Playback! with { SelectedAdditionalLoops = 0, RemainingAdditionalLoops = 0 } }
            })
            {
                var forged = new FalloutScriptSession(); forged.Restore(newest with { PlayerPackage = bad });
                rejected = false;
                try { _ = new RuntimeNativePlayerPackage(records, coldPlayer, forged, world, () => cell); }
                catch (InvalidDataException) { rejected = true; }
                if (!rejected) throw new InvalidDataException("Cold camera accepted changed source identity, phase or repetitions.");
            }
            var liveState = JsonSerializer.Serialize(owner.State);
            var liveCamera = camera(player);
            var soundChildren = player.GetChildren().OfType<NativeOwnedAnimationSoundPlayer>().ToArray();
            foreach (var bad in new[]
            {
                same.PlayerPackage! with
                {
                    PendingPackage = next.Form,
                    PendingPackageSha256 = new string('0', 64)
                },
                same.PlayerPackage! with { Elapsed = same.PlayerPackage!.Elapsed + 1 }
            })
            {
                rejected = false;
                try { owner.Restore(bad); } catch (InvalidDataException) { rejected = true; }
                if (!rejected || session.PlayerPackage != newest.PlayerPackage || camera(player) != liveCamera ||
                    JsonSerializer.Serialize(owner.State) != liveState ||
                    !player.GetChildren().OfType<NativeOwnedAnimationSoundPlayer>().SequenceEqual(soundChildren))
                    throw new InvalidDataException("Rejected live camera restore changed its assignment, clock, camera or sound owner.");
            }
            owner.Advance(35); restored.Advance(35);
            if (session.PlayerPackage != cold.PlayerPackage || camera(player) != camera(coldPlayer))
                throw new InvalidDataException("Replacement did not retain its cold infinite camera.");
            GD.Print($"OPENNV_NATIVE_PLAYER_PACKAGE_CHANGE_PASS source={initial.Form} next={next.Form} newest={replacement.Form} " +
                "infiniteChangePose=true assignmentSettled=true authoredRepeat=true sameIdleClockRetained=true coldClock=true coldCamera=true " +
                "legacyPendingSettled=true pendingMovePaused=true invalidAtomic=true liveRestoreAtomic=true sourceDriftRejected=true cancellation=true noLateReinstall=true " +
                "recording=false boundary=isolated-owned-package-fixture finiteEventTiming=unverified nonCameraTargets=unbound parity=unverified");
        }
        finally { player?.Free(); coldPlayer?.Free(); }
    }
}
