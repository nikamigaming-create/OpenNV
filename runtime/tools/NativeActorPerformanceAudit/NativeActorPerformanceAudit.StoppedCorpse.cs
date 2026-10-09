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
    private async Task SavedStoppedCorpse(string game, string mod, string root, string path,
        string actorId, string attackerId, string[] dependencies, bool pendingSelection = false, bool equipmentContinuation = false)
    {
        var fixture = new Node3D(); AddChild(fixture);
        try
        {
            var installation = new FalloutModStackSelection([new(mod, root, dependencies)]).Resolve(game);
            RuntimeLiveContentSource.Configure(game, RuntimeLiveContentSource.FalloutNewVegasGame,
                installation.ContentRoots.Skip(1).ToArray(), installation.ActivePlugins, installation.Settings);
            var content = RuntimeLiveContentSource.Current!;
            using var records = FalloutPluginStack.Load(content.PluginSources);
            var checkpointBytes = File.ReadAllBytes(path);
            var campaign = JsonSerializer.Deserialize<FalloutNativeCampaignState>(checkpointBytes) ??
                throw new InvalidDataException("Reached checkpoint is absent.");
            if (pendingSelection || equipmentContinuation)
            {
                var controls = FalloutOpeningPlayerControlResolver.Resolve(records, ["VCG00", "VCG01"]);
                var opening = FalloutCellSceneReader.Read(records, new("FalloutNV.esm", 0x103df9));
                campaign = FalloutNativeCampaignSave.Read(path, content.SaveCompatibilityId, records).State;
            }
            if (campaign.Schema is not (FalloutNativeCampaignSave.ExpectedSchema or "opennv-native-fnv-campaign-save/v44" or "opennv-native-fnv-campaign-save/v43" or "opennv-native-fnv-campaign-save/v42" or "opennv-native-fnv-campaign-save/v38" or
                "opennv-native-fnv-campaign-save/v37") || campaign.SaveCompatibilityId != content.SaveCompatibilityId)
                throw new InvalidDataException("Reached corpse fixture belongs to another schema or complete source stack.");
            static FalloutFormKey Identity(string text)
            {
                var fields = text.Split(':');
                return fields.Length == 2 ? new(fields[0], Convert.ToUInt32(fields[1], 16)) :
                    throw new ArgumentException("Corpse fixture identities require plugin:hex-object-id.");
            }
            var caller = Identity(actorId); var attacker = Identity(attackerId);
            if (records.GetEffective(caller).Signature != "ACHR")
                throw new InvalidDataException("Selected corpse fixture is not a winning ACHR.");
            if (records.GetEffective(attacker).Signature is not ("ACHR" or "ACRE"))
                throw new InvalidDataException("Selected source attacker is not an actor reference.");
            var sourceReferences = campaign.References ?? throw new InvalidDataException("Reached save has no reference state.");
            var original = sourceReferences.Single(value => value.Reference == caller);
            if (original.PackageBindingFailure is null || original.Injury?.Dead == true || original.Engagement is not null ||
                original.Ragdoll is not null)
                throw new InvalidDataException("Selected genuine checkpoint has no living stopped-binding source fixture.");
            var sourceFailure = original.PackageBindingFailure;
            var quests = new FalloutQuestState(records); quests.Restore(campaign.Quests!);
            var globals = FalloutGlobalState.Read(records); globals.Restore(campaign.Globals!);
            var clock = new FalloutGameTime(globals, FalloutGameTimeBindings.Read(records),
                FalloutCalendar.Read(Path.Combine(game, "FalloutNV.exe"))); clock.Restore(campaign.GameTime!);
            using var world = new FalloutReferenceWorld(records);
            world.RestoreEncounterZones(campaign.EncounterZones); world.Restore(sourceReferences);
            world.RestoreActorOverrides(campaign.ActorOverrides); world.RestoreFactionRelations(campaign.FactionRelations);
            var cell = FalloutCellSceneReader.Read(records, world.Placement(caller).Cell); world.LoadCell(cell);
            var placed = cell.References.Single(value => value.FormKey == caller);
            var config = RuntimeConfiguration.Load(); var units = config.World.GameUnitsToMeters;
            var vitals = campaign.Vitals ?? throw new InvalidDataException("Reached save has no player vitals.");
            var level = vitals.Level;
            var context = new NativeActorCombatContext(() => null, () => vitals,
                (_, _) => throw new InvalidDataException("Corpse fixture attempted player damage."),
                (_, _) => throw new InvalidDataException("Corpse fixture restarted living route planning."),
                _ => true, () => level, globals, config.Player.StepHeightMeters,
                config.Simulation.GravityMetersPerSecondSquared);
            RuntimeNativeNpc Assemble(FalloutReferenceWorld owner)
            {
                Transform3D Placement(FalloutPlacedReference reference)
                {
                    var pose = owner.Placement(reference.FormKey);
                    return new(GamebryoCoordinate.ConvertReferenceEuler(new(pose.RotationRadians[0], pose.RotationRadians[1],
                        pose.RotationRadians[2]), reference.Scale),
                        GamebryoCoordinate.ConvertVector(new(pose.Position[0], pose.Position[1], pose.Position[2])) * units);
                }
                var templates = owner.InitializeActorTemplates(caller, level, globals);
                var actor = RuntimeNativeNpc.Create(records, content, placed, units,
                    (_, _, _, _) => new StandardMaterial3D(), owner.EquippedArmor(caller, level, globals),
                    templates, owner.ActorAppearanceOverride(caller));
                actor.Transform = Placement(placed);
                try
                {
                    RuntimeNativeActorContacts.Configure(actor, actor.Skeleton, 2);
                    actor.Combat = RuntimeNativeActorCombat.Attach(actor, actor.Skeleton, actor.Appearance.SkeletonPath,
                        owner, owner.Get(caller), records, content, 2, 3, context);
                    actor.ConfigureAi(records, quests, cell, Placement, clock: clock, globals: globals, world: owner);
                    fixture.AddChild(actor); actor.SetProcess(false); actor.SetPhysicsProcess(false);
                    actor.Combat.SetProcess(false); actor.Combat.SetPhysicsProcess(false);
                    return actor;
                }
                catch { actor.Free(); throw; }
            }
            var warm = Assemble(world); using var warmLifetime = new PackageFixtureLifetime(warm);
            if (JsonSerializer.Serialize(world.Get(caller).Capture()) != JsonSerializer.Serialize(original))
                throw new InvalidDataException("Source cold attachment changed the selected genuine pre-hit checkpoint.");
            if (pendingSelection) warm.EvaluatePackages(false);
            var combat = warm.Combat!;
            var equipment = equipmentContinuation ? await PrepareOwnedCorpseEquipment(fixture, warm, world, records, content, cell, attacker, context) : null;
            var contact = warm.FindChildren("*", "Area3D", true, false).OfType<Area3D>()
                .First(area => area.HasMeta("opennv_nif_collision_bone") && combat.HitPart(area) == 0);
            var health = world.Health(caller).Current;
            var first = combat.Hit(contact, new(health / 4, 1, 100, 1), attacker, level, globals);
            if (first.Dead || !combat.OwnsPose || world.Get(caller).Engagement is not { StartPending: true })
                throw new InvalidDataException("Real source hit did not retain an independent pending combat history.");
            var fatal = combat.Hit(contact, new(health * 100, 1, 100, 1), attacker, level, globals);
            if (equipment is not null) await RequireOwnedCorpseEquipmentAdmission(warm, world, records, equipment);
            if (!fatal.Died || !combat.Dead || !combat.StoppedAiPoseCaptureReady || world.PendingProcedureCaptureCount != 0)
                throw new InvalidDataException("Actual source corpse did not compose with its stopped source failure.");
            if (world.PendingHitEventCount == 0)
                throw new InvalidDataException("Source hit did not retain its actual pending event admission.");
            var pendingRefused = false;
            try { _ = world.Capture(); }
            catch (NotSupportedException error) when (error.Message.Contains("pending reference hit events", StringComparison.Ordinal))
            { pendingRefused = true; }
            if (!pendingRefused) throw new InvalidDataException("Unconsumed source hit admission was admitted as a settled checkpoint.");
            // Match the shared source event boundary. The original script owns
            // this admission; a missing host effect retains its exact consumed
            // prefix/fault instead of claiming that gameplay effect completed.
            var scripts = new FalloutReferenceScripts(records, world, quests, new((_, _) => false,
                effect => throw new NotSupportedException("Owned corpse hit host effect remains unbound: " + effect.Kind),
                Globals: globals, IsInCombat: reference => world.IsInCombat(reference, () => false), PlayerLevel: () => level));
            var hitBatch = world.HitEvents.SnapshotPending(caller);
            var hitResults = scripts.DispatchFrame(caller, hitBatch.Events, 0);
            world.HitEvents.Consume(hitBatch);
            if (world.PendingHitEventCount != 0)
                throw new InvalidDataException("Source hit dispatch left an unconsumed nested admission.");
            GD.Print($"OPENNV_NATIVE_CORPSE_HIT_ADMISSION actor={caller} marks={hitBatch.Count} " +
                $"blocks={hitResults.Sum(result => result.Blocks)} fault={hitResults.FirstOrDefault(result => result.Error is not null)?.Error ?? "none"} " +
                "pendingCheckpointRefused=true sourceDispatch=true consumedPrefixRetained=true effectsNotInvented=true");
            if (pendingSelection)
            {
                await QueuedCorpseCold(warm, world, records, cell, fixture, Assemble,
                    world.CaptureActorOverrides(), campaign.FactionRelations);
                if (!checkpointBytes.SequenceEqual(File.ReadAllBytes(path)))
                    throw new InvalidDataException("Queued corpse audit changed its checkpoint input.");
                return;
            }
            var beforeUnload = JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(world.Capture()))!;
            var dead = beforeUnload.Single(value => value.Reference == caller);
            RequireStoppedCorpse(dead, original, sourceFailure);
            if (equipment is not null) RequireCorpseEquipmentConservation(dead, equipment);
            var deadRagdoll = dead.Ragdoll!;
            if (deadRagdoll.Bodies.Count == 0 || deadRagdoll.Bodies.Any(body => body.Transform.Any(value => !float.IsFinite(value))))
                throw new InvalidDataException("Corpse fixture lacks the complete native source rig.");
            fixture.RemoveChild(warm); world.UnloadCell(cell.Cell.FormKey);
            var retired = world.Get(caller);
            if (retired.CaptureEngagement is not null || retired.CaptureRagdoll is not null ||
                retired.CaptureCorpseEquipment is not null || retired.CanCaptureCorpseEquipment is not null ||
                retired.CanCapturePackageBindingFailure is not null || retired.CapturePackageBindingFailure is not null ||
                retired.PackageBindingFailure is null || retired.ProcedureCaptureBlocker != retired.PackageBindingFailure.Error ||
                world.PendingProcedureCaptureCount != 0 || world.IsResident(caller))
                throw new InvalidDataException("Child-first retirement lost its independent corpse or stopped-fault capture receipt.");
            var afterUnload = world.Capture();
            if (JsonSerializer.Serialize(afterUnload) != JsonSerializer.Serialize(beforeUnload))
                throw new InvalidDataException("Corpse unload changed consumed source, random, clock, physical state or combat history.");
            using var cold = new FalloutReferenceWorld(records);
            cold.Restore(JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(afterUnload))!);
            cold.RestoreActorOverrides(equipment is null ? campaign.ActorOverrides : world.CaptureActorOverrides());
            cold.RestoreFactionRelations(campaign.FactionRelations); cold.LoadCell(cell);
            var resumed = Assemble(cold); using var resumedLifetime = new PackageFixtureLifetime(resumed);
            if (cold.Get(caller).PackageBindingFailureCaptureReady || cold.PendingProcedureCaptureCount == 0)
                throw new InvalidDataException("Cold corpse was admitted before its deferred physical owner existed.");
            var initialized = new TaskCompletionSource(); Callable.From(() => initialized.SetResult()).CallDeferred();
            await initialized.Task;
            if (resumed.Combat!.Error is not null || !resumed.Combat.StoppedAiPoseCaptureReady || cold.PendingProcedureCaptureCount != 0)
                throw new InvalidDataException("Cold corpse did not acquire the real deferred native physical owner: " + resumed.Combat.Error);
            var actual = cold.Get(caller).Capture(); RequireStoppedCorpse(actual, original, sourceFailure);
            if (equipment is not null) RequireCorpseEquipmentConservation(actual, equipment);
            RequireCorpsePose(actual.Ragdoll!, deadRagdoll);
            if (JsonSerializer.Serialize(actual with { Ragdoll = deadRagdoll }) != JsonSerializer.Serialize(dead))
            {
                GD.Print("OPENNV_NATIVE_STOPPED_CORPSE_FIELD_DIFF " + JsonSerializer.Serialize(CorpseFieldDiff(
                    JsonSerializer.SerializeToElement(dead), JsonSerializer.SerializeToElement(actual with { Ragdoll = deadRagdoll }))));
                throw new InvalidDataException("Cold corpse changed its retained source error, prefix, history, clock or selected random state.");
            }
            var bodies = resumed.FindChildren("*", "RigidBody3D", true, false).OfType<RigidBody3D>()
                .Where(body => body.GetParent() is RuntimeNativeActorRagdoll).ToArray();
            if (bodies.Length != deadRagdoll.Bodies.Count || bodies.Any(body => !body.IsInsideTree() || body.CollisionLayer != 2))
                throw new InvalidDataException("Cold corpse has no complete live source body graph.");
            foreach (var delta in new[] { 0d, .125, .25 })
            {
                resumed._Process(delta); resumed.Combat._PhysicsProcess(delta);
                if (resumed.AiError != sourceFailure.Error || cold.Get(caller).PackageBindingFailure is null ||
                    JsonSerializer.Serialize(cold.Get(caller).Capture() with { Ragdoll = deadRagdoll }) != JsonSerializer.Serialize(dead))
                    throw new InvalidDataException("Stopped cold corpse resumed living selection or replayed consumed source effects.");
            }
            foreach (var invalid in new[]
            {
                dead with { Ragdoll = null },
                dead with { PackageBindingFailure = dead.PackageBindingFailure! with { PackageSha256 = new string('0', 64) } },
                dead with { Engagement = dead.Engagement! with { Target = dead.Base } }
            })
            {
                using var rejected = new FalloutReferenceWorld(records);
                var refused = false;
                try { rejected.Restore([invalid]); }
                catch (InvalidDataException) { refused = true; }
                if (!refused || rejected.InstanceCount != 0)
                    throw new InvalidDataException("Stopped corpse source or independent-owner corruption was not refused atomically.");
            }
            if (!checkpointBytes.SequenceEqual(File.ReadAllBytes(path)))
                throw new InvalidDataException("Corpse component changed its genuine checkpoint input.");
            if (equipment is not null)
                GD.Print($"OPENNV_NATIVE_EQUIPPED_CORPSE_PASS actor={caller} attachments={dead.CorpseEquipment!.Attachments.Count} " +
                    $"finiteGenerations={equipment.SoundGenerations.Length} muzzle={equipment.MuzzleStarted} " +
                    "actualCurrent=true nativeCold=true childFirstRetirement=true sourceTravelFault=true sourceDoorNotCompleted=true " +
                    "noCombatEnd=true itemAmmoConditionRandomConservation=true activeAudioAndEffectsRefused=true sourceGraphDriftAtomic=true " +
                    "fixture=owned-component dropPhysicsRetailCampaignAndPixels=unverified recording=false");
            GD.Print($"OPENNV_NATIVE_STOPPED_CORPSE_PASS actor={caller} sourceContact={contact.GetMeta("opennv_nif_collision_bone")} " +
                $"bodies={deadRagdoll.Bodies.Count} runtimeMvid={typeof(RuntimeConfiguration).Assembly.ManifestModule.ModuleVersionId} " +
                "sourceFaultAndConsumedPrefix=true combatHistory=true childFirstRetirement=true nativeCold=true deferredCaptureGuard=true " +
                "randomAndClocks=true deathNotRepeated=true sourceDriftAtomic=true fixture=owned-component campaignAndPixels=unverified recording=false");
        }
        finally { if (GodotObject.IsInstanceValid(fixture)) fixture.Free(); }
    }

    private static void RequireStoppedCorpse(FalloutReferenceSnapshot dead, FalloutReferenceSnapshot original,
        FalloutActorPackageBindingFailure sourceFailure)
    {
        if (dead.Injury is not { Dead: true, DeathInventoryGranted: true } || dead.DeathCount != (original.DeathCount ?? 0) + 1 ||
            dead.Ragdoll is null || dead.Engagement is not { StartPending: true, Action: "pursue", Position: not null, Rotation: not null } ||
            dead.PackageAssignment is not null || dead.PackageEvents is { Count: > 0 } || dead.PackageBindingFailure is not { } failure ||
            failure.Package != sourceFailure.Package || failure.PackageSha256 != sourceFailure.PackageSha256 ||
            failure.Error != sourceFailure.Error || failure.AiRandomState != sourceFailure.AiRandomState ||
            failure.PollRemaining != sourceFailure.PollRemaining || failure.ScheduleTime != sourceFailure.ScheduleTime ||
            JsonSerializer.Serialize(failure.Retirement) != JsonSerializer.Serialize(sourceFailure.Retirement) ||
            JsonSerializer.Serialize(failure.IdleState) != JsonSerializer.Serialize(sourceFailure.IdleState) ||
            JsonSerializer.Serialize(dead.Animation) != JsonSerializer.Serialize(original.Animation))
            throw new InvalidDataException("Stopped corpse failed to conserve the source fault, consumed prefix, clocks and independently owned death/history.");
    }

    private static void RequireCorpsePose(FalloutActorRagdollState actual, FalloutActorRagdollState expected)
    {
        if (actual.SkeletonSha256 != expected.SkeletonSha256 || actual.Bodies.Count != expected.Bodies.Count ||
            JsonSerializer.Serialize(actual.Cuts) != JsonSerializer.Serialize(expected.Cuts) || !actual.Bodies.Zip(expected.Bodies).All(pair =>
                pair.First.SourceBody == pair.Second.SourceBody && pair.First.Sleeping == pair.Second.Sleeping &&
                pair.First.LinearVelocity.SequenceEqual(pair.Second.LinearVelocity) && pair.First.AngularVelocity.SequenceEqual(pair.Second.AngularVelocity) &&
                pair.First.Transform.Zip(pair.Second.Transform).All(value => MathF.Abs(value.First - value.Second) < .0002f)))
            throw new InvalidDataException("Cold native corpse changed source bodies, physical velocities, sleep/cut state or transform beyond Float32 tolerance.");
    }

    private static IReadOnlyList<object> CorpseFieldDiff(JsonElement warm, JsonElement cold)
    {
        var result = new List<object>();
        void Compare(JsonElement first, JsonElement second, string path)
        {
            if (result.Count >= 32 || first.GetRawText() == second.GetRawText()) return;
            if (first.ValueKind == JsonValueKind.Object && second.ValueKind == JsonValueKind.Object)
            {
                foreach (var field in first.EnumerateObject())
                    if (second.TryGetProperty(field.Name, out var other)) Compare(field.Value, other, path + "." + field.Name);
                    else if (result.Count < 32) result.Add(new { path = path + "." + field.Name, warm = field.Value.GetRawText(), cold = "absent" });
            }
            else if (first.ValueKind == JsonValueKind.Array && second.ValueKind == JsonValueKind.Array && first.GetArrayLength() == second.GetArrayLength())
            {
                for (var index = 0; index < first.GetArrayLength(); ++index) Compare(first[index], second[index], path + "[" + index + "]");
            }
            else result.Add(new { path, warm = first.GetRawText(), cold = second.GetRawText() });
        }
        Compare(warm, cold, "reference"); return result;
    }
}
