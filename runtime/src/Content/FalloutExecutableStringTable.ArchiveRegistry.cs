using System.Security.Cryptography;

namespace OpenNV.Runtime.Content;

internal static partial class FalloutExecutableStringTable
{
    internal static FalloutArchiveRegistryDeclaration ReadArchiveRegistryDeclaration(string executable, string expectedSha256)
    {
        var bytes = File.ReadAllBytes(executable);
        if (Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() != expectedSha256)
            throw new InvalidDataException("Archive registration declaration changed its selected original image.");
        var (code, image) = Load(bytes);
        if (expectedSha256 == "c3f97c2255fa041a851c17cf372d69aaadd8694e2dc4230ba556001bbfbd2f3e")
            // The selected complete startup owner appends each admitted reader.
            // Its identity binds the original code, not a filename ordering.
            return new(FalloutArchiveRegistrationOrder.Append, [],
                Convert.ToHexString(SHA256.HashData(code)).ToLowerInvariant());
        if (expectedSha256 != "518c87f58a6c4d9826e9ef8fbb7f4213882fa70822675610d45aea2464502a57")
            throw new NotSupportedException("Selected image has no original archive registration declaration owner.");
        var candidates = new List<(IReadOnlyList<string> Names, int Start, int End)>();
        for (var at = 0; at + 30 <= code.Length; at++)
        {
            var position = at; int? callee = null; int? joined = null; byte? local = null;
            var names = new List<string>();
            for (var rank = 0; rank < 6; rank++)
            {
                if (!ArchiveCategoryGroup(code, position, rank, out var pointer, out var next,
                        out var target, out var end, out var storage) ||
                    callee is { } actualCallee && actualCallee != target ||
                    joined is { } actualJoin && actualJoin != end || local is { } actualLocal && actualLocal != storage) break;
                var name = image.Literal(pointer);
                if (string.IsNullOrEmpty(name) || name.Length > 64 || name.Any(character => character is < ' ' or > '~')) break;
                names.Add(name); callee = target; joined = end; local = storage; position = next;
            }
            if (names.Count == 6 && names.Distinct(StringComparer.Ordinal).Count() == 6 &&
                joined is { } join && join > position && join <= position + 64)
                candidates.Add((Array.AsReadOnly(names.ToArray()), at, join));
        }
        if (candidates.Count != 1)
            throw new NotSupportedException("Original archive registration has no unique complete six-category compiler declaration.");
        var selected = candidates[0];
        return new(FalloutArchiveRegistrationOrder.SourceSubstringInsertion, selected.Names,
            Convert.ToHexString(SHA256.HashData(code.AsSpan(selected.Start, selected.End - selected.Start))).ToLowerInvariant());
    }

    private static bool ArchiveCategoryGroup(byte[] code, int at, int rank, out uint pointer,
        out int next, out int callee, out int join, out byte local)
    {
        pointer = 0; next = callee = join = 0; local = 0;
        if (at < 0 || at + 30 > code.Length || code[at] != 0x68 || code[at + 5] != 0x8b ||
            (code[at + 6] & 0xc7) != 0x45 || code[at + 7] != 8 ||
            code[at + 8] != 0x50 + ((code[at + 6] >> 3) & 7) || code[at + 9] != 0xe8 ||
            !code.AsSpan(at + 14, 5).SequenceEqual(new byte[] { 0x83, 0xc4, 8, 0x85, 0xc0 })) return false;
        pointer = U32(code, at + 1); callee = checked(at + 14 + BitConverter.ToInt32(code, at + 10));
        var cursor = at + 19;
        if (!ArchiveRegistryBranch(code, cursor, 0x74, out next, out cursor) || cursor + 7 > code.Length ||
            code[cursor] != 0xc7 || code[cursor + 1] != 0x45 || U32(code, cursor + 3) != rank) return false;
        local = code[cursor + 2]; cursor += 7;
        return ArchiveRegistryBranch(code, cursor, 0xeb, out join, out cursor) && cursor == next;
    }

    private static bool ArchiveRegistryBranch(byte[] code, int at, byte shortOpcode, out int target, out int next)
    {
        target = next = 0;
        if (at + 2 <= code.Length && code[at] == shortOpcode)
        { next = at + 2; target = checked(next + unchecked((sbyte)code[at + 1])); return true; }
        if (shortOpcode == 0xeb && at + 5 <= code.Length && code[at] == 0xe9)
        { next = at + 5; target = checked(next + BitConverter.ToInt32(code, at + 1)); return true; }
        if (shortOpcode == 0x74 && at + 6 <= code.Length && code[at] == 0x0f && code[at + 1] == 0x84)
        { next = at + 6; target = checked(next + BitConverter.ToInt32(code, at + 2)); return true; }
        return false;
    }
}
