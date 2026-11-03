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
        var declarations = new Dictionary<string, (int Factory, uint Slot, uint Packed)>(StringComparer.OrdinalIgnoreCase);
        var factories = new Dictionary<int, bool>();
        for (var start = 0; start < code.Length; ++start)
        {
            var at = start;
            if (!Push(ref at, out var slot) || !Push(ref at, out var blue) || !Push(ref at, out var green) ||
                !Push(ref at, out var red) || !Push(ref at, out var namePointer) || !Receiver(ref at) ||
                at > code.Length - 5 || code[at] != 0xe8) continue;
            var factory = Target(at);
            if (!factories.TryGetValue(factory, out var admitted)) factories.Add(factory, admitted = HardFactory(factory));
            if (!admitted) continue;
            var name = literal(namePointer);
            if (string.IsNullOrWhiteSpace(name) || name.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '_'))
                throw new NotSupportedException("Owned hard system-color registration has no admitted name.");
            var packed = red << 24 | (green & 255) << 16 | (blue & 255) << 8 | 255;
            var declaration = (factory, slot, packed);
            if (declarations.TryGetValue(name, out var previous))
            {
                // An optimized constructor can appear in multiple callers.
                // Only the same typed factory, slot and packed channels agree.
                if (previous != declaration)
                    throw new NotSupportedException("Owned hard system-color name has ambiguous registrations.");
            }
            else { declarations.Add(name, declaration); result.Add(name, packed); }
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
        bool Receiver(ref int at)
        {
            if (at > code.Length - 2 || code[at] != 0x8b) return false;
            if ((code[at + 1] & 0xf8) == 0xc8) { at += 2; return true; }
            if (at <= code.Length - 3 && code[at + 1] == 0x4d && code[at + 2] == 0xf0)
            { at += 3; return true; }
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
            if (InlineHardFactory(factory)) return true;
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
        bool InlineHardFactory(int factory)
        {
            // The optimized factory packs the argument channels itself, keeps
            // that value in its argument slot across name construction, then
            // stores it in the same object's RTTI-typed hard-color payload.
            // Decode instruction operands; no palette names or image addresses
            // participate in admission.
            var limit = factory + Math.Min(code.Length - factory, 320);
            var end = -1;
            for (var at = factory; at <= limit - 4; ++at)
            {
                if (code[at] != 0x8b || code[at + 1] != 0xe5 || code[at + 2] != 0x5d ||
                    code[at + 3] is not (0xc2 or 0xc3)) continue;
                // A different function can follow this one in the scan bound.
                // Its matching body cannot supply this call's missing owner.
                if (code[at + 3] == 0xc3 || at > limit - 6 || code[at + 4] != 20 || code[at + 5] != 0)
                    return false;
                end = at; break;
            }
            if (end < 0) return false;
            for (var start = factory; start < end; ++start)
            {
                var at = start;
                if (!Argument(ref at, 12, false, out var red) ||
                    !Argument(ref at, 16, true, out var green) ||
                    !Argument(ref at, 20, true, out var blue) || red == green || red == blue || green == blue ||
                    red is 4 or 5 || green is 4 or 5 || blue is 4 or 5 ||
                    !Shift(ref at, red) || !Binary(ref at, 0x0b, green, red) ||
                    !ObjectConstant(ref at, 0, out var owner, out var baseType) ||
                    owner is not (3 or 6 or 7) ||
                    owner == red || owner == green || owner == blue ||
                    type(baseType) != ".?AVSystemColor@SystemColorManager@@" ||
                    !Shift(ref at, green) || !Binary(ref at, 0x33, red, red) ||
                    !Binary(ref at, 0x0b, green, blue) ||
                    !ObjectConstant(ref at, 4, out var zeroOwner, out var zero) || zeroOwner != owner || zero != 0 ||
                    !Shift(ref at, green) || !Memory(ref at, 0x8d, blue, owner, 4) ||
                    !ImmediateOr(ref at, green, 255) || !Memory(ref at, 0x89, red, blue, 4) ||
                    !Memory(ref at, 0x89, green, 5, 24) || !Memory(ref at, 0xff, 6, 5, 8)) continue;
                // The name argument is formatted as one entity reference;
                // the stack-local byte here is the compiler's cleanup state.
                if (at > code.Length - 4 || code[at] != 0xc6 || code[at + 1] != 0x45 || code[at + 2] < 0x80)
                    continue;
                at += 4;
                if (!Push(ref at, out var format) || literal(format) != "&%s;" ||
                    at > code.Length - 6 || code[at++] != 0x50 + blue || code[at] != 0xe8 || Target(at) < 0) continue;
                at += 5;
                // Name allocation/interning may call out, but its entire
                // admitted sequence preserves the saved packed argument.
                if (!Memory(ref at, 0x8b, red, owner, 4) || !StackCleanup(ref at, 12) ||
                    !Binary(ref at, 0x85, red, red) || at > code.Length - 2 || code[at++] != 0x74) continue;
                var skip = code[at++]; var skipTarget = at + skip;
                if (!Memory(ref at, 0xb7, blue, owner, 8, extended: true) ||
                    at > code.Length - 3 || code[at++] != 0x40 + blue || code[at++] != 0x50 + blue ||
                    code[at++] != 0x50 + red || at > code.Length - 5 || code[at] != 0xe8 || Target(at) < 0) continue;
                at += 5;
                if (!Memory(ref at, 0x8b, red, owner, 4) || !StackCleanup(ref at, 8) || at != skipTarget ||
                    at > code.Length - 7 || code[at] is < 0x50 or > 0x57 || code[at + 1] != 0x50 + red ||
                    code[at + 2] != 0xe8 || Target(at + 2) < 0) continue;
                at += 7;
                if (!Argument(ref at, 24, false, out var packed) || packed == owner || packed is 4 or 5 ||
                    !StackCleanup(ref at, 8) ||
                    !ObjectConstant(ref at, 0, out var hardOwner, out var hardType) || hardOwner != owner ||
                    type(hardType) != ".?AVHardSystemColor@SystemColorManager@@" ||
                    !Memory(ref at, 0x89, packed, owner, 12) || at > end) continue;
                return true;
            }
            return false;
        }
        bool Argument(ref int at, byte offset, bool byteValue, out int register)
        {
            register = -1;
            var length = byteValue ? 4 : 3;
            if (at > code.Length - length || byteValue && (code[at] != 0x0f || code[at + 1] != 0xb6) ||
                !byteValue && code[at] != 0x8b) return false;
            var operand = code[at + length - 2];
            if ((operand & 0xc7) != 0x45 || code[at + length - 1] != offset) return false;
            register = (operand >> 3) & 7; at += length; return true;
        }
        bool Memory(ref int at, byte opcode, int register, int owner, byte offset, bool extended = false)
        {
            var length = extended ? 4 : 3;
            if (at > code.Length - length || extended && code[at] != 0x0f ||
                code[at + (extended ? 1 : 0)] != opcode || code[at + length - 2] != (0x40 | register << 3 | owner) ||
                owner == 4 || code[at + length - 1] != offset) return false;
            at += length; return true;
        }
        bool ObjectConstant(ref int at, byte offset, out int owner, out uint value)
        {
            owner = -1; value = 0;
            var length = offset == 0 ? 6 : 7;
            if (at > code.Length - length || code[at] != 0xc7 ||
                (code[at + 1] & 0xf8) != (offset == 0 ? 0 : 0x40)) return false;
            owner = code[at + 1] & 7;
            if (owner is 4 or 5 || offset != 0 && code[at + 2] != offset) return false;
            value = BinaryPrimitives.ReadUInt32LittleEndian(code.AsSpan(at + length - 4)); at += length; return true;
        }
        bool Shift(ref int at, int register)
        {
            if (at > code.Length - 3 || code[at] != 0xc1 || code[at + 1] != 0xe0 + register || code[at + 2] != 8) return false;
            at += 3; return true;
        }
        bool Binary(ref int at, byte opcode, int destination, int source)
        {
            if (at > code.Length - 2 || code[at] != opcode || code[at + 1] != (0xc0 | destination << 3 | source)) return false;
            at += 2; return true;
        }
        bool ImmediateOr(ref int at, int register, uint value)
        {
            if (at > code.Length - 6 || code[at] != 0x81 || code[at + 1] != 0xc8 + register ||
                BinaryPrimitives.ReadUInt32LittleEndian(code.AsSpan(at + 2)) != value) return false;
            at += 6; return true;
        }
        bool StackCleanup(ref int at, byte bytes)
        {
            if (at > code.Length - 3 || code[at] != 0x83 || code[at + 1] != 0xc4 || code[at + 2] != bytes) return false;
            at += 3; return true;
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
