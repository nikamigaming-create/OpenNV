using System.Buffers.Binary;
using System.Reflection.PortableExecutable;

namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal enum NativePluginModuleConstructionKind : uint { NoEntry = 1, Leaf = 2, DynamicCrt = 3 }
internal sealed record NativePluginConstructorField(uint Rva, uint Bytes, uint InitialWord, string Role);
internal sealed record NativePluginConstructorInitializer(uint Rva, uint DestructorRva, bool Allocates);

// This is a source declaration, not an emulated constructor. Its only consumer
// is the original Windows loader. Current CRT predicates are independently
// borrowed immediately before that loader call on the actual native thread.
internal sealed class NativePluginModuleConstructionSource
{
    internal string Path { get; }
    internal string Sha256 { get; }
    internal string StackSha256 { get; }
    internal string Family { get; }
    internal NativePluginModuleConstructionKind Kind { get; }
    internal uint EntryRva { get; }
    internal IReadOnlyList<NativePluginConstructorField> Fields { get; }
    internal IReadOnlyList<NativePluginConstructorInitializer> Initializers { get; }
    private readonly NativePluginImageImport[] _imports;
    private NativePluginModuleConstructionSource(NativePluginImageDeclaration image, string stack,
        NativePluginModuleConstructionKind kind, uint entry, IReadOnlyList<NativePluginConstructorField> fields,
        IReadOnlyList<NativePluginConstructorInitializer> initializers)
    {
        Path = image.Path; Sha256 = image.Sha256; StackSha256 = stack.ToUpperInvariant(); Kind = kind; EntryRva = entry;
        Family = kind switch
        {
            NativePluginModuleConstructionKind.NoEntry => "PE32/no-entry/no-TLS-callbacks",
            NativePluginModuleConstructionKind.Leaf => "PE32/leaf-TRUE-stdcall/no-TLS-callbacks",
            _ => "PE32/dynamic-UCRT/bounded-memory-attach/no-pre-entry-I/O"
        };
        Fields = Array.AsReadOnly(fields.ToArray()); Initializers = Array.AsReadOnly(initializers.ToArray()); _imports = image.Imports.ToArray();
    }
    internal static NativePluginModuleConstructionSource Read(NativePluginImageDeclaration image, string stack)
    {
        if (stack.Length != 64 || !stack.All(Uri.IsHexDigit) || !image.Dll || image.Managed || image.DelayImports ||
            image.Machine != Machine.I386 || image.Magic != PEMagic.PE32)
            throw new InvalidDataException("Original construction lost its exact unmanaged source/stack declaration.");
        using var input = NativeNvseHostSource.Lease(image.Path, image.Sha256, dll: true);
        using var pe = new PEReader(input, PEStreamOptions.LeaveOpen);
        var source = new NativePluginConstructorImage(pe, input, image);
        source.RequireNoTlsCallbacks();
        var entry = checked((uint)pe.PEHeaders.PEHeader!.AddressOfEntryPoint);
        if (entry == 0) return new(image, stack, NativePluginModuleConstructionKind.NoEntry, 0, [], []);
        if (source.LeafTrue(entry)) return new(image, stack, NativePluginModuleConstructionKind.Leaf, entry, [], []);
        var reduced = NativePluginDynamicCrtConstructor.Read(source, entry);
        return new(image, stack, NativePluginModuleConstructionKind.DynamicCrt, entry, reduced.Fields, reduced.Initializers);
    }
    internal void Check(NativePluginImageDeclaration image, string? stack = null)
    {
        if (!StringComparer.OrdinalIgnoreCase.Equals(System.IO.Path.GetFullPath(Path), System.IO.Path.GetFullPath(image.Path)) ||
            !Sha256.Equals(image.Sha256, StringComparison.OrdinalIgnoreCase) ||
            stack is not null && !StackSha256.Equals(stack, StringComparison.OrdinalIgnoreCase) || !_imports.SequenceEqual(image.Imports))
            throw new InvalidDataException("Original construction source belongs to another image/stack/import graph.");
    }
}

