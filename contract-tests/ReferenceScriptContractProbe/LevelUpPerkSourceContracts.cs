using System.Buffers.Binary;
using System.Text;
using OpenNV.Runtime.Content;

internal static class LevelUpPerkSourceContracts
{
    internal static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-level-up-perks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var header = new byte[12]; BinaryPrimitives.WriteSingleLittleEndian(header, 1.34f);
            var condition = new byte[28]; condition[8] = 70;
            BinaryPrimitives.WriteSingleLittleEndian(condition.AsSpan(4), 1);
            BinaryPrimitives.WriteUInt32LittleEndian(condition.AsSpan(12), 0x701);
            var master = Join(Record("TES4", 0, Field("HEDR", header)),
                Record("PERK", 0x700, Field("EDID", Text("SourcePerk")), Field("DATA", [0, 2, 3, 1, 0])),
                Record("RACE", 0x701), Record("PERK", 0x702, Field("EDID", Text("NextSourcePerk")), Field("DATA", [0, 4, 1, 1, 0])));
            var plugin = Join(Record("TES4", 0, Field("HEDR", header), Field("MAST", Text("Master.esm")), Field("DATA", new byte[8])),
                Record("PERK", 0x700, Field("EDID", Text("SourcePerk")), Field("FULL", Text("Source perk")),
                    Field("DESC", Text("Synthetic source description.")), Field("DATA", [0, 2, 3, 1, 0]), Field("CTDA", condition),
                    Field("NNAM", BitConverter.GetBytes(0x702u)),
                    Field("PRKE", [1, 0, 7]), Field("DATA", BitConverter.GetBytes(0x01000801u)), Field("PRKF", []),
                    Field("PRKE", [2, 1, 5]), Field("DATA", [10, 2, 1]), Field("EPFT", [1]), Field("EPFD", BitConverter.GetBytes(2f)), Field("PRKF", [])),
                Record("SPEL", 0x01000801),
                Record("PERK", 0x01000802, Field("EDID", Text("Unterminated")), Field("DATA", [0, 2, 1, 1, 0]),
                    Field("PRKE", [1, 0, 0]), Field("DATA", BitConverter.GetBytes(0x01000801u))),
                Record("PERK", 0x01000803, Field("EDID", Text("ShortDeclaration")), Field("DATA", [0, 2, 1])),
                Record("PERK", 0x01000804, Field("EDID", Text("InvalidFlag")), Field("DATA", [2, 2, 1, 1, 0])),
                Record("PERK", 0x01000805, Field("EDID", Text("WrongEffectType")), Field("DATA", [0, 2, 1, 1, 0]),
                    Field("PRKE", [1, 0, 0]), Field("DATA", BitConverter.GetBytes(0x701u)), Field("PRKF", [])),
                Record("PERK", 0x01000806, Field("EDID", Text("Overlapping")), Field("DATA", [0, 2, 1, 1, 0]),
                    Field("PRKE", [1, 0, 0]), Field("DATA", BitConverter.GetBytes(0x01000801u)), Field("PRKE", [1, 0, 0]), Field("PRKF", [])));
            File.WriteAllBytes(Path.Combine(directory, "Master.esm"), master);
            File.WriteAllBytes(Path.Combine(directory, "Test.esm"), plugin);
            using var records = FalloutPluginStack.Load(directory, ["Master.esm", "Test.esm"]);
            var source = FalloutLevelUpPerkSource.Read(records, new("Master.esm", 0x700));
            Require(source.Form == new FalloutFormKey("Master.esm", 0x700) && source.MinimumLevel == 2 && source.MaximumRank == 3 &&
                source.Playable && !source.Trait && !source.Hidden && source.Conditions.Single().FormArgument1 == new FalloutFormKey("Master.esm", 0x701) &&
                source.Effects.Count == 2 && source.Effects[0].Form == new FalloutFormKey("Test.esm", 0x801) && source.Effects[1].Rank == 1 &&
                source.Effects[1].EntryPoint == 10 && source.NextPerk == new FalloutFormKey("Master.esm", 0x702),
                "Winning override metadata, condition/master context or ranked effect identity was lost.");
            foreach (var id in new uint[] { 0x802, 0x803, 0x804, 0x805, 0x806 })
                Reject(() => FalloutLevelUpPerkSource.Read(records, new("Test.esm", id)));
            Console.WriteLine("OPENNV_LEVEL_UP_PERK_SOURCE_CONTRACT_PASS fullReader=true winner=true masters=true rankMetadata=true malformed=true");
        }
        finally
        {
            File.Delete(Path.Combine(directory, "Test.esm")); File.Delete(Path.Combine(directory, "Master.esm")); Directory.Delete(directory);
        }
    }

    private static byte[] Text(string text) => Encoding.ASCII.GetBytes(text + '\0');
    private static byte[] Join(params byte[][] values) => values.SelectMany(value => value).ToArray();
    private static byte[] Field(string name, byte[] data)
    {
        var bytes = new byte[6 + data.Length]; Encoding.ASCII.GetBytes(name).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), checked((ushort)data.Length)); data.CopyTo(bytes, 6); return bytes;
    }
    private static byte[] Record(string name, uint id, params byte[][] fields)
    {
        var data = Join(fields); var bytes = new byte[24 + data.Length]; Encoding.ASCII.GetBytes(name).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)data.Length); BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), id);
        data.CopyTo(bytes, 24); return bytes;
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception error) when (error is InvalidDataException or NotSupportedException) { return; }
        throw new InvalidOperationException("Malformed perk source was admitted.");
    }
}
