using System.Buffers.Binary;
using System.Text;
using OpenNV.Runtime.Content;

var root = Path.Combine(Path.GetTempPath(), $"opennv-dat1-{Guid.NewGuid():N}");
Directory.CreateDirectory(root);
try
{
    var stored = Encoding.ASCII.GetBytes("stored-owned-bytes");
    var compressedSource = Encoding.ASCII.GetBytes("compressed-owned-bytes");
    var compressed = RawLzssBlock(compressedSource);
    var archivePath = Path.Combine(root, "master.dat");
    File.WriteAllBytes(archivePath, Archive([
        new FixtureEntry("compressed.bin", FalloutDat1Fixture.LzssFlag, compressedSource, compressed),
        new FixtureEntry("stored.bin", FalloutDat1Fixture.StoredFlag, stored, stored),
    ]));

    var archive = new FalloutDat1Archive(archivePath);
    Require(archive.Entries.Count == 2, "DAT1 entry count differs.");
    Require(archive.HeaderValues.SequenceEqual([1u, 2u, 3u]), "DAT1 header values differ.");
    Require(archive.Contains("STORED.BIN"), "DAT1 case-insensitive lookup failed.");
    Require(archive.Read("stored.bin").SequenceEqual(stored), "Stored DAT1 payload differs.");
    Require(archive.Read("COMPRESSED.BIN").SequenceEqual(compressedSource), "LZSS DAT1 payload differs.");
    ExpectFailure(() => archive.Read("../master.dat"), "escapes");

    var badFlag = Archive([
        new FixtureEntry("bad.bin", 0x80, stored, stored),
    ]);
    var badPath = Path.Combine(root, "bad.dat");
    File.WriteAllBytes(badPath, badFlag);
    ExpectFailure(() => _ = new FalloutDat1Archive(badPath), "unsupported flag");

    Console.WriteLine(
        $"OPENNV_FALLOUT_DAT1_RUNTIME_PROBE_PASS entries={archive.Entries.Count} " +
        $"stored={stored.Length} compressed={compressedSource.Length} cache=none");

    if (args.Length != 0)
    {
        if (args.Length != 2 || args[0] != "--owned-root")
            throw new ArgumentException("Usage: FalloutDat1RuntimeProbe [--owned-root <Fallout install>]");
        var ownedRoot = Path.GetFullPath(args[1]);
        var master = new FalloutDat1Archive(FindSingleFile(ownedRoot, "master.dat"));
        var critter = new FalloutDat1Archive(FindSingleFile(ownedRoot, "critter.dat"));
        var map = master.Read(@"maps\v13ent.map");
        var player = critter.Read(@"art\critters\hmjmpsaa.frm");
        Require(map.Length > 0 && player.Length > 0, "Owned DAT1 closure contains an empty member.");
        Console.WriteLine(
            $"OPENNV_FALLOUT_DAT1_OWNED_INPUT_PASS masterEntries={master.Entries.Count} " +
            $"critterEntries={critter.Entries.Count} map=maps/v13ent.map mapBytes={map.Length} " +
            $"player=art/critters/hmjmpsaa.frm playerBytes={player.Length} " +
            "source=live-owned-dat1 cache=none");
    }
}
finally
{
    Directory.Delete(root, recursive: true);
}

static byte[] Archive(IReadOnlyList<FixtureEntry> entries)
{
    var provisional = DirectoryBytes(entries, 0);
    var dataOffset = provisional.Length;
    var directory = DirectoryBytes(entries, dataOffset);
    return directory.Concat(entries.SelectMany(entry => entry.Stored)).ToArray();
}

static byte[] DirectoryBytes(IReadOnlyList<FixtureEntry> entries, int dataOffset)
{
    using var output = new MemoryStream();
    WriteUInt32(output, 1);
    WriteUInt32(output, 1);
    WriteUInt32(output, 2);
    WriteUInt32(output, 3);
    WritePascal(output, ".");
    WriteUInt32(output, checked((uint)entries.Count));
    WriteUInt32(output, 0);
    WriteUInt32(output, 0);
    WriteUInt32(output, 0);
    var offset = dataOffset;
    foreach (var entry in entries.OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase))
    {
        WritePascal(output, entry.Name);
        WriteUInt32(output, entry.Flag);
        WriteUInt32(output, checked((uint)offset));
        WriteUInt32(output, checked((uint)entry.Uncompressed.Length));
        WriteUInt32(output, entry.Flag == FalloutDat1Fixture.StoredFlag
            ? 0u
            : checked((uint)entry.Stored.Length));
        offset += entry.Stored.Length;
    }
    return output.ToArray();
}

static byte[] RawLzssBlock(byte[] source)
{
    if (source.Length > short.MaxValue)
        throw new ArgumentOutOfRangeException(nameof(source));
    var result = new byte[source.Length + sizeof(short) * 2];
    BinaryPrimitives.WriteInt16BigEndian(result, checked((short)-source.Length));
    source.CopyTo(result, sizeof(short));
    BinaryPrimitives.WriteInt16BigEndian(result.AsSpan(sizeof(short) + source.Length), 0);
    return result;
}

static void WriteUInt32(Stream output, uint value)
{
    Span<byte> bytes = stackalloc byte[sizeof(uint)];
    BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
    output.Write(bytes);
}

static void WritePascal(Stream output, string value)
{
    var bytes = Encoding.ASCII.GetBytes(value);
    output.WriteByte(checked((byte)bytes.Length));
    output.Write(bytes);
}

static void Require(bool condition, string message)
{
    if (!condition)
        throw new InvalidDataException(message);
}

static void ExpectFailure(Action action, string fragment)
{
    try
    {
        action();
    }
    catch (Exception error) when (error.Message.Contains(fragment, StringComparison.OrdinalIgnoreCase))
    {
        return;
    }
    throw new InvalidDataException($"Expected failure containing '{fragment}'.");
}

static string FindSingleFile(string root, string expectedName)
{
    var matches = Directory.EnumerateFiles(root, "*", SearchOption.TopDirectoryOnly)
        .Where(candidate => Path.GetFileName(candidate).Equals(expectedName, StringComparison.OrdinalIgnoreCase))
        .Take(2)
        .ToArray();
    if (matches.Length != 1)
        throw new FileNotFoundException($"Expected exactly one {expectedName} in {root}; found {matches.Length}.");
    return matches[0];
}

internal sealed record FixtureEntry(string Name, uint Flag, byte[] Uncompressed, byte[] Stored);

internal static class FalloutDat1Fixture
{
    internal const uint StoredFlag = 0x20;
    internal const uint LzssFlag = 0x40;
}
