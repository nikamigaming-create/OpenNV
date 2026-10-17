using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;

internal static class MusicTypeContracts
{
    internal static void Run()
    {
        var directory = Directory.CreateTempSubdirectory("opennv-music-type-");
        try
        {
            File.WriteAllBytes(Path.Combine(directory.FullName, "Music.esm"), Join(Record("TES4", 0),
                Music(0x800, "scr/test.mp3", -7.74f), Music(0x801, "scr/test.mp3", 6),
                Music(0x802, "base/", 0), Music(0x803, "../escape.mp3", 0),
                Music(0x804, "test.mp3", float.NaN), Music(0x805, "test.mp3", -0f),
                Record("MUSC", 0x806), Music(0x807, "test.mp3", 0),
                Record("STAT", 0x808), Record("MUSC", 0x809, Field("ANAM", [1, 2])),
                Record("MUSC", 0x80a, Field("ANAM", [])), Record("MUSC", 0x80b, Field("FNAM", [])),
                Record("MUSC", 0x80c, Field("ANAM", new byte[4]), Field("ANAM", new byte[4]))));
            using var records = FalloutPluginStack.Load(directory.FullName, ["Music.esm"]);
            FalloutFormKey Key(uint id) => new("Music.esm", id);
            var once = FalloutMusicType.Read(records.GetEffective(Key(0x800)));
            var loop = FalloutMusicType.Read(records.GetEffective(Key(0x801)));
            Check(once.Path == "music/scr/test.mp3" && once.GainDb == -7.74f && !once.Loop &&
                loop.GainDb == -6 && loop.Loop, "Music lost its owned path, attenuation magnitude or loop sign.");
            Check(!FalloutMusicType.Read(records.GetEffective(Key(0x805))).Loop &&
                !FalloutMusicType.Read(records.GetEffective(Key(0x807))).Loop &&
                FalloutMusicType.Read(records.GetEffective(Key(0x806))).Path is null,
                "Zero attenuation or silent music manufactured looping playback.");
            foreach (var invalid in new uint[] { 0x802, 0x803, 0x804, 0x808, 0x809, 0x80a, 0x80b, 0x80c })
                Reject(() => FalloutMusicType.Read(records.GetEffective(Key(invalid))));
            var snapshot = new FalloutMusicPlaybackSnapshot(once.Form, once.RecordSha256, once.Path,
                new string('A', 64), 12, .5, true);
            var cold = JsonSerializer.Deserialize<FalloutMusicPlaybackSnapshot>(JsonSerializer.Serialize(snapshot))!;
            Check(FalloutMusicType.ValidateSnapshot(records, cold) == once, "Music source or cursor lost its cold representation.");
            _ = FalloutMusicType.ValidateSnapshot(records, snapshot with { Playing = false, PositionSeconds = 12 });
            foreach (var invalid in new[] { snapshot with { RecordSha256 = new string('0', 64) },
                snapshot with { Path = "music/other.mp3" }, snapshot with { MediaSha256 = "missing" },
                snapshot with { PositionSeconds = double.NaN }, snapshot with { PositionSeconds = 13 },
                snapshot with { Playing = false } })
                Reject(() => FalloutMusicType.ValidateSnapshot(records, invalid));
            Console.WriteLine("OPENNV_MUSIC_TYPE_PASS filename=true attenuation=true loopSign=true silent=true invalidRefused=true activeAndCompletedCold=true");
        }
        finally { directory.Delete(true); }
    }

    private static byte[] Music(uint id, string path, float gain) => Record("MUSC", id,
        Field("FNAM", Encoding.ASCII.GetBytes(path + '\0')), Field("ANAM", BitConverter.GetBytes(gain)));
    private static void Check(bool valid, string message) { if (!valid) throw new InvalidDataException(message); }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException) { return; }
        throw new InvalidDataException("Invalid music declaration or snapshot was admitted.");
    }
    private static byte[] Join(params byte[][] values) => values.SelectMany(value => value).ToArray();
    private static byte[] Field(string name, byte[] data)
    {
        var bytes = new byte[6 + data.Length]; Encoding.ASCII.GetBytes(name).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), checked((ushort)data.Length)); data.CopyTo(bytes, 6); return bytes;
    }
    private static byte[] Record(string name, uint id, params byte[][] fields)
    {
        var data = Join(fields); var bytes = new byte[24 + data.Length]; Encoding.ASCII.GetBytes(name).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), checked((uint)data.Length));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), id); data.CopyTo(bytes, 24); return bytes;
    }
}
