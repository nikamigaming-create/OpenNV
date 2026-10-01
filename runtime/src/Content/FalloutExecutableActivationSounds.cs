using System.Buffers.Binary;
using System.Text.RegularExpressions;

namespace OpenNV.Runtime.Content;

internal static partial class FalloutExecutableStringTable
{
    internal static string ReadNoActivationSoundDefault(string path)
    {
        var (code, image) = Load(path);
        return ReadNoActivationSoundAssociation(code, image.CodeBase,
            image.ScriptCommandStart("ClearNoActivationSound", code, 0), image.Literal, image.IsWritableObject);
    }

    // Admit the source relationship: clearing command -> global sound slot ->
    // lazy editor-ID lookup. Addresses and the default identity remain owned
    // inputs; another build must satisfy the same relationships or fail closed.
    internal static string ReadNoActivationSoundAssociation(ReadOnlySpan<byte> code, uint codeBase, int commandOffset,
        Func<uint, string?> literal, Func<uint, bool> writableObject)
    {
        if (commandOffset < 0 || commandOffset > code.Length - 12)
            throw new InvalidDataException("Owned activation-sound command exceeds its code extent.");
        var command = code[commandOffset..];
        if (!command[..4].SequenceEqual(new byte[] { 0x55, 0x8b, 0xec, 0xe8 }) ||
            !command.Slice(8, 4).SequenceEqual(new byte[] { 0xb0, 1, 0x5d, 0xc3 }))
            throw new NotSupportedException("Owned activation-sound clearing command is unbound.");
        var clearAt = TargetOffset(code, codeBase, commandOffset + 4, 15);
        var clear = code[clearAt..];
        if (!clear[..5].SequenceEqual(new byte[] { 0x55, 0x8b, 0xec, 0xc7, 0x05 }) || U32(clear, 9) != 0 ||
            !clear.Slice(13, 2).SequenceEqual(new byte[] { 0x5d, 0xc3 }))
            throw new NotSupportedException("Owned activation-sound clearing owner is unbound.");
        var slot = U32(clear, 5);
        if (!writableObject(slot)) throw new InvalidDataException("Owned activation-sound slot is not writable source state.");
        string? result = null;
        for (var at = 0; at <= code.Length - 40; ++at)
        {
            var getter = code[at..];
            if (!getter[..5].SequenceEqual(new byte[] { 0x55, 0x8b, 0xec, 0x83, 0x3d }) || U32(getter, 5) != slot ||
                !getter.Slice(9, 4).SequenceEqual(new byte[] { 0, 0x75, 0x15, 0x68 }) ||
                !getter.Slice(17, 2).SequenceEqual(new byte[] { 0x8b, 0x0d }) || getter[23] != 0xe8 ||
                getter[28] != 0xa3 || U32(getter, 29) != slot || getter[33] != 0xa1 || U32(getter, 34) != slot ||
                !getter.Slice(38, 2).SequenceEqual(new byte[] { 0x5d, 0xc3 })) continue;
            var lookupAt = TargetOffset(code, codeBase, at + 24, 3);
            var name = literal(U32(getter, 13));
            if (!writableObject(U32(getter, 19)) || !code.Slice(lookupAt, 3).SequenceEqual(new byte[] { 0x55, 0x8b, 0xec }) ||
                name is null || !Regex.IsMatch(name, @"^[A-Za-z0-9_]+$", RegexOptions.CultureInvariant))
                throw new NotSupportedException("Owned activation-sound default lookup is unbound.");
            if (result is not null) throw new InvalidDataException("Owned activation-sound default lookup is ambiguous.");
            result = name;
        }
        return result ?? throw new NotSupportedException("Owned activation-sound default has no admitted association.");
    }

    private static int TargetOffset(ReadOnlySpan<byte> code, uint codeBase, int displacementAt, int extent)
    {
        var target = (long)displacementAt + 4 + BinaryPrimitives.ReadInt32LittleEndian(code[displacementAt..]);
        if (target < 0 || target > code.Length - extent || (ulong)codeBase + (ulong)target > uint.MaxValue)
            throw new InvalidDataException("Owned activation-sound call exceeds its code extent.");
        return (int)target;
    }
}
