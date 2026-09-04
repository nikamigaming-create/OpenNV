using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Campaigns.Classic.Native;

namespace OpenNV.Runtime.Campaigns.Fallout2.Native;

internal sealed record Fo2Dat2Entry(
    string LogicalPath,
    bool Compressed,
    uint DecodedBytes,
    uint StoredBytes,
    long AbsoluteOffset);

internal sealed class Fo2Dat2Archive
{
    internal const int Dat2FormatContractFooterBytes = 8;
    private const int EntryTailBytes = 13;
    private readonly string _path;
    private readonly byte[]? _memory;
    private readonly Dictionary<string, Fo2Dat2Entry> _entries;

    internal Fo2Dat2Archive(string path)
    {
        _path = Path.GetFullPath(path);
        using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read);
        (_entries, DirectoryOffset, DirectoryBytes, DataBaseOffset) = ReadDirectory(stream, _path);
    }

    internal Fo2Dat2Archive(byte[] data, string source)
    {
        ArgumentNullException.ThrowIfNull(data);
        _path = source;
        _memory = data;
        using var stream = new MemoryStream(data, writable: false);
        (_entries, DirectoryOffset, DirectoryBytes, DataBaseOffset) = ReadDirectory(stream, source);
    }

    internal int Count => _entries.Count;
    internal long DirectoryOffset { get; }
    internal uint DirectoryBytes { get; }
    internal long DataBaseOffset { get; }
    internal IReadOnlyCollection<string> LogicalPaths => _entries.Keys;
    internal bool Contains(string logicalPath) => _entries.ContainsKey(CanonicalPath(logicalPath));

    internal byte[] Read(string logicalPath)
    {
        var canonical = CanonicalPath(logicalPath);
        if (!_entries.TryGetValue(canonical, out var entry))
            throw new FileNotFoundException($"DAT2 member is absent: {canonical}", _path);
        using var stream = OpenRead();
        stream.Position = entry.AbsoluteOffset;
        var stored = new byte[checked((int)entry.StoredBytes)];
        stream.ReadExactly(stored);
        if (!entry.Compressed)
            return stored;
        using var input = new MemoryStream(stored, writable: false);
        using var inflater = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream(checked((int)entry.DecodedBytes));
        inflater.CopyTo(output);
        if (output.Length != entry.DecodedBytes)
            throw new InvalidDataException($"DAT2 decoded size differs: {canonical}");
        return output.ToArray();
    }

    internal static string CanonicalPath(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || Path.IsPathRooted(value) || value.Contains(':'))
            throw new InvalidDataException("A DAT2 member path must be relative.");
        var parts = value.Replace('/', '\\').Trim('\\').Split('\\');
        if (parts.Length == 0 || parts.Any(part => string.IsNullOrWhiteSpace(part) || part is "." or ".."))
            throw new InvalidDataException("A DAT2 member path escapes its archive namespace.");
        return string.Join('\\', parts).ToLowerInvariant();
    }

    internal string DirectorySha256()
    {
        using var stream = OpenRead();
        stream.Position = DirectoryOffset;
        var bytes = new byte[checked((int)DirectoryBytes)];
        stream.ReadExactly(bytes);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private Stream OpenRead() => _memory is null
        ? new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read)
        : new MemoryStream(_memory, writable: false);

    private static (Dictionary<string, Fo2Dat2Entry>, long, uint, long) ReadDirectory(
        Stream stream,
        string source)
    {
        if (!stream.CanSeek || stream.Length < Dat2FormatContractFooterBytes + sizeof(uint))
            throw new InvalidDataException($"DAT2 archive is too small: {source}");
        Span<byte> footer = stackalloc byte[Dat2FormatContractFooterBytes];
        stream.Position = stream.Length - Dat2FormatContractFooterBytes;
        stream.ReadExactly(footer);
        var treeBytes = BinaryPrimitives.ReadUInt32LittleEndian(footer);
        var dataBytes = BinaryPrimitives.ReadUInt32LittleEndian(footer[sizeof(uint)..]);
        var dataBase = stream.Length - dataBytes;
        var treeOffset = stream.Length - treeBytes - Dat2FormatContractFooterBytes;
        if (treeBytes < sizeof(uint) || dataBytes > stream.Length || dataBase < 0 ||
            treeOffset < dataBase ||
            treeOffset + treeBytes != stream.Length - Dat2FormatContractFooterBytes)
            throw new InvalidDataException($"DAT2 footer bounds are invalid: {source}");
        stream.Position = treeOffset;
        var tree = new byte[checked((int)treeBytes)];
        stream.ReadExactly(tree);
        var cursor = 0;
        uint ReadUInt32(string label)
        {
            if (cursor > tree.Length - sizeof(uint))
                throw new InvalidDataException($"DAT2 directory is truncated at {label}: {source}");
            var value = BinaryPrimitives.ReadUInt32LittleEndian(tree.AsSpan(cursor));
            cursor += sizeof(uint);
            return value;
        }
        var count = ReadUInt32("file count");
        var entries = new Dictionary<string, Fo2Dat2Entry>(StringComparer.OrdinalIgnoreCase);
        var previous = string.Empty;
        for (var index = 0U; index < count; ++index)
        {
            var pathBytes = ReadUInt32($"entry {index} path length");
            if (pathBytes == 0 || pathBytes > int.MaxValue ||
                cursor > tree.Length - checked((int)pathBytes) - EntryTailBytes)
                throw new InvalidDataException($"DAT2 entry {index} path length is invalid: {source}");
            var encoded = tree.AsSpan(cursor, checked((int)pathBytes));
            cursor += checked((int)pathBytes);
            string decoded;
            try { decoded = new UTF8Encoding(false, true).GetString(encoded); }
            catch (Exception error)
            { throw new InvalidDataException($"DAT2 entry {index} path is not UTF-8: {source}", error); }
            var logicalPath = CanonicalPath(decoded);
            var compressed = tree[cursor++];
            if (compressed > 1)
                throw new InvalidDataException($"DAT2 compression flag is invalid: {logicalPath}");
            var decodedBytes = ReadUInt32($"{logicalPath} decoded size");
            var storedBytes = ReadUInt32($"{logicalPath} stored size");
            var relativeOffset = ReadUInt32($"{logicalPath} stored offset");
            if (compressed == 0 && decodedBytes != storedBytes ||
                (ulong)relativeOffset + storedBytes > (ulong)(treeOffset - dataBase) ||
                previous.Length != 0 && string.CompareOrdinal(previous, logicalPath) > 0 ||
                !entries.TryAdd(logicalPath, new Fo2Dat2Entry(
                    logicalPath, compressed == 1, decodedBytes, storedBytes,
                    checked(dataBase + relativeOffset))))
                throw new InvalidDataException($"DAT2 entry is invalid or duplicated: {logicalPath}");
            previous = logicalPath;
        }
        if (cursor != tree.Length)
            throw new InvalidDataException($"DAT2 directory has trailing bytes: {source}");
        return (entries, treeOffset, treeBytes, dataBase);
    }
}

