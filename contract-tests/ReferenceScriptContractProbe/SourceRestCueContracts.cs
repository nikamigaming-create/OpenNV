using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using OpenNV.Runtime.Content;

internal static class SourceRestCueContracts
{
    internal static void Run()
    {
        var folder = Directory.CreateTempSubdirectory("opennv-source-rest-cue-");
        var path = Path.Combine(folder.FullName, "AuthoredCue.esm");
        var fields = Join(Header(), Sound(0x810, "AuthoredExact", "ui/source.wav", FalloutSoundFlags.EnvironmentIgnored),
            Sound(0x811, "AuthoredDottedDirectory", "ui/variants.wav/", FalloutSoundFlags.MenuSound),
            Sound(0x812, "AuthoredFolder", "ui/variants/", FalloutSoundFlags.MenuSound),
            Sound(0x813, "AuthoredPitch", "ui/source.wav", FalloutSoundFlags.RandomFrequencyShift),
            Sound(0x814, "AuthoredStart", "ui/source.wav", FalloutSoundFlags.StartAtRandomPosition),
            Sound(0x815, "AuthoredLoop", "ui/source.wav", FalloutSoundFlags.Loop),
            Sound(0x816, "AuthoredSchedule", "ui/source.wav", FalloutSoundFlags.PlayAtRandom),
            Sound(0x817, "AuthoredOtherCodec", "ui/source.ogg", FalloutSoundFlags.MenuSound));
        File.WriteAllBytes(path, fields); var before = SHA256.HashData(fields);
        try
        {
            using var records = FalloutPluginStack.Load(folder.FullName, ["AuthoredCue.esm"]);
            foreach (var (engine, hardcore, carry) in new[]
            {
                ("518c87f58a6c4d9826e9ef8fbb7f4213882fa70822675610d45aea2464502a57", true, true),
                ("c3f97c2255fa041a851c17cf372d69aaadd8694e2dc4230ba556001bbfbd2f3e", false, false),
            })
            {
                var rest = new FalloutSleepWaitSource(engine, new('a', 64), new('b', 64),
                    FalloutSleepWaitSource.CurrentContractSha256, hardcore, carry, 24);
                var source = FalloutMenuCuePlaybackSource.Read(rest); source.Validate();
                FalloutSoundRecord Read(uint id) => FalloutSoundRecordReader.Read(records, new("AuthoredCue.esm", id));
                var exact = Read(0x810); var normalized = source.ExactFile(records, exact);
                Require(normalized.LogicalPath == exact.LogicalPath && normalized.FormKey == exact.FormKey &&
                    normalized.FixedPitchScale == exact.FixedPitchScale && normalized.StaticAttenuationDb == exact.StaticAttenuationDb &&
                    normalized.IsTwoDimensional && (normalized.Flags & FalloutSoundFlags.EnvironmentIgnored) == 0,
                    "Exact cue changed its winning media/gain/pitch or failed its genuine menu routing.");
                // The former extension-only admission loses the authored trailing
                // separator through archive canonicalization. The actual raw
                // source path must keep this directory request refused.
                Require(Read(0x811).HasExactFile, "Authored dotted directory did not exercise canonical separator loss.");
                foreach (var id in new uint[] { 0x811, 0x812, 0x813, 0x814, 0x815, 0x816, 0x817 })
                {
                    var refused = Read(id);
                    Reject<NotSupportedException>(() => source.ExactFile(records, refused));
                    // This invokes the actual factory's preflight. No Godot
                    // initialization or stream/player allocation can precede
                    // its specific original-selector refusal.
                    Reject<NotSupportedException>(() => NativeOwnedSoundPlayback.CreateSourceMenu(refused, records, source));
                }
                var changed = exact with { LogicalPath = "sound\\ui\\another.wav" };
                Reject<InvalidDataException>(() => source.ExactFile(records, changed));
                records.SoundPaths.Set(exact.FormKey, "ui\\changed.wav");
                Reject<InvalidDataException>(() => source.ExactFile(records, exact));
                var prepared = Read(0x810); Require(source.ExactFile(records, prepared).LogicalPath == "sound\\ui\\changed.wav",
                    "New exact cue failed the real mutable source path instead of preparing its current descriptor.");
                records.SoundPaths.Set(exact.FormKey, "ui\\changed.wav\\");
                Reject<NotSupportedException>(() => source.ExactFile(records, Read(0x810)));
                records.SoundPaths.Set(exact.FormKey, "ui/source.wav");
                Reject<InvalidDataException>(() => (source with { ContractSha256 = new('0', 64) }).Validate());
                Require(source.Identity != FalloutMenuCuePlaybackSource.Read(rest with { RuntimeSha256 = new('c', 64) }).Identity,
                    "Cue source ignored its actual selected runtime/dependency identity.");
            }
            Require(SHA256.HashData(File.ReadAllBytes(path)).AsSpan().SequenceEqual(before), "Cue reader changed its authored source input.");
        }
        finally { Directory.Delete(folder.FullName, true); }
        Console.WriteLine("OPENNV_SOURCE_REST_CUES_PASS authored=true exactNoRng=true originalRawDirectoryGuard=true " +
            "randomBeforeNativeRefusal=true currentPathDrift=true sourceUnchanged=true nativeAudio=unexecuted originalRng=unowned");
    }

    private static byte[] Sound(uint id, string name, string file, FalloutSoundFlags flags)
    {
        var data = new byte[36]; BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), (uint)flags);
        for (var index = 0; index < 5; index++) BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(12 + index * 2), 100);
        return Record("SOUN", id, Field("EDID", Encoding.ASCII.GetBytes(name + '\0')),
            Field("FNAM", Encoding.ASCII.GetBytes(file + '\0')), Field("SNDD", data));
    }
    private static byte[] Header() => Record("TES4", 0, Field("HEDR", Join(BitConverter.GetBytes(1.34f), new byte[8])));
    private static byte[] Record(string type, uint id, params byte[][] fields)
    {
        var data = Join(fields); var header = new byte[24]; Encoding.ASCII.GetBytes(type).CopyTo(header, 0);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4), data.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(12), id); return Join(header, data);
    }
    private static byte[] Field(string type, byte[] data) => Join(Encoding.ASCII.GetBytes(type), BitConverter.GetBytes(checked((ushort)data.Length)), data);
    private static byte[] Join(params byte[][] data) => data.SelectMany(row => row).ToArray();
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
    private static void Reject<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new InvalidDataException("Expected exact source cue refusal " + typeof(T).Name + ".");
    }
}
