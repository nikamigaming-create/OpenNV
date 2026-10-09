using System.Buffers.Binary;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutNativeScriptConstruction(string RuntimeSha256, string DeclarationOwner,
    uint RuntimeWord, uint InitialDelayBits, uint InitialElapsedBits);

internal static partial class FalloutExecutableStringTable
{
    // Only source associations and neutral field values leave this reader.
    // Neither an original virtual table nor an instruction address is a guest
    // pointer, a persisted declaration, or executable OpenNV authority.
    internal static FalloutNativeScriptConstruction ReadScriptConstruction(string path)
    {
        var (code, image) = Load(path);
        var tables = image.SourceClassTables(".?AVScript@@", 78).ToArray();
        if (tables.Length != 1) throw new NotSupportedException("Selected source Script class association is absent or ambiguous.");
        var reset = U32(image.Read(checked(tables[0] + 5 * 4), 4), 0);
        if (reset < image.CodeBase || reset - image.CodeBase >= code.Length)
            throw new InvalidDataException("Source Script reset method is outside its original code extent.");
        var words = ReadScriptResetClocks(code.AsSpan(checked((int)(reset - image.CodeBase))), reset,
            (address, size) => image.Read(address, size), image.IsExecutableExtent);
        using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var sha = Convert.ToHexString(SHA256.HashData(source));
        return new(sha, "selected-class/Script/reset/Float32:" + sha, words[52], words[56], words[60]);
    }

    internal static IReadOnlyDictionary<int, uint> ReadScriptResetClocks(ReadOnlySpan<byte> input, uint origin,
        Func<uint, int, byte[]> read, Func<uint, bool> executable)
    {
        // The original reset method saves its actual this receiver in an EBP
        // local. Admit the final contiguous Float32 stores and the separately
        // proven type setter, rather than scanning arbitrary immediate bytes.
        if (input.Length < 8 || !input[..4].SequenceEqual(new byte[] { 0x55, 0x8b, 0xec, 0x51 }) ||
            input[4] != 0x89 || input[5] != 0x4d || unchecked((sbyte)input[6]) >= 0)
            throw new NotSupportedException("Source Script reset receiver declaration is unbound.");
        var slot = input[6];
        var end = input[..Math.Min(input.Length, 512)].IndexOf(new byte[] { 0x8b, 0xe5, 0x5d, 0xc3 });
        if (end < 0) throw new NotSupportedException("Source Script reset final extent is unbound.");
        var body = input[..end]; var result = new Dictionary<int, uint>();
        var boundaries = new HashSet<int>();
        for (var cursor = 0; cursor < body.Length;)
        {
            boundaries.Add(cursor); var size = ScriptConstructionInstruction(body[cursor..]);
            if (size > body.Length - cursor) throw new InvalidDataException("Source Script reset instruction exceeds its final extent.");
            cursor = checked(cursor + size);
        }
        for (var at = 7; at <= body.Length - 8; ++at)
        {
            if (!boundaries.Contains(at) || body[at] != 0x8b || (body[at + 1] & 0xc7) != 0x45 || body[at + 2] != slot ||
                body[at + 3] != 0xd9 || body[at + 4] != 0xee || body[at + 5] != 0xd9 ||
                (body[at + 6] & 0xf8) != 0x58 || (body[at + 1] >> 3 & 7) != (body[at + 6] & 7)) continue;
            var first = at; var cursor = at;
            while (cursor <= body.Length - 8 && body[cursor] == 0x8b && (body[cursor + 1] & 0xc7) == 0x45 && body[cursor + 2] == slot &&
                body[cursor + 3] == 0xd9 && body[cursor + 4] == 0xee && body[cursor + 5] == 0xd9 && (body[cursor + 6] & 0xf8) == 0x58 &&
                (body[cursor + 1] >> 3 & 7) == (body[cursor + 6] & 7))
            {
                var field = (int)body[cursor + 7];
                if (field is not (52 or 56 or 60) || !result.TryAdd(field, 0))
                    throw new NotSupportedException("Source Script reset writes an unowned or repeated runtime Float32 field.");
                cursor += 8;
            }
            if (result.Count != 3 || cursor + 10 != body.Length || body[cursor] != 0x6a || body[cursor + 1] != 17 ||
                !body.Slice(cursor + 2, 3).SequenceEqual(new byte[] { 0x8b, 0x4d, slot }) || body[cursor + 5] != 0xe8)
                throw new NotSupportedException("Source Script clock reset lacks its complete final source/type association.");
            var target = checked((long)origin + cursor + 10 + BinaryPrimitives.ReadInt32LittleEndian(body[(cursor + 6)..]));
            if (target is < 0 or > uint.MaxValue || !executable((uint)target))
                throw new InvalidDataException("Source Script type setter is outside its original code.");
            var setter = read((uint)target, 22);
            // This exact public x86 argument/receiver contract writes only the
            // type byte. It cannot silently replace the just-written clocks.
            if (!setter.AsSpan(0, 6).SequenceEqual(new byte[] { 0x55, 0x8b, 0xec, 0x51, 0x89, 0x4d }) ||
                unchecked((sbyte)setter[6]) >= 0 || !setter.AsSpan(7, 3).SequenceEqual(new byte[] { 0x8b, 0x45, setter[6] }) ||
                !setter.AsSpan(10, 12).SequenceEqual(new byte[] { 0x8a, 0x4d, 8, 0x88, 0x48, 4, 0x8b, 0xe5, 0x5d, 0xc2, 4, 0 }))
                throw new NotSupportedException("Source Script final type setter has unowned behavior.");
            if (first < 7) throw new InvalidDataException("Source Script reset has an invalid final field extent.");
            return result;
        }
        throw new NotSupportedException("Selected source Script clock constructor fields are unbound.");
    }

