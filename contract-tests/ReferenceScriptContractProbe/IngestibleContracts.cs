using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static class IngestibleContracts
{
    internal static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-ingestibles-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "Test.esm");
        try
        {
            var actor = new byte[24]; actor[8] = 1;
            var bytes = Join(Record("TES4", 0, Field("HEDR", new byte[12])),
                Record("NPC_", 7, Field("ACBS", actor), Field("DATA", [100, 0, 0, 0, 5, 5, 5, 5, 5, 5, 5]),
                    Field("NAM4", new byte[4]), Field("DNAM", Enumerable.Repeat((byte)50, 28).ToArray())),
                Item(1, Effect(10, 20, 0)), Item(2, Effect(11, 4, 6, true)),
                Item(3, Effect(10, 20, 0), Effect(12, 1, 0)),
                Item(4, Effect(13, 20, 99)), Item(5, Effect(14, 50, 0)),
                Food(6, Effect(11, 4, 5), Effect(15, 1, 8, actorValue: 7)),
                Modifier(15, 0, 0x76, 7), Modifier(10, 34, 0x70), Modifier(11, 0, 0x70), Modifier(12, 1, 0x70), Modifier(13, 0, 0xf0), Modifier(14, 0, 0x70, 54),
                Setting(100, "fMagicMedicineSkillBase", 1), Setting(101, "fMagicMedicineSkillMult", 2),
                Setting(110, "fMagicSurvivalSkillBase", 1), Setting(111, "fMagicSurvivalSkillMult", 2),
                Setting(112, "fAVDNPCHealthEnduranceOffset", 0), Setting(113, "fAVDNPCHealthEnduranceMult", 0),
                Setting(114, "fAVDNPCHealthLevelMult", 0),
                Setting(102, "fAVDHealthEnduranceMult", 20), Setting(103, "fAVDHealthLevelMult", 5),
                Setting(104, "fAVDActionPointsBase", 65), Setting(105, "fAVDActionPointsMult", 3),
                Record("GMST", 106, Field("EDID", Text("iXPBase")), Field("DATA", BitConverter.GetBytes(200))),
                Record("GMST", 107, Field("EDID", Text("iXPBumpBase")), Field("DATA", BitConverter.GetBytes(150))),
                Record("BPTD", 0x1d, Field("BPNN", Text("Root")), Field("BPND", new byte[84])), Record("CELL", 0x80, Field("DATA", [1])),
                ReferenceGroup(Record("ACHR", 0x91, Field("NAME", BitConverter.GetBytes(7u)), Field("DATA", new byte[24])),
                    Record("REFR", 0x92, Field("NAME", BitConverter.GetBytes(6u)), Field("DATA", new byte[24]))));
            File.WriteAllBytes(path, bytes);
            using var records = FalloutPluginStack.Load(directory, ["Test.esm"]);
            FalloutFormKey Key(uint id) => new("Test.esm", id);
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
            var use = owner.Prepare(Key(1));
            Check(vitals.State == initial && inventory.Item(Key(1))!.Count == 3, "Aid preparation mutated state.");
            var result = use.Commit();
            Check(result is { Consumed: true, HealthBefore: 100, HealthAfter: 140 } && inventory.Item(Key(1))!.Count == 2 &&
                vitals.State.LimbDamage![1] == 40, "Source medicine magnitude or health/limb transaction failed.");
            Reject(() => use.Commit());
            var before = vitals.State; var count = inventory.Item(Key(3))!.Count;
            Reject(() => owner.Prepare(Key(3)));
            Check(vitals.State == before && inventory.Item(Key(3))!.Count == count, "Unsupported later effect partially consumed/healed.");
            Reject(() => owner.Prepare(Key(5)));
            Check(vitals.State == before && inventory.Item(Key(5))!.Count == 3, "Unbound radiation behavior consumed an item or changed vitals.");
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
    private static byte[] Food(uint id, params byte[][] effects)
    {
        var header = new byte[20]; header[4] = 3;
        return Record("ALCH", id, Field("EDID", Text("Food" + id)), Field("DATA", BitConverter.GetBytes(1f)), Field("ENIT", header), Join(effects));
    }
    private static byte[] ReferenceGroup(params byte[][] references)
    {
        var data = Join(references); var bytes = new byte[24 + data.Length];
        Encoding.ASCII.GetBytes("GRUP").CopyTo(bytes, 0); UInt(bytes, 4, (uint)bytes.Length);
        UInt(bytes, 8, 0x80); UInt(bytes, 12, 6); data.CopyTo(bytes, 24); return bytes;
    }
    private static byte[] Modifier(uint id, uint archetype, uint flags, uint actorValue = 16)
    {
        var data = new byte[72]; UInt(data, 0, flags); UInt(data, 64, archetype); UInt(data, 68, actorValue);
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
