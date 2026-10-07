using System.Text.RegularExpressions;

namespace OpenNV.Runtime.Content;

internal static partial class FalloutExecutableStringTable
{
    // An optimized allocation can inline the three-field typed descriptor
    // constructor. Require its allocation/null branch, receiver, collection
    // registration and retained global slot as one compiler-owned association.
    internal static IReadOnlyDictionary<string, string> ReadInlineStringInitializers(ReadOnlySpan<byte> code, uint codeBase,
        Func<uint, string?> literal, Func<uint, SettingCollection?> collection, Func<uint, bool> writableObject,
        Func<uint, bool> executable)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var at = 0; at <= code.Length - 89; ++at)
        {
            var row = code[at..];
            if (row[0] != 0x6a || row[1] != 12 || row[2] != 0xb9) continue;
            var first = row[7] == 0xe8 && FirstInlineAllocation(code[..at], executable, writableObject);
            var callAt = first ? 7 : 20;
            if (row.Length < callAt + 82) continue;
            var body = row[callAt..];
            var receiver = (body[6] >> 3) & 7;
            if (receiver is not (3 or 6 or 7)) continue;
            if (first && code[at - 18] != 0x50 + receiver) continue;
            var same = (byte)(0xc0 | receiver << 3 | receiver);
            var absolute = (byte)(5 | receiver << 3);
            if (!first && (!row.Slice(7, 7).SequenceEqual(new byte[] { 0xc7, 0x45, 0xfc, 0xff, 0xff, 0xff, 0xff }) ||
                    row[14] != 0x89 || row[15] != absolute || !writableObject(U32(row, 16)))) continue;
            if (body[0] != 0xe8 || body[5] != 0x8b || body[6] != (0xc0 | receiver << 3) ||
                body[7] != 0x89 || body[8] != (0x45 | receiver << 3) || body[9] == 0xfc ||
                !body.Slice(10, 3).SequenceEqual(new byte[] { 0xc7, 0x45, 0xfc }) ||
                body[17] != 0x85 || body[18] != same || body[19] != 0x74 || body[20] != 39 ||
                body[21] != 0xc7 || body[22] != (0x40 | receiver) || body[23] != 8 ||
                body[28] != 0xc7 || body[29] != (0x40 | receiver) || body[30] != 4 ||
                !body.Slice(35, 3).SequenceEqual(new byte[] { 0xc6, 0x45, 0xfc }) ||
                body[39] != 0xc7 || body[40] != receiver || body[45] != 0xe8 || body[50] != 0x50 + receiver ||
                !body.Slice(51, 9).SequenceEqual(new byte[] { 0x8b, 0xc8, 0x8b, 0x10, 0xff, 0x52, 4, 0xeb, 2 }) ||
                body[60] != 0x33 || body[61] != same || body[62] != 0x6a || body[63] != 12 || body[64] != 0xb9 ||
                !body.Slice(69, 7).SequenceEqual(new byte[] { 0xc7, 0x45, 0xfc, 0xff, 0xff, 0xff, 0xff }) ||
                body[76] != 0x89 || body[77] != absolute || U32(row, 3) != U32(body, 65) ||
                !writableObject(U32(row, 3)) || !writableObject(U32(body, 78))) continue;
            var allocation = (long)codeBase + at + callAt + 5 + unchecked((int)U32(body, 1));
            var registration = (long)codeBase + at + callAt + 50 + unchecked((int)U32(body, 46));
            if (allocation is < 0 or > uint.MaxValue || registration is < 0 or > uint.MaxValue ||
                !executable((uint)allocation) || !executable((uint)registration) || collection(U32(body, 41)) != SettingCollection.Game) continue;
            var name = literal(U32(body, 24));
            if (name is null || !Regex.IsMatch(name, @"^s[A-Z][A-Za-z0-9_]+$", RegexOptions.CultureInvariant)) continue;
            var value = literal(U32(body, 31)) ?? throw new NotSupportedException($"Owned inline string setting has no admitted literal: {name}.");
            if (!result.TryAdd(name, value)) throw new InvalidDataException($"Multiple source initializers declare {name}.");
        }
        return result;
    }
    private static bool FirstInlineAllocation(ReadOnlySpan<byte> preceding, Func<uint, bool> executable, Func<uint, bool> writableObject)
    {
        if (preceding.Length < 36) return false;
        var frame = preceding[^36..];
        return frame[..6].SequenceEqual(new byte[] { 0x55, 0x8b, 0xec, 0x6a, 0xff, 0x68 }) &&
            frame.Slice(10, 8).SequenceEqual(new byte[] { 0x64, 0xa1, 0, 0, 0, 0, 0x50, 0x51 }) &&
            frame[18] is 0x53 or 0x56 or 0x57 && frame[19] == 0xa1 &&
            frame.Slice(24, 12).SequenceEqual(new byte[] { 0x33, 0xc5, 0x50, 0x8d, 0x45, 0xf4, 0x64, 0xa3, 0, 0, 0, 0 }) &&
            executable(U32(frame, 6)) && writableObject(U32(frame, 20));
    }
}
