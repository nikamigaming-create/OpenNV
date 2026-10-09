using System.Security.Cryptography;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutBsaSourceFolder(int Ordinal, ulong Hash, string OriginalName,
    string LogicalName, uint Count);
internal sealed record FalloutBsaSourceMember(int FolderOrdinal, int FileOrdinal, ulong Hash,
    string OriginalName, string LogicalPath, uint RawOffset, uint RawSize);
internal sealed record FalloutBsaDirectorySource(string Archive, long Bytes, uint ArchiveFlags,
    uint TypeFlags, string DirectorySha256);

internal sealed partial class FalloutBsaArchive
{
    private readonly uint _sourceArchiveFlags, _sourceTypeFlags;
    private readonly long _sourceLength, _directoryExtentBytes;
    private readonly IReadOnlyList<FalloutBsaSourceFolder> _sourceFolders;
    private readonly IReadOnlyList<FalloutBsaSourceMember> _sourceMembers;
    private readonly object _directorySourceGate = new();
    private string? _directorySourceSha256;

    internal FalloutBsaDirectorySource DirectorySource
    {
        get
        {
            lock (_directorySourceGate)
            {
                ObjectDisposedException.ThrowIf(_readHandle.IsClosed, this);
                if (_directorySourceSha256 is null)
                {
                    // This hashes only the original header/directory extent,
                    // on demand. It reads no audio payload, writes no inventory
                    // and makes no whole-archive/member digest claim.
                    using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                    var buffer = new byte[64 * 1024]; long at = 0;
                    while (at < _directoryExtentBytes)
                    {
                        var count = RandomAccess.Read(_readHandle,
                            buffer.AsSpan(0, checked((int)Math.Min(buffer.Length, _directoryExtentBytes - at))), at);
                        if (count == 0) throw new EndOfStreamException("Actual BSA directory identity is truncated.");
                        hash.AppendData(buffer, 0, count); at += count;
                    }
                    _directorySourceSha256 = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
                }
                return new(_path, _sourceLength, _sourceArchiveFlags, _sourceTypeFlags, _directorySourceSha256);
            }
        }
    }

    internal bool HasSourceSoundFolder(string folder)
    {
        ObjectDisposedException.ThrowIf(_readHandle.IsClosed, this);
        if ((_sourceTypeFlags & 8) == 0) return false;
        var hash = FalloutArchiveNameHash.Folder(folder);
        return _sourceFolders.Any(row => row.Hash == hash);
    }

    internal IReadOnlyList<FalloutBsaSourceMember> SourceSoundDirectory(string folder, string extension)
    {
        ObjectDisposedException.ThrowIf(_readHandle.IsClosed, this);
        if ((_sourceTypeFlags & 8) == 0) return [];
        var hash = FalloutArchiveNameHash.Folder(folder);
        var folders = _sourceFolders.Where(row => row.Hash == hash).ToArray();
        if (folders.Length == 0) return [];
        if (folders.Length != 1) throw new NotSupportedException("Source folder hash has an unowned ambiguous archive-table lookup.");
        var selected = folders[0];
        if (FalloutArchiveNameHash.Folder(selected.OriginalName) != selected.Hash || selected.LogicalName != CanonicalPath(folder))
            throw new InvalidDataException("Actual archive folder hash/name does not identify the requested source directory.");
        var pattern = FalloutArchiveNameHash.Wildcard(extension);
        var rows = _sourceMembers.Where(row => row.FolderOrdinal == selected.Ordinal).ToArray();
        if (rows.Length != selected.Count || rows.Where((row, index) => row.FileOrdinal != index).Any())
            throw new InvalidDataException("Archive directory lost its original file-table count/order.");
        var result = new List<FalloutBsaSourceMember>();
        foreach (var row in rows)
        {
            if (FalloutArchiveNameHash.File(row.OriginalName) != row.Hash)
                throw new InvalidDataException("Source archive file hash differs from its original name.");
            // Ordinary registration constructs an unmarked archive. Its mark
            // is independent of header flags; masked/nonzero offsets govern
            // visibility. Invalidation is admitted by the file-manager owner.
            if ((row.RawOffset & 0x80000000) != 0 || (row.RawOffset & 0x7fffffff) == 0) continue;
            if (FalloutArchiveNameHash.MatchesWildcard(pattern, row.Hash)) result.Add(row);
        }
        return Array.AsReadOnly(result.ToArray());
    }
}