    private static int ScriptConstructionInstruction(ReadOnlySpan<byte> code)
    {
        if (code.IsEmpty) throw new InvalidDataException("Source constructor instruction is absent.");
        var at = 0; var operand = 4;
        while (code[at] is 0x64 or 0x66)
        {
            if (code[at] == 0x66) operand = 2;
            if (++at >= code.Length) throw new InvalidDataException("Source constructor prefix lacks an instruction.");
        }
        var opcode = code[at++]; var immediate = 0; var modrm = false;
        if (opcode is >= 0x50 and <= 0x5f or 0x90) return at;
        if (opcode is >= 0xb8 and <= 0xbf or 0xa1 or 0xa3 or 0x68) immediate = operand;
        else if (opcode == 0xe8) immediate = 4;
        else if (opcode == 0x6a) immediate = 1;
        else if (opcode is 0x89 or 0x8b or 0x8a or 0x88 or 0x33 or 0x31 or 0x85 or 0x8d or >= 0xd8 and <= 0xdf) modrm = true;
        else if (opcode is 0x80 or 0x83 or 0xc6) { modrm = true; immediate = 1; }
        else if (opcode is 0x81 or 0xc7) { modrm = true; immediate = operand; }
        else if (opcode == 0x0f)
        {
            if (at >= code.Length) throw new InvalidDataException("Source constructor escaped opcode is incomplete.");
            if (code[at++] is not (0xb6 or 0xb7 or 0xbe or 0xbf))
                throw new NotSupportedException("Source Script reset escaped/control-flow instruction is unowned.");
            modrm = true;
        }
        else throw new NotSupportedException("Source Script reset instruction/control-flow family is unowned.");
        if (modrm)
        {
            if (at >= code.Length) throw new InvalidDataException("Source constructor ModRM is absent.");
            var declaration = code[at++]; var mode = declaration >> 6; var register = declaration & 7;
            if (mode != 3 && register == 4)
            {
                if (at >= code.Length) throw new InvalidDataException("Source constructor SIB is absent.");
                register = code[at++] & 7;
            }
            if (mode == 0 && register == 5 || mode == 2) at = checked(at + 4);
            else if (mode == 1) ++at;
        }
        if (at > code.Length - immediate) throw new InvalidDataException("Source constructor operand exceeds its complete extent.");
        return checked(at + immediate);
    }

    private sealed partial class Image
    {
        internal IEnumerable<uint> SourceClassTables(string name, int slots)
        {
            foreach (var section in headers.SectionHeaders.Where(section =>
                (section.SectionCharacteristics & (SectionCharacteristics.MemRead | SectionCharacteristics.MemWrite | SectionCharacteristics.MemExecute)) == SectionCharacteristics.MemRead))
            {
                var data = bytes.AsMemory(section.PointerToRawData, section.SizeOfRawData);
                for (var at = 0; at <= data.Length - 4 - slots * 4; at += 4)
                {
                    var locator = U32(data.Span, at);
                    if (!IsReadOnlyExtent(locator, 20)) continue;
                    var row = Read(locator, 20);
                    if (U32(row, 0) != 0 || U32(row, 4) != 0 || U32(row, 8) != 0 ||
                        !IsFileExtent(U32(row, 12), checked(name.Length + 9))) continue;
                    var declaration = Read(checked(U32(row, 12) + 8), name.Length + 1);
                    if (declaration[^1] != 0 || Encoding.ASCII.GetString(declaration, 0, name.Length) != name) continue;
                    var valid = true;
                    for (var slot = 0; slot < slots; ++slot)
                        if (!IsExecutableExtent(U32(data.Span, at + 4 + slot * 4))) { valid = false; break; }
                    if (valid) yield return checked(Base + (uint)section.VirtualAddress + (uint)at + 4);
                }
            }
        }
    }
}
