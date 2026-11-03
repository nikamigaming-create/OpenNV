using System.Buffers.Binary;

namespace OpenNV.Runtime.Content;

internal static partial class FalloutExecutableStringTable
{
    internal static IReadOnlyDictionary<string, uint> ReadHardSystemColors(string path)
    {
        var (code, image) = Load(path);
        return ReadHardSystemColors(code, image.CodeBase, image.Literal, image.SystemColorType);
    }

    // Decode source palette registration, not an address/name/color catalog.
    // Immediate slot/B/G/R/name arguments must reach the actual hard-color
    // constructor. Soft colors have a different RTTI owner and remain separate.
    internal static IReadOnlyDictionary<string, uint> ReadHardSystemColors(byte[] code, uint codeBase,
        Func<uint, string?> literal, Func<uint, string?> type)
    {
        var result = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        var factories = new Dictionary<int, bool>();
        for (var start = 0; start < code.Length; ++start)
        {
            var at = start;
            if (!Push(ref at, out _) || !Push(ref at, out var blue) || !Push(ref at, out var green) ||
                !Push(ref at, out var red) || !Push(ref at, out var namePointer) || at > code.Length - 8 ||
                !code.AsSpan(at, 3).SequenceEqual(new byte[] { 0x8b, 0x4d, 0xf0 }) || code[at + 3] != 0xe8) continue;
            var factory = Target(at + 3);
            if (!factories.TryGetValue(factory, out var admitted)) factories.Add(factory, admitted = HardFactory(factory));
            if (!admitted) continue;
            var name = literal(namePointer);
            if (string.IsNullOrWhiteSpace(name) || name.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '_'))
                throw new NotSupportedException("Owned hard system-color registration has no admitted name.");
            if (!result.TryAdd(name, red << 24 | (green & 255) << 16 | (blue & 255) << 8 | 255))
                throw new NotSupportedException("Owned hard system-color name has ambiguous registrations.");
        }
        if (result.Count == 0) throw new NotSupportedException("Owned executable has no admitted hard system-color registrations.");
        return new System.Collections.ObjectModel.ReadOnlyDictionary<string, uint>(result);

        bool Push(ref int at, out uint value)
        {
            value = 0;
            if (at <= code.Length - 2 && code[at] == 0x6a)
            { value = unchecked((uint)(sbyte)code[at + 1]); at += 2; return true; }
            if (at <= code.Length - 5 && code[at] == 0x68)
            { value = BinaryPrimitives.ReadUInt32LittleEndian(code.AsSpan(at + 1)); at += 5; return true; }
            return false;
        }
        int Target(int call)
        {
            var address = (long)codeBase + call + 5 + BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(call + 1));
            var offset = address - codeBase;
            return offset >= 0 && offset < code.Length ? (int)offset : -1;
        }
        bool RgbaPack(int pack)
        {
            if (pack < 0 || pack > code.Length - 70) return false;
            var body = code.AsSpan(pack, 70);
            return body.Slice(4, 6).SequenceEqual(new byte[] { 0x8b, 0x45, 0x08, 0xc1, 0xe0, 0x18 }) &&
                body.Slice(13, 12).SequenceEqual(new byte[] { 0x8b, 0x4d, 0x0c, 0x81, 0xe1, 0xff, 0, 0, 0, 0xc1, 0xe1, 0x10 }) &&
                body.Slice(31, 12).SequenceEqual(new byte[] { 0x8b, 0x55, 0x10, 0x81, 0xe2, 0xff, 0, 0, 0, 0xc1, 0xe2, 0x08 }) &&
                body.Slice(49, 8).SequenceEqual(new byte[] { 0x8b, 0x45, 0x14, 0x25, 0xff, 0, 0, 0 });
        }
        bool HardFactory(int factory)
        {
            if (factory < 0 || factory > code.Length - 160 ||
                !code.AsSpan(factory, 3).SequenceEqual(new byte[] { 0x55, 0x8b, 0xec })) return false;
            // The owned factory packs R/G/B plus literal alpha255, then passes
            // name/packed value/slot to a typed hard-color constructor.
            for (var at = factory; at <= factory + 130; ++at)
            {
                if (!code.AsSpan(at, 4).SequenceEqual(new byte[] { 0x8b, 0x4d, 0xe8, 0xe8 })) continue;
                if (at < factory + 34) continue;
                var arguments = code.AsSpan(at - 34, 34);
                if (!arguments[..21].SequenceEqual(new byte[] { 0x8b, 0x4d, 0x18, 0x51, 0x68, 0xff, 0, 0, 0,
                    0x8b, 0x55, 0x14, 0x52, 0x8b, 0x45, 0x10, 0x50, 0x8b, 0x4d, 0x0c, 0x51 }) ||
                    arguments[21] != 0xe8 || !arguments[26..].SequenceEqual(new byte[] { 0x83, 0xc4, 0x10, 0x50, 0x8b, 0x55, 0x08, 0x52 }) ||
                    !RgbaPack(Target(at - 13))) continue;
                var constructor = Target(at + 3);
                if (constructor < 0 || constructor > code.Length - 48) continue;
                var body = code.AsSpan(constructor, 48);
                if (!body[..7].SequenceEqual(new byte[] { 0x55, 0x8b, 0xec, 0x51, 0x89, 0x4d, 0xfc }) ||
                    !body.Slice(23, 5).SequenceEqual(new byte[] { 0x8b, 0x55, 0xfc, 0xc7, 0x02 }) ||
                    !body.Slice(32, 12).SequenceEqual(new byte[] { 0x8b, 0x45, 0xfc, 0x8b, 0x4d, 0x0c, 0x89, 0x48, 0x0c, 0x8b, 0x45, 0xfc })) continue;
                if (type(BinaryPrimitives.ReadUInt32LittleEndian(body[28..])) == ".?AVHardSystemColor@SystemColorManager@@") return true;
            }
            return false;
        }
    }

    private sealed partial class Image
    {
        internal string? SystemColorType(uint vtable)
        {
            if (vtable < 4 || !IsReadOnlyExtent(vtable - 4, 12) || !IsExecutableExtent(U32(Read(vtable, 4), 0))) return null;
            var locator = U32(Read(vtable - 4, 4), 0);
            if (!IsReadOnlyExtent(locator, 20)) return null;
            var row = Read(locator, 20);
            if (U32(row, 0) != 0 || U32(row, 4) != 0 || U32(row, 8) != 0) return null;
            var descriptor = U32(row, 12);
            if (descriptor > uint.MaxValue - 8 || !IsFileExtent(descriptor, 9)) return null;
            var name = new List<byte>();
            for (var index = 0U; index < 128; ++index)
            {
                var address = (ulong)descriptor + 8 + index;
                if (address > uint.MaxValue || !IsFileExtent((uint)address, 1)) return null;
                var character = Read((uint)address, 1)[0];
                if (character == 0) return System.Text.Encoding.ASCII.GetString(name.ToArray());
                if (character is < 32 or > 126) return null;
                name.Add(character);
            }
            return null;
        }
    }
}
