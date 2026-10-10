using System.Buffers.Binary;
using System.Reflection.PortableExecutable;

namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal sealed record NativeEngineTlsRelocation(uint Offset, ushort Kind);
internal sealed record NativeEngineStaticTlsSource(string RuntimeSha256, uint DirectoryRva, uint IndexAddress,
    byte[] InitialTemplate, uint ZeroFillBytes, uint Characteristics, IReadOnlyList<uint> Callbacks,
    IReadOnlyList<NativeEngineTlsRelocation> Relocations)
{
    internal uint InitialBytes => checked((uint)InitialTemplate.Length + ZeroFillBytes);
    internal void RequireAlignment(uint pointer)
    {
        var encoded = (Characteristics >> 20) & 15U;
        if ((Characteristics & ~0x00f00000U) != 0 || encoded == 15)
            throw new NotSupportedException("Original static TLS declares unowned characteristic/alignment bits.");
        if (encoded != 0 && pointer % (1U << checked((int)encoded - 1)) != 0)
            throw new InvalidDataException("Actual Windows TLS allocation does not meet its original declared alignment.");
    }

    // Windows' source loader performs these copies before any TLS callback.
    // They are initial declared data, not constructed gameplay/current fields.
    internal byte[] ReadLoaderInitialImage()
    {
        // An unspecified alignment is a genuine declaration. Unknown bits
        // stay refused rather than becoming an invented loader policy.
        RequireAlignment(0);
        if (Callbacks.Count != 0)
            throw new NotSupportedException("Source TLS callbacks require their actual first-party callable/event owners before initial publication.");
        if (Relocations.Count != 0)
            throw new NotSupportedException("Source TLS initial pointers require their actual native relocation/object owners.");
        var image = new byte[checked((int)InitialBytes)]; InitialTemplate.CopyTo(image, 0); return image;
    }
}

internal static class NativePluginEngineTlsSource
{
    internal static NativeEngineStaticTlsSource Read(NativeNvseHostSource host, ulong generation)
    {
        host.Check(generation);
        using var input = NativeNvseHostSource.Lease(host.RuntimePath, host.RuntimeSha256, dll: false);
        using var pe = new PEReader(input, PEStreamOptions.LeaveOpen);
        var h = pe.PEHeaders.PEHeader ?? throw new InvalidDataException("Source TLS has no complete executable header.");
        if (pe.PEHeaders.CoffHeader.Machine != Machine.I386 || h.Magic != PEMagic.PE32 ||
            h.ThreadLocalStorageTableDirectory.RelativeVirtualAddress <= 0 || h.ThreadLocalStorageTableDirectory.Size != 24)
            throw new NotSupportedException("Selected source has no complete PE32 static TLS-directory owner.");
        var basis = checked((uint)h.ImageBase); var directory = ReadRva(checked((uint)h.ThreadLocalStorageTableDirectory.RelativeVirtualAddress), 24);
        var start = U32(directory, 0); var end = U32(directory, 4); var index = U32(directory, 8);
        var callbacks = U32(directory, 12); var zero = U32(directory, 16); var characteristics = U32(directory, 20);
        var empty = start == 0 && end == 0;
        if ((!empty && (start < basis || end < start || (ulong)end > (ulong)basis + (uint)h.SizeOfImage)) ||
            (ulong)end - start + zero is 0 or > 16 * 1024 * 1024 || index % 4 != 0 || !Writable(index, 4))
            throw new InvalidDataException("Source TLS directory/template/index leaves its declared writable image.");
        byte[] initial = empty || start == end ? [] : ReadRva(start - basis, checked((int)(end - start))); var callbackEntries = new List<uint>();
        if (callbacks != 0)
        {
            var terminated = false;
            for (uint ordinal = 0; ordinal < 4096; ++ordinal)
            {
                var address = checked(callbacks + ordinal * 4);
                if (address < basis) throw new InvalidDataException("Source TLS callback table lies outside its image.");
                var callback = U32(ReadRva(address - basis, 4), 0);
                if (callback == 0) { terminated = true; break; }
                if (!Executable(callback)) throw new InvalidDataException("Source TLS callback is not an executable source declaration.");
                callbackEntries.Add(callback);
            }
            if (!terminated) throw new NotSupportedException("Source TLS callback list exceeds its complete declared inspection budget.");
        }
        var relocations = new List<NativeEngineTlsRelocation>(); var relocationDirectory = h.BaseRelocationTableDirectory;
        if (relocationDirectory.RelativeVirtualAddress != 0 || relocationDirectory.Size != 0)
        {
            if (relocationDirectory.RelativeVirtualAddress <= 0 || relocationDirectory.Size <= 0)
                throw new InvalidDataException("Source TLS relocation envelope is incomplete.");
            var bytes = ReadRva(checked((uint)relocationDirectory.RelativeVirtualAddress), relocationDirectory.Size); var at = 0;
            while (at < bytes.Length)
            {
                if (bytes.Length - at < 8) throw new InvalidDataException("Source TLS relocation block is truncated.");
                var page = U32(bytes, at); var extent = U32(bytes, at + 4);
                if (extent < 8 || extent % 2 != 0 || extent > bytes.Length - at)
                    throw new InvalidDataException("Source TLS relocation block has an invalid full extent.");
                for (var slot = at + 8; slot < at + extent; slot += 2)
                {
                    var entry = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(slot)); var kind = checked((ushort)(entry >> 12));
                    if (kind == 0) continue;
                    var address = checked(basis + page + (uint)(entry & 0xfff));
                    if (address >= start && address < end) relocations.Add(new(address - start, kind));
                }
                at = checked(at + (int)extent);
            }
        }
        host.Check(generation);
        return new(host.RuntimeSha256, checked((uint)h.ThreadLocalStorageTableDirectory.RelativeVirtualAddress), index,
            initial, zero, characteristics, callbackEntries.AsReadOnly(), relocations.AsReadOnly());

