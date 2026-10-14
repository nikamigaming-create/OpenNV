using System.Buffers.Binary;
using System.Reflection.PortableExecutable;

namespace OpenNV.Runtime.Compatibility.NativePlugins;

// This capability is derived before Windows enters the image. An import list
// alone cannot prove that DllMain or a TLS callback did not already perform I/O.
internal sealed class NativePluginModuleReadIoSource
{
    internal string Path { get; }
    internal string Sha256 { get; }
    internal string StackSha256 { get; }
    internal string Construction { get; }
    internal uint BoundImports { get; }
    private readonly HashSet<string> _imports;
    private readonly NativePluginImageImport[] _imageImports;
    private NativePluginModuleReadIoSource(NativePluginImageDeclaration image, string stack, string construction)
    {
        Path = image.Path; Sha256 = image.Sha256; StackSha256 = stack; Construction = construction;
        _imageImports = image.Imports.ToArray();
        _imports = image.Imports.Where(row => row.Name is not null && OwnsExport(row.Library, row.Name))
            .Select(row => row.Library.ToLowerInvariant() + "!" + row.Name).ToHashSet(StringComparer.Ordinal);
        BoundImports = checked((uint)image.Imports.Count(row => row.Name is not null && OwnsExport(row.Library, row.Name)));
    }
    internal static bool OwnsExport(string library, string name) =>
        (library is "kernel32.dll" or "kernelbase.dll" || library.StartsWith("api-ms-win-core-", StringComparison.Ordinal)) &&
        name is "CreateFileW" or "CloseHandle" or "GetPrivateProfileStringA" or "GetModuleFileNameA";

    internal static NativePluginModuleReadIoSource? Read(NativePluginImageDeclaration image, string stack)
    {
        if (!image.Imports.Any(row => row.Name is not null && OwnsExport(row.Library, row.Name))) return null;
        if (stack.Length != 64 || !stack.All(Uri.IsHexDigit) || !image.Dll || image.Managed || image.DelayImports ||
            image.Machine != Machine.I386 || image.Magic != PEMagic.PE32)
            throw new InvalidDataException("Module read I/O lost its exact selected unmanaged source/stack declaration.");
        using var input = NativeNvseHostSource.Lease(image.Path, image.Sha256, dll: true);
        using var pe = new PEReader(input, PEStreamOptions.LeaveOpen);
        var header = pe.PEHeaders.PEHeader!;
        var tls = header.ThreadLocalStorageTableDirectory;
        if (tls.RelativeVirtualAddress != 0 || tls.Size != 0)
        {
            if (tls.RelativeVirtualAddress <= 0 || tls.Size < 24)
                throw new InvalidDataException("Module read I/O has an incomplete original TLS declaration.");
            var bytes = pe.GetSectionData(tls.RelativeVirtualAddress).GetContent(0, 24).AsSpan();
            var callbacks = BinaryPrimitives.ReadUInt32LittleEndian(bytes[12..]);
            if (callbacks != 0)
            {
                if (callbacks < header.ImageBase || callbacks - header.ImageBase > int.MaxValue ||
                    BinaryPrimitives.ReadUInt32LittleEndian(pe.GetSectionData(checked((int)(callbacks - header.ImageBase))).GetContent(0, 4).AsSpan()) != 0)
                    throw new NotSupportedException("Module read I/O requires the actual pre-entry TLS callback producer before LoadLibrary.");
            }
        }
        var entry = header.AddressOfEntryPoint;
        if (entry == 0) return new(image, stack.ToUpperInvariant(), "PE32/no-entry/no-TLS-callbacks");
        var section = pe.PEHeaders.SectionHeaders.SingleOrDefault(row => entry >= row.VirtualAddress &&
            (long)entry + 8 <= (long)row.VirtualAddress + row.SizeOfRawData);
        if (!section.SectionCharacteristics.HasFlag(SectionCharacteristics.MemExecute))
            throw new InvalidDataException("Module read I/O entry has no original executable file backing.");
        var code = pe.GetSectionData(entry).GetContent().AsSpan();
        // General leaf declarations: return TRUE with Win32's twelve-byte
        // argument cleanup and no branches, calls or writes to shared state.
        if (code.StartsWith(new byte[] { 0xb8, 1, 0, 0, 0, 0xc2, 12, 0 }) ||
            code.StartsWith(new byte[] { 0x6a, 1, 0x58, 0xc2, 12, 0 }) ||
            code.StartsWith(new byte[] { 0x33, 0xc0, 0x40, 0xc2, 12, 0 }) ||
            code.StartsWith(new byte[] { 0x55, 0x8b, 0xec, 0xb8, 1, 0, 0, 0, 0x5d, 0xc2, 12, 0 }))
            return new(image, stack.ToUpperInvariant(), "PE32/leaf-TRUE-stdcall/no-TLS-callbacks");
        throw new NotSupportedException("Module read I/O requires the complete original pre-entry DllMain/CRT initializer producer before LoadLibrary.");
    }
    internal bool Owns(string library, string name) => _imports.Contains(library.ToLowerInvariant() + "!" + name);
    internal void Check(NativePluginImageDeclaration image, string? stack = null)
    {
        if (!StringComparer.OrdinalIgnoreCase.Equals(System.IO.Path.GetFullPath(image.Path), System.IO.Path.GetFullPath(Path)) ||
            !Sha256.Equals(image.Sha256, StringComparison.OrdinalIgnoreCase) ||
            stack is not null && !StackSha256.Equals(stack, StringComparison.OrdinalIgnoreCase) ||
            !_imageImports.SequenceEqual(image.Imports))
            throw new InvalidDataException("Module read I/O source capability belongs to another original image/stack/import graph.");
    }
}
