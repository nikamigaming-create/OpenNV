using System.Reflection.PortableExecutable;
using System.Security.Cryptography;

namespace OpenNV.Runtime.Compatibility.NativePlugins;

// FILE stays opaque and comes from the actual imported UCRT export provider.
// No game static CRT, FILE layout, pointer/HANDLE conversion or original code
// is inferred by this public import owner.
internal static class NativePluginCrtImports
{
    internal static readonly IReadOnlySet<string> StreamLibraries = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    { "ucrtbase.dll", "api-ms-win-crt-stdio-l1-1-0.dll" };
    internal static readonly IReadOnlySet<string> DirectoryLibraries = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    { "ucrtbase.dll", "api-ms-win-crt-filesystem-l1-1-0.dll" };
    internal static readonly IReadOnlySet<string> Owned = new HashSet<string>(StringComparer.Ordinal)
    {
        "fopen", "_wfopen", "fopen_s", "_wfopen_s", "_fsopen", "_wfsopen", "fclose",
        "fread", "fwrite", "fseek", "_fseeki64", "ftell", "_ftelli64", "rewind", "fflush",
        "fputc", "fputs", "__stdio_common_vfprintf", "feof", "ferror", "clearerr", "_mkdir", "_wmkdir",
    }.Union(NativePluginCrtExtendedImports.Stream).Union(NativePluginCrtExtendedImports.Environment).ToHashSet(StringComparer.Ordinal);
    internal static readonly IReadOnlySet<string> Unowned = new HashSet<string>(StringComparer.Ordinal)
    {
        "freopen", "_wfreopen", "freopen_s", "_wfreopen_s", "_fdopen", "_wfdopen", "_fcloseall", "_flushall",
        "fgetpos", "fsetpos", "setbuf", "setvbuf", "_fileno", "_get_osfhandle", "_open_osfhandle",
        "_open", "_wopen", "_sopen", "_wsopen", "_sopen_s", "_wsopen_s", "_close", "_read", "_write",
        "_lseek", "_lseeki64", "_tell", "_telli64", "_eof", "_filelength", "_filelengthi64",
        "_rmdir", "_wrmdir", "_unlink", "_wunlink", "rename", "_wrename", "_chmod", "_wchmod",
        "_access", "_waccess", "_access_s", "_waccess_s", "tmpfile", "tmpfile_s", "_tempnam", "_wtempnam",
        "__acrt_iob_func", "__stdio_common_vfwprintf", "__stdio_common_vfscanf", "__stdio_common_vfwscanf",
    };
    private static readonly IReadOnlySet<string> PureFormatting = new HashSet<string>(StringComparer.Ordinal)
    {
        "__stdio_common_vsprintf", "__stdio_common_vsprintf_s", "__stdio_common_vsnprintf_s", "__stdio_common_vsscanf",
        "__stdio_common_vswprintf", "__stdio_common_vswprintf_s", "__stdio_common_vsnwprintf_s", "__stdio_common_vswscanf",
        "sprintf", "sprintf_s", "snprintf", "_snprintf", "_snprintf_s", "vsprintf", "vsprintf_s", "vsnprintf", "_vsnprintf", "_vsnprintf_s",
        "sscanf", "sscanf_s", "swprintf", "swprintf_s", "vswprintf", "vswprintf_s", "swscanf", "swscanf_s", "__local_stdio_printf_options", "__local_stdio_scanf_options",
    };
    internal static bool IsFileImport(string library, string name)
        => NativePluginCrtExtendedImports.Owns(library, name) || library.Equals("api-ms-win-crt-stdio-l1-1-0.dll", StringComparison.OrdinalIgnoreCase) && !PureFormatting.Contains(name) ||
            library.Equals("api-ms-win-crt-filesystem-l1-1-0.dll", StringComparison.OrdinalIgnoreCase) ||
            (StreamLibraries.Contains(library) || DirectoryLibraries.Contains(library)) && (Owned.Contains(name) || Unowned.Contains(name));
    internal static bool IsFileDeclaration(string declaration)
    {
        var separator = declaration.LastIndexOf('!');
        return separator > 0 && IsFileImport(declaration[..separator], declaration[(separator + 1)..]);
    }
    internal static void Require(NativePluginPrivateIo io, string library, string name)
    {
        if (!Owned.Contains(name) || !io.CrtProviders.Any(provider => provider.Imports.TryGetValue(library, out var names) && names.Contains(name)))
            throw new NotSupportedException("Original CRT file import has no actual selected provider/callable owner: " + library + "!" + name);
    }
    internal static IReadOnlyList<NativePluginCrtProviderSelection> ReadProviders(string selectedModule, string expectedSha256)
    {
        using var input = new FileStream(selectedModule, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (!StringComparer.OrdinalIgnoreCase.Equals(Convert.ToHexString(SHA256.HashData(input)), expectedSha256))
            throw new InvalidDataException("CRT declaration source is not the actual selected original module hash.");
        input.Position = 0;
        using var pe = new PEReader(input, PEStreamOptions.LeaveOpen);
        if (pe.PEHeaders.CoffHeader.Machine != Machine.I386 || pe.PEHeaders.PEHeader is not { Magic: PEMagic.PE32 } header)
            throw new InvalidDataException("CRT source import requires its actual unmanaged x86 module.");
        var byLibrary = new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase);
        var extent = header.ImportTableDirectory;
        if (extent.Size == 0 && extent.RelativeVirtualAddress == 0) return [];
        if (extent.Size < 20 || extent.RelativeVirtualAddress <= 0 || header.DelayImportTableDirectory.Size != 0 || header.DelayImportTableDirectory.RelativeVirtualAddress != 0)
            throw new NotSupportedException("CRT delay/incomplete import entry ownership is absent.");
        var block = pe.GetSectionData(extent.RelativeVirtualAddress);
        if (extent.Size > block.Length) throw new InvalidDataException("CRT import descriptor exceeds source bytes.");
        var rows = block.GetReader(0, extent.Size); var ended = false;
        while (rows.RemainingBytes >= 20)
        {
            var namesRva = rows.ReadUInt32(); var time = rows.ReadUInt32(); var forward = rows.ReadUInt32();
            var libraryRva = rows.ReadUInt32(); var slots = rows.ReadUInt32();
            if ((namesRva | time | forward | libraryRva | slots) == 0) { ended = true; break; }
            if (namesRva == 0 || libraryRva == 0 || slots == 0) throw new InvalidDataException("CRT original import source lacks names/slots.");
            var library = Text(pe, libraryRva).ToLowerInvariant(); var relevant = NativePluginCrtExtendedImports.Libraries.Contains(library);
            var names = pe.GetSectionData(checked((int)namesRva)).GetReader(); var complete = false; var selected = new HashSet<string>(StringComparer.Ordinal);
            while (names.RemainingBytes >= 4)
            {
                var pointer = names.ReadUInt32(); if (pointer == 0) { complete = true; break; }
                if ((pointer & 0x80000000) != 0) { if (relevant) throw new NotSupportedException("CRT ordinal file interface ownership is absent."); continue; }
                var name = Text(pe, checked(pointer + 2));
                if (relevant && IsFileImport(library, name) && !Owned.Contains(name)) throw new NotSupportedException("CRT imported file arm remains unowned: " + name);
                if (relevant && IsFileImport(library, name) && Owned.Contains(name)) selected.Add(name);
            }
            if (!complete) throw new InvalidDataException("CRT import name table has no original terminator.");
            if (selected.Count != 0)
            {
                if (byLibrary.TryGetValue(library, out var prior))
                {
                    var merged = new HashSet<string>(prior, StringComparer.Ordinal); merged.UnionWith(selected);
                    byLibrary[library] = merged;
                }
                else byLibrary.Add(library, selected);
            }
        }
        if (!ended) throw new InvalidDataException("CRT source import directory has no original terminator.");
        if (byLibrary.Count == 0) return [];
        var providerPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.SystemX86), "ucrtbase.dll");
        using var provider = new FileStream(providerPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var providerPe = new PEReader(provider, PEStreamOptions.LeaveOpen);
        if (providerPe.PEHeaders.CoffHeader.Machine != Machine.I386) throw new InvalidDataException("CRT selected export provider is not x86.");
        provider.Position = 0; var sha = Convert.ToHexString(SHA256.HashData(provider));
        return [new(Path.GetFullPath(providerPath), sha, "actual-selected-UCRT-import-provider:" + sha, byLibrary)];
    }
    private static string Text(PEReader pe, uint rva)
    {
        var input = pe.GetSectionData(checked((int)rva)).GetReader(); var result = new System.Text.StringBuilder();
        while (input.RemainingBytes != 0)
        {
            var value = input.ReadByte();
            if (value == 0) return result.Length != 0 ? result.ToString() : throw new InvalidDataException("CRT source name is empty.");
            if (value is < 0x21 or > 0x7e) throw new InvalidDataException("CRT source name is not exact ASCII."); result.Append((char)value);
        }
        throw new InvalidDataException("CRT source name has no complete backed terminator.");
    }
}
