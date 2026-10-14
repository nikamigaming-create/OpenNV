using System.Buffers.Binary;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using OpenNV.Runtime.Compatibility.NativePlugins;
using OpenNV.Runtime.Content;
using static NativeBookSoundOperandContracts;

internal static class NativeBookSoundDataAccessContracts
{
    private const uint Read = 0x40000040, Write = 0xc0000040, Execute = 0x60000020;
    private const uint CString = 0x2000;
    private static readonly byte[] Text = Encoding.ASCII.GetBytes("AuthoredBookPageInput\0");
    internal static void Run()
    {
        var readOnly = Image(Read); var before = SHA256.HashData(readOnly);
        using (var input = new MemoryStream(readOnly, writable: false))
        using (var pe = new PEReader(input, PEStreamOptions.LeaveOpen))
        {
            Require(FalloutSourceModuleDataBorrow.HasCompleteReadableData(pe.PEHeaders, input.Length, CString, (uint)Text.Length),
                "read-only file-backed CString is a valid input borrow");
            var source = Source(pe, input); source.RequireReadableData(CString, (uint)Text.Length);
            Require(source.Read(CString, Text.Length).AsSpan().SequenceEqual(Text), "actual source data bytes retained");
            NativeBookSoundOperandContracts.Reject<NotSupportedException>(() => source.RequireWritable(CString, (uint)Text.Length));
            foreach (var (rva, count) in new (uint, uint)[] { (CString, 0), (CString - 1, 2), (CString + 0x1ff, 2),
                (CString + 0x200, 1), (0x3000, 1), (uint.MaxValue, 2), (CString, uint.MaxValue) })
            {
                Require(!FalloutSourceModuleDataBorrow.HasCompleteReadableData(pe.PEHeaders, input.Length, rva, count),
                    "unbacked, empty, header/outside or wrapping data extent rejected");
                NativeBookSoundOperandContracts.Reject<NotSupportedException>(() => source.RequireReadableData(rva, count));
            }
            Require(!FalloutSourceModuleDataBorrow.HasCompleteReadableData(pe.PEHeaders, 0x400 + Text.Length - 1, CString, (uint)Text.Length),
                "implicit terminator from a missing file byte is not an input");
            NativeBookSoundOperandContracts.Reject<InvalidDataException>(() => FalloutExecutableStringTable.ReadSoundEditorIdBytes(Text.AsSpan(0, Text.Length - 1)));
        }
        Require(SHA256.HashData(readOnly).AsSpan().SequenceEqual(before), "source read borrow did not alter the image");
        using (var input = new MemoryStream(Image(Write), writable: false))
        using (var pe = new PEReader(input, PEStreamOptions.LeaveOpen))
        {
            var source = Source(pe, input); source.RequireReadableData(CString, (uint)Text.Length); source.RequireWritable(CString, (uint)Text.Length);
            Require(FalloutSourceModuleDataBorrow.HasCompleteReadableData(pe.PEHeaders, input.Length, CString, (uint)Text.Length),
                "genuine writable model/source data retains its independent write requirement");
        }
        foreach (var flags in new[] { Execute, 0xe0000020U, 0x80000040U, 0x00000040U })
        {
            using var input = new MemoryStream(Image(flags), writable: false); using var pe = new PEReader(input, PEStreamOptions.LeaveOpen);
            Require(!FalloutSourceModuleDataBorrow.HasCompleteReadableData(pe.PEHeaders, input.Length, CString, (uint)Text.Length),
                "code or unreadable data is not a CString data borrow");
            var source = Source(pe, input); NativeBookSoundOperandContracts.Reject<NotSupportedException>(() => source.RequireReadableData(CString, (uint)Text.Length));
        }
        foreach (var codeRva in new[] { CString, CString + 8 })
        {
            using var input = new MemoryStream(Image(Read, codeRva), writable: false); using var pe = new PEReader(input, PEStreamOptions.LeaveOpen);
            Require(!FalloutSourceModuleDataBorrow.HasCompleteReadableData(pe.PEHeaders, input.Length, CString, (uint)Text.Length),
                "full or partial overlapping source sections cannot supply one unique CString");
        }
        var truncated = Image(Read)[..(0x400 + Text.Length - 1)];
        using (var input = new MemoryStream(truncated, writable: false))
        using (var pe = new PEReader(input, PEStreamOptions.LeaveOpen))
        { var source = Source(pe, input); NativeBookSoundOperandContracts.Reject<NotSupportedException>(() => source.RequireReadableData(CString, (uint)Text.Length)); }
        Console.WriteLine("OPENNV_BOOK_DATA_BORROW_CONTRACT_PASS readOnlyInput=true realConstructorGuard=true writableStoreSeparate=true " +
            "executeAndUnreadableRefused=true fullFileBacking=true overlapRefused=true sourceUnchanged=true nativeCalls=UNEXECUTED");
    }
    private static NativePluginConstructorImage Source(PEReader pe, Stream input) => new(pe, input,
        new("authored-book-data-memory", new('A', 64), true, [], [], [], [], false) { Machine = Machine.I386, Magic = PEMagic.PE32 });
    private static byte[] Image(uint flags, uint codeRva = 0x1000)
    {
        var bytes = new byte[0x600]; void U16(int at, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(at), value);
        void U32(int at, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(at), value);
        bytes[0] = (byte)'M'; bytes[1] = (byte)'Z'; U32(0x3c, 0x80); U32(0x80, 0x4550);
        U16(0x84, 0x14c); U16(0x86, 2); U16(0x94, 0xe0); U16(0x96, 0x2102);
        const int optional = 0x98; U16(optional, 0x10b); U32(optional + 4, 0x200); U32(optional + 8, 0x200);
        U32(optional + 16, codeRva); U32(optional + 20, codeRva); U32(optional + 24, CString); U32(optional + 28, 0x10000000);
        U32(optional + 32, 0x1000); U32(optional + 36, 0x200); U16(optional + 40, 6); U16(optional + 48, 6);
        U32(optional + 56, 0x3000); U32(optional + 60, 0x200); U16(optional + 68, 3);
        U32(optional + 72, 0x100000); U32(optional + 76, 0x1000); U32(optional + 80, 0x100000); U32(optional + 84, 0x1000); U32(optional + 92, 16);
        Section(0x178, ".text", codeRva, 0x200, 0x200, Execute); Section(0x1a0, ".rdata", CString, 0x300, 0x400, flags);
        Text.CopyTo(bytes, 0x400); return bytes;
        void Section(int at, string name, uint rva, uint virtualBytes, uint raw, uint access)
        { Encoding.ASCII.GetBytes(name).CopyTo(bytes, at); U32(at + 8, virtualBytes); U32(at + 12, rva); U32(at + 16, 0x200); U32(at + 20, raw); U32(at + 36, access); }
    }
}