internal sealed class NativePluginConstructorImage
{
    private readonly PEReader _pe;
    private readonly Stream _input;
    private readonly PEHeader _header;
    private readonly Dictionary<uint, NativePluginImageImport> _imports;
    private readonly HashSet<uint> _relocations = [];
    internal uint Basis => checked((uint)_header.ImageBase);
    internal int Instructions { get; set; }
    internal NativePluginConstructorImage(PEReader pe, Stream input, NativePluginImageDeclaration declaration)
    {
        _pe = pe; _input = input; _header = pe.PEHeaders.PEHeader!; _imports = declaration.Imports.ToDictionary(row => row.SlotRva);
        var directory = _header.BaseRelocationTableDirectory;
        if (directory.RelativeVirtualAddress == 0 && directory.Size == 0) return;
        if (directory.RelativeVirtualAddress <= 0 || directory.Size <= 0) throw new InvalidDataException("Original construction relocations are incomplete.");
        var bytes = Read(checked((uint)directory.RelativeVirtualAddress), directory.Size); var at = 0;
        while (at < bytes.Length)
        {
            if (bytes.Length - at < 8) throw new InvalidDataException("Original construction relocation block is truncated.");
            var page = Word(bytes, at); var size = Word(bytes, at + 4);
            if (size < 8 || size % 2 != 0 || size > bytes.Length - at) throw new InvalidDataException("Original construction relocation block extent is invalid.");
            for (var slot = at + 8; slot < at + size; slot += 2)
            {
                var row = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(slot)); var kind = row >> 12;
                if (kind == 0) continue;
                if (kind != 3 || !_relocations.Add(checked(page + (uint)(row & 0xfff))))
                    throw new NotSupportedException("Original construction requires an independent relocation producer.");
            }
            at = checked(at + (int)size);
        }
    }
    internal byte[] Read(uint rva, int count, bool executable = false)
    {
        if (count < 0 || count > 1024 * 1024 || (ulong)rva + (uint)count > (uint)_header.SizeOfImage)
            throw new InvalidDataException("Original construction byte extent leaves its source image.");
        var sections = _pe.PEHeaders.SectionHeaders.Where(row => rva >= row.VirtualAddress &&
            (ulong)rva + (uint)count <= (ulong)row.VirtualAddress + (uint)Math.Max(row.VirtualSize, row.SizeOfRawData)).ToArray();
        if (sections.Length != 1 || executable && (!sections[0].SectionCharacteristics.HasFlag(SectionCharacteristics.MemExecute) ||
            (ulong)rva + (uint)count > (ulong)sections[0].VirtualAddress + (uint)sections[0].SizeOfRawData))
            throw new InvalidDataException("Original construction lacks complete unique source section backing.");
        var section = sections[0]; var delta = checked(rva - (uint)section.VirtualAddress);
        var stored = delta < (uint)section.SizeOfRawData ? checked((int)Math.Min((uint)count, (uint)section.SizeOfRawData - delta)) : 0;
        var bytes = new byte[count]; var prior = _input.Position;
        try
        {
            if (stored != 0)
            {
                var disk = checked((long)section.PointerToRawData + delta);
                if (disk < 0 || disk + stored > _input.Length) throw new InvalidDataException("Original construction source section is truncated.");
                _input.Position = disk; _input.ReadExactly(bytes.AsSpan(0, stored));
            }
            return bytes; // Only the declared virtual section tail supplies zero-fill.
        }
        finally { _input.Position = prior; }
    }
    internal uint Address(uint rva, int offset) => ToRva(Word(Read(rva, checked(offset + 4)), offset));
    internal uint ToRva(uint address) => address >= Basis && (ulong)address - Basis < (uint)_header.SizeOfImage ? address - Basis :
        throw new NotSupportedException("Original constructor references an unowned foreign address.");
    internal uint Call(uint at)
    {
        Need(at, [0xe8], "selected direct child"); var code = Read(at, 5, true);
        var target = checked((long)at + 5 + BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(1)));
        if (target is < 0 or > uint.MaxValue) throw new InvalidDataException("Original constructor child address wraps.");
        _ = Read((uint)target, 1, true); return (uint)target;
    }
    internal uint Tail(uint at)
    {
        Need(at, [0xe9], "selected direct continuation"); var bytes = Read(at, 5, true);
        var target = checked((long)at + 5 + BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(1)));
        if (target is < 0 or > uint.MaxValue) throw new InvalidDataException("Original constructor continuation wraps.");
        _ = Read((uint)target, 1, true); return (uint)target;
    }
    internal uint Push(uint at) { Need(at, [0x68], "selected source operand"); return Address(at, 1); }
    internal uint Getter(uint function)
    { Need(function, [0xb8], "source cell getter"); Need(function + 5, [0xc3], "source cell getter return"); return Address(function, 1); }
    internal bool LeafTrue(uint rva)
    {
        var bytes = Read(rva, 12, true);
        return bytes.AsSpan().StartsWith(new byte[] { 0xb8, 1, 0, 0, 0, 0xc2, 12, 0 }) ||
            bytes.AsSpan().StartsWith(new byte[] { 0x6a, 1, 0x58, 0xc2, 12, 0 }) ||
            bytes.AsSpan().StartsWith(new byte[] { 0x33, 0xc0, 0x40, 0xc2, 12, 0 }) ||
            bytes.AsSpan().StartsWith(new byte[] { 0x55, 0x8b, 0xec, 0xb8, 1, 0, 0, 0, 0x5d, 0xc2, 12, 0 });
    }
    internal void Need(uint at, byte[] bytes, string role)
    { if (!Read(at, bytes.Length, true).AsSpan().SequenceEqual(bytes)) throw new NotSupportedException("Original construction changed its source " + role + "."); }
    internal NativePluginConstructorField Field(uint rva, uint count, uint initial, string role, bool writable = true)
    {
        var sections = _pe.PEHeaders.SectionHeaders.Where(row => rva >= row.VirtualAddress && (ulong)rva + count <=
            (ulong)row.VirtualAddress + (uint)Math.Max(row.VirtualSize, row.SizeOfRawData)).ToArray();
        if (count < 4 || count % 4 != 0 || sections.Length != 1 ||
            sections[0].SectionCharacteristics.HasFlag(SectionCharacteristics.MemExecute) ||
            sections[0].SectionCharacteristics.HasFlag(SectionCharacteristics.MemWrite) != writable ||
            _relocations.Any(at => at < (ulong)rva + count && (ulong)at + 4 > rva))
            throw new NotSupportedException("Original construction field has an unowned layout/protection/relocation: " + role);
        var bytes = Read(rva, checked((int)count));
        if (Word(bytes, 0) != initial || bytes.AsSpan(4).IndexOfAnyExcept((byte)0) >= 0)
            throw new NotSupportedException("Original construction changed its initial " + role + ".");
        return new(rva, count, initial, role);
    }
    internal NativePluginImageImport Import(uint slot, string name)
    {
        if (!_imports.TryGetValue(slot, out var row) || row.Ordinal is not null || row.Name != name)
            throw new NotSupportedException("Original construction lost its selected named provider child: " + name);
        return row;
    }
    internal NativePluginImageImport Import(uint slot) => _imports.TryGetValue(slot, out var row) ? row :
        throw new NotSupportedException("Original constructor indirect entry is not its named source IAT.");
    internal void RequireWritable(uint rva, uint count)
    {
        if (!_pe.PEHeaders.SectionHeaders.Any(row => row.SectionCharacteristics.HasFlag(SectionCharacteristics.MemWrite) &&
            !row.SectionCharacteristics.HasFlag(SectionCharacteristics.MemExecute) && rva >= row.VirtualAddress &&
            (ulong)rva + count <= (ulong)row.VirtualAddress + (uint)Math.Max(row.VirtualSize, row.SizeOfRawData)))
            throw new NotSupportedException("Original constructor store leaves its actual writable source data.");
    }
    internal NativePluginImageImport ImportAt(uint at, string name)
    { Need(at, [0xff, 0x15], "actual imported child"); return Import(Address(at, 2), name); }
    internal NativePluginImageImport Thunk(uint function, string name)
    { Need(function, [0xff, 0x25], "actual named provider tail"); return Import(Address(function, 2), name); }
    internal void RequireNoTlsCallbacks()
    {
        var tls = _header.ThreadLocalStorageTableDirectory;
        if (tls.RelativeVirtualAddress == 0 && tls.Size == 0) return;
        if (tls.RelativeVirtualAddress <= 0 || tls.Size != 24) throw new InvalidDataException("Original construction TLS directory is incomplete.");
        var rows = Read(checked((uint)tls.RelativeVirtualAddress), 24); var pointer = Word(rows, 12);
        if (pointer != 0 && Word(Read(ToRva(pointer), 4), 0) != 0)
            throw new NotSupportedException("Original construction requires its actual pre-entry TLS callback owner.");
    }
    internal static uint Word(byte[] bytes, int at) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at));
}
