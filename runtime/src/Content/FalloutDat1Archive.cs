using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutDat1Entry(
    string LogicalPath,
    bool Compressed,
    uint UncompressedBytes,
    uint StoredBytes,
    uint StoredOffset);

internal sealed class FalloutDat1Archive
{
    private const uint StoredFlag = 0x20;
    private const uint LzssFlag = 0x40;
    private const int MinimumArchiveBytes = 16;
    private const uint MaximumFolderCount = 65_535;
    private const uint MaximumFilesPerFolder = 1_000_000;
    private const int DictionaryBytes = 4_096;
    private const int InitialWriteCursor = DictionaryBytes - 18;
    private const int DictionaryMask = DictionaryBytes - 1;
    private const int LzssControlFlagBits = sizeof(ulong);
    private const int DirectoryHashBufferBytes = 64 * 1_024;

    private readonly string _path;
    private readonly long _length;
    private readonly ReadOnlyDictionary<string, FalloutDat1Entry> _entries;

    internal FalloutDat1Archive(string path)
    {
        _path = Path.GetFullPath(path);
        var info = new FileInfo(_path);
        if (!info.Exists)
            throw new FileNotFoundException("Fallout DAT1 archive does not exist.", _path);
        _length = info.Length;
        if (_length < MinimumArchiveBytes)
            throw new InvalidDataException($"DAT1 archive is too small: {_path}");

        using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var folderCount = ReadUInt32(stream, "folder count");
        if (folderCount is 0 or > MaximumFolderCount)
            throw Error($"folder count is invalid: {folderCount}");
        HeaderValues = new ReadOnlyCollection<uint>(
            Enumerable.Range(0, 3).Select(index => ReadUInt32(stream, $"header value {index}")).ToArray());
        var folders = Enumerable.Range(0, checked((int)folderCount))
            .Select(index => ReadPascalAscii(stream, $"folder {index}"))
            .ToArray();

        var entries = new Dictionary<string, FalloutDat1Entry>(StringComparer.OrdinalIgnoreCase);
        for (var folderIndex = 0; folderIndex < folders.Length; ++folderIndex)
        {
            var folder = folders[folderIndex];
            var fileCount = ReadUInt32(stream, $"folder {folderIndex} file count");
            if (fileCount > MaximumFilesPerFolder)
                throw Error($"folder {folder} file count is invalid: {fileCount}");
            for (var index = 0; index < 3; ++index)
                _ = ReadUInt32(stream, $"folder {folderIndex} metadata {index}");
            var previousFilename = string.Empty;
            for (var fileIndex = 0; fileIndex < fileCount; ++fileIndex)
            {
                var filename = ReadPascalAscii(stream, $"folder {folderIndex} file {fileIndex}");
                var flag = ReadUInt32(stream, $"member {filename} attributes");
                if (flag is not (StoredFlag or LzssFlag))
                    throw Error($"member {filename} has unsupported flag 0x{flag:x2}");
                var storedOffset = ReadUInt32(stream, $"member {filename} offset");
                var uncompressedBytes = ReadUInt32(stream, $"member {filename} unpacked size");
                var declaredStoredBytes = ReadUInt32(stream, $"member {filename} stored size");
                var storedBytes = flag == LzssFlag ? declaredStoredBytes : uncompressedBytes;
                if (storedOffset > _length || storedBytes > _length - storedOffset)
                    throw Error($"member {filename} escapes the archive");
                if (flag == StoredFlag && declaredStoredBytes is not 0 && declaredStoredBytes != uncompressedBytes)
                    throw Error($"stored member {filename} has invalid packed size");
                var logicalPath = CanonicalPath(folder == "." ? filename : $"{folder}\\{filename}");
                if (!entries.TryAdd(logicalPath, new FalloutDat1Entry(
                        logicalPath, flag == LzssFlag, uncompressedBytes, storedBytes, storedOffset)))
                    throw Error($"duplicate member path: {logicalPath}");
                if (previousFilename.Length > 0 && string.Compare(
                        previousFilename.ToLowerInvariant(),
                        filename.ToLowerInvariant(),
                        StringComparison.Ordinal) > 0)
                    throw Error(
                        $"folder {folder} is not sorted case-insensitively: {previousFilename} before {filename}");
                previousFilename = filename;
            }
        }

        var firstMemberOffset = entries.Count == 0
            ? _length
            : entries.Values.Min(entry => (long)entry.StoredOffset);
        if (stream.Position > firstMemberOffset)
            throw Error("directory overlaps archive member data");
        DirectoryBytes = stream.Position;
        _entries = new ReadOnlyDictionary<string, FalloutDat1Entry>(entries);
    }

    internal IReadOnlyList<uint> HeaderValues { get; }
    internal long DirectoryBytes { get; }
    internal IReadOnlyDictionary<string, FalloutDat1Entry> Entries => _entries;