        byte[] ReadRva(uint rva, int count)
        {
            if (count < 0 || count > 16 * 1024 * 1024 || (ulong)rva + (uint)count > (uint)h.SizeOfImage)
                throw new InvalidDataException("Source TLS byte request exceeds its declared image bounds.");
            var retained = input.Position;
            try
            {
                var bytes = new byte[count]; var used = 0;
                while (used < count)
                {
                    var cursor = (ulong)rva + (uint)used;
                    if (cursor < (uint)h.SizeOfHeaders)
                    {
                        var take = checked((int)Math.Min((ulong)(count - used), (uint)h.SizeOfHeaders - cursor));
                        if (cursor + (uint)take > (ulong)input.Length) throw new InvalidDataException("Source TLS header data is truncated.");
                        input.Position = checked((long)cursor); input.ReadExactly(bytes.AsSpan(used, take)); used += take; continue;
                    }
                    var sections = pe.PEHeaders.SectionHeaders.Where(s => s.VirtualAddress >= 0 && s.VirtualSize >= 0 && s.SizeOfRawData >= 0 &&
                        cursor >= (uint)s.VirtualAddress && cursor < (ulong)(uint)s.VirtualAddress + (uint)Math.Max(s.VirtualSize, s.SizeOfRawData)).ToArray();
                    if (sections.Length != 1) throw new InvalidDataException("Source TLS data has an absent or overlapping complete image section.");
                    var section = sections[0]; var offset = cursor - (uint)section.VirtualAddress;
                    var extent = (ulong)(uint)Math.Max(section.VirtualSize, section.SizeOfRawData);
                    var takeSection = checked((int)Math.Min((ulong)(count - used), extent - offset));
                    var stored = offset < (uint)section.SizeOfRawData
                        ? checked((int)Math.Min((ulong)(uint)takeSection, (uint)section.SizeOfRawData - offset)) : 0;
                    if (stored != 0)
                    {
                        if (section.PointerToRawData < 0 || (ulong)(uint)section.PointerToRawData + offset + (uint)stored > (ulong)input.Length)
                            throw new InvalidDataException("Source TLS section data is truncated.");
                        input.Position = checked(section.PointerToRawData + (long)offset); input.ReadExactly(bytes.AsSpan(used, stored));
                    }
                    // The PE virtual tail is loader-declared zero data. It is
                    // distinct from absent stored bytes or unknown layout.
                    used += takeSection;
                }
                return bytes;
            }
            finally { input.Position = retained; }
        }
        bool Writable(uint address, uint count) => address >= basis && pe.PEHeaders.SectionHeaders.Any(s =>
            s.SectionCharacteristics.HasFlag(SectionCharacteristics.MemWrite) && !s.SectionCharacteristics.HasFlag(SectionCharacteristics.MemExecute) &&
            address - basis >= s.VirtualAddress && (ulong)(address - basis) + count <= (ulong)s.VirtualAddress + (uint)Math.Max(s.VirtualSize, s.SizeOfRawData));
        bool Executable(uint address) => address >= basis && pe.PEHeaders.SectionHeaders.Any(s =>
            s.SectionCharacteristics.HasFlag(SectionCharacteristics.MemExecute) && address - basis >= s.VirtualAddress &&
            (ulong)(address - basis) < (ulong)s.VirtualAddress + (uint)s.SizeOfRawData);
    }

    internal static void RequireWritableCell(NativeNvseHostSource host, ulong generation, uint address)
    {
        host.Check(generation);
        using var input = NativeNvseHostSource.Lease(host.RuntimePath, host.RuntimeSha256, dll: false);
        using var pe = new PEReader(input, PEStreamOptions.LeaveOpen);
        var header = pe.PEHeaders.PEHeader ?? throw new InvalidDataException("Source global has no complete executable header.");
        if (address % 4 != 0 || address < header.ImageBase || !pe.PEHeaders.SectionHeaders.Any(section =>
            section.SectionCharacteristics.HasFlag(SectionCharacteristics.MemWrite) && !section.SectionCharacteristics.HasFlag(SectionCharacteristics.MemExecute) &&
            (ulong)address - header.ImageBase >= (ulong)section.VirtualAddress &&
            (ulong)address - header.ImageBase + 4 <= (ulong)section.VirtualAddress + (uint)Math.Max(section.VirtualSize, section.SizeOfRawData)))
            throw new InvalidDataException("Engine global is not a complete aligned non-executable source data cell.");
    }
    private static uint U32(byte[] bytes, int at) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at));
}
