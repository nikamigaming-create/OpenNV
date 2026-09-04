using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace OpenNV.Runtime.Content;

internal sealed class FalloutBsaArchive
{
    private const uint ExpectedMagic = 0x00415342;
    private const uint ExpectedVersion = 104;
    private const uint DirectoryNamesFlag = 0x0001;
    private const uint FileNamesFlag = 0x0002;
    private const uint ArchiveCompressedFlag = 0x0004;
    private const uint EmbeddedNamesFlag = 0x0100;
    private const uint CompressedOverrideFlag = 0x40000000;
    private const uint ReservedFileFlag = 0x80000000;
    private const uint FileSizeMask = 0x3fffffff;
    private const int HeaderBytes = 36;
    private const int FolderRecordBytes = 16;
    private const int FileRecordBytes = 16;

    private readonly string _path;
    private readonly bool _embeddedNames;
    private readonly Dictionary<string, Member> _members;

    internal int FolderCount { get; }
    internal int FileCount { get; }
    internal int DirectoryTableReadOperations { get; }
    internal long DirectoryTableBytes { get; }

    internal FalloutBsaArchive(string path)
        : this(path, useOffsetDirectoryForAudit: false)
    {
    }

    internal FalloutBsaArchive(string path, bool useOffsetDirectoryForAudit)
    {
        _path = Path.GetFullPath(path);
        using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        if (stream.Length < HeaderBytes || reader.ReadUInt32() != ExpectedMagic)
            throw new InvalidDataException($"Not a Fallout BSA archive: {_path}");
        if (reader.ReadUInt32() != ExpectedVersion)
            throw new InvalidDataException($"OpenNV requires Fallout BSA version {ExpectedVersion}: {_path}");
        var folderRecordsOffset = reader.ReadUInt32();
        var archiveFlags = reader.ReadUInt32();
        var folderCount = reader.ReadUInt32();
        var fileCount = reader.ReadUInt32();
        var totalFolderNameBytes = reader.ReadUInt32();
        var totalFileNameBytes = reader.ReadUInt32();
        _ = reader.ReadUInt32();
        var requiredFlags = DirectoryNamesFlag | FileNamesFlag;
        if ((archiveFlags & requiredFlags) != requiredFlags || folderRecordsOffset < HeaderBytes)
            throw new InvalidDataException($"BSA name tables are unavailable or invalid: {_path}");
        var folderTableBytes = checked((long)folderCount * FolderRecordBytes);
        if (folderRecordsOffset + folderTableBytes > stream.Length)
            throw new InvalidDataException($"BSA folder table is truncated: {_path}");
        stream.Position = folderRecordsOffset;
        var folders = new Folder[checked((int)folderCount)];
        for (var index = 0; index < folders.Length; ++index)
        {
            _ = reader.ReadUInt64();
            folders[index] = new Folder(reader.ReadUInt32(), reader.ReadUInt32());
        }

        var minimumFolderOffset = checked((long)folderRecordsOffset + folderTableBytes);
        var directory = useOffsetDirectoryForAudit
            ? null
            : TryReadSequentialDirectory(
                stream,
                folders,
                minimumFolderOffset,
                totalFolderNameBytes,
                totalFileNameBytes,
                fileCount);
        if (directory is null)
            directory = ReadOffsetDirectory(
                stream,
                reader,
                folders,
                minimumFolderOffset,
                totalFolderNameBytes,
                totalFileNameBytes,
                fileCount);
        var indexed = directory.Value.Members;
        var fileNames = directory.Value.FileNames;
        var folderBlocksEnd = directory.Value.FolderBlocksEnd;
        FolderCount = checked((int)folderCount);
        FileCount = checked((int)fileCount);
        DirectoryTableReadOperations = directory.Value.ReadOperations;
        DirectoryTableBytes = directory.Value.BytesRead;
        var archiveCompressed = (archiveFlags & ArchiveCompressedFlag) != 0;
        _embeddedNames = (archiveFlags & EmbeddedNamesFlag) != 0;
        var minimumDataOffset = checked(folderBlocksEnd + totalFileNameBytes);
        _members = new Dictionary<string, Member>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < indexed.Count; ++index)
        {
            var row = indexed[index];
            if ((row.RawSize & ReservedFileFlag) != 0)
                throw new InvalidDataException($"BSA member uses a reserved size flag: {_path}");
            var storedBytes = row.RawSize & FileSizeMask;
            if (row.Offset < minimumDataOffset || checked((long)row.Offset + storedBytes) > stream.Length)
                throw new InvalidDataException($"BSA member data falls outside the archive: {_path}");
            var logicalPath = CanonicalPath($"{row.Folder}\\{fileNames[index]}");
            if (!_members.TryAdd(logicalPath, new Member(
                    row.Offset,
                    storedBytes,
                    archiveCompressed != ((row.RawSize & CompressedOverrideFlag) != 0))))
                throw new InvalidDataException($"Duplicate BSA member path: {logicalPath}");
        }
    }

    internal bool Contains(string logicalPath) => _members.ContainsKey(CanonicalPath(logicalPath));

    internal byte[] Read(string logicalPath)
    {
        var canonical = CanonicalPath(logicalPath);
        if (!_members.TryGetValue(canonical, out var member))
            throw new FileNotFoundException($"BSA member not found: {canonical}", _path);
        using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read);
        stream.Position = member.Offset;
        var payload = new byte[checked((int)member.StoredBytes)];
        stream.ReadExactly(payload);
        var content = _embeddedNames ? StripEmbeddedName(payload, canonical) : payload;
        if (!member.Compressed)
            return content;
        if (content.Length < sizeof(uint))
            throw new InvalidDataException($"Compressed BSA member is truncated: {canonical}");
        var expectedBytes = BitConverter.ToUInt32(content, 0);
        using var compressed = new MemoryStream(content, sizeof(uint), content.Length - sizeof(uint), writable: false);
        using var inflater = new ZLibStream(compressed, CompressionMode.Decompress);
        using var output = new MemoryStream(checked((int)expectedBytes));
        inflater.CopyTo(output);
        if (output.Length != expectedBytes)
            throw new InvalidDataException($"Inflated BSA member size differs: {canonical}");
        return output.ToArray();
    }

    internal static string CanonicalPath(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || Path.IsPathRooted(value) || value.Contains(':'))
            throw new InvalidDataException("A BSA member path must be relative.");
        var segments = value.Replace('/', '\\').Trim('\\').Split('\\');
        if (segments.Length == 0 || segments.Any(segment =>
                string.IsNullOrWhiteSpace(segment) || segment is "." or ".."))
            throw new InvalidDataException("A BSA member path escapes the archive namespace.");
        return string.Join('\\', segments).ToLowerInvariant();
    }

    private static byte[] StripEmbeddedName(byte[] payload, string expectedPath)
    {
        if (payload.Length == 0 || payload[0] >= payload.Length)
            throw new InvalidDataException($"Embedded BSA member name is truncated: {expectedPath}");
        var nameBytes = payload[0];
        var embedded = CanonicalPath(Encoding.UTF8.GetString(payload, 1, nameBytes));
        if (!string.Equals(embedded, expectedPath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Embedded BSA member name differs: {expectedPath}");
        return payload[(nameBytes + 1)..];
    }

    private static byte[] ReadExact(BinaryReader reader, int count, string label)
    {
        var value = reader.ReadBytes(count);
        if (value.Length != count)
            throw new InvalidDataException($"BSA {label} is truncated.");
        return value;
    }

    private DirectoryIndex? TryReadSequentialDirectory(
        FileStream stream,
        IReadOnlyList<Folder> folders,
        long minimumFolderOffset,
        uint totalFolderNameBytes,
        uint totalFileNameBytes,
        uint fileCount)
    {
        var folderBlockBytes = checked(
            (long)folders.Count + totalFolderNameBytes + (long)fileCount * FileRecordBytes);
        var directoryBytes = checked(folderBlockBytes + totalFileNameBytes);
        if (directoryBytes > int.MaxValue || minimumFolderOffset + directoryBytes > stream.Length)
            throw new InvalidDataException($"BSA directory tables are truncated: {_path}");
        var buffer = new byte[(int)directoryBytes];
        stream.Position = minimumFolderOffset;
        stream.ReadExactly(buffer);
        var cursor = 0;
        long observedFolderNameBytes = 0;
        var indexed = new List<IndexedMember>(checked((int)fileCount));
        foreach (var folder in folders)
        {
            var blockOffset = checked((long)folder.StoredOffset - totalFileNameBytes);
            if (blockOffset != minimumFolderOffset + cursor)
                return null;
            var folderNameBytes = buffer[cursor++];
            if (folderNameBytes == 0 || folderNameBytes > buffer.Length - cursor ||
                buffer[cursor + folderNameBytes - 1] != 0)
                throw new InvalidDataException($"BSA folder name is unterminated: {_path}");
            var folderName = CanonicalPath(
                Encoding.UTF8.GetString(buffer.AsSpan(cursor, folderNameBytes - 1)));
            cursor += folderNameBytes;
            observedFolderNameBytes += folderNameBytes;
            for (var index = 0U; index < folder.FileCount; ++index)
            {
                if (buffer.Length - cursor < FileRecordBytes)
                    throw new InvalidDataException($"BSA file table is truncated: {_path}");
                cursor += sizeof(ulong);
                var rawSize = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(cursor));
                cursor += sizeof(uint);
                var offset = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(cursor));
                cursor += sizeof(uint);
                indexed.Add(new IndexedMember(folderName, rawSize, offset));
            }
        }
        if (cursor != folderBlockBytes || indexed.Count != fileCount ||
            observedFolderNameBytes != totalFolderNameBytes)
            throw new InvalidDataException($"BSA header counts do not match its tables: {_path}");
        var fileNames = SplitNullTerminatedNames(
            buffer.AsSpan(cursor, checked((int)totalFileNameBytes)),
            checked((int)fileCount));
        return new DirectoryIndex(
            indexed,
            fileNames,
            minimumFolderOffset + folderBlockBytes,
            ReadOperations: 1,
            directoryBytes);
    }

    private DirectoryIndex ReadOffsetDirectory(
        FileStream stream,
        BinaryReader reader,
        IReadOnlyList<Folder> folders,
        long minimumFolderOffset,
        uint totalFolderNameBytes,
        uint totalFileNameBytes,
        uint fileCount)
    {
        var indexed = new List<IndexedMember>(checked((int)fileCount));
        long folderBlocksEnd = 0;
        long observedFolderNameBytes = 0;
        long bytesRead = 0;
        foreach (var folder in folders)
        {
            var blockOffset = checked((long)folder.StoredOffset - totalFileNameBytes);
            if (blockOffset < minimumFolderOffset || blockOffset >= stream.Length)
                throw new InvalidDataException($"BSA folder offset is invalid: {_path}");
            stream.Position = blockOffset;
            var folderNameBytes = reader.ReadByte();
            var rawFolderName = ReadExact(reader, folderNameBytes, "folder name");
            bytesRead += sizeof(byte) + folderNameBytes;
            if (rawFolderName.Length == 0 || rawFolderName[^1] != 0)
                throw new InvalidDataException($"BSA folder name is unterminated: {_path}");
            var folderName = CanonicalPath(Encoding.UTF8.GetString(rawFolderName, 0, rawFolderName.Length - 1));
            observedFolderNameBytes += folderNameBytes;
            for (var index = 0U; index < folder.FileCount; ++index)
            {
                _ = reader.ReadUInt64();
                indexed.Add(new IndexedMember(folderName, reader.ReadUInt32(), reader.ReadUInt32()));
                bytesRead += FileRecordBytes;
            }
            folderBlocksEnd = Math.Max(folderBlocksEnd, stream.Position);
        }
        if (indexed.Count != fileCount || observedFolderNameBytes != totalFolderNameBytes)
            throw new InvalidDataException($"BSA header counts do not match its tables: {_path}");
        stream.Position = folderBlocksEnd;
        var fileNameTable = ReadExact(reader, checked((int)totalFileNameBytes), "file-name table");
        bytesRead += totalFileNameBytes;
        return new DirectoryIndex(
            indexed,
            SplitNullTerminatedNames(fileNameTable, checked((int)fileCount)),
            folderBlocksEnd,
            checked(folders.Count + 1),
            bytesRead);
    }

    private static string[] SplitNullTerminatedNames(ReadOnlySpan<byte> table, int expectedCount)
    {
        if (table.Length == 0 || table[^1] != 0)
            throw new InvalidDataException("BSA file-name table is unterminated.");
        var names = new string[expectedCount];
        var offset = 0;
        for (var index = 0; index < names.Length; ++index)
        {
            var terminator = table[offset..].IndexOf((byte)0);
            if (terminator <= 0)
                throw new InvalidDataException("BSA file-name count differs from its header.");
            names[index] = Encoding.UTF8.GetString(table.Slice(offset, terminator));
            offset += terminator + 1;
        }
        if (table[offset..].IndexOfAnyExcept((byte)0) >= 0)
            throw new InvalidDataException("BSA file-name count differs from its header.");
        return names;
    }

    private readonly record struct Folder(uint FileCount, uint StoredOffset);
    private readonly record struct IndexedMember(string Folder, uint RawSize, uint Offset);
    private readonly record struct Member(uint Offset, uint StoredBytes, bool Compressed);
    private readonly record struct DirectoryIndex(
        List<IndexedMember> Members,
        string[] FileNames,
        long FolderBlocksEnd,
        int ReadOperations,
        long BytesRead);
}
