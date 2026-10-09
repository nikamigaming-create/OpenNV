using System.Buffers.Binary;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;

namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal sealed record NativePluginImageImport(string Library, string? Name, ushort? Ordinal, uint SlotRva);
internal sealed record NativePluginImageExport(string Name, uint Rva, bool Executable, bool Forwarded);
internal sealed record NativePluginCodeView(Guid Guid, int Age, string Path);
internal sealed record NativePluginImageDeclaration(string Path, string Sha256, bool Dll,
    IReadOnlyList<NativePluginImageImport> Imports, IReadOnlyList<NativePluginImageExport> Exports,
    IReadOnlyList<NativePluginCodeView> CodeViews, IReadOnlyList<string> PathLiterals, bool DelayImports)
{
    internal Machine Machine { get; init; }
    internal PEMagic Magic { get; init; }
    internal bool Managed { get; init; }
}

// Read the original PE declaration. Neither an image nor an instruction is
// mapped/executed here; retained addresses are source coordinates only.
internal static class NativePluginImageDeclarations
{
    internal static NativePluginImageDeclaration Read(string path, bool dll, bool requireX86 = true)
    {
        using var input = new FileStream(Path.GetFullPath(path), FileMode.Open, FileAccess.Read, FileShare.Read);
        var hash = Convert.ToHexString(SHA256.HashData(input)); input.Position = 0;
        using var pe = new PEReader(input, PEStreamOptions.LeaveOpen);
        var header = pe.PEHeaders.PEHeader ?? throw new InvalidDataException("Native declaration has no complete PE header.");
        if (header.Magic is not (PEMagic.PE32 or PEMagic.PE32Plus) ||
            requireX86 && (pe.PEHeaders.CoffHeader.Machine != Machine.I386 || header.Magic != PEMagic.PE32 || pe.PEHeaders.CorHeader is not null) ||
            pe.PEHeaders.CoffHeader.Characteristics.HasFlag(Characteristics.Dll) != dll)
            throw new InvalidDataException("Native declaration requires its exact unmanaged PE32/I386 image kind.");
        var imports = Imports(pe, header.ImportTableDirectory, header.Magic == PEMagic.PE32Plus);
        var exports = Exports(pe, header.ExportTableDirectory);
        var views = new List<NativePluginCodeView>();
        foreach (var entry in pe.ReadDebugDirectory().Where(entry => entry.Type == DebugDirectoryEntryType.CodeView))
        {
            var value = pe.ReadCodeViewDebugDirectoryData(entry);
            if (value.Age < 1 || string.IsNullOrEmpty(value.Path)) throw new InvalidDataException("Original CodeView identity is incomplete.");
            views.Add(new(value.Guid, value.Age, value.Path));
        }
        var literals = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var section in pe.PEHeaders.SectionHeaders.Where(section => !section.SectionCharacteristics.HasFlag(SectionCharacteristics.MemExecute)))
        {
            if (section.SizeOfRawData < 0 || section.PointerToRawData < 0 ||
                (long)section.PointerToRawData + section.SizeOfRawData > input.Length)
                throw new InvalidDataException("Original PE declaration section has no complete file backing.");
            if (section.SizeOfRawData == 0) continue;
            // Raw section alignment can extend beyond the mapped virtual extent.
            // Scan the backed block exposed by PEReader, without requesting its
            // unmapped padding or manufacturing zero-filled declaration bytes.
            var bytes = pe.GetSectionData(section.VirtualAddress).GetContent().AsSpan();
            for (var at = 0; at < bytes.Length;)
            {
                var start = at;
                while (at < bytes.Length && bytes[at] is >= 0x20 and <= 0x7e) ++at;
                if (at < bytes.Length && bytes[at] == 0 && at - start is >= 5 and <= 1024)
                {
                    var text = Encoding.ASCII.GetString(bytes[start..at]);
                    if (Path.GetExtension(text).ToLowerInvariant() is ".ini" or ".cfg" or ".log" or ".txt") literals.Add(text);
                }
                ++at;
            }
        }
        return new(Path.GetFullPath(path), hash, dll, imports, exports, views.AsReadOnly(),
            Array.AsReadOnly(literals.Order(StringComparer.OrdinalIgnoreCase).ToArray()),
            header.DelayImportTableDirectory.RelativeVirtualAddress != 0 || header.DelayImportTableDirectory.Size != 0)
        { Machine = pe.PEHeaders.CoffHeader.Machine, Magic = header.Magic, Managed = pe.PEHeaders.CorHeader is not null };
    }

    private static IReadOnlyList<NativePluginImageImport> Imports(PEReader pe, DirectoryEntry directory, bool wide)
    {
        if (directory.RelativeVirtualAddress == 0 && directory.Size == 0) return [];
        if (directory.RelativeVirtualAddress <= 0 || directory.Size < 20) throw new InvalidDataException("Original import directory is incomplete.");
        var table = pe.GetSectionData(directory.RelativeVirtualAddress).GetReader(0, directory.Size);
        var result = new List<NativePluginImageImport>(); var ended = false;
        while (table.RemainingBytes >= 20)
        {
            var lookup = table.ReadUInt32(); var time = table.ReadUInt32(); var forward = table.ReadUInt32();
            var library = table.ReadUInt32(); var slots = table.ReadUInt32();
            if ((lookup | time | forward | library | slots) == 0) { ended = true; break; }
            if (lookup == 0 || library == 0 || slots == 0) throw new InvalidDataException("Original imports lack names or native slots.");
            var libraryName = Text(pe, library).ToLowerInvariant();
            if (libraryName.IndexOfAny(['/', '\\', ':']) >= 0 || !libraryName.EndsWith(".dll", StringComparison.Ordinal))
                throw new InvalidDataException("Original import library is not a bounded DLL basename.");
            var names = pe.GetSectionData(checked((int)lookup)).GetReader(); var terminated = false;
            var stride = wide ? 8 : 4;
            for (var index = 0; names.RemainingBytes >= stride; ++index)
            {
                var pointer = wide ? names.ReadUInt64() : names.ReadUInt32(); if (pointer == 0) { terminated = true; break; }
                var slot = checked(slots + checked((uint)index * (uint)stride));
                _ = pe.GetSectionData(checked((int)slot)).GetContent(0, stride);
                result.Add((pointer & (wide ? 0x8000000000000000UL : 0x80000000UL)) != 0
                    ? new(libraryName, null, checked((ushort)(pointer & 0xffff)), slot)
                    : new(libraryName, Text(pe, checked((uint)pointer + 2)), null, slot));
            }
            if (!terminated) throw new InvalidDataException("Original import names have no backed final terminator.");
        }
        if (!ended) throw new InvalidDataException("Original import descriptors have no complete final member.");
        return result.AsReadOnly();
    }

    private static IReadOnlyList<NativePluginImageExport> Exports(PEReader pe, DirectoryEntry directory)
    {
        if (directory.RelativeVirtualAddress == 0 && directory.Size == 0) return [];
        if (directory.RelativeVirtualAddress <= 0 || directory.Size < 40) throw new InvalidDataException("Original export directory is incomplete.");
        var table = pe.GetSectionData(directory.RelativeVirtualAddress).GetContent(0, 40).AsSpan();
        var functionCount = U32(table, 20); var nameCount = U32(table, 24);
        if (nameCount > functionCount || functionCount > 1048576) throw new InvalidDataException("Original export table extent is invalid.");
        // Helpers may export only ordinals or no entries. Neither disposition
        // invents a named Query entry at image coordinate zero.
        if (nameCount == 0) return [];
        if (U32(table, 28) == 0 || U32(table, 32) == 0 || U32(table, 36) == 0)
            throw new InvalidDataException("Original named export table lacks complete source coordinates.");
        var functions = pe.GetSectionData(checked((int)U32(table, 28))).GetContent(0, checked((int)functionCount * 4));
        var names = pe.GetSectionData(checked((int)U32(table, 32))).GetContent(0, checked((int)nameCount * 4));
        var ordinals = pe.GetSectionData(checked((int)U32(table, 36))).GetContent(0, checked((int)nameCount * 2));
        var result = new List<NativePluginImageExport>(); var unique = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < nameCount; ++index)
        {
            var name = Text(pe, U32(names.AsSpan(), checked((int)index * 4)));
            var ordinal = BinaryPrimitives.ReadUInt16LittleEndian(ordinals.AsSpan().Slice(checked((int)index * 2), 2));
            if (ordinal >= functionCount || !unique.Add(name)) throw new InvalidDataException("Original export name/ordinal is duplicated or invalid.");
            var rva = U32(functions.AsSpan(), ordinal * 4);
            var forwarded = rva >= directory.RelativeVirtualAddress && rva < (long)directory.RelativeVirtualAddress + directory.Size;
            var executable = rva != 0 && pe.PEHeaders.SectionHeaders.Any(section =>
                rva >= section.VirtualAddress && rva < (long)section.VirtualAddress + Math.Max(section.VirtualSize, section.SizeOfRawData) &&
                section.SectionCharacteristics.HasFlag(SectionCharacteristics.MemExecute));
            result.Add(new(name, rva, executable, forwarded));
        }
        return result.AsReadOnly();
    }
    private static string Text(PEReader pe, uint rva)
    {
        var bytes = pe.GetSectionData(checked((int)rva)).GetReader(); var text = new StringBuilder();
        while (bytes.RemainingBytes != 0)
        {
            var value = bytes.ReadByte(); if (value == 0) return text.Length > 0 ? text.ToString() : throw new InvalidDataException("Original PE name is empty.");
            if (value is < 0x20 or > 0x7e || text.Length == 1024) throw new InvalidDataException("Original PE name is malformed or excessive.");
            text.Append((char)value);
        }
        throw new InvalidDataException("Original PE name is unterminated.");
    }
    private static uint U32(ReadOnlySpan<byte> value, int at) => BinaryPrimitives.ReadUInt32LittleEndian(value[at..]);
}