    internal string DirectorySha256()
    {
        using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[DirectoryHashBufferBytes];
        var remaining = DirectoryBytes;
        while (remaining > 0)
        {
            var read = stream.Read(buffer, 0, checked((int)Math.Min(buffer.Length, remaining)));
            if (read == 0)
                throw Error("directory ended while hashing");
            hash.AppendData(buffer, 0, read);
            remaining -= read;
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    internal bool Contains(string logicalPath) => _entries.ContainsKey(CanonicalPath(logicalPath));

    internal byte[] Read(string logicalPath)
    {
        var canonical = CanonicalPath(logicalPath);
        if (!_entries.TryGetValue(canonical, out var entry))
            throw new FileNotFoundException($"DAT1 member not found: {canonical}", _path);
        var payload = new byte[checked((int)entry.StoredBytes)];
        using (var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            stream.Position = entry.StoredOffset;
            stream.ReadExactly(payload);
        }
        return entry.Compressed ? DecodeLzssBlocks(payload, entry.UncompressedBytes) : payload;
    }

    internal static string CanonicalPath(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || Path.IsPathRooted(value) || value.Contains(':'))
            throw new InvalidDataException("A DAT1 member path must be relative.");
        var segments = value.Replace('/', '\\').Trim('\\').Split('\\');
        if (segments.Length == 0 || segments.Any(segment =>
                string.IsNullOrWhiteSpace(segment) || segment is "." or ".."))
            throw new InvalidDataException("A DAT1 member path escapes the archive namespace.");
        return string.Join('\\', segments).ToLowerInvariant();
    }

    private static byte[] DecodeLzssBlocks(ReadOnlySpan<byte> payload, uint expectedBytes)
    {
        if (expectedBytes > int.MaxValue)
            throw new InvalidDataException($"DAT1 member declares unsupported size {expectedBytes}.");
        var output = new List<byte>(checked((int)expectedBytes));
        var dictionary = Enumerable.Repeat((byte)' ', DictionaryBytes).ToArray();
        var writeCursor = InitialWriteCursor;
        var cursor = 0;
        while (cursor + sizeof(short) <= payload.Length)
        {
            var blockSize = BinaryPrimitives.ReadInt16BigEndian(payload[cursor..]);
            cursor += sizeof(short);
            if (blockSize == 0)
                break;
            var storedBytes = Math.Abs((int)blockSize);
            if (storedBytes > payload.Length - cursor)
                throw new InvalidDataException("DAT1 LZSS block escapes the stored member.");
            var block = payload.Slice(cursor, storedBytes);
            cursor += storedBytes;
            if (blockSize < 0)
            {
                AppendBlock(output, block, expectedBytes);
                continue;
            }

            var blockCursor = 0;
            while (blockCursor < block.Length)
            {
                var flags = block[blockCursor++];
                for (var bit = 0; bit < LzssControlFlagBits && blockCursor < block.Length; ++bit)
                {
                    if ((flags & (1 << bit)) != 0)
                    {
                        var value = block[blockCursor++];
                        Append(output, value, expectedBytes);
                        dictionary[writeCursor] = value;
                        writeCursor = (writeCursor + 1) & DictionaryMask;
                        continue;
                    }
                    if (block.Length - blockCursor < 2)
                        throw new InvalidDataException("DAT1 LZSS back-reference is truncated.");
                    var low = block[blockCursor++];
                    var high = block[blockCursor++];
                    var readCursor = low | ((high & 0xf0) << 4);
                    var length = (high & 0x0f) + 3;
                    for (var index = 0; index < length; ++index)
                    {
                        var value = dictionary[readCursor];
                        readCursor = (readCursor + 1) & DictionaryMask;
                        Append(output, value, expectedBytes);
                        dictionary[writeCursor] = value;
                        writeCursor = (writeCursor + 1) & DictionaryMask;
                    }
                }
            }
        }
        if (cursor != payload.Length)
            throw new InvalidDataException("DAT1 LZSS member has trailing or truncated stored bytes.");
        if (output.Count != expectedBytes)
            throw new InvalidDataException(
                $"DAT1 inflated size mismatch: expected {expectedBytes}, found {output.Count}.");
        return output.ToArray();
    }

    private static void AppendBlock(List<byte> output, ReadOnlySpan<byte> source, uint expectedBytes)
    {
        if (source.Length > expectedBytes - output.Count)
            throw new InvalidDataException("DAT1 LZSS output exceeds the declared size.");
        foreach (var value in source)
            output.Add(value);
    }

    private static void Append(List<byte> output, byte value, uint expectedBytes)
    {
        if (output.Count >= expectedBytes)
            throw new InvalidDataException("DAT1 LZSS output exceeds the declared size.");
        output.Add(value);
    }

    private static uint ReadUInt32(Stream stream, string label)
    {
        Span<byte> bytes = stackalloc byte[sizeof(uint)];
        try
        {
            stream.ReadExactly(bytes);
        }
        catch (EndOfStreamException error)
        {
            throw new InvalidDataException($"DAT1 directory is truncated at {label}.", error);
        }
        return BinaryPrimitives.ReadUInt32BigEndian(bytes);
    }

    private string ReadPascalAscii(Stream stream, string label)
    {
        var length = stream.ReadByte();
        if (length <= 0)
            throw Error($"{label} has invalid length {length}");
        var bytes = new byte[length];
        try
        {
            stream.ReadExactly(bytes);
        }
        catch (EndOfStreamException error)
        {
            throw new InvalidDataException($"DAT1 directory is truncated at {label}.", error);
        }
        if (bytes.Any(value => value > 0x7f))
            throw Error($"{label} is not ASCII");
        return Encoding.ASCII.GetString(bytes);
    }

    private InvalidDataException Error(string detail) => new($"DAT1 {_path}: {detail}.");
}
