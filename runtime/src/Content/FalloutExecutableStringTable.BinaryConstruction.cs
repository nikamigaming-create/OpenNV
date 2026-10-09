using System.Buffers.Binary;
using System.Security.Cryptography;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutNativeBinaryInitialField(int Offset, byte[] Bytes);
internal sealed record FalloutNativeBinaryFileConstruction(string RuntimePath, string RuntimeSha256,
    string DeclarationOwner, uint ReadMode, uint SeekSet, uint SeekCurrent, uint SeekEnd,
    uint ConstructorBufferCapacity, IReadOnlyList<FalloutNativeBinaryInitialField> InitialFields)
{
    internal void Require()
    {
        if (string.IsNullOrWhiteSpace(RuntimePath) || string.IsNullOrWhiteSpace(RuntimeSha256) ||
            RuntimeSha256.Length != 64 || !RuntimeSha256.All(Uri.IsHexDigit) ||
            string.IsNullOrWhiteSpace(DeclarationOwner) || SeekSet == SeekCurrent || SeekSet == SeekEnd || SeekCurrent == SeekEnd ||
            InitialFields is null || InitialFields.Any(field => field is null || field.Bytes is null) ||
            InitialFields.GroupBy(field => field.Offset).Any(group => group.Count() != 1))
            throw new InvalidDataException("Binary construction has no exact selected source/field/seek declaration.");
        foreach (var (at, length) in new[] { (4, 4), (0x14, 4), (0x18, 4), (0x1c, 4), (0x30, 1),
            (0x34, 4), (0x38, 4), (0x3c, 4), (0x40, 4), (0x148, 4), (0x14c, 4), (0x150, 4), (0x154, 4) })
            if (InitialFields.SingleOrDefault(field => field.Offset == at) is not { } field || field.Bytes.Length != length)
                throw new NotSupportedException("Binary construction has an unowned scalar field: " + at.ToString("x"));
    }
    internal uint Word(int at)
    {
        var field = InitialFields.Single(value => value.Offset == at);
        if (field.Bytes.Length != 4) throw new InvalidDataException("Binary scalar is not its original UInt32 extent.");
        return BinaryPrimitives.ReadUInt32LittleEndian(field.Bytes);
    }
}

internal static partial class FalloutExecutableStringTable
{
    // Original addresses select only read-only source declarations. No address,
    // original virtual table or original instruction is published as guest code.
    internal static FalloutNativeBinaryFileConstruction ReadBinaryFileConstruction(string path)
    {
        var (code, image) = Load(path);
        var binary = One(".?AVNiBinaryStream@@", 5);
        var file = One(".?AVNiFile@@", 8);
        var derived = One(".?AVBSFile@@", 19);
        var initialization = U32(image.Read(checked(derived + 4 * 4), 4), 0);
        RequireBinaryProcedureInitialization(initialization, image);
        var candidates = new List<(int At, byte Slot, Dictionary<int, byte[]> Fields)>();
        for (var at = 3; at <= code.Length - 9; ++at)
        {
            // A receiver reloaded from the constructor's EBP local receives
            // this exact source class table, followed by its actual fields.
            if (code[at - 3] != 0x8b || (code[at - 2] & 0xc7) != 0x45 || unchecked((sbyte)code[at - 1]) >= 0 ||
                code[at] != 0xc7 || (code[at + 1] & 0xf8) != 0 || (code[at - 2] >> 3 & 7) != (code[at + 1] & 7) ||
                U32(code, at + 2) != derived) continue;
            var slot = code[at - 1];
            if (TryBinaryConstructionPrefix(code.AsSpan(at + 6), checked(image.CodeBase + (uint)at + 6),
                slot, initialization, out var fields)) candidates.Add((at, slot, fields));
        }
        if (candidates.Count != 1) throw new NotSupportedException("Selected BSFile constructor/parameter/field association is absent or ambiguous.");
        var candidate = candidates[0]; var parents = new List<uint>();
        for (var at = Math.Max(0, candidate.At - 64); at <= candidate.At - 8; ++at)
            if (code[at] == 0x8b && code[at + 1] == 0x4d && code[at + 2] == candidate.Slot && code[at + 3] == 0xe8)
                parents.Add(RelativeTarget(image.CodeBase, at + 3, code));
        if (parents.Count != 1) throw new NotSupportedException("Source BSFile base constructor association is unbound.");
        var bases = ReadSimpleBinaryInitialization(parents[0], file, binary, image, code);
        foreach (var at in new[] { 4, 0x1c })
            if (!bases.TryGetValue(at, out var bytes) || bytes.Length != 4)
                throw new NotSupportedException("Source binary base offset field is unowned.");
            else candidate.Fields.Add(at, bytes);

        var origins = SeekOrigins(image.Read(U32(image.Read(checked(derived + 5 * 4), 4), 0), 192), image);
        var defaultBuffer = DefaultContributorBuffer(code, image);
        using var original = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var sha = Convert.ToHexString(SHA256.HashData(original));
        var result = new FalloutNativeBinaryFileConstruction(Path.GetFullPath(path), sha,
            "selected-NiBinaryStream/NiFile/BSFile-construction:" + sha, 0, origins[0], origins[1], origins[2], defaultBuffer,
            candidate.Fields.OrderBy(pair => pair.Key).Select(pair => new FalloutNativeBinaryInitialField(pair.Key, pair.Value)).ToArray());
        result.Require(); return result;

        uint One(string name, int slots)
        {
            var tables = image.SourceClassTables(name, slots).ToArray();
            return tables.Length == 1 ? tables[0] : throw new NotSupportedException("Selected binary class declaration is absent or ambiguous: " + name);
        }
    }

