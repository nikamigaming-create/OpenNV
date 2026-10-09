using System.Security.Cryptography;
using OpenNV.Runtime.Content;

// Every byte here is authored first-party ISA input. This tests source
// classification only; no original module, callable page or ABI is executed.
internal static class NativeEngineCommandLeafContracts
{
    internal static void Run()
    {
        Positive([0xb0, 1, 0xc3], true);
        Positive([0xb0, 0, 0xc3], false);
        Positive([0xb8, 1, 0, 0, 0, 0xc3], true);
        Positive([0xb8, 0, 0, 0, 0, 0xc3], false);
        Positive([0xb8, 1, 0x12, 0x34, 0x56, 0xc3], true);
        Positive([0xb8, 2, 0, 0, 0, 0xb0, 1, 0xc3], true);
        Positive([0x31, 0xc0, 0xb0, 1, 0xc3], true);
        Positive([0x32, 0xc0, 0xc3], false);
        Positive([0x90, 0xb0, 1, 0x90, 0xc3], true);
        Positive([0x55, 0x8b, 0xec, 0xb0, 1, 0x5d, 0xc3], true);
        Positive([0x55, 0x89, 0xe5, 0x33, 0xc0, 0xc9, 0xc3], false);
        Reject([0xc3]);
        Reject([0xb0, 2, 0xc3]);
        Reject([0xb8, 1, 0]);
        Reject([0xb0, 1]);
        Reject([0xb0, 1, 0xc2, 0x20, 0]);
        Reject([0x55, 0x8b, 0xec, 0xb0, 1, 0xc3]);
        Reject([0x5d, 0xb0, 1, 0xc3]);
        Reject([0x66, 0xb8, 1, 0, 0xc3]);
        Reject([0xf0, 0x31, 0xc0, 0xc3]);
        Reject([0x8b, 0x44, 0x24, 4, 0xb0, 1, 0xc3]);
        Reject([0xa1, 0, 0x20, 0, 0, 0xc3]);
        Reject([0xa3, 0, 0x20, 0, 0, 0xb0, 1, 0xc3]);
        Reject([0xe8, 0, 0, 0, 0, 0xb0, 1, 0xc3]);
        Reject([0xeb, 0, 0xb0, 1, 0xc3]);
        Reject([0x31, 0xdb, 0xb0, 1, 0xc3]);
        var input = new byte[] { 0x90, 0xb0, 1, 0xc3, 0xcc };
        var identity = Convert.ToHexString(SHA256.HashData(input));
        var leaf = FalloutExecutableStringTable.ReadEngineCommandBooleanLeaf(input, 1, identity, 0x2100, 0x1000);
        if (leaf.SourceRva != 0x1100 || leaf.BodyBytes != 3 || leaf.Instructions != 2 ||
            leaf.BodySha256 != Convert.ToHexString(SHA256.HashData(input.AsSpan(1, 3))) || leaf.RuntimeSha256 != identity)
            throw new InvalidDataException("Authored Boolean declaration erased source/body identity or inspected a sibling.");
        Console.WriteLine("OPENNV_NATIVE_ENGINE_BOOLEAN_SOURCE_PASS nativePublication=unexecuted originalCommand=unexecuted");
    }
    private static void Positive(byte[] code, bool expected)
    {
        var unchanged = code.ToArray(); var identity = Convert.ToHexString(SHA256.HashData(code));
        var leaf = FalloutExecutableStringTable.ReadEngineCommandBooleanLeaf(code, 0, identity, 0x2100, 0x1000);
        if (leaf.Result != expected || leaf.BodyBytes != code.Length || !code.AsSpan().SequenceEqual(unchanged))
            throw new InvalidDataException("Authored pure Boolean source declaration changed bytes/result/complete extent.");
    }
    private static void Reject(byte[] code)
    {
        var unchanged = code.ToArray();
        try { _ = FalloutExecutableStringTable.ReadEngineCommandBooleanLeaf(code, 0, new string('a', 64), 0x2100, 0x1000); }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException)
        {
            if (!code.AsSpan().SequenceEqual(unchanged)) throw new InvalidDataException("Refused declaration changed its original input.");
            return;
        }
        throw new InvalidDataException("Authored unowned/truncated engine source unexpectedly acquired a Boolean owner.");
    }
}
