using System.Buffers.Binary;
using System.Security.Cryptography;

namespace OpenNV.Runtime.Content;

internal static partial class FalloutExecutableStringTable
{
    internal static FalloutDefaultStatusActorValues ReadDefaultStatusActorValues(string path)
    {
        var (code, image) = Load(path);
        using var input = File.OpenRead(path);
        var executable = Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant();
        if (!FalloutSourceMainFamily.IsFallout3(executable))
            throw new NotSupportedException("Status actor-value defaults have no selected source family.");
        var npc = Table(".?AVTESNPC@@", 3, 0x100);
        var getter = Offset(Slot(npc, 8));
        if (!Match(code, getter, [0x55, 0x8b, 0xec, 0x8b, 0x55, 8, 0x57, 0x8b, 0xf9, 0x8d, 0x42, 0xe0, 0x83, 0xf8, 0x0d, 0x77, 0x44]) ||
            !Match(code, getter + 0x55, [0x52, 0xe8]) || !Match(code, getter + 0x5b, [0x5f, 0x5d, 0xc2, 4, 0]))
            throw new NotSupportedException("NPC status base-data getter lost its independent skill/default dispatch.");
        var basis = Child(getter + 0x56);
        if (!Match(code, basis, [0x55, 0x8b, 0xec, 0x8b, 0x45, 8, 0x33, 0xd2, 0x83, 0xf8, 0x39, 0x0f, 0x87]) ||
            !Match(code, basis + 0x11, [0x0f, 0xb6, 0x80]) || !Match(code, basis + 0x18, [0xff, 0x24, 0x85]))
            throw new NotSupportedException("Actor base-data defaults lost their zero initialization and source switch.");
        var defaultAt = checked(basis + 0x11 + BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(basis + 0xd)));
        if (!Match(code, defaultAt, [0x8b, 0xc2, 0x5d, 0xc2, 4, 0]))
            throw new NotSupportedException("Actor base-data default no longer returns its actual zero field.");
        var cases = image.Read(Word(code, basis + 0x14), 58);
        var targets = Word(code, basis + 0x1b);
        var zeros = Enumerable.Range(0, 73).Where(index => index > 57 ||
            Offset(Word(image.Read(targets + (uint)cases[index] * 4, 4), 0)) == defaultAt).ToHashSet();
        var values = new List<FalloutDefaultStatusActorValue>();
        int? registration = null;
        for (var at = 0; at <= code.Length - 21; ++at)
        {
            if (code[at] != 0x6a || code[at + 1] != 0) continue;
            var cursor = at; var args = new uint[8]; var complete = true;
            for (var index = 0; index < args.Length; ++index)
                if (!Push(ref cursor, out args[index])) { complete = false; break; }
            if (!complete || cursor > code.Length - 5 || code[cursor] != 0xe8 ||
                args[0] != 0 || args[2] != 0 || args[3] != 0 || args[5] != 5 || args[7] >= 73 ||
                !zeros.Contains((int)args[7]) || image.Literal(args[6]) is not { } name) continue;
            var register = Child(cursor);
            if (!Match(code, register + 0x64, [0x8b, 0x45, 0x10, 0xba, 0x0f, 0, 0, 0, 0x89, 0x47, 0x48]) ||
                !Match(code, register + 0x75, [0x8b, 0x45, 0x18, 0x89, 0x47, 0x4c]) ||
                !Match(code, register + 0x81, [0x8b, 0x45, 0x20, 0x89, 0x47, 0x54])) continue;
            if (registration is { } prior && prior != register)
                throw new NotSupportedException("Status actor-value declarations changed registration ownership.");
            registration = register;
            values.Add(new((int)args[7], name, args[1] != 0));
        }
        var player = Table(".?AVPlayerCharacter@@", 191);
        var owner = Table(".?AVPlayerCharacter@@", 4, 0x9c);
        var current = Offset(Slot(owner, 0xc));
        var floatBase = Offset(Slot(owner, 4));
        RequireStatusBaseAssociations(code, image, player, floatBase, registration ??
            throw new NotSupportedException("Status actor values omitted their actual descriptor writer."));
        if (!Match(code, floatBase + 9, [0xc7, 0x45, 0xfc, 0, 0, 0, 0]) ||
            !Match(code, floatBase + 0x15, [0x8b, 0x4b, 0x80, 0x81, 0xc1, 0, 1, 0, 0, 0x56, 0x8b, 1, 0xff, 0x50, 8]) ||
            !Match(code, current + 0x45, [0x8b, 0x40, 4, 0xff, 0xd0]))
            throw new NotSupportedException("Status current/base getters lost their actual NPC base-data association.");
        if (!Match(code, current + 9, [0xf3, 0x0f, 0x10, 0x84, 0x91, 0x64, 1, 0, 0]) ||
            !Match(code, current + 0x17, [0xf3, 0x0f, 0x10, 0x84, 0x91, 0x88, 2, 0, 0]) ||
            !Match(code, current + 0x2a, [0xf3, 0x0f, 0x10, 0x84, 0x91, 0xb0, 3, 0, 0]) ||
            !Match(code, current + 0x4a, [0xd9, 0x5d, 0xfc, 0xf3, 0x0f, 0x10, 0x45, 0xfc, 0xf3, 0x0f, 0x58, 0x45, 0xf8,
                0xf3, 0x0f, 0x58, 0x45, 0xf4, 0xf3, 0x0f, 0x58, 0x45, 8]))
            throw new NotSupportedException("Player status getter changed its separate Float32 pool order.");
        var constructors = 0;
        for (var at = 0; at <= code.Length - 0x316; ++at)
            if (Match(code, at, [0xc7, 7]) && Word(code, at + 2) == player &&
                Match(code, at + 0x2cc, [0x8d, 0x87, 0x24, 3, 0, 0]) &&
                Match(code, at + 0x2da, [0xb9, 0x49, 0, 0, 0]) &&
                Match(code, at + 0x2ee, [0xc7, 0x80, 0xdc, 0xfe, 0xff, 0xff, 0, 0, 0, 0, 0x8d, 0x40, 4,
                    0xc7, 0x40, 0xfc, 0, 0, 0, 0, 0xc7, 0x80, 0x24, 1, 0, 0, 0, 0, 0, 0, 0x83, 0xe9, 1, 0x75, 0xdd])) ++constructors;
        if (constructors != 1 || !values.Any(value => value is { Index: 47, Name: "Paralysis" }))
            throw new NotSupportedException("Player status values lost their actual constructor pools or requested descriptor.");
        return FalloutDefaultStatusActorValues.Create(executable, values);

        bool Push(ref int at, out uint value)
        {
            value = 0;
            if (at <= code.Length - 2 && code[at] == 0x6a)
            { value = unchecked((uint)(sbyte)code[at + 1]); at += 2; return true; }
            if (at <= code.Length - 5 && code[at] == 0x68)
            { value = Word(code, at + 1); at += 5; return true; }
            return false;
        }
        uint Table(string name, int slots, uint displacement = 0)
        {
            var tables = image.SourceClassTables(name, slots, displacement).ToArray();
            return tables.Length == 1 ? tables[0] : throw new NotSupportedException("Status actor-value interface is absent or ambiguous: " + name);
        }
        uint Slot(uint table, uint slot) => Word(image.Read(table + slot, 4), 0);
        int Offset(uint address) => SourceOffset(code, image.CodeBase, address);
        int Child(int at) => checked(at + 5 + BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(at + 1)));
    }
}
