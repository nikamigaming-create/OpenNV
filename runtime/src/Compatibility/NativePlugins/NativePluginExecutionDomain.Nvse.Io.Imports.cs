using System.Reflection.PortableExecutable;

namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal static class NativePluginIoImports
{
    // These names are file APIs, not an escape hatch through the caller's
    // non-I/O declarations. Their actual CRT/async/enumeration/NT lifetimes
    // remain refused until corresponding first-party owners are implemented.
    internal static readonly IReadOnlySet<string> Unowned = new HashSet<string>(StringComparer.Ordinal) {
        "fopen", "_wfopen", "fopen_s", "_wfopen_s", "freopen", "_wfreopen", "freopen_s", "_wfreopen_s", "_fsopen", "_wfsopen",
        "_open", "_wopen", "_sopen", "_wsopen", "_sopen_s", "_wsopen_s", "open", "creat", "_creat", "_wcreat",
        "FindFirstFileA", "FindFirstFileW", "FindFirstFileExA", "FindFirstFileExW", "FindNextFileA", "FindNextFileW", "FindClose",
        "CopyFileA", "CopyFileW", "CopyFileExA", "CopyFileExW", "CopyFile2", "MoveFileA", "MoveFileW", "MoveFileExA", "MoveFileExW",
        "MoveFileWithProgressA", "MoveFileWithProgressW", "ReplaceFileA", "ReplaceFileW", "SetFileAttributesA", "SetFileAttributesW",
        "CreateFileMappingA", "CreateFileMappingW", "OpenFileMappingA", "OpenFileMappingW", "MapViewOfFile", "MapViewOfFileEx",
        "WriteFileEx", "ReadFileEx", "GetPrivateProfileSectionA", "GetPrivateProfileSectionW", "WritePrivateProfileSectionA", "WritePrivateProfileSectionW",
        "NtCreateFile", "NtOpenFile", "NtWriteFile", "NtDeleteFile", "NtSetInformationFile" };
    internal static readonly IReadOnlySet<string> Owned = new HashSet<string>(StringComparer.Ordinal) {
        "CreateFileW", "CreateFileA", "CloseHandle", "ReadFile", "WriteFile", "SetFilePointer", "SetFilePointerEx",
        "GetFileSize", "GetFileSizeEx", "FlushFileBuffers", "CreateDirectoryW", "CreateDirectoryA", "DeleteFileW", "DeleteFileA",
        "RemoveDirectoryW", "RemoveDirectoryA", "GetFileAttributesW", "GetFileAttributesA", "GetPrivateProfileStringW", "GetPrivateProfileStringA",
        "GetPrivateProfileIntW", "GetPrivateProfileIntA", "WritePrivateProfileStringW", "WritePrivateProfileStringA", "GetProcAddress",
        "LoadLibraryA", "LoadLibraryW", "LoadLibraryExA", "LoadLibraryExW", "GetCurrentDirectoryW", "GetCurrentDirectoryA",
        "SetCurrentDirectoryW", "SetCurrentDirectoryA", "GetFileType", "SetEndOfFile" };
    internal static void Admit(FileStream original, NativePluginPrivateIo io)
    {
        using var pe = new PEReader(original, PEStreamOptions.LeaveOpen);
        var header = pe.PEHeaders.PEHeader ?? throw new InvalidDataException("Native I/O source PE header is absent.");
        if (header.DelayImportTableDirectory.Size != 0 || header.DelayImportTableDirectory.RelativeVirtualAddress != 0)
            throw new NotSupportedException("Native delay import I/O/entry lifetime remains unowned.");
        var directory = header.ImportTableDirectory;
        if (directory.Size == 0 && directory.RelativeVirtualAddress == 0) return;
        if (directory.Size <= 0 || directory.RelativeVirtualAddress <= 0)
            throw new InvalidDataException("Native import directory has an incomplete extent.");
        var data = pe.GetSectionData(directory.RelativeVirtualAddress);
        if (directory.Size > data.Length) throw new InvalidDataException("Native import directory exceeds original backed bytes.");
        var reader = data.GetReader(0, directory.Size); var ended = false;
        while (reader.RemainingBytes >= 20)
        {
            var lookup = reader.ReadUInt32(); var time = reader.ReadUInt32(); var forward = reader.ReadUInt32();
            var libraryRva = reader.ReadUInt32(); var slots = reader.ReadUInt32();
            if ((lookup | time | forward | libraryRva | slots) == 0) { ended = true; break; }
            if (lookup == 0 || libraryRva == 0 || slots == 0) throw new InvalidDataException("Native import declaration lacks original names/slots.");
            var library = Text(pe, libraryRva).ToLowerInvariant(); var platform = library is "kernel32.dll" or "kernelbase.dll" || library.StartsWith("api-ms-win-core-", StringComparison.Ordinal);
            var names = pe.GetSectionData(checked((int)lookup)).GetReader(); var terminated = false;
            for (var at = 0; names.RemainingBytes >= 4; ++at)
            {
                var pointer = names.ReadUInt32();
                if (pointer == 0) { terminated = true; break; }
                if ((pointer & 0x80000000) != 0) throw new NotSupportedException("Original ordinal import I/O/call ownership is unbound.");
                var name = Text(pe, checked(pointer + 2));
                var crtFile = NativePluginCrtImports.IsFileImport(library, name);
                if (crtFile) NativePluginCrtImports.Require(io, library, name);
                else if (Unowned.Contains(name)) throw new NotSupportedException("Original file import has no callable private I/O owner: " + library + "!" + name);
                if (!crtFile && !(platform && Owned.Contains(name)) && !io.Selection.DeclaredNonIoImports.Contains(library + "!" + name))
                    throw new NotSupportedException("Original import lacks native I/O or an exact declared non-I/O owner: " + library + "!" + name);
                _ = pe.GetSectionData(checked((int)(slots + checked((uint)at * 4)))).GetReader(0, 4).ReadUInt32();
            }
            if (!terminated) throw new InvalidDataException("Original import thunk has no complete terminator.");
        }
        if (!ended) throw new InvalidDataException("Original import descriptors have no complete terminator.");
    }
    private static string Text(PEReader pe, uint rva)
    {
        var bytes = pe.GetSectionData(checked((int)rva)).GetReader(); var output = new System.Text.StringBuilder();
        while (bytes.RemainingBytes != 0)
        {
            var value = bytes.ReadByte(); if (value == 0) return output.Length != 0 ? output.ToString() : throw new InvalidDataException("Native import name is empty.");
            if (value is < 0x21 or > 0x7e || output.Length >= 1024) throw new InvalidDataException("Native import name is malformed or beyond its declaration budget.");
            output.Append((char)value);
        }
        throw new InvalidDataException("Native import name has no original backed terminator.");
    }
}
