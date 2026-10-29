using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static partial class IngestibleContracts
{
    internal static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-ingestibles-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "Test.esm");
        try
        {
            var actor = new byte[24]; actor[8] = 1;
            var creatureStats = new byte[17]; creatureStats[1] = 30;
            BinaryPrimitives.WriteInt16LittleEndian(creatureStats.AsSpan(4), 50);
            for (var index = 10; index < 17; index++) creatureStats[index] = 5;
            var bytes = Join(Record("TES4", 0, Field("HEDR", new byte[12])),
                Record("NPC_", 7, Field("ACBS", actor), Field("DATA", [100, 0, 0, 0, 5, 5, 5, 5, 5, 5, 5]),
                    Field("NAM4", new byte[4]), Field("DNAM", Enumerable.Repeat((byte)50, 28).ToArray())),
                Record("CREA", 0x20, Field("ACBS", actor), Field("DATA", creatureStats),
                    Field("NAM4", new byte[4]), Field("PNAM", BitConverter.GetBytes(0x1du))),
                Record("CONT", 0x21, Field("CNTO", Join(BitConverter.GetBytes(6u), BitConverter.GetBytes(3))),
                    Field("COED", Join(new byte[8], BitConverter.GetBytes(.75f)))),
                Idle(0x30, "Test/IdleAnims", 0, 0x87, Predicate(143, 10)),
                Idle(0x31, "Test/IdleAnims/Meal.kf", 0x30, 7, Predicate(246, 1, 6)),
                Idle(0x32, "Test2/IdleAnims", 0, 0x87, Predicate(159, 3)),
                Idle(0x33, "Test2/IdleAnims/Food", 0x32, 7, Predicate(143, 10)),
                Idle(0x34, "Test2/IdleAnims/Meal.kf", 0x33, 7),
                Item(1, Effect(10, 20, 0)), Item(2, Effect(11, 4, 6, true)),
                Item(3, Effect(10, 20, 0), Effect(12, 1, 0)),
                Item(4, Effect(13, 20, 99)), Item(5, Effect(11, 0, 0), Effect(14, 50, 0)),
                Spell(0x40, Effect(11, 2, 5), Effect(12, 1, 0, true)), Spell(0x41, Effect(15, 1, 8, actorValue: 7)),
                Food(6, Effect(11, 4, 5), Effect(15, 1, 8, actorValue: 7)),
                Food(0x18, Effect(11, 4, 5)),
                Ingredient(8, Effect(11, 4, 5)),
                NativeInventoryOrderFixture(actor),
                Record("INGR", 9, Field("EDID", Text("UnknownIngredientFlags")), Field("DATA", BitConverter.GetBytes(1f)),
                    Field("ENIT", [0, 0, 0, 0, 4, 0, 0, 0]), Effect(11, 4, 5)),
                Modifier(15, 0, 0x76, 7), Modifier(10, 34, 0x70), Modifier(11, 0, 0x70), Modifier(12, 1, 0x70), Modifier(13, 0, 0xf0), Modifier(14, 0, 0x1000075, 54, 20),
                Setting(100, "fMagicMedicineSkillBase", 1), Setting(101, "fMagicMedicineSkillMult", 2),
                Setting(110, "fMagicSurvivalSkillBase", 1), Setting(111, "fMagicSurvivalSkillMult", 2),
                Setting(112, "fAVDNPCHealthEnduranceOffset", 0), Setting(113, "fAVDNPCHealthEnduranceMult", 0),
                Setting(114, "fAVDNPCHealthLevelMult", 0), Setting(115, "fPlayerMaxResistance", 85),
                Setting(116, "fAVDRadResistEnduranceOffset", 0), Setting(117, "fAVDRadResistEnduranceMult", 2),
                Setting(102, "fAVDHealthEnduranceMult", 20), Setting(103, "fAVDHealthLevelMult", 5),
                Setting(104, "fAVDActionPointsBase", 65), Setting(105, "fAVDActionPointsMult", 3),
                Record("GMST", 106, Field("EDID", Text("iXPBase")), Field("DATA", BitConverter.GetBytes(200))),
                Record("GMST", 107, Field("EDID", Text("iXPBumpBase")), Field("DATA", BitConverter.GetBytes(150))),
                Record("BPTD", 0x1d, Field("BPNN", Text("Root")), Field("BPND", new byte[84])), Record("CELL", 0x80, Field("DATA", [1])),
                ReferenceGroup(Record("ACHR", 0x91, Field("NAME", BitConverter.GetBytes(7u)), Field("DATA", new byte[24])),
                    Record("REFR", 0x92, Field("NAME", BitConverter.GetBytes(6u)), Field("DATA", new byte[24])),
                    Record("REFR", 0x95, Field("NAME", BitConverter.GetBytes(6u)), Field("DATA", new byte[24]), Field("XCNT", BitConverter.GetBytes(2))),
                    Record("REFR", 0x96, Field("NAME", BitConverter.GetBytes(0x21u)), Field("DATA", new byte[24])),
                    Record("ACHR", 0x97, Field("NAME", BitConverter.GetBytes(0x23u)), Field("DATA", new byte[24])),
                    Record("REFR", 0x98, Field("NAME", BitConverter.GetBytes(0x24u)), Field("DATA", new byte[24])),
                    Record("ACRE", 0x94, Field("NAME", BitConverter.GetBytes(0x20u)), Field("DATA", new byte[24]))));
            File.WriteAllBytes(path, bytes);
            using var records = FalloutPluginStack.Load(directory, ["Test.esm"]);
            FalloutFormKey Key(uint id) => new("Test.esm", id);
            InventoryTraversalContracts(records);
            using (var foodWorld = new FalloutReferenceWorld(records))
            {
                Check(foodWorld.IsActorFood(Key(6)) && !foodWorld.IsActorFood(Key(1)),
                    "Cached actor-food classification changed the winning source declarations.");
                var beforeFoodReads = GC.GetAllocatedBytesForCurrentThread();
                for (var index = 0; index < 8192; index++)
                    if (!foodWorld.IsActorFood(Key(6)) || foodWorld.IsActorFood(Key(1)))
                        throw new InvalidDataException("Cached actor-food classification changed during repeated reads.");
                Check(GC.GetAllocatedBytesForCurrentThread() == beforeFoodReads,
                    "Warmed actor-food classification allocated or decoded source records again.");
                for (var attempt = 0; attempt < 2; attempt++)
                {
                    try { _ = foodWorld.IsActorFood(Key(0xffff)); }
                    catch (KeyNotFoundException) { continue; }
                    throw new InvalidOperationException("Missing actor-food source was cached as a valid result.");
                }
            }
            using (var freshWorld = new FalloutReferenceWorld(records))
            {
                var freshActor = freshWorld.Get(Key(0x91));
                freshActor.Animation.Bind("meshes/test/idle.kf", new string('a', 64));
                Check(freshActor.MayInitializeLaunchProcess(false) && !freshActor.MayInitializeLaunchProcess(true),
                    "A first native process was confused with unloaded source animation or an existing native owner.");
                using var restoredWorld = new FalloutReferenceWorld(records);
                restoredWorld.Restore(freshWorld.Capture());
                var restoredActor = restoredWorld.Get(Key(0x91));
                Check(restoredActor.HasRestoredSnapshot && !restoredActor.MayInitializeLaunchProcess(false),
                    "A restored actor with unknown launch history was silently reset.");
                freshActor.ProjectileLaunch = new(0, 0);
                Check(!freshActor.MayInitializeLaunchProcess(false), "An existing launch process was initialized twice.");
            }
            var idles = new FalloutFurnitureIdleTree(records, "meshes/Test/skeleton.nif");
            Check(idles.SelectProcedure(10, condition => condition.Function == 143 ? 10 : 0) is null,
                "A source-authorized no-file selection fabricated an animation.");
            Check(idles.SelectProcedure(10, condition => condition.Function == 143 ? 10 : 1)?.FormKey == Key(0x31),
                "Item-conditioned eating failed to select its source leaf.");
            var nested = new FalloutFurnitureIdleTree(records, "meshes/Test2/skeleton.nif");
            Check(nested.SelectProcedure(10, condition => condition.Function == 143 ? 10 : 0) is null &&
                nested.SelectProcedure(10, condition => condition.Function == 143 ? 10 : 3)?.FormKey == Key(0x34),
                "Procedure selection bypassed furniture ancestry or required a root-only predicate.");
            var inventory = new FalloutPlayerInventory();
            foreach (var id in new[] { 1u, 2u, 3u, 4u, 5u }) inventory.Add(records, Key(id), 3, 1, true);
            var initial = new GameplayVitals(1, 100, 200, 50, 50, 0, 100, LimbDamage: new Dictionary<byte, float> { [1] = 80 });
            FalloutPlayerVitals Vitals(GameplayVitals state) => new(records, Key(7), new(5, 5, 5, 5, 5, 5, 5), state);
            var vitals = Vitals(initial);
            var gore = new FalloutBodyPartGore(0, null, null, 0, null, 0);
            var parts = new FalloutBodyPartData(Key(29), [new(1, "Limb", "Limb", "Limb", 0, 1, 50, 25, 0, 0, 0,
                gore, gore, default, default, null, 0, "")]);
            var hardcore = false;
            FalloutPlayerIngestibles Owner(FalloutPlayerVitals health) => new(records, inventory, health, parts, _ => 50, _ => false, () => hardcore);
            var owner = Owner(vitals);
            var ingredientInventory = new FalloutPlayerInventory(44);
            ingredientInventory.Add(records, Key(9), 1, 1, true);
            Reject(() => FalloutIngestible.Read(records, Key(9)));
            ingredientInventory.Add(records, Key(8), 2, 1, true);
            var ingredientVitals = Vitals(initial);
            var ingredientOwner = new FalloutPlayerIngestibles(records, ingredientInventory, ingredientVitals, parts, _ => 50, _ => false, () => false);
            var ingredientUse = ingredientOwner.Prepare(Key(8));
            Check(ingredientUse.Sound is null && ingredientInventory.Item(Key(8))!.Count == 2,
                "Ingredient preparation invented source sound or consumed early.");
            ingredientUse.Commit(); ingredientOwner.Advance(2);
            var ingredientColdVitals = Vitals(ingredientVitals.State);
            var ingredientCold = new FalloutPlayerIngestibles(records, ingredientInventory, ingredientColdVitals, parts, _ => 50, _ => false, () => false);
            ingredientCold.Restore(JsonSerializer.Deserialize<FalloutIngestiblesSnapshot>(JsonSerializer.Serialize(ingredientOwner.Capture()))!);
            ingredientOwner.Advance(3); ingredientCold.Advance(3);
            Check(ingredientInventory.Item(Key(8))!.Count == 1 && ingredientVitals.State.ExactHitPoints == 140 &&
                ingredientColdVitals.State.ExactHitPoints == ingredientVitals.State.ExactHitPoints && ingredientOwner.Capture().Effects.Count == 0,
                "Ingredient use lost consumption, source scaling or its cold timed effect.");
            using (var ingredientWorld = new FalloutReferenceWorld(records))
            {
                var eater = ingredientWorld.Get(Key(0x91)); _ = ingredientWorld.Health(eater.Reference);
                eater.ActorValues["health"] = eater.ActorValues["health"] with { Damage = -60 };
                var carried = ingredientWorld.Inventory(eater.Reference, 1).Contents;
                carried.Add(records, Key(8), 1, 1, true);
                ingredientWorld.PrepareActorIngestible(eater.Reference, Key(8), 1, null, false).Commit();
                ingredientWorld.AdvanceActorIngestibles(2);
                using var coldIngredientWorld = new FalloutReferenceWorld(records);
                coldIngredientWorld.Restore(ingredientWorld.Capture());
                ingredientWorld.AdvanceActorIngestibles(3); coldIngredientWorld.AdvanceActorIngestibles(3);
                Check(carried.Item(Key(8)) is null && ingredientWorld.Health(eater.Reference).Current == coldIngredientWorld.Health(eater.Reference).Current,
                    "Actor ingredient use lost inventory ownership or its cold timed remainder.");
            }
            using (var acquisitionWorld = new FalloutReferenceWorld(records))
            {
                var eater = acquisitionWorld.Get(Key(0x91));
                var carried = acquisitionWorld.Inventory(eater.Reference, 1).Contents;
                Reject(() => acquisitionWorld.PrepareActorFoodAcquisition(eater.Reference, Key(0x95), 1, 1, null));
                var staleFood = acquisitionWorld.PrepareActorFoodAcquisition(eater.Reference, Key(0x95), 2, 1, null);
                acquisitionWorld.Get(Key(0x95)).Taken = true;
                Reject(() => staleFood.Commit());
                Check(carried.Item(Key(6)) is null, "Stale actor pickup published food inventory.");
                acquisitionWorld.Get(Key(0x95)).Taken = false;
                var staleOwner = acquisitionWorld.PrepareActorFoodAcquisition(eater.Reference, Key(0x95), 2, 1, null);
                acquisitionWorld.SetOwnership(Key(0x95), Key(7));
                Reject(() => staleOwner.Commit());
                Check(carried.Item(Key(6)) is null && !acquisitionWorld.Get(Key(0x95)).Taken,
                    "Changed ownership published a stale actor food pickup.");
                acquisitionWorld.Get(Key(0x95)).OwnershipOverride = null;
                var rollbackFood = acquisitionWorld.PrepareActorFoodAcquisition(eater.Reference, Key(0x95), 2, 1, null);
                carried.PrepareChange = (_, _) => new(() => throw new InvalidOperationException("fixture publication failure"), () => { });
                Reject(() => rollbackFood.Commit());
                carried.PrepareChange = null;
                Check(carried.Item(Key(6)) is null && !acquisitionWorld.Get(Key(0x95)).Taken,
                    "Failed actor pickup partially published its inventory or placed stack.");
                var interleavedFood = acquisitionWorld.PrepareActorFoodAcquisition(eater.Reference, Key(0x95), 2, 1, null);
                carried.PrepareChange = (_, _) => new(() => acquisitionWorld.Get(Key(0x95)).Taken = true,
                    () => acquisitionWorld.Get(Key(0x95)).Taken = false);
                Reject(() => interleavedFood.Commit());
                carried.PrepareChange = null;
                Check(carried.Item(Key(6)) is null && !acquisitionWorld.Get(Key(0x95)).Taken,
                    "Interleaved source retirement escaped actor pickup rollback.");
                var takeFood = acquisitionWorld.PrepareActorFoodAcquisition(eater.Reference, Key(0x95), 2, 1, null);
                takeFood.Commit(); Reject(() => takeFood.Commit());
                Check(takeFood.Count == 2 && carried.Item(Key(6))?.Count == 2 && acquisitionWorld.Get(Key(0x95)).Taken,
                    "Actor pickup lost its real stack, duplicated it or did not retire the source.");
                var holder = acquisitionWorld.Inventory(Key(0x96), 1).Contents;
                var staleTransfer = acquisitionWorld.PrepareActorFoodTransfer(eater.Reference, Key(0x96), Key(6), 0, 2, 1, null);
                holder.Remove(Key(6), 1, true);
                Reject(() => staleTransfer.Commit());
                Check(holder.Item(Key(6))?.Count == 2 && carried.Item(Key(6))?.Count == 2,
                    "Stale food transfer changed either inventory.");
                var rollbackTransfer = acquisitionWorld.PrepareActorFoodTransfer(eater.Reference, Key(0x96), Key(6), 0, 2, 1, null);
                carried.PrepareChange = (_, _) => new(() => throw new InvalidOperationException("fixture transfer failure"), () => { });
                Reject(() => rollbackTransfer.Commit());
                carried.PrepareChange = null;
                Check(holder.Item(Key(6))?.Count == 2 && carried.Item(Key(6))?.Count == 2,
                    "Failed actor food transfer did not roll back both inventories.");
                var interleavedTransfer = acquisitionWorld.PrepareActorFoodTransfer(eater.Reference, Key(0x96), Key(6), 0, 2, 1, null);
                var oldHolderOwnership = acquisitionWorld.Get(Key(0x96)).OwnershipOverride;
                holder.PrepareChange = (_, _) => new(() => acquisitionWorld.SetOwnership(Key(0x96), Key(7)),
                    () => acquisitionWorld.Get(Key(0x96)).OwnershipOverride = oldHolderOwnership);
                Reject(() => interleavedTransfer.Commit());
                holder.PrepareChange = null;
                Check(holder.Item(Key(6))?.Count == 2 && carried.Item(Key(6))?.Count == 2 &&
                    acquisitionWorld.Ownership(Key(0x96)).Owner is null,
                    "Interleaved holder access escaped actor food transfer rollback.");
                var livingHolder = acquisitionWorld.Inventory(Key(0x94), 1).Contents;
                livingHolder.Add(records, Key(6), 1, 1, true);
                Reject(() => acquisitionWorld.PrepareActorFoodTransfer(eater.Reference, Key(0x94), Key(6), 0, 1, 1, null));
                Check(acquisitionWorld.KillActor(Key(0x94), eater.Reference, 1), "Fixture creature did not enter its source death transition.");
                Reject(() => acquisitionWorld.PrepareActorFoodTransfer(eater.Reference, Key(0x94), Key(6), 0, 1, 1, null));
                Check(acquisitionWorld.AdvanceDeathEvent(Key(0x94), 0, 0, false), "Fixture death event did not become ready.");
                var corpseTransfer = acquisitionWorld.PrepareActorFoodTransfer(eater.Reference, Key(0x94), Key(6), 0, 1, 1, null);
                corpseTransfer.Commit();
                Check(livingHolder.Item(Key(6)) is null && carried.Item(Key(6))?.Count == 3,
                    "Dead actor food transfer did not retain the actual corpse inventory.");
                var transfer = acquisitionWorld.PrepareActorFoodTransfer(eater.Reference, Key(0x96), Key(6), 0, 2, 1, null);
                transfer.Commit(); Reject(() => transfer.Commit());
                Check(holder.Item(Key(6)) is null && carried.Item(Key(6))?.Count == 5 &&
                    carried.Item(Key(6))!.Variants!.Any(value => value.Count == 2 && value.Condition == .75f),
                    "Actor food transfer lost the selected variant or duplicated the stack.");
                using var coldAcquisition = new FalloutReferenceWorld(records);
                coldAcquisition.Restore(acquisitionWorld.Capture());
                Check(coldAcquisition.Get(Key(0x95)).Taken && coldAcquisition.Inventory(eater.Reference, 1).Contents.Item(Key(6)) is { Count: 5 } coldFood &&
                    coldFood.Variants!.Any(value => value.Count == 2 && value.Condition == .75f) &&
                    coldAcquisition.Inventory(Key(0x96), 1).Contents.Item(Key(6)) is null,
                    "Actor food pickup or transfer lost ownership during cold restoration.");
            }
            var use = owner.Prepare(Key(1));
            Check(vitals.State == initial && inventory.Item(Key(1))!.Count == 3, "Aid preparation mutated state.");
            var result = use.Commit();
            Check(result is { Consumed: true, HealthBefore: 100, HealthAfter: 140 } && inventory.Item(Key(1))!.Count == 2 &&
                vitals.State.LimbDamage![1] == 40, "Source medicine magnitude or health/limb transaction failed.");
            Reject(() => use.Commit());
            var before = vitals.State; var count = inventory.Item(Key(3))!.Count;
            Reject(() => owner.Prepare(Key(3)));
            Check(vitals.State == before && inventory.Item(Key(3))!.Count == count, "Unsupported later effect partially consumed/healed.");
            owner.Prepare(Key(5)).Commit();
            Check(vitals.State.RadiationRads == 25 && vitals.State.ExactHitPoints == before.ExactHitPoints &&
                inventory.Item(Key(5))!.Count == 2, "Radiation lost source resistance, changed health, or consumed twice.");
            before = vitals.State;
            var stale = owner.Prepare(Key(1)); inventory.Remove(Key(1), 1, true);
            Reject(() => stale.Commit());
            Check(vitals.State == before && inventory.Item(Key(1))!.Count == 1, "Stale use changed health or consumed twice.");
            hardcore = true;
            owner.Prepare(Key(2)).Commit(); owner.Advance(1.25);
            Check(vitals.State.ExactHitPoints == 150 && owner.Capture().Effects.Single().Elapsed == 1.25,
                "Timed health effect did not advance on the supplied gameplay seconds.");
            var saved = JsonSerializer.Deserialize<FalloutIngestiblesSnapshot>(JsonSerializer.Serialize(owner.Capture()))!;
            var coldVitals = Vitals(JsonSerializer.Deserialize<GameplayVitals>(JsonSerializer.Serialize(vitals.State))!);
            var cold = Owner(coldVitals); cold.Restore(saved);
            owner.Advance(2.75); cold.Advance(2.75);
            Check(vitals.State.ExactHitPoints == 172 && coldVitals.State.ExactHitPoints == vitals.State.ExactHitPoints,
                "Cold timed effect lost its remainder or reapplied elapsed healing.");
            owner.Advance(50); cold.Advance(50);
            Check(vitals.State.ExactHitPoints == 188 && coldVitals.State.ExactHitPoints == 188 && owner.Capture().Effects.Count == 0,
                "Long frame overran effect duration or lost its final remainder.");
            owner.Prepare(Key(4)).Commit();
            Check(vitals.State.ExactHitPoints == 200 && owner.Capture().Effects.Count == 0,
                "No Duration ignored its source flag or healing exceeded maximum health.");
            Reject(() => Owner(Vitals(initial)).Restore(saved with { Effects = [saved.Effects[0] with { ItemHash = new string('0', 64) }] }));
            Reject(() => Owner(Vitals(initial)).Restore(saved with { Effects = [saved.Effects[0] with { Magnitude = -1 }] }));
            Check(inventory.Item(Key(2))!.Count == 2, "Timed healing consumed more than one inventory item.");
            var spellVitals = Vitals(initial);
            hardcore = false;
            var spellOwner = Owner(spellVitals);
            var preparedSpell = spellOwner.PrepareActorEffect(Key(0x40));
            Check(!preparedSpell.Commit().Consumed && inventory.Item(Key(0x40)) is null,
                "Actor effect incorrectly consumed an inventory item.");
            spellOwner.Advance(2);
            var spellColdVitals = Vitals(spellVitals.State);
            var spellCold = Owner(spellColdVitals);
            spellCold.Restore(JsonSerializer.Deserialize<FalloutIngestiblesSnapshot>(JsonSerializer.Serialize(spellOwner.Capture()))!);
            spellOwner.Advance(3); spellCold.Advance(3);
            Check(spellVitals.State.ExactHitPoints == 110 && spellColdVitals.State.ExactHitPoints == 110 &&
                spellOwner.Capture().Effects.Count == 0, "Actor effect lost its source timing or cold remainder.");
            hardcore = true;
            var spellBefore = spellVitals.State;
            Reject(() => spellOwner.PrepareActorEffect(Key(0x40)));
            Check(spellVitals.State == spellBefore && spellOwner.Capture().Effects.Count == 0,
                "Unsupported selected actor effect partially applied its preceding effect.");
            hardcore = false;
            var staleSpell = spellOwner.PrepareActorEffect(Key(0x40));
            spellOwner.PrepareActorEffect(Key(0x40)).Commit();
            Reject(() => staleSpell.Commit());
            Check(spellOwner.Capture().Effects.Count == 1, "A stale effect delivery overwrote the live timer set.");
            var deadVitals = Vitals(initial.Damage(1000));
            var dead = Owner(deadVitals); var retainedCount = inventory.Item(Key(1))!.Count;
            Reject(() => dead.Prepare(Key(1)));
            Check(deadVitals.State.HitPoints == 0 && inventory.Item(Key(1))!.Count == retainedCount,
                "Dead Aid use consumed an item or silently revived the player.");
            // Food modifies the consuming actor's own values once, while its
            // health component integrates only the remaining source duration.
            inventory.Add(records, Key(6), 2, 1, true);
            var pools = new FalloutPlayerActorValues(records);
            pools.BindConstantModifiers((_, _) => []);
            var foodVitals = Vitals(initial);
            var foodOwner = new FalloutPlayerIngestibles(records, inventory, foodVitals, parts,
                _ => 50, _ => false, () => false, pools);
            foodOwner.Prepare(Key(6)).Commit();
            Check(pools.ReadCurrent(7) == 4 && foodVitals.State.ExactHitPoints == 100,
                "Food modifier was integrated as a rate or health applied before its duration.");
            foodOwner.Advance(3);
            Check(pools.ReadCurrent(7) == 4 && foodVitals.State.ExactHitPoints == 124,
                "Timed food reapplied its temporary modifier.");
            var foodColdPools = new FalloutPlayerActorValues(records, pools.Capture());
            foodColdPools.BindConstantModifiers((_, _) => []);
            var foodColdVitals = Vitals(foodVitals.State);
            var foodCold = new FalloutPlayerIngestibles(records, inventory, foodColdVitals, parts,
                _ => 50, _ => false, () => false, foodColdPools);
            foodCold.Restore(foodOwner.Capture());
            foodOwner.Advance(100); foodCold.Advance(100);
            Check(pools.ReadCurrent(7) == 5 && foodColdPools.ReadCurrent(7) == 5 &&
                foodVitals.State.ExactHitPoints == 140 && foodColdVitals.State.ExactHitPoints == 140,
                "Cold food expiry duplicated healing or failed to remove its retained modifier.");

            var failModifier = true;
            void RefuseModifier() { if (failModifier) { failModifier = false; throw new InvalidOperationException("native publication refused"); } }
            pools.Changed += RefuseModifier;
            var rollbackCount = inventory.Item(Key(6))!.Count;
            Reject(() => foodOwner.Prepare(Key(6)).Commit());
            pools.Changed -= RefuseModifier;
            Check(pools.ReadCurrent(7) == 5 && inventory.Item(Key(6))!.Count == rollbackCount &&
                foodOwner.Capture().Effects.Count == 0 && foodVitals.State.ExactHitPoints == 140,
                "Failed native modifier publication partially consumed or retained effects.");
            failModifier = true;
            pools.Changed += RefuseModifier;
            Reject(() => foodOwner.PrepareActorEffect(Key(0x41)).Commit());
            pools.Changed -= RefuseModifier;
            Check(pools.ReadCurrent(7) == 5 && foodOwner.Capture().Effects.Count == 0 && foodVitals.State.ExactHitPoints == 140,
                "Failed actor-effect publication retained a modifier or timer.");

            var world = new FalloutReferenceWorld(records);
            var consumer = world.Get(Key(0x91));
            _ = world.Health(consumer.Reference);
            consumer.ActorValues["health"] = consumer.ActorValues["health"] with { Damage = -60 };
            var actorInventory = world.Inventory(consumer.Reference, 1, null).Contents;
            actorInventory.Add(records, Key(6), 2, 1, true);
            var playerCount = inventory.Item(Key(6))!.Count;
            var actorUse = world.PrepareActorIngestible(consumer.Reference, Key(6), 1, null, false);
            actorUse.Commit(); Reject(() => actorUse.Commit());
            Check(actorInventory.Item(Key(6))!.Count == 1 && inventory.Item(Key(6))!.Count == playerCount &&
                world.ActorValue(consumer.Reference, "endurance") == 4,
                "Actor food consumed player inventory, consumed twice, or lost its modifier.");
            world.AdvanceActorIngestibles(3);
            var actorSaved = world.Capture();
            var coldWorld = new FalloutReferenceWorld(records); coldWorld.Restore(actorSaved);
            world.AdvanceActorIngestibles(100); coldWorld.AdvanceActorIngestibles(100);
            Check(world.Health(consumer.Reference).Current == 80 && coldWorld.Health(consumer.Reference).Current == 80 &&
                world.ActorValue(consumer.Reference, "endurance") == 5 &&
                coldWorld.ActorValue(consumer.Reference, "endurance") == 5,
                "Actor cold continuation lost duration, health, or temporary modifier ownership.");
            consumer.PrepareNativeInventoryRemoval = (_, _) => new(() => throw new InvalidOperationException("native inventory refused"), () => { });
            Reject(() => world.PrepareActorIngestible(consumer.Reference, Key(6), 1, null, false).Commit());
            consumer.PrepareNativeInventoryRemoval = null;
            Check(actorInventory.Item(Key(6))!.Count == 1 && world.ActorValue(consumer.Reference, "endurance") == 5 &&
                consumer.Ingestibles!.Effects.Count == 0, "Actor native publication failure partially consumed food or effects.");
            var first = world.PrepareActorIngestible(consumer.Reference, Key(6), 1, null, false, Key(0x92));
            var second = world.PrepareActorIngestible(consumer.Reference, Key(6), 1, null, false, Key(0x92));
            first.Commit(); Reject(() => second.Commit());
            Check(world.Get(Key(0x92)).Taken && actorInventory.Item(Key(6))!.Count == 1,
                "World food was consumed twice or removed unrelated carried food.");
            actorInventory.Add(records, Key(5), 1, 1, true);
            world.PrepareActorIngestible(consumer.Reference, Key(5), 1, null, false).Commit();
            // The still-active cactus reduced this actor's Endurance to4:
            // derived resistance8%, independent from the player's50% fixture.
            Check(world.ActorValue(consumer.Reference, "radiationrads") == 46,
                "Actor radiation used player resistance or lost its accumulation pool.");
            var creature = world.Get(Key(0x94)); _ = world.Health(creature.Reference);
            creature.ActorValues["health"] = creature.ActorValues["health"] with { Damage = -30 };
            var creatureInventory = world.Inventory(creature.Reference, 1, null).Contents;
            creatureInventory.Add(records, Key(6), 1, 1, true);
            Check(world.ActorValue(creature.Reference, "survival") == 0 && world.ActorValue(creature.Reference, "medicine") == 0,
                "Creature noncombat skills borrowed its combat rating.");
            world.PrepareActorIngestible(creature.Reference, Key(6), 1, null, false).Commit();
            world.AdvanceActorIngestibles(8);
            Check(world.Health(creature.Reference).Current == 40 && world.ActorValue(creature.Reference, "endurance") == 5,
                "Creature food lost the qualified empty skill pool or source modifier lifetime.");
            new FalloutActorEatProgress(Key(6), FalloutIngestible.Read(records, Key(6)).Hash, null,
                Consumed: true, NoIdle: true).Validate();
            Reject(() => new FalloutActorEatProgress(Key(6), FalloutIngestible.Read(records, Key(6)).Hash, null,
                Consumed: false, NoIdle: true).Validate());
            var pendingSandbox = FalloutActorPendingSandboxStart.CaptureBlocker;
            creature.ProcedureCaptureBlocker = pendingSandbox;
            Reject(() => creature.ObserveInitialSandboxMotion(Key(0x80)));
            creature.PackageMotion = new(Key(0x80), new string('a', 64), "test/idle.kf", new string('b', 64),
                1, false, [0, 0, 0], [0, 0, 0, 1]);
            Reject(() => creature.ObserveInitialSandboxMotion(Key(0x80)));
            creature.PackageMotion = creature.PackageMotion with
            { Sandbox = new(FalloutSandboxActivity.Eat, 1, 1, new byte[7]) };
            Reject(() => creature.ObserveInitialSandboxMotion(Key(0x81)));
            creature.ObserveInitialSandboxMotion(Key(0x80));
            Check(creature.ProcedureCaptureBlocker is null && creature.PackageMotion.Sandbox is not null,
                "A published Eat motion did not retire only its matching initial Sandbox blocker.");
            creature.ProcedureCaptureBlocker = FalloutSandboxPackage.CaptureBlockerPrefix + "independent failure";
            creature.ObserveInitialSandboxMotion(Key(0x80));
            Check(creature.ProcedureCaptureBlocker.EndsWith("independent failure", StringComparison.Ordinal),
                "An Eat motion erased a different Sandbox failure.");
            Console.WriteLine("OPENNV_FOOD_RADIATION_CONTRACT_PASS playerResistance=true actorResistance=true separatePools=true creatureSkill=true noIdleReceipt=true");
            Console.WriteLine("OPENNV_ACTOR_INGESTIBLE_CONTRACT_PASS ownInventory=true worldContention=true reversible=true cold=true");
            Console.WriteLine("OPENNV_INGESTIBLE_CONTRACT_PASS sourceMagnitude=true limbs=true atomic=true staleInput=true duration=true finalRemainder=true noDuration=true cold=true driftRejected=true");
        }
        finally { File.Delete(path); Directory.Delete(directory); }
    }

    private static byte[] Item(uint id, params byte[][] effects)
    {
        var header = new byte[20]; header[0] = 20; header[4] = 5;
        return Record("ALCH", id, Field("EDID", Text("Aid" + id)), Field("DATA", BitConverter.GetBytes(1f)), Field("ENIT", header), Join(effects));
    }
    private static byte[] Spell(uint id, params byte[][] effects) =>
        Record("SPEL", id, Field("SPIT", new byte[16]), Join(effects));
    private static byte[] Predicate(ushort function, float comparison, uint argument = 0)
    {
        var bytes = new byte[28]; BitConverter.GetBytes(comparison).CopyTo(bytes, 4);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(8), function); UInt(bytes, 12, argument);
        return Field("CTDA", bytes);
    }
    private static byte[] Idle(uint id, string path, uint parent, byte group, params byte[][] conditions)
    {
        var related = new byte[8]; UInt(related, 0, parent);
        return Record("IDLE", id, Field("MODL", Text(path)), Field("ANAM", related), Field("DATA", [group, 0, 0, 0, 0, 0]), Join(conditions));
    }
    private static byte[] Food(uint id, params byte[][] effects)
    {
        var header = new byte[20]; header[4] = 3;
        return Record("ALCH", id, Field("EDID", Text("Food" + id)), Field("DATA", BitConverter.GetBytes(1f)), Field("ENIT", header), Join(effects));
    }
    private static byte[] Ingredient(uint id, params byte[][] effects)
    {
        var header = new byte[8]; header[4] = 3;
        return Record("INGR", id, Field("EDID", Text("Ingredient" + id)), Field("DATA", BitConverter.GetBytes(1f)), Field("ENIT", header), Join(effects));
    }
    private static byte[] ReferenceGroup(params byte[][] references)
    {
        var data = Join(references); var bytes = new byte[24 + data.Length];
        Encoding.ASCII.GetBytes("GRUP").CopyTo(bytes, 0); UInt(bytes, 4, (uint)bytes.Length);
        UInt(bytes, 8, 0x80); UInt(bytes, 12, 6); data.CopyTo(bytes, 24); return bytes;
    }
    private static byte[] Modifier(uint id, uint archetype, uint flags, uint actorValue = 16, int resistance = -1)
    {
        var data = new byte[72]; UInt(data, 16, unchecked((uint)resistance)); UInt(data, 0, flags); UInt(data, 64, archetype); UInt(data, 68, actorValue);
        return Record("MGEF", id, Field("DATA", data));
    }
    private static byte[] Effect(uint id, uint magnitude, uint duration, bool hardcore = false, uint actorValue = 16)
    {
        var data = new byte[20]; UInt(data, 0, magnitude); UInt(data, 8, duration); UInt(data, 16, actorValue);
        var condition = new byte[28]; BitConverter.GetBytes(1f).CopyTo(condition, 4);
        BinaryPrimitives.WriteUInt16LittleEndian(condition.AsSpan(8), 586);
        return Join(Field("EFID", BitConverter.GetBytes(id)), Field("EFIT", data), hardcore ? Field("CTDA", condition) : []);
    }
    private static byte[] Setting(uint id, string name, float value) => Record("GMST", id, Field("EDID", Text(name)), Field("DATA", BitConverter.GetBytes(value)));
    private static byte[] Text(string value) => Encoding.ASCII.GetBytes(value + '\0');
    private static byte[] Join(params byte[][] fields) => fields.SelectMany(value => value).ToArray();
    private static void UInt(byte[] bytes, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
    private static byte[] Field(string name, byte[] data)
    {
        var bytes = new byte[6 + data.Length]; Encoding.ASCII.GetBytes(name).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), checked((ushort)data.Length)); data.CopyTo(bytes, 6); return bytes;
    }
    private static byte[] Record(string name, uint id, params byte[][] fields)
    {
        var data = Join(fields); var bytes = new byte[24 + data.Length]; Encoding.ASCII.GetBytes(name).CopyTo(bytes, 0);
        UInt(bytes, 4, (uint)data.Length); UInt(bytes, 12, id); data.CopyTo(bytes, 24); return bytes;
    }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception error) when (error is InvalidDataException or NotSupportedException or InvalidOperationException) { return; }
        throw new Exception("Unsupported ingestible state was accepted.");
    }
}
