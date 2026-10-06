using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using OpenNV.Runtime.Compatibility.NativePlugins;

internal static class OwnedPluginMemoryAudit
{
    internal static void Run(string path)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (file.Length is <= 0 or > int.MaxValue)
            throw new InvalidDataException("Owned plugin file extent exceeds the audit memory budget.");
        var bytes = new byte[checked((int)file.Length)];
        file.ReadExactly(bytes);
        using var pe = new PEReader(new MemoryStream(bytes, writable: false));
        var headers = pe.PEHeaders;
        var optional = headers.PEHeader;
        if (headers.CoffHeader.Machine != Machine.I386 || optional is null || optional.Magic != PEMagic.PE32 ||
            (headers.CoffHeader.Characteristics & Characteristics.Dll) == 0 || headers.CorHeader is not null)
            throw new NotSupportedException("Owned native-plugin memory requires an original unmanaged I386 PE32 DLL.");
        if (optional.SizeOfHeaders <= 0 || optional.SizeOfHeaders > bytes.Length ||
            optional.SizeOfImage < optional.SizeOfHeaders)
            throw new InvalidDataException("Owned DLL headers have invalid extents.");

        var imageBase = checked((uint)optional.ImageBase);
        var imageEnd = (ulong)imageBase + (uint)optional.SizeOfImage;
        if (imageEnd > 1UL << 32)
            throw new InvalidDataException("Owned DLL preferred image extent wraps the x86 address space.");
        using var memory = new X86GuestMemory(bytes.Length);
        MapAndCompare(memory, imageBase, bytes.AsSpan(0, optional.SizeOfHeaders));
        var mapped = optional.SizeOfHeaders;
        var sections = 0;
        foreach (var section in headers.SectionHeaders)
        {
            if (section.SizeOfRawData == 0) continue;
            if (section.SizeOfRawData < 0 || section.PointerToRawData < 0 ||
                section.PointerToRawData > bytes.Length - section.SizeOfRawData)
                throw new InvalidDataException("Owned DLL section is not backed by complete file bytes.");
            var start = (ulong)imageBase + unchecked((uint)section.VirtualAddress);
            if (start > uint.MaxValue || start + (uint)section.SizeOfRawData > imageEnd)
                throw new InvalidDataException("Owned DLL section exceeds its preferred image extent.");
            MapAndCompare(memory, (uint)start, bytes.AsSpan(section.PointerToRawData, section.SizeOfRawData));
            mapped = checked(mapped + section.SizeOfRawData);
            ++sections;
        }
        if (sections == 0) throw new InvalidDataException("Owned DLL has no file-backed sections.");

        var before = SHA256.HashData(bytes);
        file.Position = 0;
        var after = SHA256.HashData(file);
        if (!before.AsSpan().SequenceEqual(after))
            throw new InvalidDataException("Owned plugin changed during its read-only memory audit.");
        Console.WriteLine($"OPENNV_OWNED_X86_SOURCE_MEMORY_PASS sha256={Convert.ToHexString(before)} sections={sections} fileBackedBytes={mapped}");
        Console.WriteLine("NOT_DLL_COMPATIBILITY execution=absent callbacks=unbound objects=unbound hooks=unbound loader=absent");
    }

    private static void MapAndCompare(X86GuestMemory memory, uint address, ReadOnlySpan<byte> source)
    {
        memory.Map(address, source, X86GuestMemoryAccess.ReadOnly);
        var observed = new byte[source.Length];
        memory.Read(address, observed);
        if (!source.SequenceEqual(observed))
            throw new InvalidOperationException("Guest source memory differs from the unchanged owned DLL.");
        try { memory.Write(address, [0]); }
        catch (NotSupportedException) { return; }
        throw new InvalidOperationException("Owned source memory admitted a write.");
    }
}