internal sealed class Fo2NativeOwnedSource : IFalloutClassicOwnedSource
{
    private const string ProfileSchema = "opennv-fo2-owned-profile/v1";
    private const int Sha256Characters = 64;
    private readonly IReadOnlyList<Fo2Dat2Archive> _precedence;

    private Fo2NativeOwnedSource(string profileId, IReadOnlyList<Fo2Dat2Archive> precedence)
    {
        ProfileId = profileId;
        _precedence = precedence;
    }

    public string ProfileId { get; }
    internal IReadOnlyList<Fo2Dat2Archive> Archives => _precedence;

    internal static Fo2NativeOwnedSource Load(string profilePath)
    {
        var bytes = File.ReadAllBytes(Path.GetFullPath(profilePath));
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        var profileId = root.GetProperty("sourceProfileId").GetString() ?? string.Empty;
        if (root.GetProperty("schema").GetString() != ProfileSchema ||
            root.GetProperty("campaign").GetString() != "Fallout2" ||
            profileId.Length != Sha256Characters)
            throw new InvalidDataException("The Fallout 2 owned profile identity is invalid.");
        var byName = new Dictionary<string, Fo2Dat2Archive>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in root.GetProperty("install").GetProperty("archives").EnumerateArray())
        {
            var file = row.GetProperty("file").GetString() ?? string.Empty;
            var path = row.GetProperty("source").GetString() ?? string.Empty;
            if (Path.GetFileName(path) != file || !File.Exists(path) ||
                new FileInfo(path).Length != row.GetProperty("bytes").GetInt64())
                throw new InvalidDataException($"A registered Fallout 2 DAT changed or is missing: {file}");
            var archive = new Fo2Dat2Archive(path);
            var format = row.GetProperty("formatIdentity");
            if (format.GetProperty("format").GetString() != "fallout-dat2" ||
                format.GetProperty("footerBytes").GetInt32() !=
                    Fo2Dat2Archive.Dat2FormatContractFooterBytes ||
                archive.DirectoryOffset != format.GetProperty("directoryOffset").GetInt64() ||
                archive.DirectoryBytes != format.GetProperty("directoryBytes").GetUInt32() ||
                archive.Count != format.GetProperty("entries").GetInt32() ||
                archive.DirectorySha256() != format.GetProperty("directorySha256").GetString() ||
                !byName.TryAdd(file, archive))
                throw new InvalidDataException($"A registered Fallout 2 DAT2 index differs: {file}");
        }
        var order = new[] { "patch000.dat", "critter.dat", "master.dat" };
        if (byName.Count != order.Length || order.Any(name => !byName.ContainsKey(name)))
            throw new InvalidDataException("Fallout 2 requires patch000.dat, critter.dat, and master.dat.");
        return new Fo2NativeOwnedSource(profileId, order.Select(name => byName[name]).ToArray());
    }

    internal static Fo2NativeOwnedSource LoadInstall(string installDirectory)
    {
        var installRoot = Path.GetFullPath(installDirectory);
        if (!Directory.Exists(installRoot))
            throw new DirectoryNotFoundException($"Fallout 2 install root does not exist: {installRoot}");
        var archives = new Dictionary<string, Fo2Dat2Archive>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in new[] { "patch000.dat", "critter.dat", "master.dat" })
        {
            var path = Directory.EnumerateFiles(installRoot)
                .SingleOrDefault(candidate => Path.GetFileName(candidate).Equals(name, StringComparison.OrdinalIgnoreCase))
                ?? throw new FileNotFoundException($"Fallout 2 requires {name} in the selected root.", installRoot);
            archives.Add(name, new Fo2Dat2Archive(path));
        }
        var profileId = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            string.Join("\0", new[]
            {
                installRoot,
                archives["patch000.dat"].DirectorySha256(),
                archives["critter.dat"].DirectorySha256(),
                archives["master.dat"].DirectorySha256(),
            }))))
            .ToLowerInvariant();
        return new Fo2NativeOwnedSource(profileId, new[]
        {
            archives["patch000.dat"], archives["critter.dat"], archives["master.dat"],
        });
    }

    public byte[] Read(string logicalPath, out int archiveIndex)
    {
        var canonical = Fo2Dat2Archive.CanonicalPath(logicalPath);
        for (var index = 0; index < _precedence.Count; ++index)
            if (_precedence[index].Contains(canonical))
            {
                archiveIndex = index;
                return _precedence[index].Read(canonical);
            }
        throw new FileNotFoundException($"No active Fallout 2 DAT contains {canonical}.");
    }

    public IReadOnlyList<string> EffectiveLogicalPaths(string prefix, string extension)
    {
        var canonicalPrefix = Fo2Dat2Archive.CanonicalPath(prefix).TrimEnd('\\') + "\\";
        return _precedence
            .SelectMany(archive => archive.LogicalPaths)
            .Where(path => path.StartsWith(canonicalPrefix, StringComparison.OrdinalIgnoreCase) &&
                path.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public void Dispose() { }
}

internal sealed record Fo2NativeMap(
    int Version,
    string Name,
    int EnteringTile,
    int EnteringElevation,
    int EnteringRotation,
    int Flags,
    int MapIndex,
    IReadOnlyDictionary<int, uint[]> Elevations,
    int ContentOffsetAfterTiles);

internal static class Fo2NativeMapReader
{
    private const int HeaderBytes = 0xec;
    private const int HeaderNameOffset = 0x04;
    private const int HeaderNameBytes = 16;
    private const int HeaderValuesOffset = 0x14;
    private const int MapFormatContractHeaderValueCount = 10;
    private const int Fallout1MapVersion = 19;
    private const int Fallout2MapVersion = 20;
    private const int MaximumElevation = 2;
    private const int MaximumRotation = 5;
    private const int EnteringTileIndex = 0;
    private const int EnteringElevationIndex = 1;
    private const int EnteringRotationIndex = 2;
    private const int LocalVariableCountIndex = 3;
    private const int FlagsIndex = 5;
    private const int GlobalVariableCountIndex = 7;
    private const int MapFormatContractMapIndexIndex = 8;
    private const int KnownFlagsMask = 0x0f;
    private const int ElevationZeroAbsentFlag = 0x02;
    private const int ElevationOneAbsentFlag = 0x04;
    private const int ElevationTwoAbsentFlag = 0x08;
    private const int TileEntries = 10000;
    private const int TileBytes = TileEntries * sizeof(uint);
    private const int MapWidth = 200;
    private const int MapHeight = 200;

    internal static Fo2NativeMap Read(byte[] data)
    {
        if (data.Length < HeaderBytes)
            throw new InvalidDataException("Fallout MAP header is truncated.");
        var version = ReadInt32(data, 0);
        if (version is not (Fallout1MapVersion or Fallout2MapVersion))
            throw new NotSupportedException($"Fallout MAP version {version} is unsupported.");
        var nameBytes = data.AsSpan(HeaderNameOffset, HeaderNameBytes);
        var terminator = nameBytes.IndexOf((byte)0);
        if (terminator <= 0)
            throw new InvalidDataException("Fallout MAP name is empty or unterminated.");
        var name = Encoding.ASCII.GetString(nameBytes[..terminator]);
        var values = Enumerable.Range(0, MapFormatContractHeaderValueCount)
            .Select(index => ReadInt32(data, HeaderValuesOffset + index * sizeof(int))).ToArray();
        if (values[EnteringTileIndex] is < 0 or >= MapWidth * MapHeight ||
            values[EnteringElevationIndex] is < 0 or > MaximumElevation ||
            values[EnteringRotationIndex] is < 0 or > MaximumRotation ||
            values[LocalVariableCountIndex] < 0 || values[GlobalVariableCountIndex] < 0 ||
            (values[FlagsIndex] & ~KnownFlagsMask) != 0 ||
            values[MapFormatContractMapIndexIndex] < -1)
            throw new InvalidDataException("Fallout MAP header values are outside the admitted contract.");
        var offset = HeaderBytes + checked((values[GlobalVariableCountIndex] +
            values[LocalVariableCountIndex]) * sizeof(int));
        var elevations = new Dictionary<int, uint[]>();
        for (var elevation = 0; elevation < 3; ++elevation)
        {
            var absentBit = elevation switch
            {
                0 => ElevationZeroAbsentFlag,
                1 => ElevationOneAbsentFlag,
                _ => ElevationTwoAbsentFlag,
            };
            if ((values[FlagsIndex] & absentBit) != 0) continue;
            if (offset > data.Length - TileBytes)
                throw new InvalidDataException($"Fallout MAP elevation {elevation} grid is truncated.");
            var tiles = new uint[TileEntries];
            for (var index = 0; index < tiles.Length; ++index)
                tiles[index] = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset + index * sizeof(uint)));
            elevations.Add(elevation, tiles);
            offset += TileBytes;
        }
        return new Fo2NativeMap(
            version,
            name,
            values[EnteringTileIndex],
            values[EnteringElevationIndex],
            values[EnteringRotationIndex],
            values[FlagsIndex],
            values[MapFormatContractMapIndexIndex],
            elevations,
            offset);
    }

    private static int ReadInt32(byte[] data, int offset) =>
        BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(offset, sizeof(int)));
}

