using System.Buffers.Binary;
using System.Security.Cryptography;
using OpenNV.Runtime.Compatibility.NativePlugins;

namespace OpenNV.Runtime.Content;

internal static partial class FalloutExecutableStringTable
{
    // This reader elects ABI members from the selected class declaration and
    // complete loaded-file wrappers, rather than SDK slot labels. Addresses
    // remain private source associations and never execute original code.
    internal static NativeNvseBinaryDeclaration ReadBinaryMemberDeclaration(string runtimePath, string pluginSha256)
    {
        if (pluginSha256.Length != 64 || !pluginSha256.All(Uri.IsHexDigit))
            throw new InvalidDataException("Binary member source lacks the exact selected module identity.");
        var (_, image) = Load(runtimePath);
        var tables = image.SourceClassTables(".?AVBSFile@@", 19).ToArray();
        if (tables.Length != 1) throw new NotSupportedException("Original binary class table/layout is absent or ambiguous.");
        var table = image.Read(tables[0], 19 * 4);
        var entries = Enumerable.Range(0, 19).Select(slot => (Slot: slot, Address: U32(table, slot * 4))).ToArray();
        var readCandidates = new List<(int Slot, uint Address, uint Inherited)>();
        foreach (var entry in entries)
            if (HasBinaryLoadedReadFrame(image.Read(entry.Address, 10)) &&
                TryBinaryLoadedRead(image.Read(entry.Address, 128), entry.Address, image, out var inherited))
                readCandidates.Add((entry.Slot, entry.Address, inherited));
        if (readCandidates.Count != 1) throw new NotSupportedException("Actual loaded-CRT byte-read wrapper is absent or ambiguous.");
        var read = readCandidates[0];
        var cursor = entries.Where(entry => BinaryFieldGetter(image.Read(entry.Address, 8), 4)).ToArray();
        if (cursor.Length != 1) throw new NotSupportedException("Actual independent binary cursor getter is absent or ambiguous.");
        var initialization = entries.Single(entry => entry.Slot == 4);
        RequireBinaryProcedureInitialization(initialization.Address, image);
        var procedures = BinaryProcedureEntries(initialization.Address, image);
        RequireBinaryPlainReadProcedure(procedures.Read, read.Inherited, image);
        var current = entries.Single(entry => entry.Slot == 2);
        var seek = entries.Single(entry => entry.Slot == 5);
        var origins = SeekOrigins(image.Read(seek.Address, 192), image);
        RequireBinaryCurrentSeek(current.Address, origins[1], image);
        var construction = ReadBinaryFileConstruction(runtimePath);
        if (construction.SeekCurrent != origins[1]) throw new InvalidDataException("Binary constructor/member seek declarations disagree.");
        using var original = new FileStream(runtimePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var sha = Convert.ToHexString(SHA256.HashData(original));
        if (!StringComparer.OrdinalIgnoreCase.Equals(sha, construction.RuntimeSha256))
            throw new InvalidDataException("Original binary member source changed during declaration election.");
        var owner = "selected-binary-loaded-CRT-member-ABI:" + sha;
        // Size/flush and the entire derived seek body are not elected merely
        // because a public header names their slots. Their callable C# owners
        // exist, but their original source entry remains unadmitted here.
        return new(sha, pluginSha256, owner, 0x158, 19,
        [
            new(read.Address, NativeNvseBinaryMethod.Read, NativePluginAbi.Thiscall, read.Slot, owner + ":actual-count/derived-logical-position"),
            new(read.Inherited, NativeNvseBinaryMethod.ReadInherited, NativePluginAbi.Thiscall, null, owner + ":same-constructor/read-backend"),
            new(cursor[0].Address, NativeNvseBinaryMethod.Cursor, NativePluginAbi.Thiscall, cursor[0].Slot, owner + ":independent-binary-offset"),
            new(current.Address, NativeNvseBinaryMethod.SeekCurrent, NativePluginAbi.Thiscall, current.Slot, owner + ":source-origin/derived-seek"),
            new(initialization.Address, NativeNvseBinaryMethod.SelectProcedures, NativePluginAbi.Thiscall, initialization.Slot, owner + ":complete-two-pair-selection"),
        ], new(procedures.Read, procedures.Write, procedures.AlternateRead, procedures.AlternateWrite, owner + ":actual-cdecl-read/procedure-pairs"));
    }

    private static bool BinaryFieldGetter(byte[] bytes, byte offset)
        => bytes.Length >= 4 && bytes[0] == 0x8b && bytes[1] == 0x41 && bytes[2] == offset && bytes[3] == 0xc3;

    private static bool HasBinaryLoadedReadFrame(byte[] bytes)
        => bytes.Length >= 9 && bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 0x55, 0x8b, 0xec, 0x83, 0xec, 8, 0x89, 0x4d }) &&
            unchecked((sbyte)bytes[8]) < 0;

    private static bool TryBinaryLoadedRead(byte[] bytes, uint origin, Image image, out uint inherited)
    {
        inherited = 0;
        try
        {
            var code = new BinaryMemberCode(bytes, origin);
            code.Frame(8); var receiver = code.StoreLocal(1); // actual this=ECX
            var pointer = code.LoadLocal(receiver);
            var condition = code.ZeroRegister(); code.CompareFieldZero(pointer, 0x24);
            code.SetNotEqual(condition); var widened = code.WidenByte(condition); code.TestRegister(widened);
            var loaded = code.JumpNotEqual();
            // This branch is outside the admitted member's loaded-CRT
            // precondition. The complete class owner must establish nonnull
            // genuine CRT state before binding, not pretend the open succeeded.
            code.Position(loaded);
            var count = code.LoadLocal(12); code.PushRegister(count);
            var output = code.LoadLocal(8); code.PushRegister(output); code.LoadLocalInto(1, receiver);
            var target = code.Call();
            if (!image.IsExecutableExtent(target)) return false;
            var result = code.StoreLocal(0); // returned EAX
            var cursorReceiver = code.LoadLocal(receiver); var logical = code.LoadField(cursorReceiver, 0x150);
            code.AddLocal(logical, result);
            var writeReceiver = code.LoadLocal(receiver); code.StoreField(writeReceiver, 0x150, logical);
            code.LoadLocalInto(0, result); code.ReturnFrame(8);
            // The inherited backend is the same one reached by the plain
            // procedure below. Its read-mode behavior is the selected binary
            // authority; no original inherited instructions are executed.
            inherited = target; return true;
        }
        catch (NotSupportedException) { return false; }
    }

    private static void RequireBinaryPlainReadProcedure(uint entry, uint inherited, Image image)
    {
        var bytes = image.Read(entry, 32); var at = 0;
        var count = StackLoad(12); var output = StackLoad(8);
        Push(count); Push(output);
        if (StackLoad(12) != 1 || bytes[at++] != 0xe8)
            throw new NotSupportedException("Original plain binary procedure has no actual cdecl receiver/call declaration.");
        var target = unchecked(entry + checked((uint)at) + 4 + (uint)BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(at, 4))); at += 4;
        if (target != inherited || bytes[at] != 0xc3)
            throw new NotSupportedException("Original plain binary procedure reaches a different backend or stack cleanup.");
        return;
        int StackLoad(byte offset)
        {
            if (at > bytes.Length - 4 || bytes[at] != 0x8b || (bytes[at + 1] & 0xc7) != 0x44 || bytes[at + 2] != 0x24 || bytes[at + 3] != offset)
                throw new NotSupportedException("Binary cdecl procedure has an unowned stack argument.");
            var register = bytes[at + 1] >> 3 & 7; at += 4; return register;
        }
        void Push(int register)
        {
            if (at >= bytes.Length || bytes[at++] != 0x50 + register)
                throw new NotSupportedException("Binary cdecl procedure does not forward its actual byte output/count.");
        }
    }

    private static void RequireBinaryCurrentSeek(uint entry, uint origin, Image image)
    {
        var bytes = image.Read(entry, 32);
        if (bytes[0] != 0x8b || bytes[1] != 0x15 || U32(image.Read(U32(bytes, 2), 4), 0) != origin ||
            !bytes.AsSpan(6, 8).SequenceEqual(new byte[] { 0x8b, 1, 0x8b, 0x40, 0x14, 0x52, 0x8b, 0x54 }) ||
            !bytes.AsSpan(14, 8).SequenceEqual(new byte[] { 0x24, 8, 0x52, 0xff, 0xd0, 0xc2, 4, 0 }))
            throw new NotSupportedException("Original relative binary seek lacks its actual source origin/derived virtual/cleanup declaration.");
    }

    private static (uint Read, uint Write, uint AlternateRead, uint AlternateWrite) BinaryProcedureEntries(uint entry, Image image)
    {
        // The existing complete branch validator runs before this extraction;
        // these operands do not independently admit an unknown initialization.
        var bytes = image.Read(entry, 128);
        var first = 15; var second = checked(first + unchecked((sbyte)bytes[14]));
        if (second < 0 || second > bytes.Length - 20)
            throw new InvalidDataException("Original binary procedures lost their validated complete branch extent.");
        // Boolean true is the first complete branch, false the second.
        return (U32(bytes, second + 6), U32(bytes, second + 16), U32(bytes, first + 6), U32(bytes, first + 16));
    }
}