    private static bool TryBinaryConstructionPrefix(ReadOnlySpan<byte> input, uint origin, byte slot, uint initialization,
        out Dictionary<int, byte[]> fields)
    {
        fields = []; var registers = new int[8]; Array.Fill(registers, -1); var at = 0; var parameters = new HashSet<int>();
        var initializationArgument = false; var initialized = false;
        // Static scalar declarations end before path/open/control behavior.
        while (at < Math.Min(input.Length, 256))
        {
            var bytes = input[at..];
            if (bytes.Length >= 3 && bytes[0] == 0x8b && (bytes[1] & 0xc7) == 0x45)
            { registers[bytes[1] >> 3 & 7] = bytes[2] == slot ? -2 : unchecked((sbyte)bytes[2]); at += 3; continue; }
            if (bytes.Length >= 3 && bytes[0] == 0x89 && (bytes[1] & 0xc0) == 0x40 && registers[bytes[1] & 7] == -2)
            {
                var parameter = registers[bytes[1] >> 3 & 7];
                if (!((bytes[2] == 0x28 && parameter == 12) || (bytes[2] == 0x10 && parameter == 16))) return false;
                if (!parameters.Add(bytes[2])) return false; at += 3; continue;
            }
            if (bytes.Length >= 4 && (bytes[0] is 0xc6 or 0xc7) && (bytes[1] & 0x38) == 0 && registers[bytes[1] & 7] == -2)
            {
                var mode = bytes[1] >> 6; var displacement = mode == 1 ? 1 : mode == 2 ? 4 : 0;
                if (displacement == 0) return false;
                var width = bytes[0] == 0xc6 ? 1 : 4;
                if (bytes.Length < 2 + displacement + width) return false;
                var offset = displacement == 1 ? (int)bytes[2] : BinaryPrimitives.ReadInt32LittleEndian(bytes[2..]);
                if (offset is < 0 or >= 0x158 || offset > 0x158 - width || !fields.TryAdd(offset, bytes.Slice(2 + displacement, width).ToArray())) return false;
                at += 2 + displacement + width; continue;
            }
            if (bytes.Length >= 2 && bytes[0] == 0x6a && bytes[1] == 0)
            {
                if (initializationArgument || initialized || parameters.Count != 0 || fields.Count != 0) return false;
                initializationArgument = true; at += 2; continue;
            }
            if (bytes.Length >= 5 && bytes[0] == 0xe8)
            {
                var target = (long)origin + at + 5 + BinaryPrimitives.ReadInt32LittleEndian(bytes[1..]);
                if (!initializationArgument || initialized || registers[1] != -2 || target != initialization ||
                    parameters.Count != 0 || fields.Count != 0) return false;
                initialized = true; registers[0] = registers[1] = registers[2] = -1; at += 5; continue;
            }
            if (bytes[0] is >= 0x50 and <= 0x57) break;
            return false;
        }
        return initialized && parameters.SetEquals(new[] { 0x10, 0x28 }) &&
            new[] { 0x14, 0x18, 0x20, 0x24, 0x30, 0x34, 0x38, 0x3c, 0x40, 0x148, 0x14c, 0x150, 0x154 }.All(fields.ContainsKey);
    }

