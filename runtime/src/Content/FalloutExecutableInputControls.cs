using System.Buffers.Binary;

namespace OpenNV.Runtime.Content;

internal static partial class FalloutExecutableStringTable
{
    internal static IReadOnlyDictionary<string, (byte Keyboard, byte Mouse)> ReadControlDefaults(string path)
    {
        var (code, image) = Load(path);
        return ReadControlDefaults(code, image.Literal, image.Read, image.ImportName);
    }

    // Associate the original profile reader's names and byte lanes with the
    // constructor's aggregate initialization. No key codes or retail addresses
    // are a fallback input. Other device lanes retain their separate boundary.
    internal static IReadOnlyDictionary<string, (byte Keyboard, byte Mouse)> ReadControlDefaults(ReadOnlySpan<byte> code,
        Func<uint, string?> literal, Func<uint, int, byte[]> read, Func<uint, string?> import)
    {
        uint? table = null; int? mouseBase = null;
        for (var at = 110; at <= code.Length - 9; ++at)
        {
            if (code[at] != 0x68 || literal(U32(code, at + 1)) != "Controls" ||
                !code.Slice(at - 2, 2).SequenceEqual(new byte[] { 0xff, 0x37 }) ||
                !code.Slice(at + 5, 4).SequenceEqual(new byte[] { 0xff, 0xd5, 0x85, 0xc0 })) continue;
            uint? names = null; int? lane = null; uint? count = null; string? imported = null;
            for (var before = at - 110; before < at - 5; ++before)
            {
                if (code[before] == 0xbf) names = U32(code, before + 1);
                if (code[before] == 0xbb) count = U32(code, before + 1);
                if (code[before] == 0x81 && code[before + 1] == 0xc6) lane = BinaryPrimitives.ReadInt32LittleEndian(code[(before + 2)..]);
                if (code[before] == 0x8b && code[before + 1] == 0x2d) imported = import(U32(code, before + 2));
            }
            if (names is null || lane is null || count != 28 || imported != "GetPrivateProfileStringA")
                throw new NotSupportedException("Owned input profile table declaration is unbound.");
            var store = new byte[] { 0x8b, 0xc8, 0x88, 0x86, 0x38, 0, 0, 0, 0xc1, 0xe9, 0x10,
                0x83, 0xc4, 0x0c, 0x88, 0x4e, 0xe4, 0x8b, 0xc8, 0xc1, 0xe9, 8, 0x88, 0x0e };
            var shortStore = new byte[] { 0x8b, 0xc8, 0x88, 0x46, 0x38, 0xc1, 0xe9, 0x10,
                0x83, 0xc4, 0x0c, 0x88, 0x4e, 0xe4, 0x8b, 0xc8, 0xc1, 0xe9, 8, 0x88, 0x0e };
            var body = code.Slice(at + 9, Math.Min(100, code.Length - at - 9));
            if (body.IndexOf(store) < 0 && body.IndexOf(shortStore) < 0)
                throw new NotSupportedException("Owned input profile byte order is unbound.");
            if (table is not null) throw new InvalidDataException("Owned input profile reader is ambiguous.");
            table = names; mouseBase = lane;
        }
        if (table is null || mouseBase is null) throw new NotSupportedException("Owned input profile reader is missing.");
        var pointers = read(table.Value, 28 * sizeof(uint));
        var labels = Enumerable.Range(0, 28).Select(index => literal(U32(pointers, index * 4)) ??
            throw new InvalidDataException("Owned control table has an absent name.")).ToArray();
        if (labels.Distinct(StringComparer.OrdinalIgnoreCase).Count() != 28 || labels[0] != "Forward" || labels[^1] != "Grab")
            throw new InvalidDataException("Owned control table names are invalid.");

        byte[]? bindings = null;
        var fill = new byte[] { 0xc6, 0x40, 0xe4, 0xff, 0x8d, 0x40, 1, 0xc6, 0x40, 0xff, 0xff,
            0xc6, 0x40, 0x1b, 0xff, 0xc6, 0x40, 0x37, 0xff, 0x83, 0xe9, 1, 0x75, 0xe8 };
        for (var at = 0; at <= code.Length - 40; ++at)
        {
            if (!code.Slice(at, 2).SequenceEqual(new byte[] { 0x8d, 0x86 }) ||
                BinaryPrimitives.ReadInt32LittleEndian(code[(at + 2)..]) != mouseBase ||
                code[at + 6] != 0xb9 || U32(code, at + 7) != 28) continue;
            var next = at + 11;
            while (next < at + 32 && code[next] != 0xc6) next++;
            if (next > code.Length - fill.Length || !code.Slice(next, fill.Length).SequenceEqual(fill)) continue;
            next += fill.Length;
            if (next > code.Length - 4 || !code.Slice(next, 4).SequenceEqual(new byte[] { 0xf6, 0x46, 4, 1 })) continue;
            next += 4;
            var values = Enumerable.Repeat(byte.MaxValue, 56).ToArray(); var written = new bool[56];
            var begin = next; var stopped = false;
            while (next < code.Length - 10 && next - begin < 512)
            {
                int width, offset, length; uint payload;
                if (code[next] == 0xc7 && code[next + 1] == 0x86)
                { width = 4; offset = BinaryPrimitives.ReadInt32LittleEndian(code[(next + 2)..]); payload = U32(code, next + 6); length = 10; }
                else if (code.Slice(next, 3).SequenceEqual(new byte[] { 0x66, 0xc7, 0x86 }))
                { width = 2; offset = BinaryPrimitives.ReadInt32LittleEndian(code[(next + 3)..]); payload = BinaryPrimitives.ReadUInt16LittleEndian(code[(next + 7)..]); length = 9; }
                else if (code[next] == 0xc6 && code[next + 1] == 0x86)
                { width = 1; offset = BinaryPrimitives.ReadInt32LittleEndian(code[(next + 2)..]); payload = code[next + 6]; length = 7; }
                else if (code[next] == 0x88 && code[next + 1] == 0x8e)
                { width = 1; offset = BinaryPrimitives.ReadInt32LittleEndian(code[(next + 2)..]); payload = 0; length = 6; }
                else if (code[next] == 0x74) { stopped = true; break; }
                else break;
                var index = offset - mouseBase.Value + 28;
                if (index < 0 || index + width > values.Length)
                    throw new NotSupportedException("Owned input initialization reaches another device lane.");
                for (var lane = 0; lane < width; ++lane)
                {
                    if (written[index + lane]) throw new InvalidDataException("Owned input initialization overwrites a byte lane.");
                    written[index + lane] = true; values[index + lane] = (byte)(payload >> (lane * 8));
                }
                next += length;
            }
            if (!stopped || !written.Take(28).Any(value => value) || !written.Skip(28).Any(value => value))
                throw new NotSupportedException("Owned input defaults are incomplete.");
            if (bindings is not null) throw new InvalidDataException("Owned input constructor is ambiguous.");
            bindings = values;
        }
        if (bindings is null) throw new NotSupportedException("Owned input constructor is unbound.");
        return labels.Select((name, index) => (name, Keyboard: bindings[index], Mouse: bindings[index + 28]))
            .ToDictionary(row => row.name, row => (row.Keyboard, row.Mouse), StringComparer.OrdinalIgnoreCase);
    }
}
