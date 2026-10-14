using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Reflection.PortableExecutable;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal sealed class NativePluginModuleBookSoundSource
{
    internal string Sha256 { get; }
    internal string Family { get; }
    internal uint LoadRva { get; }
    internal uint WriterRva { get; }
    internal uint ProtectReturnRva => checked(WriterRva + 25);
    internal uint RestoreReturnRva => checked(WriterRva + 43);
    internal uint ForwardCallRva { get; }
    internal uint BackwardCallRva { get; }
    internal uint FirstCallRva => ForwardCallRva;
    internal uint SecondCallRva => BackwardCallRva;
    internal uint BufferRva { get; }
    internal uint Capacity => checked((uint)InitialBytes.Length);
    internal uint ImageBytes { get; }
    internal string EditorId { get; }
    internal ImmutableArray<byte> InitialBytes { get; }
    private readonly string _path;
    private readonly NativePluginImageImport[] _imports;
    private readonly NativePluginImageExport[] _exports;
    private readonly byte[] _writerCode;
    private readonly byte[] _pairCode;
    private readonly uint _protectionSlot;
    private NativePluginModuleBookSoundSource(NativePluginImageDeclaration image, FalloutSpecialBookPageSoundModuleAssociation association,
        NativePluginConstructorImage source, uint load, uint imageBytes, string family)
    {
        _path = image.Path; Sha256 = image.Sha256; Family = family; _imports = image.Imports.ToArray(); _exports = image.Exports.ToArray();
        LoadRva = load; WriterRva = source.ToRva(association.Writer); BufferRva = source.ToRva(association.Buffer);
        ForwardCallRva = source.ToRva(association.ForwardCall); BackwardCallRva = source.ToRva(association.BackwardCall);
        ImageBytes = imageBytes; EditorId = association.EditorId; InitialBytes = [.. source.Read(BufferRva, association.Capacity)];
        source.RequireReadableData(BufferRva, Capacity); _writerCode = source.Read(WriterRva, 47, true);
        _pairCode = source.Read(checked(ForwardCallRva - 12), 29, true);
        _protectionSlot = source.ImportAt(WriterRva + 19, "VirtualProtect").SlotRva;
        if (ForwardCallRva < load || BackwardCallRva <= ForwardCallRva || source.Call(ForwardCallRva) != WriterRva || source.Call(BackwardCallRva) != WriterRva)
            throw new InvalidDataException("Book sound module lost its actual ordered Load/helper call associations.");
    }
    internal static NativePluginModuleBookSoundSource? Read(NativePluginImageDeclaration image, FalloutSpecialBookPageSoundsSource field)
    {
        if (!image.Imports.Any(row => row.Name == "VirtualProtect")) return null;
        using var input = NativeNvseHostSource.Lease(image.Path, image.Sha256, dll: true);
        var bytes = new byte[checked((int)input.Length)]; input.ReadExactly(bytes);
        var loads = image.Exports.Where(row => row.Name == "NVSEPlugin_Load").ToArray();
        if (loads.Length != 1) throw new InvalidDataException("Book sound source has no unique real Load export.");
        var association = FalloutExecutableStringTable.ReadSpecialBookPageSoundModule(field, bytes, loads[0].Rva);
        if (association is null) return null;
        input.Position = 0; using var pe = new PEReader(input, PEStreamOptions.LeaveOpen);
        return new(image, association, new(pe, input, image), loads[0].Rva, checked((uint)pe.PEHeaders.PEHeader!.SizeOfImage), "PE32/Load/independent-Book-page-PUSH-inputs/shared-CString/SafeWrite32");
    }
    internal static NativePluginModuleBookSoundSource? ReadFocus(NativePluginImageDeclaration image, FalloutSpecialBookPageSoundsSource page,
        FalloutSpecialBookFocusSoundsSource focus)
    {
        if (!image.Imports.Any(row => row.Name == "VirtualProtect")) return null;
        using var input = NativeNvseHostSource.Lease(image.Path, image.Sha256, dll: true);
        var bytes = new byte[checked((int)input.Length)]; input.ReadExactly(bytes);
        var loads = image.Exports.Where(row => row.Name == "NVSEPlugin_Load").ToArray();
        if (loads.Length != 1) throw new InvalidDataException("Book focus source has no unique original Load export.");
        var association = FalloutExecutableStringTable.ReadSpecialBookFocusSoundModule(page, focus, bytes, loads[0].Rva);
        if (association is null) return null;
        input.Position = 0; using var pe = new PEReader(input, PEStreamOptions.LeaveOpen);
        return new(image, association, new(pe, input, image), loads[0].Rva, checked((uint)pe.PEHeaders.PEHeader!.SizeOfImage),
            "PE32/Load/independent-Book-focus-PUSH-inputs/shared-CString/SafeWrite32");
    }
    internal void Check(NativePluginImageDeclaration image)
    {
        if (!StringComparer.OrdinalIgnoreCase.Equals(_path, image.Path) || !StringComparer.OrdinalIgnoreCase.Equals(Sha256, image.Sha256) ||
            !_imports.SequenceEqual(image.Imports) || !_exports.SequenceEqual(image.Exports))
            throw new InvalidDataException("Book sound producer changed its original module/import/export identity.");
    }
    internal byte[] MappedWriterCode(uint image)
    {
        var result = _writerCode.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(21), checked(image + _protectionSlot));
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(39), checked(image + _protectionSlot)); return result;
    }
    internal byte[] MappedLoadPairCode(uint image)
    {
        var result = _pairCode.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(1), checked(image + BufferRva)); return result;
    }
}