    private static Dictionary<int, byte[]> ReadSimpleBinaryInitialization(uint address, uint expectedTable, uint binaryTable,
        Image image, byte[] code)
    {
        if (!image.IsExecutableExtent(address)) throw new InvalidDataException("Binary base constructor is outside original code.");
        var registers = new int[8]; Array.Fill(registers, -1); registers[1] = -2;
        var values = new uint[8]; var fields = new Dictionary<int, byte[]>(); var tableFound = false;
        var input = code.AsSpan(checked((int)(address - image.CodeBase))); var at = 0;
        while (at < Math.Min(input.Length, 192))
        {
            var bytes = input[at..]; var opcode = bytes[0];
            if (opcode is >= 0x50 and <= 0x5f) { ++at; continue; }
            if (opcode == 0xc3)
            {
                if (!tableFound) throw new NotSupportedException("Binary base constructor has no matching source class.");
                return fields;
            }
            if (bytes.Length >= 2 && (opcode is 0x8b or 0x89) && (bytes[1] >> 6) == 3)
            {
                var target = opcode == 0x8b ? bytes[1] >> 3 & 7 : bytes[1] & 7;
                var source = opcode == 0x8b ? bytes[1] & 7 : bytes[1] >> 3 & 7;
                registers[target] = registers[source]; values[target] = values[source]; at += 2; continue;
            }
            if (bytes.Length >= 2 && (opcode is 0x31 or 0x33) && (bytes[1] >> 6) == 3 && (bytes[1] >> 3 & 7) == (bytes[1] & 7))
            { var target = bytes[1] & 7; registers[target] = 1; values[target] = 0; at += 2; continue; }
            if (bytes.Length >= 5 && opcode == 0xe8)
            {
                if (registers[1] != -2 || expectedTable == binaryTable)
                    throw new NotSupportedException("Binary base constructor has an unowned call/receiver.");
                var parent = checked((uint)((long)address + at + 5 + BinaryPrimitives.ReadInt32LittleEndian(bytes[1..])));
                foreach (var pair in ReadSimpleBinaryInitialization(parent, binaryTable, binaryTable, image, code)) fields.Add(pair.Key, pair.Value);
                registers[0] = registers[1] = registers[2] = -1; at += 5; continue;
            }
            if (bytes.Length >= 3 && (opcode is 0x89 or 0xc7))
            {
                var declaration = bytes[1]; var mode = declaration >> 6; var basis = declaration & 7;
                if (mode > 1 || basis == 4 || registers[basis] != -2) throw new NotSupportedException("Binary base field receiver/extent is unowned.");
                var offset = mode == 0 ? 0 : bytes[2]; var prefix = mode == 0 ? 2 : 3; uint value;
                if (opcode == 0x89)
                {
                    var source = declaration >> 3 & 7;
                    if (registers[source] != 1) throw new NotSupportedException("Binary base scalar is not a declared constant.");
                    value = values[source];
                }
                else
                {
                    if ((declaration & 0x38) != 0 || bytes.Length < prefix + 4) throw new InvalidDataException("Binary base immediate is incomplete.");
                    value = U32(bytes, prefix); prefix += 4;
                }
                if (offset == 0)
                { if (value != expectedTable) throw new NotSupportedException("Binary base class table differs from its original association."); tableFound = true; }
                else
                { var scalar = new byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(scalar, value); if (!fields.TryAdd(offset, scalar)) throw new NotSupportedException("Binary base scalar is written twice."); }
                at += prefix; continue;
            }
            throw new NotSupportedException("Binary base constructor contains unowned declaration/control behavior.");
        }
        throw new InvalidDataException("Binary base constructor has no complete final extent.");
    }

    private static uint[] SeekOrigins(ReadOnlySpan<byte> code, Image image)
    {
        var values = new List<uint>();
        for (var at = 0; at <= code.Length - 6; ++at)
            if (code[at] == 0x3b && code[at + 1] == 0x05)
            { var address = U32(code, at + 2); if (!image.IsFileExtent(address, 4)) throw new InvalidDataException("Source binary seek declaration is not backed."); values.Add(U32(image.Read(address, 4), 0)); }
        if (values.Count != 3 || values.Distinct().Count() != 3) throw new NotSupportedException("Binary seek constants are absent/ambiguous.");
        return values.ToArray();
    }

    private static uint DefaultContributorBuffer(ReadOnlySpan<byte> code, Image image)
    {
        var values = new List<uint>();
        for (var at = 0; at <= code.Length - 19; ++at)
        {
            if (code[at] != 0x8b || (code[at + 1] & 0xc7) != 0x45 || unchecked((sbyte)code[at + 2]) >= 0 ||
                code[at + 3] != 0x8b || (code[at + 4] & 0xc7) != 5 || code[at + 9] != 0x89 ||
                (code[at + 10] & 0xc7) != 0x80 || (code[at + 1] >> 3 & 7) != (code[at + 10] & 7) ||
                (code[at + 4] >> 3 & 7) != (code[at + 10] >> 3 & 7) || U32(code, at + 11) != 0x22c ||
                code[at + 15] != 0x6a || code[at + 16] != 24 || code[at + 17] != 0x6a || code[at + 18] != 0) continue;
            var address = U32(code, at + 5);
            if (!image.IsFileExtent(address, 4)) throw new InvalidDataException("Contributor initial buffer declaration is not backed.");
            values.Add(U32(image.Read(address, 4), 0));
        }
        return values.Count == 1 ? values[0] : throw new NotSupportedException("Contributor initial buffer declaration is absent/ambiguous.");
    }
    private static uint RelativeTarget(uint origin, int instruction, ReadOnlySpan<byte> code)
        => checked((uint)((long)origin + instruction + 5 + BinaryPrimitives.ReadInt32LittleEndian(code[(instruction + 1)..])));
}
