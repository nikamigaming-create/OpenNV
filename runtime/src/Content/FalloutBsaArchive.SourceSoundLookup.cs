namespace OpenNV.Runtime.Content;

internal sealed partial class FalloutBsaArchive
{
    internal FalloutBsaSourceMember? SourceSoundMember(string logicalPath)
    {
        ObjectDisposedException.ThrowIf(_readHandle.IsClosed, this);
        if ((_sourceTypeFlags & 8) == 0) return null;
        var path = CanonicalPath(logicalPath); var split = path.LastIndexOf('\\');
        if (split <= 0 || split == path.Length - 1)
            throw new NotSupportedException("Original sound lookup requires its actual folder/file split.");
        var folder = path[..split]; var file = path[(split + 1)..];
        var folderHash = FalloutArchiveNameHash.Folder(folder); var fileHash = FalloutArchiveNameHash.File(file);
        var folders = _sourceFolders.Where(row => row.Hash == folderHash).ToArray();
        if (folders.Length == 0) return null;
        if (folders.Length != 1 || folders[0].LogicalName != folder ||
            FalloutArchiveNameHash.Folder(folders[0].OriginalName) != folderHash)
            throw new NotSupportedException("Sound lookup has an unowned colliding source folder hash.");
        var candidates = _sourceMembers.Where(row => row.FolderOrdinal == folders[0].Ordinal && row.Hash == fileHash).ToArray();
        if (candidates.Length == 0) return null;
        if (candidates.Length != 1 || candidates[0].LogicalPath != path ||
            FalloutArchiveNameHash.File(candidates[0].OriginalName) != fileHash)
            throw new NotSupportedException("Sound lookup has an unowned colliding source file hash.");
        var member = candidates[0];
        return (member.RawOffset & 0x80000000) == 0 && (member.RawOffset & 0x7fffffff) != 0 ? member : null;
    }
}
