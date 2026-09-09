using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;
using OpenNV.Runtime.World.Actors;

internal static class ActorDamageContracts
{
    internal static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-actor-damage-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var header = new byte[12]; Float(header, 0, 1.34f);
            var stats = new byte[17]; BinaryPrimitives.WriteInt16LittleEndian(stats.AsSpan(4), 50);
            var acbs = new byte[24]; acbs[8] = 1;
            var entry = new byte[12]; entry[0] = 1; UInt(entry, 4, 3); entry[8] = 2;
            var references = Join(Reference(0x91), Reference(0x92));
            var group = new byte[24 + references.Length]; Encoding.ASCII.GetBytes("GRUP").CopyTo(group, 0);
            UInt(group, 4, (uint)group.Length); UInt(group, 8, 0x80); UInt(group, 12, 6); references.CopyTo(group, 24);
            File.WriteAllBytes(Path.Combine(directory, "Test.esm"), Join(Record("TES4", 0, Field("HEDR", header)),
                Record("CREA", 1, Field("EDID", Text("SourceCreature")), Field("ACBS", acbs), Field("DATA", stats),
                    Field("PNAM", BitConverter.GetBytes(2u)), Field("INAM", BitConverter.GetBytes(4u)), Field("NAM4", BitConverter.GetBytes(6u))),
                Record("BPTD", 2, Part(0, 1, "Root"), Part(1, 2, "Neck")),
                Record("MISC", 3, Field("EDID", Text("SourceDeathLoot")), Field("DATA", new byte[8])),
                Record("LVLI", 4, Field("LVLD", [0]), Field("LVLF", [0]), Field("LVLO", entry)),
                Record("BPTD", 5, Field("BPNN", Text("Root")), Field("BPND", new byte[83])),
                Record("CELL", 0x80, Field("EDID", Text("SourceCell")), Field("DATA", [1])), group));
            using var records = FalloutPluginStack.Load(directory, ["Test.esm"]);
            FalloutFormKey Key(uint id) => new("Test.esm", id);
            var parts = FalloutBodyPartData.Read(records.GetEffective(Key(2)));
            Check(parts.Parts[1] is { Type: 1, DamageMultiplier: 2, ReplacementModel: "meshes/Gore/source-head.nif", GoreBone: "Neck" },
                "Post-BPND model/attachment fields escaped their source body part.");
            Reject(() => FalloutBodyPartData.Read(records.GetEffective(Key(5))));
            var cell = FalloutCellSceneReader.Read(records, Key(0x80));
            using var world = new FalloutReferenceWorld(records);
            world.LoadCell(cell);
            Check(world.ActorValue(Key(0x91), "Health") == 50 && !world.IsDead(Key(0x91)), "Source CREA health was not initialized.");
            var first = world.DamageActor(Key(0x91), Key(0x92), 0, 10, 1.5f, 1);
            Check(first is { HealthBefore: 50, HealthAfter: 40, LimbDamage: 15, Died: false } && world.Health(Key(0x92)).Current == 50,
                "Damage pools, limb multiplier or per-reference isolation failed.");
            var killed = world.DamageActor(Key(0x91), Key(0x92), 1, 25, 1, 1);
            Check(killed is { Died: true, Dead: true, HealthAfter: -10 } && world.Inventory(Key(0x91), 1).Contents.Item(Key(3))!.Count == 2,
                "Head multiplier, death transition or source death loot failed.");
            var player = new FalloutPlayerInventory();
            world.Inventory(Key(0x91), 1).Contents.TransferTo(player, Key(3), 1);
            var saved = JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(world.Capture()))!;
            using var cold = new FalloutReferenceWorld(records); cold.Restore(saved); cold.LoadCell(cell);
            var corpse = cold.DamageActor(Key(0x91), Key(0x92), 1, 25, 1, 1);
            Check(corpse is { Died: false, Dead: true, HealthDamage: 0, HealthAfter: -10 } &&
                cold.Get(Key(0x91)).Injury!.LimbDamage[1] == 50 && cold.Inventory(Key(0x91), 1).Contents.Item(Key(3))!.Count == 1,
                "Corpse hit resurrected health, lost limb damage or granted death loot twice after reload.");
            Reject(() => cold.DamageActor(Key(0x91), Key(0x92), 14, 1, 1, 1));
            var corrupt = saved.Select(value => value.Reference == Key(0x91) ? value with { Injury = value.Injury! with { Dead = false } } : value).ToArray();
            using var rejected = new FalloutReferenceWorld(records); Reject(() => rejected.Restore(corrupt));
            var body = new FalloutRagdollBodyState(7, [1, 0, 0, 0, 1, 0, 0, 0, 1, 2, 3, 4], [0, 0, 0], [0, 0, 0], true);
            var pose = new FalloutActorRagdollState(new string('a', 64), [body]); pose.Validate();
            Reject(() => (pose with { Bodies = [body, body] }).Validate());
            Reject(() => (pose with { Bodies = [body with { Transform = new float[12] }] }).Validate());
            Reject(() => (pose with { Bodies = [body with { LinearVelocity = [float.NaN, 0, 0] }] }).Validate());
            Reject(() => (pose with { Bodies = [null!] }).Validate());
            Reject(() => (pose with { SkeletonSha256 = "missing" }).Validate());
            using var livingPose = new FalloutReferenceWorld(records);
            Reject(() => livingPose.Restore(saved.Select(value => value.Reference == Key(0x92) ? value with { Ragdoll = pose } : value).ToArray()));
            using var savedPose = new FalloutReferenceWorld(records);
            savedPose.Restore(saved.Select(value => value.Reference == Key(0x91) ? value with { Ragdoll = pose } : value).ToArray());
            RequirePose(savedPose.Get(Key(0x91)).Capture().Ragdoll!, pose);
            Console.WriteLine("OPENNV_ACTOR_DAMAGE_CONTRACT_PASS health=source limbDamage=source deathLoot=once coldRestore=true corpseTransfer=conserved");
        }
        finally { Directory.Delete(directory, true); }
    }

    private static byte[] Part(byte type, float multiplier, string node)
    {
        var data = new byte[84]; Float(data, 0, multiplier); data[4] = 1; data[5] = type; data[6] = 25;
        Float(data, 24, 1); Float(data, 40, 1); Float(data, 80, 1);
        return Join(Field("BPNN", Text(node)), Field("BPNT", Text(node)), Field("BPND", data),
            Field("NAM1", Text(type == 1 ? "Gore/source-head.nif" : "")), Field("NAM4", Text(node)));
    }
    private static byte[] Reference(uint id) => Record("ACRE", id, Field("NAME", BitConverter.GetBytes(1u)), Field("DATA", new byte[24]));
    private static byte[] Text(string value) => Encoding.ASCII.GetBytes(value + '\0');
    private static byte[] Join(params byte[][] fields) => fields.SelectMany(value => value).ToArray();
    private static void Float(byte[] bytes, int offset, float value) => BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(offset), value);
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
    private static void RequirePose(FalloutActorRagdollState actual, FalloutActorRagdollState expected) =>
        Check(JsonSerializer.Serialize(actual) == JsonSerializer.Serialize(expected), "Cold reference restoration lost its death pose.");
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception error) when (error is InvalidDataException or NotSupportedException) { return; }
        throw new InvalidOperationException("Invalid actor damage state was accepted.");
    }
}
