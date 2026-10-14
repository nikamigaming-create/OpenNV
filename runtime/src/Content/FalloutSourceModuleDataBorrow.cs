using System.Reflection.PortableExecutable;

namespace OpenNV.Runtime.Content;

// A CString input borrows readable data. It does not own a store into that
// data and cannot borrow executable or implicit zero-fill bytes. Its native
// lease separately checks the current committed module mapping on every read.
internal static class FalloutSourceModuleDataBorrow
{
    internal static bool HasCompleteReadableData(PEHeaders headers, long fileBytes, uint rva, uint count)
    {
        if (count == 0 || fileBytes < 0 || headers.PEHeader is not { SizeOfImage: > 0 } image ||
            (ulong)rva + count > (uint)image.SizeOfImage) return false;
        var sections = headers.SectionHeaders.Where(section => section.VirtualAddress >= 0 && section.VirtualSize >= 0 && section.SizeOfRawData >= 0 &&
            (ulong)rva < (ulong)section.VirtualAddress + (uint)Math.Max(section.VirtualSize, section.SizeOfRawData) &&
            (uint)section.VirtualAddress < (ulong)rva + count).ToArray();
        if (sections.Length != 1) return false;
        var section = sections[0]; var access = section.SectionCharacteristics;
        if (!access.HasFlag(SectionCharacteristics.MemRead) || access.HasFlag(SectionCharacteristics.MemExecute) || section.PointerToRawData < 0 ||
            rva < section.VirtualAddress || (ulong)rva + count > (ulong)section.VirtualAddress + (uint)Math.Max(section.VirtualSize, section.SizeOfRawData)) return false;
        var offset = rva - (uint)section.VirtualAddress;
        return (ulong)offset + count <= (uint)section.SizeOfRawData &&
            (ulong)section.PointerToRawData + offset + count <= (ulong)fileBytes;
    }
}

internal static partial class FalloutExecutableStringTable
{
    private sealed partial class Image
    {
        internal bool IsReadableDataExtent(uint address, int count) => count > 0 && address >= Base &&
            FalloutSourceModuleDataBorrow.HasCompleteReadableData(headers, bytes.LongLength, address - Base, checked((uint)count));
    }
}
