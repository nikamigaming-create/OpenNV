using System.Buffers.Binary;
using System.Text;
using OpenNV.Runtime.Content;

internal static class CreatureSoundInheritanceContracts
{
    internal static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-creature-sounds-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var header = new byte[12]; BinaryPrimitives.WriteSingleLittleEndian(header, 1.34f);
            File.WriteAllBytes(Path.Combine(directory, "Sounds.esm"), Join(
                Record("TES4", 0, Field("HEDR", header)),
                Record("CREA", 1, Field("CSCR", U32(2)), Field("TPLT", U32(9))),
                Record("CREA", 2, Field("CSCR", U32(3))),
                Record("CREA", 3, Field("CSDT", U32(12)), Field("CSDI", U32(10)), Field("CSDC", [20]),
                    Field("CSDT", U32(2)), Field("CSDI", U32(11)), Field("CSDC", [100]),
                    Field("CSDT", U32(19)), Field("CSDI", U32(10)), Field("CSDC", [50])),
                Record("CREA", 4, Field("CSCR", U32(5))), Record("CREA", 5, Field("CSCR", U32(4))),
                Record("CREA", 6, Field("CSCR", U32(10))),
                Record("CREA", 7, Field("CSCR", [1, 2])),
                Record("CREA", 8, Field("CSCR", U32(3)), Field("CSDT", U32(12))),
                Record("CREA", 9, Field("CSDT", U32(12)), Field("CSDI", U32(11)), Field("CSDC", [100])),
                Record("SOUN", 10), Record("SOUN", 11)));
            using var records = FalloutPluginStack.Load(directory, ["Sounds.esm"]);
            FalloutFormKey Key(uint id) => new("Sounds.esm", id);
            var declaration = FalloutCreatureAnimationSounds.ReadSingleSound(records, Key(1), "Auxiliary1");
            if (declaration.Creature != Key(1) || declaration.Sound != Key(10) || declaration.Chance != 20)
                throw new InvalidDataException("Inherited sound lost the actor identity, source path or candidate.");
            var draws = 0;
            var selected = declaration.Select(bound => { draws++; return bound - 1; });
            if (selected.Kind != FalloutCreatureAnimationSounds.SelectionKind.ChanceMiss || draws != 1)
                throw new InvalidDataException("Inherited sound changed the shared chance draw.");
            var contact = FalloutCreatureAnimationSounds.ReadSingleSound(records, Key(1), "Enum: BackLeft");
            var auxiliary = FalloutCreatureAnimationSounds.ReadSingleSound(records, Key(1), "Auxiliary8");
            var missing = FalloutCreatureAnimationSounds.ReadSingleSound(records, Key(1), "Enum: BackRight");
            if (contact.Type != 2 || contact.Sound != Key(11) || contact.Chance != 100 ||
                auxiliary.Type != 19 || auxiliary.Sound != Key(10) || auxiliary.Chance != 50 ||
                missing.Select(_ => throw new InvalidDataException("Absent sound consumed shared RNG.")).Kind !=
                    FalloutCreatureAnimationSounds.SelectionKind.DeclaredNull)
                throw new InvalidDataException("Creature contact/auxiliary enum source indices or absent declaration changed.");
            foreach (var malformed in new[] { "Enum: Eight", "Eum: Left", "Enum: Auxiliary 3" })
                if (FalloutCreatureAnimationSounds.TryType(malformed, out _))
                    throw new InvalidDataException("Malformed authored enum acquired a fabricated alias.");
            foreach (var id in new uint[] { 4, 6, 7, 8 })
            {
                try { _ = FalloutCreatureAnimationSounds.ReadSingleSound(records, Key(id), "Auxiliary1"); }
                catch (Exception error) when (error is InvalidDataException or NotSupportedException) { continue; }
                throw new InvalidDataException($"Invalid inherited sound source {id} was accepted.");
            }
            Console.WriteLine("OPENNV_CREATURE_SOUND_INHERITANCE_PASS chain=true actorIdentity=true templateIndependent=true cycle=true malformed=true conflicting=true random=true retail=unverified");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static byte[] U32(uint value) => BitConverter.GetBytes(value);
    private static byte[] Join(params byte[][] parts) => parts.SelectMany(value => value).ToArray();
    private static byte[] Field(string name, byte[] data)
    {
        var result = new byte[6 + data.Length]; Encoding.ASCII.GetBytes(name).CopyTo(result, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(4), checked((ushort)data.Length)); data.CopyTo(result, 6); return result;
    }
    private static byte[] Record(string name, uint id, params byte[][] fields)
    {
        var data = Join(fields); var result = new byte[24 + data.Length]; Encoding.ASCII.GetBytes(name).CopyTo(result, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), (uint)data.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(12), id); data.CopyTo(result, 24); return result;
    }
}
