using System.Collections.Immutable;
using System.Buffers.Binary;
using System.Reflection.PortableExecutable;
using System.Text;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal sealed class NativePluginModuleModelPathSource
{
    internal string Sha256 { get; }
    internal string Family => "PE32/RaceMenu/shared-CString/SafeWrite32/PUSH-input";
    internal uint LoadRva { get; }
    internal uint WriterRva { get; }
    internal uint ProtectReturnRva => checked(WriterRva + 25);
    internal uint RestoreReturnRva => checked(WriterRva + 43);
    internal uint BufferRva { get; }
    internal uint Capacity { get; }
    internal uint ImageBytes { get; }
    internal ImmutableArray<byte> InitialBytes { get; }
    internal string DefaultModel { get; }
    internal string AlternateModel { get; }
    private readonly string _path;
    private readonly NativePluginImageImport[] _imports;
    private readonly NativePluginImageExport[] _exports;
    private readonly byte[] _writerCode;
    private readonly uint _protectionSlot;
    private NativePluginModuleModelPathSource(NativePluginImageDeclaration image, FalloutRaceMenuBufferAssociation association,
        uint load, uint writer, uint buffer, uint imageBytes, byte[] initial, byte[] writerCode, uint protectionSlot)
    {
        _path = image.Path; Sha256 = image.Sha256; LoadRva = load; WriterRva = writer; BufferRva = buffer;
        Capacity = checked((uint)association.Capacity); ImageBytes = imageBytes; InitialBytes = [.. initial];
        DefaultModel = association.Devices.DefaultModel; AlternateModel = association.Devices.GeneProjectorModel;
        _imports = image.Imports.ToArray(); _exports = image.Exports.ToArray(); _writerCode = writerCode; _protectionSlot = protectionSlot;
    }
    internal static NativePluginModuleModelPathSource? Read(NativePluginImageDeclaration image, FalloutRaceMenuModelPathSource model)
    {
        if (!image.Imports.Any(row => row.Name == "VirtualProtect")) return null;
        using var input = NativeNvseHostSource.Lease(image.Path, image.Sha256, dll: true);
        var bytes = new byte[checked((int)input.Length)]; input.ReadExactly(bytes);
        if (bytes.AsSpan().IndexOf(Encoding.ASCII.GetBytes("TTW_ShowGeneProjector\0")) < 0) return null;
        var association = FalloutExecutableStringTable.ReadRaceMenuBufferPatch(model.RuntimePath, bytes);
        if (association.Operand != model.Operand || association.Devices.DefaultModel != model.Model)
            throw new InvalidDataException("The module model patch does not target the selected mode's exact input operand.");
        input.Position = 0; using var pe = new PEReader(input, PEStreamOptions.LeaveOpen);
        var source = new NativePluginConstructorImage(pe, input, image);
        var load = image.Exports.Single(row => row.Name == "NVSEPlugin_Load").Rva;
        var call = source.ToRva(association.Call); var writer = source.ToRva(association.Writer);
        if (call < load || source.Call(call) != writer)
            throw new NotSupportedException("The selected model writer has no actual Load call association.");
        var buffer = source.ToRva(association.Buffer); source.RequireWritable(buffer, checked((uint)association.Capacity));
        var protection = source.ImportAt(writer + 19, "VirtualProtect");
        return new(image, association, load, writer, buffer, checked((uint)pe.PEHeaders.PEHeader!.SizeOfImage),
            source.Read(buffer, association.Capacity), source.Read(writer, 47, true), protection.SlotRva);
    }
    internal void Check(NativePluginImageDeclaration image)
    {
        if (!StringComparer.OrdinalIgnoreCase.Equals(_path, image.Path) || !StringComparer.OrdinalIgnoreCase.Equals(Sha256, image.Sha256) ||
            !_imports.SequenceEqual(image.Imports) || !_exports.SequenceEqual(image.Exports))
            throw new InvalidDataException("The retained model producer changed its actual original module source.");
    }
    internal byte[] MappedWriterCode(uint image)
    {
        var code = _writerCode.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(code.AsSpan(21), checked(image + _protectionSlot));
        BinaryPrimitives.WriteUInt32LittleEndian(code.AsSpan(39), checked(image + _protectionSlot));
        return code;
    }
}
