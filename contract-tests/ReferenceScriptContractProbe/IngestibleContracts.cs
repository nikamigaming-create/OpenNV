using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

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
                Record("NPC_", 7, Field("ACBS", actor), Field("DATA", [100, 0, 0, 0, 5, 5, 5, 5, 5, 5, 5])),
                Item(1, Effect(10, 20, 0)), Item(2, Effect(11, 4, 6, true)),
                Item(3, Effect(10, 20, 0), Effect(12, 1, 0)),
                Item(4, Effect(13, 20, 99)), Item(5, Effect(14, 50, 0)),
                Modifier(10, 34, 0x70), Modifier(11, 0, 0x70), Modifier(12, 1, 0x70), Modifier(13, 0, 0xf0), Modifier(14, 0, 0x70, 54),
                Setting(100, "fMagicMedicineSkillBase", 1), Setting(101, "fMagicMedicineSkillMult", 2),
                Setting(102, "fAVDHealthEnduranceMult", 20), Setting(103, "fAVDHealthLevelMult", 5),
                Setting(104, "fAVDActionPointsBase", 65), Setting(105, "fAVDActionPointsMult", 3),
                Record("GMST", 106, Field("EDID", Text("iXPBase")), Field("DATA", BitConverter.GetBytes(200))),
                Record("GMST", 107, Field("EDID", Text("iXPBumpBase")), Field("DATA", BitConverter.GetBytes(150))));
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
            Console.WriteLine("OPENNV_INGESTIBLE_CONTRACT_PASS sourceMagnitude=true limbs=true atomic=true staleInput=true duration=true finalRemainder=true noDuration=true cold=true driftRejected=true");
        }
        finally { File.Delete(path); Directory.Delete(directory); }
    }

    private static byte[] Item(uint id, params byte[][] effects)
    {
        var header = new byte[20]; header[0] = 20; header[4] = 5;
        return Record("ALCH", id, Field("EDID", Text("Aid" + id)), Field("DATA", BitConverter.GetBytes(1f)), Field("ENIT", header), Join(effects));
    }
    private static byte[] Modifier(uint id, uint archetype, uint flags, uint actorValue = 16)
    {
        var data = new byte[72]; UInt(data, 0, flags); UInt(data, 64, archetype); UInt(data, 68, actorValue);
        return Record("MGEF", id, Field("DATA", data));
    }
    private static byte[] Effect(uint id, uint magnitude, uint duration, bool hardcore = false)
    {
        var data = new byte[20]; UInt(data, 0, magnitude); UInt(data, 8, duration); UInt(data, 16, 16);
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
