using System.Buffers.Binary;
using System.Text;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

internal static class PlayerSkillContracts
{
    internal static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-player-skills-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var header = new byte[12]; Float(header, 0, 1.34f);
            var misc = new byte[8]; Float(misc, 4, 10);
            var ingestible = new byte[20]; UInt(ingestible, 0, 17);
            var ammo = new byte[20]; Float(ammo, 8, .25f);
            var condition = new byte[28]; condition[0] = 0x80; Float(condition, 4, 160); condition[8] = 14; UInt(condition, 12, 46);
            var badWeight = new byte[4]; Float(badWeight, 0, float.NaN);
            var bytes = Join(Record("TES4", 0, Field("HEDR", header)), Record("NPC_", 7, Field("ACBS", new byte[24])), Record("RACE", 8),
                Record("MISC", 1, Field("EDID", Text("Ballast")), Field("DATA", misc)),
                Record("ALCH", 2, Field("EDID", Text("Medicine")), Field("DATA", BitConverter.GetBytes(5f)), Field("ENIT", ingestible)),
                Record("AMMO", 3, Field("EDID", Text("Ammunition")), Field("DATA", new byte[13]), Field("DAT2", ammo)),
                Record("ALCH", 4, Field("EDID", Text("InvalidMedicine")), Field("DATA", badWeight), Field("ENIT", ingestible)),
                Record("ALCH", 5, Field("EDID", Text("TruncatedMedicine")), Field("DATA", new byte[3]), Field("ENIT", ingestible)),
                Record("PERK", 30, Field("PRKE", [1, 0, 0]), Field("DATA", BitConverter.GetBytes(31u)), Field("PRKF", [])),
                Record("SPEL", 31, Field("SPIT", Declaration(4)), Effect(34, 1, condition)),
                Modifier(34, 10, 6), Modifier(41, 41, 2),
                Record("ENCH", 40, Field("ENIT", Declaration(3)), Effect(41, 2)),
                Record("ARMO", 42, Field("EDID", Text("SkillOutfit")), Field("DATA", new byte[12]),
                    Field("BMDT", [4, 0, 0, 0, 0, 0, 0, 0]), Field("EITM", BitConverter.GetBytes(40u))),
                Record("SPEL", 49, Field("SPIT", Declaration(0)), Effect(41, 3)),
                Setting(50, "fAVDSkillSmallGunsBase", 2), Setting(51, "fAVDSkillPrimaryBonusMult", 2),
                Setting(52, "fAVDSkillLuckBonusMult", .5f), Setting(53, "fAVDTagSkillBonus", 11));
            var path = Path.Combine(directory, "Test.esm"); File.WriteAllBytes(path, bytes);
            using var records = FalloutPluginStack.Load(directory, ["Test.esm"]);
            FalloutFormKey Key(uint id) => new("Test.esm", id);
            var inventory = new FalloutPlayerInventory();
            inventory.Add(records, Key(1), 15, 1, true); inventory.Add(records, Key(3), 50, 1, true);
            inventory.Add(records, Key(42), 1, 1, true); inventory.Equip(records, Key(42));
            var hardcore = false; var tagged = true;
            var skills = new FalloutPlayerSkills(records, () => new(5, 5, 5, 5, 5, 5, 5), _ => tagged,
                () => [new(30, "ConditionalTrait", "Conditional trait")], null, inventory, Key(7), () => Key(8), () => hardcore);
            Check(skills.Value(46) == 150 && skills.Value("Agility") == 4 && skills.Value("Guns") == 26,
                "Conditional SPECIAL, source tag bonus, luck rounding or worn effect failed.");
            inventory.Add(records, Key(2), 2, 1, true);
            Check(inventory.Item(Key(2)) is { Value: 17, Weight: 5 } && skills.Value(46) == 160 && skills.Value("Guns") == 28,
                "Ingestible source economics did not update the live trait condition.");
            inventory.Remove(Key(2), 2, true); hardcore = true;
            Check(skills.Value(46) == 162.5f && skills.Value("Guns") == 28, "Hardcore ammunition weight did not change the same skill owner.");
            hardcore = false; inventory.Unequip(records, Key(42));
            Check(skills.Value("Guns") == 24, "Unequipped clothing kept its skill modifier.");
            tagged = false; Check(skills.Value("Guns") == 13, "Removed tag bonus persisted in base skills.");
            Reject(() => inventory.Add(records, Key(4), 1, 1, true));
            Reject(() => inventory.Add(records, Key(5), 1, 1, true));
            Check(inventory.Item(Key(4)) is null && inventory.Item(Key(5)) is null, "Invalid ingestible was partially published.");
            Reject(() => new FalloutAbilityModifiers(records).Spell(Key(49)));
            Console.WriteLine("OPENNV_PLAYER_SKILL_CONTRACT_PASS sourceSettings=true liveTrait=true equipment=true hardcoreWeight=true ingestible=true invalid=true");
        }
        finally
        {
            File.Delete(Path.Combine(directory, "Test.esm"));
            Directory.Delete(directory);
        }
    }

    private static byte[] Declaration(uint type) { var result = new byte[16]; UInt(result, 0, type); return result; }
    private static byte[] Modifier(uint id, int value, uint flags)
    {
        var data = new byte[72]; UInt(data, 0, flags); UInt(data, 68, (uint)value);
        return Record("MGEF", id, Field("DATA", data));
    }
    private static byte[] Effect(uint id, uint magnitude, byte[]? condition = null)
    {
        var data = new byte[20]; UInt(data, 0, magnitude);
        return Join(Field("EFID", BitConverter.GetBytes(id)), Field("EFIT", data), condition is null ? [] : Field("CTDA", condition));
    }
    private static byte[] Setting(uint id, string name, float value) => Record("GMST", id, Field("EDID", Text(name)), Field("DATA", BitConverter.GetBytes(value)));
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
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception error) when (error is InvalidDataException or NotSupportedException) { return; }
        throw new InvalidOperationException("Unsupported source state was accepted.");
    }
}
