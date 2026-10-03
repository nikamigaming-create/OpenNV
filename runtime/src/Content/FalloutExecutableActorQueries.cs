using System.Buffers.Binary;
using System.Security.Cryptography;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutVampireQueryDeclaration(string SourceSha256)
{
    internal int Value => 0;
}

internal static partial class FalloutExecutableStringTable
{
    internal static FalloutVampireQueryDeclaration ReadVampireQueryDeclaration(string path)
    {
        var (code, image) = Load(path);
        var (execute, evaluate) = image.ReferenceConditionEntries("GetVampire", 40, code);
        return ReadVampireQueryAssociation(code, image.CodeBase, execute, evaluate);
    }

    // Admit a named zero-argument reference query whose result starts at zero
    // and whose only true branch depends on an unconditional false predicate.
    // All call targets and branch joins come from the owned declaration.
    internal static FalloutVampireQueryDeclaration ReadVampireQueryAssociation(ReadOnlySpan<byte> code,
        uint codeBase, int executeOffset, int evaluateOffset)
    {
        if (executeOffset < 0 || executeOffset > code.Length - 25 || evaluateOffset < 0 || evaluateOffset > code.Length - 116)
            throw new InvalidDataException("Owned vampire query exceeds its code extent.");
        var execute = code[executeOffset..];
        if (!execute[..3].SequenceEqual(new byte[] { 0x55, 0x8b, 0xec }) ||
            !execute.Slice(3, 12).SequenceEqual(new byte[] { 0x8b, 0x45, 0x20, 0x50, 0x6a, 0, 0x6a, 0, 0x8b, 0x4d, 0x10, 0x51 }) ||
            execute[15] != 0xe8 || !execute.Slice(20, 5).SequenceEqual(new byte[] { 0x83, 0xc4, 0x10, 0x5d, 0xc3 }) ||
            TargetOffset(code, codeBase, executeOffset + 16, 116) != evaluateOffset)
            throw new NotSupportedException("Owned vampire execution/condition forwarding is unbound.");
        var query = code[evaluateOffset..];
        if (!query[..10].SequenceEqual(new byte[] { 0x55, 0x8b, 0xec, 0x8b, 0x45, 0x14, 0xd9, 0xee, 0xdd, 0x18 }) ||
            !query.Slice(10, 5).SequenceEqual(new byte[] { 0x83, 0x7d, 8, 0, 0x74 }) ||
            !query.Slice(16, 10).SequenceEqual(new byte[] { 0x8b, 0x4d, 8, 0x8b, 0x11, 0x8b, 0x4d, 8, 0x8b, 0x82 }) ||
            !query.Slice(30, 8).SequenceEqual(new byte[] { 0xff, 0xd0, 0x0f, 0xb6, 0xc8, 0x85, 0xc9, 0x74 }) ||
            !query.Slice(39, 4).SequenceEqual(new byte[] { 0x8b, 0x4d, 8, 0xe8 }) ||
            !query.Slice(47, 6).SequenceEqual(new byte[] { 0x0f, 0xb6, 0xd0, 0x85, 0xd2, 0x74 }) ||
            !query.Slice(54, 7).SequenceEqual(new byte[] { 0x8b, 0x45, 0x14, 0xd9, 0xe8, 0xdd, 0x18 }))
            throw new NotSupportedException("Owned vampire condition result flow is unbound.");
        var join = evaluateOffset + 61;
        foreach (var displacement in new[] { 15, 38, 53 })
            if (evaluateOffset + displacement + 1 + unchecked((sbyte)query[displacement]) != join)
                throw new NotSupportedException("Owned vampire condition branches have different result joins.");
        var slot = U32(query, 26);
        if (slot == 0 || slot % sizeof(uint) != 0)
            throw new NotSupportedException("Owned vampire actor-type dispatch is unbound.");
        // The joined suffix optionally copies the result by value to a debug
        // call, then returns success. It cannot replace the numeric result.
        if (!query.Slice(61, 2).SequenceEqual(new byte[] { 0x8b, 0x0d }) ||
            !query.Slice(67, 3).SequenceEqual(new byte[] { 0x64, 0x8b, 0x15 }) ||
            !query.Slice(74, 6).SequenceEqual(new byte[] { 0x8b, 4, 0x8a, 0x0f, 0xb6, 0x88 }) ||
            !query.Slice(84, 3).SequenceEqual(new byte[] { 0x85, 0xc9, 0x74 }) ||
            88 + unchecked((sbyte)query[87]) != 112 ||
            !query.Slice(88, 12).SequenceEqual(new byte[] { 0x8b, 0x55, 0x14, 0x83, 0xec, 8, 0xdd, 2, 0xdd, 0x1c, 0x24, 0x68 }) ||
            query[104] != 0xe8 || !query.Slice(109, 7).SequenceEqual(new byte[] { 0x83, 0xc4, 12, 0xb0, 1, 0x5d, 0xc3 }))
            throw new NotSupportedException("Owned vampire result return is unbound.");
        _ = TargetOffset(code, codeBase, evaluateOffset + 105, 1);
        var predicateAt = TargetOffset(code, codeBase, evaluateOffset + 43, 13);
        var predicate = code[predicateAt..];
        var cursor = 0;
        void Unavailable() => throw new NotSupportedException("Owned vampire predicate is not an unconditional false query.");
        // Standard x86 frame setup and one local receiver spill have no actor
        // reads or branches. Admit the false return and its matching epilogue.
        if (predicate[cursor++] != 0x55 || predicate[cursor++] != 0x8b || predicate[cursor++] != 0xec ||
            predicate[cursor++] != 0x51 || predicate[cursor++] != 0x89 || predicate[cursor++] != 0x4d || predicate[cursor++] != 0xfc)
            Unavailable();
        if (predicate[cursor++] != 0x32 || predicate[cursor++] != 0xc0 || predicate[cursor++] != 0x8b ||
            predicate[cursor++] != 0xe5 || predicate[cursor++] != 0x5d || predicate[cursor++] != 0xc3) Unavailable();
        return new(Convert.ToHexString(SHA256.HashData([.. execute[..25], .. query[..116], .. predicate[..cursor]])).ToLowerInvariant());
    }
}
