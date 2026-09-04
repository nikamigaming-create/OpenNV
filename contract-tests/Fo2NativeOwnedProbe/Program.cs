using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using OpenNV.Runtime.Campaigns.Fallout2.Native;

var syntheticMap = BuildMap("ARCAVES.MAP", 3, 28707);
var syntheticDat = BuildDat2(new Dictionary<string, (byte[] Data, bool Compressed)>
{
    ["maps\\arcaves.map"] = (syntheticMap, true),
    ["art\\tiles\\tiles.lst"] = (Encoding.ASCII.GetBytes("grid001.frm\r\n"), false),
});
var archive = new Fo2Dat2Archive(syntheticDat, "synthetic-memory.dat");
var decodedMap = Fo2NativeMapReader.Read(archive.Read("MAPS/ARCAVES.MAP"));
Require(archive.Count == 2 && decodedMap.MapIndex == 3 && decodedMap.EnteringTile == 28707 &&
    decodedMap.Elevations.Count == 1 && decodedMap.Elevations[0].Length == 10000,
    "Synthetic DAT2/MAP direct reader drifted.");
ExpectFailure(() => archive.Read("..\\escape"), "escapes");
Console.WriteLine("OPENNV_FO2_NATIVE_SYNTHETIC_PASS dat2=1 compressed=1 stored=1 map=1 failClosed=1 writes=0");

if (args.Length == 1)
{
    using var source = Fo2NativeOwnedSource.Load(args[0]);
    var map3Bytes = source.Read("maps\\arcaves.map", out var map3Archive);
    var templeBytes = source.Read("maps\\artemple.map", out var templeArchive);
    var map3 = Fo2NativeMapReader.Read(map3Bytes);
    var temple = Fo2NativeMapReader.Read(templeBytes);
    Require(map3.MapIndex == 3 && map3.Name.Equals("ARCAVES.MAP", StringComparison.OrdinalIgnoreCase) &&
        temple.MapIndex == 126 && temple.Name.Equals("ARTEMPLE.MAP", StringComparison.OrdinalIgnoreCase),
        "Owned Map 3/Temple identities drifted.");
    var tiles = Encoding.ASCII.GetString(source.Read("art\\tiles\\tiles.lst", out var listArchive))
        .Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n');
    var firstTileId = map3.Elevations[0]
        .Select(value => (int)(value & 0x0fffU)).First(id => id != 1);
    Require(firstTileId >= 0 && firstTileId < tiles.Length && !string.IsNullOrWhiteSpace(tiles[firstTileId]),
        "Owned Map 3 floor tile does not resolve through tiles.lst.");
    var frmPath = $"art\\tiles\\{tiles[firstTileId].Trim()}";
    var frm = Fo2NativeFrmReader.ReadFirstFrame(source.Read(frmPath, out var frmArchive));
    Require(frm.Width > 0 && frm.Height > 0 && frm.PaletteIndexes.Length == frm.Width * frm.Height,
        "Owned Map 3 floor FRM did not decode in memory.");
    _ = source.Read("color.pal", out var paletteArchive);
    Console.WriteLine(
        $"OPENNV_FO2_NATIVE_OWNED_PASS profile={source.ProfileId} archives={source.Archives.Count} " +
        $"members={source.Archives.Sum(row => row.Count)} map3Bytes={map3Bytes.Length} " +
        $"map3Archive={map3Archive} templeBytes={templeBytes.Length} templeArchive={templeArchive} " +
        $"tileId={firstTileId} frm={frmPath} frmArchive={frmArchive} frmSize={frm.Width}x{frm.Height} " +
        $"listArchive={listArchive} paletteArchive={paletteArchive} writes=0");
}

static byte[] BuildMap(string name, int mapIndex, int entryTile)
{
    const int headerBytes = 0xec;
    const int tileEntries = 10000;
    var data = new byte[headerBytes + tileEntries * sizeof(uint)];
    BinaryPrimitives.WriteInt32BigEndian(data, 20);
    Encoding.ASCII.GetBytes(name).CopyTo(data, 4);
    var values = new[] { entryTile, 0, 0, 0, -1, 0x0c, 0, 0, mapIndex, 0 };
    for (var index = 0; index < values.Length; ++index)
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(0x14 + index * sizeof(int)), values[index]);
    for (var index = 0; index < tileEntries; ++index)
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(headerBytes + index * sizeof(uint)), 1U);
    return data;
}

static byte[] BuildDat2(IReadOnlyDictionary<string, (byte[] Data, bool Compressed)> members)
{
    using var payload = new MemoryStream();
    using var tree = new MemoryStream();
    WriteUInt32(tree, (uint)members.Count);
    foreach (var pair in members.OrderBy(row => row.Key, StringComparer.Ordinal))
    {
        var path = Encoding.UTF8.GetBytes(pair.Key.ToLowerInvariant());
        var stored = pair.Value.Compressed ? Compress(pair.Value.Data) : pair.Value.Data;
        WriteUInt32(tree, (uint)path.Length);
        tree.Write(path);
        tree.WriteByte(pair.Value.Compressed ? (byte)1 : (byte)0);
        WriteUInt32(tree, (uint)pair.Value.Data.Length);
        WriteUInt32(tree, (uint)stored.Length);
        WriteUInt32(tree, (uint)payload.Position);
        payload.Write(stored);
    }
    var treeBytes = tree.ToArray();
    var dataBytes = payload.ToArray();
    using var archive = new MemoryStream();
    archive.Write(dataBytes);
    archive.Write(treeBytes);
    WriteUInt32(archive, (uint)treeBytes.Length);
    WriteUInt32(archive, (uint)archive.Length + sizeof(uint));
    return archive.ToArray();
}

static byte[] Compress(byte[] data)
{
    using var output = new MemoryStream();
    using (var zlib = new ZLibStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        zlib.Write(data);
    return output.ToArray();
}

static void WriteUInt32(Stream stream, uint value)
{
    Span<byte> bytes = stackalloc byte[sizeof(uint)];
    BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
    stream.Write(bytes);
}

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static void ExpectFailure(Action action, string fragment)
{
    try { action(); }
    catch (Exception error) when (error.Message.Contains(fragment, StringComparison.OrdinalIgnoreCase)) { return; }
    throw new InvalidOperationException($"Expected failure containing {fragment}.");
}