internal sealed record Fo2NativeFrmFrame(
    uint Version,
    ushort StoredFps,
    ushort FramesPerDirection,
    int Rotation,
    short DirectionX,
    short DirectionY,
    ushort Width,
    ushort Height,
    short FrameX,
    short FrameY,
    byte[] PaletteIndexes);

internal static class Fo2NativeFrmReader
{
    private const int FrmFormatContractFramesOffset = 8;
    private const int DirectionXOffsetsOffset = 0x0a;
    private const int DirectionYOffsetsOffset = 0x16;
    private const int FrmFormatContractFrameXOffset = 8;
    private const int FrmFormatContractFrameYOffset = 10;
    private const int HeaderBytes = 0x3e;
    private const int RotationDataOffsetsOffset = 0x22;
    private const int FrameAreaBytesOffset = 0x3a;
    private const int FrameHeaderBytes = 12;
    private const int DirectionCount = 6;
    private const uint Fallout1FrmVersion = 3U;
    private const uint Fallout2FrmVersion = 4U;

    internal static Fo2NativeFrmFrame ReadFirstFrame(byte[] data, int rotation = 0)
    {
        if (data.Length < HeaderBytes || rotation is < 0 or >= DirectionCount)
            throw new InvalidDataException("Fallout FRM header or rotation is invalid.");
        var version = BinaryPrimitives.ReadUInt32BigEndian(data);
        var fps = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(4));
        var frames = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(FrmFormatContractFramesOffset));
        if (version is not (Fallout1FrmVersion or Fallout2FrmVersion) || frames == 0)
            throw new NotSupportedException($"Fallout FRM {version}/{frames} is unsupported.");
        var relativeOffset = BinaryPrimitives.ReadUInt32BigEndian(
            data.AsSpan(RotationDataOffsetsOffset + rotation * sizeof(uint)));
        var directionX = BinaryPrimitives.ReadInt16BigEndian(
            data.AsSpan(DirectionXOffsetsOffset + rotation * sizeof(short)));
        var directionY = BinaryPrimitives.ReadInt16BigEndian(
            data.AsSpan(DirectionYOffsetsOffset + rotation * sizeof(short)));
        var frameAreaBytes = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(FrameAreaBytesOffset));
        var frameAreaEnd = frameAreaBytes == 0
            ? data.Length
            : checked((int)Math.Min(data.Length, HeaderBytes + (long)frameAreaBytes));
        var cursor = checked(HeaderBytes + (int)relativeOffset);
        if (cursor > frameAreaEnd - FrameHeaderBytes)
            throw new InvalidDataException("Fallout FRM frame header escapes its frame area.");
        var width = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(cursor));
        var height = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(cursor + 2));
        var payloadBytes = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(cursor + 4));
        var x = BinaryPrimitives.ReadInt16BigEndian(data.AsSpan(cursor + FrmFormatContractFrameXOffset));
        var y = BinaryPrimitives.ReadInt16BigEndian(data.AsSpan(cursor + FrmFormatContractFrameYOffset));
        cursor += FrameHeaderBytes;
        if (width == 0 || height == 0 || payloadBytes != (uint)width * height ||
            payloadBytes > int.MaxValue || cursor > frameAreaEnd - (int)payloadBytes)
            throw new InvalidDataException("Fallout FRM first-frame dimensions are invalid.");
        return new Fo2NativeFrmFrame(
            version, fps, frames, rotation, directionX, directionY, width, height, x, y,
            data.AsSpan(cursor, (int)payloadBytes).ToArray());
    }
}
