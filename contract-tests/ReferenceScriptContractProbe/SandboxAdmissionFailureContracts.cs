using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;
using OpenNV.Runtime.World.Actors;

internal static class SandboxAdmissionFailureContracts
{
    internal static void Run()
    {
        var directory = Directory.CreateTempSubdirectory("opennv-sandbox-admission-").FullName;
        var path = Path.Combine(directory, "Admission.esm");
        try
        {
            var data = new byte[12]; data[4] = 12;
            var location = new byte[12]; location[0] = 3;
            var reference = Record("ACHR", 0x900, Field("NAME", BitConverter.GetBytes(0x10u)), Field("DATA", new byte[24]));
            var creatureReference = Record("ACRE", 0x902, Field("NAME", BitConverter.GetBytes(0x11u)), Field("DATA", new byte[24]));
            var turningReference = Record("ACRE", 0x903, Field("NAME", BitConverter.GetBytes(0x12u)), Field("DATA", new byte[24]));
            var bodyPart = new byte[84]; BinaryPrimitives.WriteSingleLittleEndian(bodyPart, 1); bodyPart[4] = 1; bodyPart[6] = 100;
            BinaryPrimitives.WriteSingleLittleEndian(bodyPart.AsSpan(24), 1);
            BinaryPrimitives.WriteSingleLittleEndian(bodyPart.AsSpan(40), 1);
            BinaryPrimitives.WriteSingleLittleEndian(bodyPart.AsSpan(80), 1);
            var creatureStats = new byte[24]; BinaryPrimitives.WriteUInt16LittleEndian(creatureStats.AsSpan(14), 100);
            var group = new byte[24 + reference.Length + creatureReference.Length + turningReference.Length]; Encoding.ASCII.GetBytes("GRUP").CopyTo(group, 0);
            BinaryPrimitives.WriteUInt32LittleEndian(group.AsSpan(4), (uint)group.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(group.AsSpan(8), 0x800);
            BinaryPrimitives.WriteUInt32LittleEndian(group.AsSpan(12), 6); reference.CopyTo(group, 24);
            creatureReference.CopyTo(group, 24 + reference.Length);
            turningReference.CopyTo(group, 24 + reference.Length + creatureReference.Length);
            byte[] Package(uint id, bool idle, uint flags = 0)
            {
                var declaration = (byte[])data.Clone();
                BinaryPrimitives.WriteUInt32LittleEndian(declaration, flags);
                return Record("PACK", id,
                    Field("EDID", Encoding.ASCII.GetBytes("Sandbox\0")), Field("PKDT", declaration), Field("PLDT", location),
                    Field("IDLC", [idle ? (byte)1 : (byte)0]), Field("IDLA", idle ? BitConverter.GetBytes(0x30u) : []),
                    Field("POBA", []));
            }
            var currentLocation = (byte[])location.Clone(); currentLocation[0] = 2;
            var foodData = new byte[8]; foodData[4] = 2;
            File.WriteAllBytes(path, Join(Record("TES4", 0, Field("HEDR", new byte[12])),
                Record("BPTD", 0x1d, Field("BPNN", Encoding.ASCII.GetBytes("Root\0")),
                    Field("BPNT", Encoding.ASCII.GetBytes("Root\0")), Field("BPND", bodyPart)),
                Record("NPC_", 0x10, Field("ACBS", new byte[24]), Field("DATA", new byte[11]), Field("NAM4", BitConverter.GetBytes(6u)), Field("PKID", BitConverter.GetBytes(0x20u)),
                    Field("PKID", BitConverter.GetBytes(0x21u)), Field("PKID", BitConverter.GetBytes(0x24u))), Package(0x20, false), Package(0x21, true), Package(0x22, false, 0x200),
                Package(0x23, false, 0x400000),
                Record("PACK", 0x24, Field("EDID", Encoding.ASCII.GetBytes("CurrentSandbox\0")), Field("PKDT", data),
                    Field("PLDT", currentLocation), Field("IDLC", [0]), Field("IDLA", []), Field("POBA", [])),
                Record("CREA", 0x11, Field("ACBS", creatureStats), Field("TNAM", BitConverter.GetBytes(0f)), Field("PKID", BitConverter.GetBytes(0x20u)),
                    Field("PKID", BitConverter.GetBytes(0x24u))),
                Record("CREA", 0x12, Field("ACBS", creatureStats), Field("TNAM", BitConverter.GetBytes(180f)), Field("PKID", BitConverter.GetBytes(0x20u))),
                Record("INGR", 0x40, Field("EDID", Encoding.ASCII.GetBytes("FirstFood\0")), Field("ENIT", foodData)),
                Record("INGR", 0x41, Field("EDID", Encoding.ASCII.GetBytes("SecondFood\0")), Field("ENIT", foodData)),
                Record("CELL", 0x800, Field("DATA", [1, 0])), group,
                Record("CELL", 0x801, Field("DATA", [1, 0])), NavigationFloor(0x802, 0x801)));
            using var records = FalloutPluginStack.Load(directory, ["Admission.esm"]);
            FalloutFormKey Key(uint id) => new("Admission.esm", id);
            string Hash(uint id) => FalloutActorFurnitureContinuation.RecordHash(records.GetEffective(Key(id)));
            var continuingSandbox = FalloutSandboxPackage.Read(records.GetEffective(Key(0x22)));
            Require(FalloutSandboxPackage.Read(records.GetEffective(Key(0x23))).DefensiveCombat &&
                !FalloutSandboxPackage.Read(records.GetEffective(Key(0x20))).DefensiveCombat,
                "Sandbox defensive combat flag was not retained from the source package.");
            Require(continuingSandbox.ContinueIfPlayerNear && continuingSandbox.RetainsSelection(true, false) &&
                !continuingSandbox.RetainsSelection(false, false) && !continuingSandbox.RetainsSelection(true, true) &&
                !FalloutSandboxPackage.Read(records.GetEffective(Key(0x20))).RetainsSelection(true, false),
                "Sandbox continuation ignored its source flag, cell boundary or explicit evaluation override.");
            var actor = new FalloutReferenceInstance(records.GetEffective(Key(0x900)), null);
            var effects = 0;
            void Dispatch(FalloutScriptPackage package, string kind)
            {
                if (kind == "POBA" && package.EventPrograms.ContainsKey(kind)) effects++;
            }
            var live = new FalloutPackageEvents(Dispatch);
            live.Change(FalloutScriptPackage.Read(records.GetEffective(Key(0x20))));
            Require(effects == 1 && !live.Done && live.Error is null, "POBA effect did not execute exactly once.");
            var pendingActor = new FalloutReferenceInstance(records.GetEffective(Key(0x900)), null)
            {
                PackageAssignment = new(Key(0x20), Hash(0x20), false),
                ProcedureCaptureBlocker = FalloutActorPendingSandboxStart.CaptureBlocker,
            };
            pendingActor.Animation.Restore(new("meshes/fixture/idle.kf", new('a', 64), 7.25, false));
            var pendingStart = new FalloutActorPendingSandboxStart(Key(0x900), Key(0x10), Hash(0x10),
                Key(0x20), Hash(0x20), [1, 0, 0, 0, 1, 0, 0, 0, 1, 3, 4, 5],
                123, .25, new(1, 10, 3, 9), 4, 0, new(false, false, false, false, false, false, 0),
                live.Revision, live.LastEvent!, live.LastPackage!.Value, []);
            pendingStart.Validate(records, pendingActor);
            pendingActor.PendingSandboxStart = pendingStart;
            var pendingSnapshot = pendingActor.Capture();
            FalloutReferenceSnapshot.Validate([pendingSnapshot]);
            var pendingCold = JsonSerializer.Deserialize<FalloutActorPendingSandboxStart>(JsonSerializer.Serialize(pendingStart))!;
            var pendingEvents = new FalloutPackageEvents(Dispatch);
            pendingCold.RestoreEvents(records, pendingActor, pendingEvents);
            pendingCold.RestoreEvents(records, pendingActor, pendingEvents);
            Require(effects == 1 && pendingEvents.Revision == live.Revision &&
                pendingEvents.LastEvent == "POBA" && pendingActor.PackageMotion is null &&
                pendingActor.Animation.ElapsedSeconds == 7.25 && pendingCold.RandomState == 123,
                "Pending Sandbox cold binding replayed Start, reset its clock or fabricated motion.");
            var pendingCopy = pendingStart.Copy(); pendingCopy.Pose[9]++;
            Require(pendingStart.Pose[9] == 3, "Pending Sandbox pose aliases a copied receipt.");
            var untouchedEvents = new FalloutPackageEvents(Dispatch);
            Reject(() => (pendingStart with { PackageSha256 = new('0', 64) }).RestoreEvents(records, pendingActor, untouchedEvents));
            Require(untouchedEvents.Revision == 0 && effects == 1, "Rejected pending Sandbox source mutated events.");
            Reject(() => (pendingStart with { EventRevision = 0 }).Validate());
            Reject(() => pendingStart.ValidateShape(pendingSnapshot with { ScriptError = "independent source failure" }));
            var currentPending = pendingStart with { Package = Key(0x24), PackageSha256 = Hash(0x24), LastPackage = Key(0x24) };
            pendingActor.PackageAssignment = new(Key(0x24), Hash(0x24), false);
            Reject(() => currentPending.Validate(records, pendingActor));
            pendingActor.PackageStartLocation = new(Key(0x800), [10, 20, 30]);
            currentPending.Validate(records, pendingActor);
            pendingActor.PackageStartLocation = null;
            pendingActor.PackageAssignment = new(Key(0x20), Hash(0x20), false);
            using (var activeWorld = new FalloutReferenceWorld(records))
            {
                var active = activeWorld.Get(Key(0x900));
                active.PackageAssignment = new(Key(0x24), Hash(0x24), false);
                active.PackageStartLocation = new(Key(0x800), [10, 20, 30]);
                active.PackageMotion = new(Key(0x24), Hash(0x24), "meshes/fixture/idle.kf", new('a', 64), 1, false,
                    [0, 0, 0], [0, 0, 0, 1], Sandbox: new(FalloutSandboxActivity.Wander, 9, 1,
                        new FalloutSandboxPreferences().Capture()));
                var activeSnapshot = activeWorld.Capture();
                using var activeCold = new FalloutReferenceWorld(records);
                activeCold.Restore(activeSnapshot);
                Require(activeCold.Get(Key(0x900)).PackageStartLocation!.Position.SequenceEqual([10f, 20f, 30f]),
                    "Active Sandbox lost its actor search center during cold restore.");
                using var missingCenter = new FalloutReferenceWorld(records);
                Reject(() => missingCenter.Restore(activeSnapshot.Select(value => value.Reference == Key(0x900)
                    ? value with { PackageStartLocation = null } : value).ToArray()));
                Require(missingCenter.InstanceCount == 0, "Missing Sandbox center partially restored the world.");
            }
            var saved = new FalloutActorSandboxAdmissionFailure(Key(0x20), Hash(0x20),
                FalloutActorSandboxAdmissionFailure.AdmissionErrorPrefix + "IdleMarker reference=Admission.esm:000901 base=Admission.esm:000030 inventory=False",
                [3, 4, 5], [1, 0, 0, 0, 1, 0, 0, 0, 1],
                0x123456789abcdef0, .25, new(1, 10, 3, 9), 4, 2,
                new(false, true, false, false, false, false, 2), live.Revision, live.LastEvent!,
                live.LastPackage!.Value, Hash(0x20));
            saved.Validate(records, actor);
            using (var mealWorld = new FalloutReferenceWorld(records))
            {
                var hungry = mealWorld.Get(Key(0x900));
                hungry.Animation.Restore(new("meshes/fixture/idle.kf", new('a', 64), 12.5, false));
                hungry.PackageAssignment = new(saved.Package, saved.PackageSha256, false);
                hungry.Inventory = new(55);
                hungry.Inventory.Contents.AddGrant(new(new([
                    new(Key(0x40), records.RuntimeFormId(Key(0x40)), "FirstFood", "INGR", 1, null, null),
                    new(Key(0x41), records.RuntimeFormId(Key(0x41)), "SecondFood", "INGR", 1, null, null)], null), []));
                var randomBefore = hungry.SoundRandom.State;
                _ = hungry.SoundRandom.NextUInt32(); _ = hungry.SoundRandom.NextUnitFloat();
                var afterSelection = hungry.SoundRandom.State;
                var mealPrefix = new FalloutSandboxUnchosenMeal(new(FalloutSandboxActivity.Eat, 9.25f, 1.75f,
                    new FalloutSandboxPreferences().Capture(), DailyFoodAvailable: true),
                    [new(Key(0x40), Hash(0x40), 1), new(Key(0x41), Hash(0x41), 1)], true,
                    randomBefore, afterSelection, afterSelection);
                var stoppedMeal = saved with { Error = mealPrefix.Error, UnchosenMeal = mealPrefix };
                stoppedMeal.Validate(records, hungry);
                hungry.SandboxAdmissionFailure = stoppedMeal;
                hungry.ProcedureCaptureBlocker = FalloutSandboxPackage.CaptureBlockerPrefix + stoppedMeal.Error;
                var inventoryBefore = JsonSerializer.Serialize(hungry.Inventory.Capture());
                var bytes = JsonSerializer.Serialize(mealWorld.Capture());
                using var coldMealWorld = new FalloutReferenceWorld(records);
                coldMealWorld.Restore(JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(bytes)!);
                var coldHungry = coldMealWorld.Get(hungry.Reference);
                var coldMeal = coldHungry.SandboxAdmissionFailure!;
                var mealEvents = new FalloutPackageEvents(Dispatch);
                coldMeal.RestoreEvents(records, coldHungry, mealEvents);
                Require(JsonSerializer.Serialize(coldMealWorld.Capture()) == bytes && effects == 1 &&
                    mealEvents.Revision == live.Revision && mealEvents.LastEvent == "POBA" && !mealEvents.Done &&
                    coldHungry.PackageMotion is null && coldHungry.Animation.ElapsedSeconds == 12.5 &&
                    coldHungry.SoundRandom.State == afterSelection && coldMeal.UnchosenMeal!.Activity.Duration == 1.75f &&
                    JsonSerializer.Serialize(coldHungry.Inventory!.Capture()) == inventoryBefore,
                    "Unchosen meal restoration lost its failure prefix or selected/consumed food, replayed POBA or changed clock/RNG.");
                var mealCopy = stoppedMeal.Copy(); mealCopy.UnchosenMeal!.Activity.Preferences[0]++;
                Require(mealCopy.UnchosenMeal.Activity.Preferences[0] != stoppedMeal.UnchosenMeal!.Activity.Preferences[0],
                    "Unchosen meal receipt aliases selected preferences.");
                Reject(() => (stoppedMeal with { UnchosenMeal = null }).Validate());
                Reject(() => (stoppedMeal with { UnchosenMeal = mealPrefix with { SelectedActivity = false } }).Validate());
                Reject(() => (stoppedMeal with { UnchosenMeal = mealPrefix with { RandomAfterFailure = afterSelection + 1 } }).Validate());
                Reject(() => (stoppedMeal with { UnchosenMeal = mealPrefix with
                { RandomBeforeMeal = afterSelection + 1, RandomAfterFailure = afterSelection + 1 } }).Validate(records, hungry));
                Reject(() => (stoppedMeal with { UnchosenMeal = mealPrefix with
                { Inputs = [mealPrefix.Inputs[0] with { Sha256 = new('0', 64) }, mealPrefix.Inputs[1]] } }).Validate(records, hungry));
                Reject(() => (stoppedMeal with { UnchosenMeal = mealPrefix with
                { Activity = mealPrefix.Activity with { Target = Key(0x900) } } }).Validate());
                hungry.ActorValues["health"] = new FalloutActorValue(10, Damage: -10);
                hungry.Injury = new(true, null, new Dictionary<byte, float>());
                hungry.Ragdoll = new(new('c', 64), [new(7, [1,0,0,0,1,0,0,0,1,9,8,7], [0,0,0], [0,0,0], true)]);
                _ = hungry.SoundRandom.NextUInt32();
                hungry.Inventory = new(55);
                hungry.Inventory.Contents.AddGrant(new(new([
                    new(Key(0x41), records.RuntimeFormId(Key(0x41)), "SecondFood", "INGR", 1, null, null)], null), []));
                Reject(() => stoppedMeal.Validate(records, hungry));
                Require(FalloutActorSandboxAdmissionFailure.RetainInterrupted(records, hungry, stoppedMeal,
                    stoppedMeal.Package, stoppedMeal.Error, live, true) && hungry.SandboxAdmissionFailure is null &&
                    hungry.InterruptedSandboxAdmission is { UnchosenMeal.Inputs.Count: 2 },
                    "A matching captured meal prefix depended on the corpse's later inventory/random state.");
                var historicalBytes = JsonSerializer.Serialize(mealWorld.Capture());
                using var historicalCold = new FalloutReferenceWorld(records);
                historicalCold.Restore(JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(historicalBytes)!);
                var historicalActor = historicalCold.Get(hungry.Reference);
                var history = historicalActor.InterruptedSandboxAdmission!;
                Require(JsonSerializer.Serialize(historicalCold.Capture()) == historicalBytes && effects == 1 &&
                    historicalActor.SandboxAdmissionFailure is null && historicalActor.PackageMotion is null &&
                    historicalActor.Inventory!.Contents.Items.Count == 1 && historicalActor.SoundRandom.State != afterSelection &&
                    history.UnchosenMeal!.Inputs.Count == 2 && history.UnchosenMeal!.RandomAfterFailure == afterSelection,
                    "Cold corpse history changed its original unchosen prefix, current loot/randomness or replayed ingestion/events.");
                Reject(() => history.RestoreEvents(records, historicalActor, new FalloutPackageEvents(Dispatch)));
                Reject(() => (history with { UnchosenMeal = history.UnchosenMeal! with
                { Inputs = [mealPrefix.Inputs[0] with { Sha256 = new('0', 64) }, mealPrefix.Inputs[1]] } })
                    .ValidateInterrupted(records, historicalActor));
                Console.WriteLine("OPENNV_SANDBOX_UNCHOSEN_MEAL_PASS consumedActivity=true exactFoodInputs=true failureVisible=true cold=true noChoiceOrIngestion=true noStartReplay=true clockAndRandomRetained=true corpseHistoryIndependentOfCurrentInputs=true historicalResumeRefused=true");
            }
            var currentAdmission = saved with { Package = Key(0x24), PackageSha256 = Hash(0x24),
                LastPackage = Key(0x24), LastPackageSha256 = Hash(0x24) };
            Reject(() => currentAdmission.Validate(records, actor));
            actor.PackageStartLocation = new(Key(0x800), [10, 20, 30]);
            currentAdmission.Validate(records, actor);
            actor.PackageStartLocation = null;
            using (var interruptedWorld = new FalloutReferenceWorld(records))
            {
                var dead = interruptedWorld.Get(Key(0x900));
                dead.Animation.Restore(new("meshes/fixture/idle.kf", new('a', 64), 12.5, false));
                dead.PackageAssignment = new(saved.Package, saved.PackageSha256, false);
                dead.SandboxAdmissionFailure = saved.Copy();
                dead.ProcedureCaptureBlocker = FalloutSandboxPackage.CaptureBlockerPrefix + saved.Error;
                dead.ActorValues["health"] = new FalloutActorValue(10, Damage: -10);
                dead.Injury = new(true, null, new Dictionary<byte, float>());
                dead.Ragdoll = new(new('c', 64), [new(7, [1,0,0,0,1,0,0,0,1,9,8,7], [0,0,0], [0,0,0], true)]);
                var receipt = dead.SandboxAdmissionFailure;
                Require(!FalloutActorSandboxAdmissionFailure.RetainInterrupted(records, dead, receipt, saved.Package,
                    saved.Error, live, false) && ReferenceEquals(dead.SandboxAdmissionFailure, receipt),
                    "An unready corpse consumed its prior Sandbox failure.");
                Require(!FalloutActorSandboxAdmissionFailure.RetainInterrupted(records, dead, receipt, saved.Package,
                    "different source failure", live, true) && ReferenceEquals(dead.SandboxAdmissionFailure, receipt),
                    "A different live failure consumed its prior Sandbox receipt.");
                Require(FalloutActorSandboxAdmissionFailure.RetainInterrupted(records, dead, receipt, saved.Package,
                    saved.Error, live, true) && dead.SandboxAdmissionFailure is null &&
                    dead.InterruptedSandboxAdmission is not null && interruptedWorld.PendingProcedureCaptureCount == 0,
                    "Matching death did not preserve the earlier stopped source failure as history.");
                var retained = dead.Capture();
                FalloutReferenceSnapshot.Validate([retained]);
                Require(retained.InterruptedSandboxAdmission!.Position.SequenceEqual(saved.Position) &&
                    retained.Ragdoll!.Bodies[0].Transform[9] == 9 && retained.SandboxAdmissionFailure is null,
                    "Historical failure replaced the actual corpse pose or became a live Sandbox continuation.");
                Reject(() => FalloutReferenceSnapshot.Validate([retained with { Injury = retained.Injury! with { Dead = false } }]));
                Reject(() => FalloutReferenceSnapshot.Validate([retained with { Ragdoll = null }]));
                var serialized = JsonSerializer.Serialize(interruptedWorld.Capture());
                using var interruptedCold = new FalloutReferenceWorld(records);
                interruptedCold.Restore(JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(serialized)!);
                Require(JsonSerializer.Serialize(interruptedCold.Capture()) == serialized &&
                    interruptedCold.Get(dead.Reference).InterruptedSandboxAdmission is not null && effects == 1,
                    "Cold restore changed the corpse/history or replayed its package start.");
            }
            var creature = new FalloutReferenceInstance(records.GetEffective(Key(0x902)), null);
            creature.Animation.Restore(new("meshes/fixture/idle.kf", new('a', 64), 12.5, false));
            using (var pendingWorld = new FalloutReferenceWorld(records))
            {
                var pendingCreature = pendingWorld.Get(Key(0x902));
                pendingCreature.Animation.Restore(new("meshes/fixture/idle.kf", new('a', 64), 12.5, false));
                pendingCreature.PackageAssignment = new(Key(0x24), Hash(0x24), false);
                pendingCreature.PackageStartLocation = new(Key(0x800), [10, 20, 30]);
                pendingCreature.ProcedureCaptureBlocker = FalloutActorPendingSandboxStart.CaptureBlocker;
                var pendingCreatureStart = currentPending with
                {
                    Reference = pendingCreature.Reference, PriorityOwner = Key(0x11), PrioritySha256 = Hash(0x11),
                    RandomState = pendingCreature.SoundRandom.State, SelectionAttempts = 2,
                };
                pendingCreatureStart.Validate(records, pendingCreature);
                Reject(() => (pendingCreatureStart with { SelectionAttempts = null }).Validate(records, pendingCreature));
                Reject(() => (pendingCreatureStart with { RandomState = pendingCreatureStart.RandomState + 1 }).Validate(records, pendingCreature));
                pendingCreature.PendingSandboxStart = pendingCreatureStart;
                var creatureSnapshots = pendingWorld.Capture();
                var campaign = JsonSerializer.Deserialize<FalloutNativeCampaignState>(
                    "{\"Schema\":\"opennv-native-fnv-campaign-save/v64\",\"References\":[]}")!;
                Reject(() => FalloutNativeCampaignSave.AdmitPreviousOwnerSchema(campaign with { References = creatureSnapshots }));
                var previousWithoutCreature = FalloutNativeCampaignSave.AdmitPreviousOwnerSchema(campaign);
                Require(previousWithoutCreature.Schema == FalloutNativeCampaignSave.ExpectedSchema,
                    "Previous campaign schema did not migrate without inventing a creature continuation.");
                var bytes = JsonSerializer.Serialize(creatureSnapshots);
                using var pendingColdWorld = new FalloutReferenceWorld(records);
                pendingColdWorld.Restore(JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(bytes)!);
                var coldCreature = pendingColdWorld.Get(pendingCreature.Reference);
                var coldEvents = new FalloutPackageEvents(Dispatch);
                coldCreature.PendingSandboxStart!.RestoreEvents(records, coldCreature, coldEvents);
                Require(effects == 1 && coldEvents.Revision == pendingCreatureStart.EventRevision &&
                    coldCreature.PendingSandboxStart.SelectionAttempts == 2 && coldCreature.PackageMotion is null &&
                    coldCreature.Animation.ElapsedSeconds == 12.5 && coldCreature.PackageStartLocation!.Position.SequenceEqual([10f, 20f, 30f]),
                    "Creature pending Sandbox cold restore replayed POBA or lost its source state.");
                pendingWorld.SetActorAlerted(pendingCreature.Reference, true);
                var alerted = pendingCreature.PendingSandboxStart!;
                Require(alerted.Activity.Alerted && alerted.Activity.Revision == pendingCreatureStart.Activity.Revision + 1 &&
                    alerted.ActivityRevision == alerted.Activity.Revision && pendingWorld.ActorAlerted(pendingCreature.Reference),
                    "An unloaded creature pending Sandbox lost its shared alert transition.");
            }
            var creatureEffects = 0;
            var creatureLive = new FalloutPackageEvents((_, kind) => { if (kind == "POBA") creatureEffects++; });
            creatureLive.Change(FalloutScriptPackage.Read(records.GetEffective(Key(0x20))));
            var creatureSaved = saved with
            {
                Error = FalloutActorSandboxAdmissionFailure.AdmissionErrorPrefix + "Eat reference=Admission.esm:000901 base=Admission.esm:000030 inventory=False",
                AiRandomState = creature.SoundRandom.State,
                SelectionAttempts = 1,
                ActivityRevision = saved.Activity.Revision,
                EventRevision = creatureLive.Revision,
                LastEvent = creatureLive.LastEvent!,
                LastPackage = creatureLive.LastPackage!.Value,
            };
            creatureSaved.Validate(records, creature);
            Reject(() => (creatureSaved with { SelectionAttempts = null }).Validate(records, creature));
            Reject(() => (creatureSaved with { AiRandomState = creatureSaved.AiRandomState + 1 }).Validate(records, creature));
            creature.SandboxAdmissionFailure = creatureSaved;
            creature.ProcedureCaptureBlocker = FalloutSandboxPackage.CaptureBlockerPrefix + creatureSaved.Error;
            var creatureSnapshot = creature.Capture();
            FalloutReferenceSnapshot.Validate([creatureSnapshot]);
            ActorAlertContracts.RunStoppedCreature(records, creatureSnapshot);
            var creatureCold = JsonSerializer.Deserialize<FalloutActorSandboxAdmissionFailure>(JsonSerializer.Serialize(creatureSaved))!;
            var creatureEvents = new FalloutPackageEvents((_, kind) => { if (kind == "POBA") creatureEffects++; });
            creatureCold.RestoreEvents(records, creature, creatureEvents);
            Require(creatureEffects == 1 && creatureEvents.Revision == creatureLive.Revision &&
                creatureEvents.LastEvent == "POBA" && creature.PackageMotion is null &&
                creature.Animation.ElapsedSeconds == 12.5 && creatureCold.AiRandomState == creatureSaved.AiRandomState,
                "Creature admission restoration replayed Start or fabricated an activity, motion or base clock.");
            var motionActor = new FalloutReferenceInstance(records.GetEffective(Key(0x902)), null);
            motionActor.Animation.Restore(creature.Animation.Capture()!);
            var motionEffects = 0;
            var motionLive = new FalloutPackageEvents((_, kind) => { if (kind == "POBA") motionEffects++; });
            motionLive.Change(FalloutScriptPackage.Read(records.GetEffective(Key(0x20))));
            var acceptedActivity = new FalloutSandboxActivityProgress(FalloutSandboxActivity.Wander, 9, 1,
                new FalloutSandboxPreferences().Capture());
            var acceptedWander = new FalloutWanderProgress(Key(0x800), new('a', 64), [5, 6, 7], false);
            var motionFailure = new FalloutActorSandboxMotionFailure(motionActor.Reference, motionActor.Base, Hash(0x11),
                Key(0x11), Hash(0x11), Key(0x20), Hash(0x20), FalloutActorZeroTurnException.SourceError,
                new(acceptedActivity, acceptedWander, true, true, 1, 2, 2),
                [3, 4, 5], [1, 0, 0, 0, 1, 0, 0, 0, 1], [0, 0, 0],
                new("meshes/fixture/skeleton.nif", new('a', 64), []), motionActor.Animation.Capture()!, null,
                motionActor.SoundRandom.State, saved.Activity, .25, saved.ScheduleTime, 4, 1,
                motionLive.Revision, motionLive.LastEvent!, motionLive.LastPackage!.Value);
            motionFailure.Validate(records, motionActor);
            var currentMotionFailure = motionFailure with { Package = Key(0x24), PackageSha256 = Hash(0x24), LastPackage = Key(0x24) };
            Reject(() => currentMotionFailure.Validate(records, motionActor));
            motionActor.PackageStartLocation = new(Key(0x800), [10, 20, 30]);
            currentMotionFailure.Validate(records, motionActor);
            motionActor.PackageStartLocation = null;
            var motionCold = JsonSerializer.Deserialize<FalloutActorSandboxMotionFailure>(JsonSerializer.Serialize(motionFailure))!;
            var motionEvents = new FalloutPackageEvents((_, kind) => { if (kind == "POBA") motionEffects++; });
            motionCold.RestoreEvents(records, motionActor, motionEvents);
            Require(motionEffects == 1 && motionEvents.Revision == motionLive.Revision && motionActor.PackageMotion is null &&
                JsonSerializer.Serialize(motionCold.Prefix) == JsonSerializer.Serialize(motionFailure.Prefix) &&
                motionActor.Animation.ElapsedSeconds == 12.5,
                "Stopped zero-turn restoration replayed selection, Start, motion or the base clock.");
            Reject(() => (motionFailure with { Prefix = motionFailure.Prefix with { RandomAfterFailure = 3 } }).Validate());
            var turningActor = new FalloutReferenceInstance(records.GetEffective(Key(0x903)), null);
            turningActor.Animation.Restore(motionActor.Animation.Capture()!);
            var wrongTurn = motionFailure with
            {
                Reference = turningActor.Reference,
                Base = turningActor.Base,
                BaseSha256 = Hash(0x12),
                ModelOwner = Key(0x12),
                ModelOwnerSha256 = Hash(0x12),
                SoundRandomState = turningActor.SoundRandom.State,
            };
            var refusedTurnEvents = new FalloutPackageEvents((_, _) => motionEffects++);
            Reject(() => wrongTurn.RestoreEvents(records, turningActor, refusedTurnEvents));
            Require(refusedTurnEvents.Revision == 0 && motionEffects == 1,
                "A nonzero source turn rate admitted a zero-turn receipt or dispatched its lifecycle.");
            var cold = JsonSerializer.Deserialize<FalloutActorSandboxAdmissionFailure>(JsonSerializer.Serialize(saved))!;
            var restored = new FalloutPackageEvents(Dispatch);
            cold.RestoreEvents(records, actor, restored);
            Require(effects == 1 && restored.Active?.Form == saved.Package && !restored.Done &&
                restored.Error is null && restored.Revision == live.Revision && restored.LastEvent == live.LastEvent &&
                restored.LastPackage == live.LastPackage && JsonSerializer.Serialize(cold) == JsonSerializer.Serialize(saved),
                "Cold admission restore replayed POBA or changed the stopped state.");
            var copy = saved.Copy(); copy.Position[0]++; copy.Basis[0]++;
            Require(saved.Position[0] == 3 && saved.Basis[0] == 1, "Admission copy aliases its pose arrays.");
            foreach (var invalid in new[]
            {
                saved with { PackageSha256 = new('0', 64), LastPackageSha256 = new('0', 64) },
                saved with { LastPackageSha256 = new('0', 64) }, saved with { LastPackage = Key(0x21) },
                saved with { EventRevision = 0 }, saved with { EventRevision = long.MaxValue },
                saved with { LastEvent = "POCA" }, saved with { LastEvent = "POEA" },
                saved with { Error = "" }, saved with { Basis = new float[9] },
                saved with { SourcePlacement = new(Key(0x800), [float.NaN, 0, 0], [0, 0, 0]) },
                saved with { SourcePlacement = new(Key(0x800), [0, 0, 0], [0, 0, 0]) },
                saved with { Error = FalloutActorSandboxAdmissionFailure.AdmissionErrorPrefix },
                saved with { Error = "Sandbox location needs an other-region approach owner." },
                saved with { Activity = saved.Activity with { Revision = -1 } },
                saved with { Package = Key(0x21), PackageSha256 = Hash(0x21), LastPackage = Key(0x21), LastPackageSha256 = Hash(0x21) }
            }) RejectFresh(invalid);

            actor.PackageMotion = new(Key(0x20), Hash(0x20), "meshes/fixture/idle.kf", new('a', 64),
                0, false, [0, 0, 0], [0, 0, 0]);
            RejectFresh(saved); actor.PackageMotion = null;
            actor.PackageStarts.Add(new(Key(0x20), Hash(0x20), 1));
            actor.PackageAssignment = new(Key(0x20), Hash(0x20), false);
            var assigned = new FalloutPackageEvents(Dispatch);
            cold.RestoreEvents(records, actor, assigned);
            Require(assigned.Active?.Form == saved.Package && effects == 1 && actor.PackageStarts.Count == 1,
                "Matching active assignment or prior package-start history was lost during restore.");
            actor.PackageAssignment = new(Key(0x20), Hash(0x20), true);
            RejectFresh(saved);
            actor.PackageAssignment = new(Key(0x21), Hash(0x21), false);
            RejectFresh(saved); actor.PackageAssignment = null;
            actor.PackageIdle = new(Key(0x20), Hash(0x20), "POBA", Key(0x30), new('a', 64),
                "meshes/fixture/idle.kf", new('b', 64), new(0, 0, 0, 0, false, false));
            RejectFresh(saved); actor.PackageIdle = null;
            Reject(() => saved.RestoreEvents(records, actor, restored));
            Require(restored.Revision == 1 && effects == 1, "Occupied event owner was partially overwritten.");
            var faulted = new FalloutPackageEvents((_, _) => throw new InvalidDataException("POBA fault"));
            Reject(() => faulted.Change(FalloutScriptPackage.Read(records.GetEffective(Key(0x20)))));
            Reject(() => saved.RestoreEvents(records, actor, faulted));
            Require(faulted.Error == "POBA fault" && faulted.Revision == 1, "Faulted event history was overwritten.");
            using var world = new FalloutReferenceWorld(records);
            var worldActor = world.Get(Key(0x900));
            worldActor.Animation.Change("meshes/fixture/mtidle.kf", new('a', 64));
            worldActor.Animation.Advance(6.25);
            worldActor.SoundRandom.Restore(0x1122334455667788);
            worldActor.ConversationHistory = new(12.5f, [worldActor.Reference]);
            worldActor.ConversationSuppressionSeconds = 3.75f;
            worldActor.PackageAssignment = new(saved.Package, saved.PackageSha256, false);
            worldActor.ProcedureCaptureBlocker = FalloutSandboxPackage.CaptureBlockerPrefix + saved.Error;
            worldActor.CanCaptureSandboxAdmissionFailure = () => true;
            var spatialSaved = saved with { Basis = [0, 0, 1, 0, 1, 0, -1, 0, 0] };
            worldActor.CaptureSandboxAdmissionFailure = () => spatialSaved.Copy();
            var snapshotJson = JsonSerializer.Serialize(world.Capture());
            var snapshots = JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(snapshotJson)!;
            using var coldWorld = new FalloutReferenceWorld(records);
            coldWorld.Restore(snapshots);
            Require(JsonSerializer.Serialize(coldWorld.Capture()) == snapshotJson,
                "World cold restore changed base animation, shared RNG, conversation history or admission state.");
            var resumed = coldWorld.Get(worldActor.Reference);
            var playerPose = new FalloutReferencePlacement(Key(0x800), [13, -5, 4], [0, 0, 0]);
            var stoppedPose = coldWorld.SpawnPlacement(resumed.Reference, playerPose, 1);
            Require(stoppedPose.Position.SequenceEqual(new float[] { 3, -5, 4 }) &&
                Math.Abs(stoppedPose.RotationRadians[2] - MathF.PI / 2) < .0001f &&
                Math.Abs(coldWorld.Distance(resumed.Reference, records.RuntimeFormKey(0x14), playerPose, 1) - 10) < .0001f &&
                Math.Abs(coldWorld.HeadingAngle(resumed.Reference, records.RuntimeFormKey(0x14), playerPose, 1)) < .0001f,
                "Offcell distance or heading used the authored origin instead of the retained moved and rotated pose.");
            var unloadedExecutions = 0;
            coldWorld.UnloadedPackages = new(records, coldWorld, new(records), null, null,
                (_, _) => unloadedExecutions++, () => 1);
            Require(coldWorld.CurrentPackage(resumed.Reference) == saved.Package && unloadedExecutions == 0 &&
                coldWorld.PendingProcedureCaptureCount == 0 && coldWorld.PendingPackageEventCount == 0,
                "Unloaded admission query reelected or replayed the stopped package.");
            var bound = new FalloutPackageEvents(Dispatch);
            coldWorld.UnloadedPackages.BindNative(resumed.Reference, bound);
            Require(bound.Active is null && bound.Revision == 0,
                "Unloaded binding stole the native stopped-history restoration.");
            resumed.SandboxAdmissionFailure!.RestoreEvents(records, resumed, bound);
            coldWorld.UnloadedPackages.Retain(resumed.Reference, bound);
            Require(coldWorld.CurrentPackage(resumed.Reference) == saved.Package && effects == 1 && unloadedExecutions == 0,
                "Retired stopped actor reelected its active package.");
            var rebound = new FalloutPackageEvents(Dispatch);
            coldWorld.UnloadedPackages.BindNative(resumed.Reference, rebound);
            resumed.SandboxAdmissionFailure.RestoreEvents(records, resumed, rebound);
            Require(rebound.Revision == saved.EventRevision && rebound.LastEvent == "POBA" && !rebound.Done &&
                effects == 1 && unloadedExecutions == 0 && JsonSerializer.Serialize(coldWorld.Capture()) == snapshotJson,
                "Retain-to-native handoff changed the stopped lifecycle or consumed results again.");
            ExerciseRelocation();
            ExerciseDetachedStaging();
            ExercisePlacementAcknowledgment();
            using var rejectedWorld = new FalloutReferenceWorld(records);
            var invalidSnapshots = snapshots.Select(value => value with
            { SandboxAdmissionFailure = saved with { PackageSha256 = new('0', 64), LastPackageSha256 = new('0', 64) } }).ToArray();
            Reject(() => rejectedWorld.Restore(invalidSnapshots));
            Require(rejectedWorld.Capture().Count == 0,
                "Rejected Sandbox source restore partially committed a world.");
            var snapshot = snapshots.Single();
            foreach (var invalid in new[]
            {
                snapshot with { SoundRandomState = null }, snapshot with { Animation = null },
                snapshot with { Unconscious = true }, snapshot with { Restrained = true },
                snapshot with { KnockedDown = true },
                snapshot with { SandboxAdmissionFailure = saved with { Activity = saved.Activity with { InCombat = true } } },
                snapshot with { PackageMotion = new(saved.Package, saved.PackageSha256,
                    "meshes/fixture/mtidle.kf", new('a', 64), 0, false, [0, 0, 0], [0, 0, 0]) },
                snapshot with { PackageBindingFailure = new(saved.Package, saved.PackageSha256,
                    "Pre-begin failure", [0, 0, 0], [1, 0, 0, 0, 1, 0, 0, 0, 1], false, 1, 0, null,
                    new(0, null, null, null)) }
            })
            {
                using var rejected = new FalloutReferenceWorld(records);
                Reject(() => rejected.Restore([invalid]));
                Require(rejected.Capture().Count == 0,
                    "Conflicting Sandbox snapshot partially committed a world.");
            }
            var previous = JsonSerializer.Deserialize<FalloutNativeCampaignState>(JsonSerializer.Serialize(new
            {
                Schema = FalloutNativeCampaignSave.BeforeSandboxAdmissionSchema,
                References = new[] { snapshot with { SandboxAdmissionFailure = null } }
            }))!;
            var upgraded = FalloutNativeCampaignSave.AdmitPreviousOwnerSchema(previous);
            Require(upgraded.Schema == FalloutNativeCampaignSave.ExpectedSchema &&
                ReferenceEquals(upgraded.References, previous.References),
                "Prior schema admission changed existing reference state.");
            Reject(() => FalloutNativeCampaignSave.AdmitPreviousOwnerSchema(previous with { References = [snapshot] }));
            var current = previous with { Schema = FalloutNativeCampaignSave.ExpectedSchema, References = [snapshot] };
            Require(ReferenceEquals(FalloutNativeCampaignSave.AdmitPreviousOwnerSchema(current), current),
                "Current schema admission replaced stopped Sandbox state.");
            Console.WriteLine("OPENNV_SANDBOX_ADMISSION_FAILURE_CONTRACT_PASS countedBeginDispatch=true compiledResultProof=false " +
                "coldWorldRestoreNoReplay=true unloadedHandoff=true sourceAndConflictRejection=true " +
                "atomicEventRestore=true exclusiveSnapshot=true priorSchemaGuard=true relocationCold=true sourceParentTransform=true sharedAlertRestore=true detachedStagingNoReplay=true placementAcknowledged=true laterFacingCold=true gameplay=unverified");

            void ExercisePlacementAcknowledgment()
            {
                using var owner = new FalloutReferenceWorld(records);
                owner.Restore(snapshots);
                var target = owner.Get(worldActor.Reference);
                var destination = new FalloutReferencePlacement(Key(0x801), [25, 30, 7], [0, 0, MathF.PI / 2]);
                owner.SetPlacement(target.Reference, destination);
                var pending = target.SandboxAdmissionFailure!;
                var intended = pending.ResolveTransform(Transform3D.Identity, .02f, 1);
                var stalePose = new FalloutReferencePlacement(Key(0x800), [3, -5, 4], [0, 0, 0]);
                target.QuerySpatialPlacement = () => stalePose;
                var player = new FalloutReferencePlacement(Key(0x801), [35, 30, 7], [0, 0, 0]);
                Require(owner.SpawnPlacement(target.Reference, player, .02f).Position.SequenceEqual(destination.Position),
                    "Pending relocation stopped protecting source queries before publication.");
                var failed = new FalloutSandboxAdmissionStaging(target);
                Reject(() => failed.EnterLive(static () => { }, () => new Transform3D(Basis.Identity, new(float.NaN, 0, 0))));
                Require(!failed.IsLive && ReferenceEquals(target.SandboxAdmissionFailure, pending),
                    "Invalid observed pose consumed the pending source placement or committed staging.");
                var published = new FalloutSandboxAdmissionStaging(target);
                stalePose = destination.Copy();
                published.EnterLive(static () => { }, () => intended);
                published.RequirePublished();
                var publishedReceipt = target.SandboxAdmissionFailure!;
                Require(publishedReceipt.SourcePlacement is null &&
                    publishedReceipt.ResolveTransform(Transform3D.Identity, .02f, 1).IsEqualApprox(intended),
                    "Successful publication retained pending source placement or lost actual native pose.");
                // Native dialogue turning publishes a later world basis. This
                // managed receipt check does not claim an actual Look fixture.
                var turned = new Transform3D(GamebryoCoordinate.ConvertReferenceEuler(new(0, 0, -.4f), 1), intended.Origin);
                var later = publishedReceipt.PublishPose(turned);
                target.SandboxAdmissionFailure = later;
                target.QuerySpatialPlacement = null;
                published.EnterLive(() => throw new InvalidDataException("Repeated publication reapplied the old placement."), () => intended);
                Require(ReferenceEquals(target.SandboxAdmissionFailure, later) &&
                    !FalloutActorSandboxAdmissionFailure.AcknowledgePlacement(target, pending, intended, intended),
                    "Repeated acknowledgment or publication replaced the later actual pose.");
                var expectedHeading = FalloutHeadingAngle.Between(destination.Position, player.Position, -.4f);
                var laterPose = owner.SpawnPlacement(target.Reference, player, .02f);
                Require(Math.Abs(laterPose.RotationRadians[2] + .4f) < .0001f &&
                    Math.Abs(owner.HeadingAngle(target.Reference, records.RuntimeFormKey(0x14), player, .02f) - expectedHeading) < .0001f,
                    "Evicted stopped actor returned its earlier placement facing after a later basis publication.");
                var coldJson = JsonSerializer.Serialize(owner.Capture());
                using var next = new FalloutReferenceWorld(records);
                next.Restore(JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(coldJson)!);
                var retained = next.Get(target.Reference).SandboxAdmissionFailure!;
                Require(retained.SourcePlacement is null && retained.ResolveTransform(Transform3D.Identity, .02f, 1).IsEqualApprox(turned) &&
                    Math.Abs(next.SpawnPlacement(target.Reference, player, .02f).RotationRadians[2] + .4f) < .0001f &&
                    JsonSerializer.Serialize(retained with { Position = pending.Position, Basis = pending.Basis, SourcePlacement = pending.SourcePlacement }) ==
                    JsonSerializer.Serialize(pending),
                    "Save/cold restored the earlier source heading or changed gameplay history/RNG during pose acknowledgment.");
                owner.SetPlacement(target.Reference, destination);
                var livePending = target.SandboxAdmissionFailure!;
                Reject(() => FalloutActorSandboxAdmissionFailure.AcknowledgePlacement(target, livePending, intended, turned));
                Require(ReferenceEquals(target.SandboxAdmissionFailure, livePending),
                    "Failed already-live readback consumed pending placement.");
                Require(FalloutActorSandboxAdmissionFailure.AcknowledgePlacement(target, livePending, intended, intended),
                    "Already-live native placement did not acknowledge its observed pose.");
                var acknowledged = target.SandboxAdmissionFailure;
                Require(!FalloutActorSandboxAdmissionFailure.AcknowledgePlacement(target, livePending, intended, intended) &&
                    ReferenceEquals(target.SandboxAdmissionFailure, acknowledged),
                    "Repeated already-live acknowledgment was not idempotent.");
                owner.SetPlacement(target.Reference, new(Key(0x801), [30, 30, 7], [0, 0, 0]));
                var replacement = target.SandboxAdmissionFailure;
                Reject(() => FalloutActorSandboxAdmissionFailure.AcknowledgePlacement(target, livePending, intended, intended));
                Require(ReferenceEquals(target.SandboxAdmissionFailure, replacement) && replacement!.SourcePlacement is not null && effects == 1,
                    "Stale already-live acknowledgment erased a newer authoritative placement.");
            }

            void ExerciseDetachedStaging()
            {
                foreach (var afterEvents in new[] { false, true })
                {
                    using var stagedWorld = new FalloutReferenceWorld(records);
                    stagedWorld.Restore(snapshots);
                    stagedWorld.UnloadedPackages = new(records, stagedWorld, new(records), null, null,
                        (_, _) => throw new InvalidDataException("Staging dispatched an unloaded package result."), () => 1);
                    var stagedActor = stagedWorld.Get(worldActor.Reference);
                    var before = JsonSerializer.Serialize(stagedWorld.Capture());
                    var staging = new FalloutSandboxAdmissionStaging(stagedActor);
                    Require(!staging.IsLive && staging.PublicationError is null,
                        "Detached staging became capture-admitted before publication.");
                    Reject(staging.RequirePublished);
                    var emptyOrRestored = new FalloutPackageEvents(Dispatch);
                    try
                    {
                        stagedWorld.UnloadedPackages.BindNative(stagedActor.Reference, emptyOrRestored);
                        if (afterEvents)
                        {
                            var activity = new FalloutActorActivityState();
                            stagedWorld.BindActorAlert(stagedActor.Reference, activity);
                            stagedActor.SandboxAdmissionFailure!.RestoreActivity(records, stagedActor, activity);
                            stagedActor.SandboxAdmissionFailure.RestoreEvents(records, stagedActor, emptyOrRestored);
                        }
                        // The production staging boundary owns a private clock;
                        // even initialization work cannot advance the retained one.
                        staging.Clock.Advance(1.5);
                        throw new IOException("Injected detached materialization failure.");
                    }
                    catch (IOException)
                    {
                        Require(!staging.Retire(() => stagedWorld.UnloadedPackages.Retain(stagedActor.Reference, emptyOrRestored)),
                            "Detached failed construction published its empty or partially restored lifecycle.");
                    }
                    Require(JsonSerializer.Serialize(stagedWorld.Capture()) == before &&
                        stagedActor.PackageAssignment?.Package == saved.Package &&
                        stagedActor.SandboxAdmissionFailure!.EventRevision == saved.EventRevision && effects == 1,
                        "Failed detached staging changed the complete retained world snapshot or consumed POBA.");
                    var retry = new FalloutPackageEvents(Dispatch);
                    stagedWorld.UnloadedPackages.BindNative(stagedActor.Reference, retry);
                    stagedActor.SandboxAdmissionFailure!.RestoreEvents(records, stagedActor, retry);
                    Require(retry.Active?.Form == saved.Package && retry.Revision == saved.EventRevision &&
                        retry.LastEvent == "POBA" && effects == 1,
                        "Retry after detached failure began the stopped package again.");
                    var live = new FalloutSandboxAdmissionStaging(stagedActor);
                    live.EnterLive();
                    live.RequirePublished();
                    Require(live.Retire(() => stagedWorld.UnloadedPackages.Retain(stagedActor.Reference, retry)) &&
                        stagedWorld.CurrentPackage(stagedActor.Reference) == saved.Package && effects == 1 &&
                        JsonSerializer.Serialize(stagedWorld.Capture()) == before,
                        "Committed live staging skipped real retirement or changed stopped history.");
                }
                foreach (var relocate in new[] { true, false })
                {
                    using var changedWorld = new FalloutReferenceWorld(records);
                    changedWorld.Restore(snapshots);
                    var changedActor = changedWorld.Get(worldActor.Reference);
                    var stale = new FalloutSandboxAdmissionStaging(changedActor);
                    if (relocate)
                        changedWorld.SetPlacement(changedActor.Reference, new(Key(0x801), [20, 30, 7], [0, 0, MathF.PI / 2]));
                    else changedWorld.SetActorAlert(changedActor.Reference, 1);
                    var currentReceipt = changedActor.SandboxAdmissionFailure;
                    var currentWorld = JsonSerializer.Serialize(changedWorld.Capture());
                    var currentOverrides = JsonSerializer.Serialize(changedWorld.CaptureActorOverrides());
                    changedWorld.UnloadedPackages = new(records, changedWorld, new(records), null, null,
                        (_, _) => throw new InvalidDataException("Stale staging dispatched an unloaded package event."), () => 1);
                    Reject(stale.EnterLive);
                    Reject(stale.RequirePublished);
                    Require(!stale.IsLive && stale.PublicationError is not null &&
                        !stale.Retire(() => changedWorld.UnloadedPackages.Retain(changedActor.Reference, new(Dispatch))) &&
                        ReferenceEquals(changedActor.SandboxAdmissionFailure, currentReceipt) &&
                        JsonSerializer.Serialize(changedWorld.Capture()) == currentWorld &&
                        JsonSerializer.Serialize(changedWorld.CaptureActorOverrides()) == currentOverrides && effects == 1,
                        "Stale staging committed or rolled back a newer placement/alert authority.");
                    var fresh = new FalloutSandboxAdmissionStaging(changedActor);
                    var events = new FalloutPackageEvents(Dispatch);
                    changedWorld.UnloadedPackages.BindNative(changedActor.Reference, events);
                    var activity = new FalloutActorActivityState();
                    changedWorld.BindActorAlert(changedActor.Reference, activity);
                    currentReceipt!.RestoreActivity(records, changedActor, activity);
                    currentReceipt.RestoreEvents(records, changedActor, events);
                    fresh.EnterLive();
                    fresh.RequirePublished();
                    Require(fresh.Retire(() => changedWorld.UnloadedPackages.Retain(changedActor.Reference, events)) &&
                        JsonSerializer.Serialize(changedWorld.Capture()) == currentWorld &&
                        JsonSerializer.Serialize(changedWorld.CaptureActorOverrides()) == currentOverrides &&
                        events.LastEvent == "POBA" && events.Revision == saved.EventRevision && effects == 1,
                        "Fresh staging failed to commit the new authority without replaying package history.");
                }
                using var failedWorld = new FalloutReferenceWorld(records);
                failedWorld.Restore(snapshots);
                var failedActor = failedWorld.Get(worldActor.Reference);
                var failedBefore = JsonSerializer.Serialize(failedWorld.Capture());
                var failedPublication = new FalloutSandboxAdmissionStaging(failedActor);
                Reject(() => failedPublication.EnterLive(() => throw new InvalidDataException("Injected native publication failure.")));
                Reject(failedPublication.RequirePublished);
                Reject(failedPublication.EnterLive);
                Require(!failedPublication.IsLive && failedPublication.PublicationError == "Injected native publication failure." &&
                    !failedPublication.Retire(() => throw new InvalidDataException("Uncommitted actor ran live retirement.")) &&
                    JsonSerializer.Serialize(failedWorld.Capture()) == failedBefore && effects == 1,
                    "Publication failure was swallowed, capture-admitted, retried or retired as a live actor.");
            }

            void ExerciseRelocation()
            {
                using var movedWorld = new FalloutReferenceWorld(records);
                movedWorld.Restore(snapshots);
                var movedActor = movedWorld.Get(worldActor.Reference);
                var before = JsonSerializer.Serialize(movedActor.SandboxAdmissionFailure);
                var invalidPlacement = new FalloutReferencePlacement(Key(0x20), [20, 30, 7], [0, 0, 0]);
                Reject(() => movedWorld.SetPlacement(movedActor.Reference, invalidPlacement));
                Require(movedWorld.PlacementRevision == 0 && JsonSerializer.Serialize(movedActor.SandboxAdmissionFailure) == before &&
                    movedActor.Placement is null, "Rejected relocation changed the receipt or authoritative placement.");
                var placement = new FalloutReferencePlacement(Key(0x801), [20, 30, 7], [0, 0, MathF.PI / 2]);
                movedWorld.SetPlacement(movedActor.Reference, placement);
                placement.Position[0] = -999; placement.RotationRadians[2] = 0;
                // A still-resident old actor has not yet published the move.
                movedActor.QuerySpatialPlacement = () => new(Key(0x800), [3, -5, 4], [0, 0, 0]);
                var newPlayer = new FalloutReferencePlacement(Key(0x801), [35, 30, 7], [0, 0, 0]);
                var immediatelyMoved = movedWorld.SpawnPlacement(movedActor.Reference, newPlayer, .02f);
                Require(immediatelyMoved.Cell == Key(0x801) && immediatelyMoved.Position.SequenceEqual(new float[] { 20, 30, 7 }) &&
                    immediatelyMoved.RotationRadians[2] == MathF.PI / 2,
                    "Authoritative SetPlacement lost to a stale live callback or aliased its input.");
                movedActor.QuerySpatialPlacement = null;
                var beforeRefusal = JsonSerializer.Serialize(movedWorld.Capture());
                var priorRevision = movedWorld.PlacementRevision;
                movedActor.CanCaptureSandboxAdmissionFailure = () => false;
                movedActor.CaptureSandboxAdmissionFailure = () => movedActor.SandboxAdmissionFailure!.Copy();
                Reject(() => movedWorld.SetPlacement(movedActor.Reference, new(Key(0x800), [1, 2, 3], [0, 0, 0])));
                Require(movedWorld.PlacementRevision == priorRevision &&
                    JsonSerializer.Serialize(movedActor.SandboxAdmissionFailure) ==
                    JsonSerializer.Serialize(JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(beforeRefusal)!.Single().SandboxAdmissionFailure),
                    "Uncapturable live relocation discarded the stopped owner or changed placement revision.");
                movedActor.CanCaptureSandboxAdmissionFailure = () => true;
                movedWorld.MoveTo(movedActor.Reference, movedActor.Reference, 5, 0, 11);
                movedActor.CanCaptureSandboxAdmissionFailure = null;
                movedActor.CaptureSandboxAdmissionFailure = null;
                var movedPose = movedWorld.SpawnPlacement(movedActor.Reference, newPlayer, .02f);
                Require(movedPose.Cell == Key(0x801) && movedPose.Position.SequenceEqual(new float[] { 25, 30, 7 }) &&
                    Math.Abs(movedWorld.Distance(movedActor.Reference, records.RuntimeFormKey(0x14), newPlayer, .02f) - 10) < .0001f &&
                    Math.Abs(movedWorld.HeadingAngle(movedActor.Reference, records.RuntimeFormKey(0x14), newPlayer, .02f)) < .0001f,
                    "MoveTo did not rebase stopped Sandbox cell, projected position and facing.");
                var receipt = movedActor.SandboxAdmissionFailure!;
                Require(JsonSerializer.Serialize(receipt with { SourcePlacement = null }) == before,
                    "Relocation altered stopped package history, activity, clocks, RNG or original global fallback.");
                var copied = receipt.Copy();
                var copiedPlacement = copied.SourcePlacement!;
                copiedPlacement.Position[0] = -1; copiedPlacement.RotationRadians[2] = 0;
                var retainedPlacement = receipt.SourcePlacement!;
                Require(retainedPlacement.Position[0] == 25 && retainedPlacement.RotationRadians[2] == MathF.PI / 2,
                    "Stopped relocation copy aliases source placement arrays.");
                var parent = new Transform3D(new Basis(Vector3.Up, .4f).Scaled(Vector3.One * 2), new(8, -4, 6));
                var resolved = receipt.ResolveTransform(parent, .02f, .75f);
                var expectedPosition = parent * new Vector3(.5f, .14f, -.6f);
                Require(resolved.Origin.IsEqualApprox(expectedPosition) &&
                    resolved.Basis.X.IsEqualApprox(parent.Basis * new Vector3(0, 0, .75f)) &&
                    resolved.Basis.Y.IsEqualApprox(parent.Basis * new Vector3(0, .75f, 0)) &&
                    resolved.Basis.Z.IsEqualApprox(parent.Basis * new Vector3(-.75f, 0, 0)),
                    "Native relocation assumed unit conversion, root identity or unit reference scale.");
                var movedJson = JsonSerializer.Serialize(movedWorld.Capture());
                using var movedCold = new FalloutReferenceWorld(records);
                movedCold.Restore(JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(movedJson)!);
                var coldActor = movedCold.Get(movedActor.Reference);
                Require(JsonSerializer.Serialize(movedCold.Capture()) == movedJson &&
                    movedCold.SpawnPlacement(coldActor.Reference, newPlayer, .0142875f).Position.SequenceEqual(movedPose.Position) &&
                    coldActor.SandboxAdmissionFailure!.ResolveTransform(parent, .02f, .75f).IsEqualApprox(resolved) &&
                    coldActor.Animation.ElapsedSeconds == movedActor.Animation.ElapsedSeconds &&
                    coldActor.SoundRandom.State == movedActor.SoundRandom.State,
                    "Cold relocation lost source units, native transform, base clock or shared RNG.");
                var noReplay = 0;
                movedCold.UnloadedPackages = new(records, movedCold, new(records), null, null, (_, _) => noReplay++, () => 1);
                var events = new FalloutPackageEvents(Dispatch);
                movedCold.UnloadedPackages.BindNative(coldActor.Reference, events);
                coldActor.SandboxAdmissionFailure!.RestoreEvents(records, coldActor, events);
                Require(noReplay == 0 && effects == 1 && events.Revision == saved.EventRevision && events.LastEvent == "POBA" &&
                    events.Active?.Form == saved.Package && events.LastPackage == saved.Package,
                    "Cold relocated binding replayed begin or discarded consumed events.");
                var wrong = receipt with { SourcePlacement = retainedPlacement with { Cell = Key(0x800) } };
                var fresh = new FalloutPackageEvents(Dispatch);
                Reject(() => wrong.RestoreEvents(records, movedActor, fresh));
                Require(fresh.Active is null && fresh.Revision == 0 && effects == 1,
                    "Mismatched relocation changed event ownership before rejection.");
                var stoppedBeforeAlert = coldActor.SandboxAdmissionFailure!.Copy();
                foreach (var alerted in new[] { true, false })
                {
                    var previousAlertRevision = movedCold.ActorAlertRevision(coldActor.Reference);
                    movedCold.SetActorAlert(coldActor.Reference, alerted ? 1 : 0);
                    var updated = coldActor.SandboxAdmissionFailure!;
                    var activity = new FalloutActorActivityState();
                    movedCold.BindActorAlert(coldActor.Reference, activity);
                    updated.RestoreActivity(records, coldActor, activity);
                    Require(activity.Alerted == alerted && activity.Capture() == updated.Activity &&
                        movedCold.ActorAlertRevision(coldActor.Reference) == previousAlertRevision + 1 &&
                        JsonSerializer.Serialize(updated with { Activity = stoppedBeforeAlert.Activity }) == JsonSerializer.Serialize(stoppedBeforeAlert) &&
                        updated.Activity.Revision == stoppedBeforeAlert.Activity.Revision + movedCold.ActorAlertRevision(coldActor.Reference),
                        "Offcell SetAlert lost shared authority, activity revision or stopped continuation fields.");
                    var alertEvents = new FalloutPackageEvents(Dispatch);
                    updated.RestoreEvents(records, coldActor, alertEvents);
                    Require(effects == 1 && noReplay == 0 && alertEvents.LastEvent == "POBA" && alertEvents.Revision == saved.EventRevision,
                        "Shared alert synchronization replayed package events.");
                    var alertJson = JsonSerializer.Serialize(movedCold.Capture());
                    var alertOverridesJson = JsonSerializer.Serialize(movedCold.CaptureActorOverrides());
                    using var alertCold = new FalloutReferenceWorld(records);
                    alertCold.Restore(JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(alertJson)!);
                    alertCold.RestoreActorOverrides(JsonSerializer.Deserialize<FalloutActorOverrides[]>(alertOverridesJson)!);
                    var alertActor = alertCold.Get(coldActor.Reference);
                    var reboundActivity = new FalloutActorActivityState();
                    alertCold.BindActorAlert(alertActor.Reference, reboundActivity);
                    alertActor.SandboxAdmissionFailure!.RestoreActivity(records, alertActor, reboundActivity);
                    Require(reboundActivity.Capture() == updated.Activity && JsonSerializer.Serialize(alertCold.Capture()) == alertJson &&
                        JsonSerializer.Serialize(alertCold.CaptureActorOverrides()) == alertOverridesJson,
                        "Cold shared alert reconstruction changed the stopped activity or retained world state.");
                    var mismatchedActivity = new FalloutActorActivityState();
                    movedCold.BindActorAlert(coldActor.Reference, mismatchedActivity);
                    Reject(() => (updated with { Activity = updated.Activity with { Alerted = !alerted } })
                        .RestoreActivity(records, coldActor, mismatchedActivity));
                    Require(mismatchedActivity.Alerted == alerted && effects == 1,
                        "Genuine shared-alert snapshot mismatch was weakened by stopped restore.");
                }
                var beforeOverflow = coldActor.SandboxAdmissionFailure!;
                coldActor.SandboxAdmissionFailure = beforeOverflow with
                { Activity = beforeOverflow.Activity with { Revision = long.MaxValue - 1 } };
                var overflowReceipt = JsonSerializer.Serialize(coldActor.SandboxAdmissionFailure);
                var worldAlertRevision = movedCold.ActorAlertRevision(coldActor.Reference);
                Reject(() => movedCold.SetActorAlert(coldActor.Reference, 1));
                Require(!movedCold.ActorAlerted(coldActor.Reference) && movedCold.ActorAlertRevision(coldActor.Reference) == worldAlertRevision &&
                    JsonSerializer.Serialize(coldActor.SandboxAdmissionFailure) == overflowReceipt,
                    "Refused activity revision overflow partially changed shared alert authority.");
                coldActor.SandboxAdmissionFailure = beforeOverflow;
            }

            void RejectFresh(FalloutActorSandboxAdmissionFailure invalid)
            {
                var target = new FalloutPackageEvents(Dispatch);
                Reject(() => invalid.RestoreEvents(records, actor, target));
                Require(target.Active is null && !target.Done && target.Revision == 0 && target.LastEvent is null &&
                    target.LastPackage is null && target.Error is null && effects == 1,
                    "Rejected admission restore mutated event state or dispatched an effect.");
            }
        }
        finally { File.Delete(path); Directory.Delete(directory, recursive: false); }
    }

    private static byte[] Join(params byte[][] values) => values.SelectMany(value => value).ToArray();
    private static byte[] NavigationFloor(uint id, uint cell)
    {
        var triangle = new byte[16];
        BinaryPrimitives.WriteUInt16LittleEndian(triangle.AsSpan(2), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(triangle.AsSpan(4), 2);
        for (var edge = 0; edge < 3; edge++) BinaryPrimitives.WriteInt16LittleEndian(triangle.AsSpan(6 + edge * 2), -1);
        return Record("NAVM", id, Field("NVER", BitConverter.GetBytes(11u)),
            Field("DATA", new uint[] { cell, 3, 1, 0, 0, 0 }.SelectMany(BitConverter.GetBytes).ToArray()),
            Field("NVVX", new float[] { -100, -100, 7, 200, -100, 7, -100, 200, 7 }.SelectMany(BitConverter.GetBytes).ToArray()),
            Field("NVTR", triangle));
    }
    private static byte[] Field(string signature, byte[] data)
    {
        var bytes = new byte[6 + data.Length]; Encoding.ASCII.GetBytes(signature).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), checked((ushort)data.Length)); data.CopyTo(bytes, 6);
        return bytes;
    }
    private static byte[] Record(string signature, uint id, params byte[][] fields)
    {
        var data = Join(fields); var bytes = new byte[24 + data.Length]; Encoding.ASCII.GetBytes(signature).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)data.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), id); data.CopyTo(bytes, 24);
        return bytes;
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException) { return; }
        throw new InvalidDataException("Invalid Sandbox admission continuation was accepted.");
    }
}
