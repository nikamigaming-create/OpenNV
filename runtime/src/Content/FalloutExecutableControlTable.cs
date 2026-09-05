using System.Buffers.Binary;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutControlLimit(string? Setting, float Constant)
{
    internal float Resolve(Func<string, float> settings) => Setting is null ? Constant : settings(Setting);
}

internal sealed record FalloutFaceControlBinding(int Group, int Index, int Page, string Setting,
    FalloutControlLimit Minimum, FalloutControlLimit Maximum);

internal sealed record FalloutFaceControlTable(IReadOnlyList<FalloutFaceControlBinding> Controls,
    IReadOnlyList<int> TextureOrder);

internal static partial class FalloutExecutableStringTable
{
    internal static FalloutFaceControlTable ReadFaceControls(string path)
    {
        var (code, image) = Load(path);
        var descriptors = ControlDescriptors(code, image);
        return ReadFaceControls(code, descriptors, address => BitConverter.Int32BitsToSingle(unchecked((int)U32(image.Read(address, 4), 0))));
    }

    private static Dictionary<uint, string> ControlDescriptors(byte[] code, Image image)
    {
        var descriptors = new Dictionary<uint, string>();
        for (var at = 0; at <= code.Length - 28; at++)
        {
            var input = code.AsSpan(at);
            uint descriptor, name;
            if (input[..4].SequenceEqual(new byte[] { 0x55, 0x8b, 0xec, 0x68 }) && input[8] == 0x68 && input[13] == 0xb9 && input[18] == 0xe8)
            { descriptor = U32(input, 14); name = U32(input, 9); }
            else if (input[..6].SequenceEqual(new byte[] { 0x55, 0x8b, 0xec, 0x51, 0xd9, 0x05 }) &&
                input.Slice(10, 4).SequenceEqual(new byte[] { 0xd9, 0x1c, 0x24, 0x68 }) && input[18] == 0xb9 && input[23] == 0xe8)
            { descriptor = U32(input, 19); name = U32(input, 14); }
            else continue;
            if (image.IsWritableObject(descriptor) && image.Literal(name) is { } text && !descriptors.TryAdd(descriptor, text))
                throw new InvalidDataException("Owned setting descriptor has multiple initializers.");
        }
        return descriptors;
    }

    internal static IReadOnlyList<string> ReadCreationHeaders(string path)
    {
        var (code, image) = Load(path);
        var settings = ControlDescriptors(code, image);
        List<string>? result = null;
        for (var at = 0; at <= code.Length - 13; at++)
        {
            var input = code.AsSpan(at);
            if (input[0] != 0xb9 || input[5] != 0xe8 || input[10] != 0x89 || input[11] != 0x45 ||
                !settings.TryGetValue(U32(input, 1), out var label) || label != "sRSMCustomize") continue;
            var labels = new List<string>(); var stack = unchecked((sbyte)input[12]);
            var getter = at + 10 + BinaryPrimitives.ReadInt32LittleEndian(input[6..]);
            for (var index = 0; index < 16; index++)
            {
                var offset = at + index * 13;
                if (offset > code.Length - 13) break;
                var row = code.AsSpan(offset);
                if (row[0] != 0xb9 || row[5] != 0xe8 || row[10] != 0x89 || row[11] != 0x45 ||
                    unchecked((sbyte)row[12]) != stack + index * 4 ||
                    offset + 10 + BinaryPrimitives.ReadInt32LittleEndian(row[6..]) != getter || !settings.TryGetValue(U32(row, 1), out var name)) break;
                labels.Add(name);
            }
            if (labels.Count != 16) continue;
            if (result is not null) throw new InvalidDataException("Owned creation page declaration is ambiguous.");
            result = ["sRSMSex", "sRSMRace", "sRSMFace", "sRSMHair", .. labels];
        }
        return result ?? throw new NotSupportedException("Owned creation page headers have no admitted declaration.");
    }

    // Decode the compiler's consecutive aggregate initializers, retaining their
    // setting references, source indices and page association. Neither a retail
    // address list nor a hand-selected subset of controls is a runtime input.
    internal static FalloutFaceControlTable ReadFaceControls(ReadOnlyMemory<byte> code,
        IReadOnlyDictionary<uint, string> settings, Func<uint, float> constant)
    {
        List<ControlInitializer>? rows = null;
        var end = 0;
        for (var offset = 0; offset < code.Length - 16; offset++)
        {
            var reader = new ControlInitializerReader(code, offset, settings, constant);
            if (!reader.TryRow(out var first) || first.Setting != "sRSMShapeOption01") continue;
            if (rows is not null) throw new InvalidDataException("Owned face-control declarations are ambiguous.");
            rows = [first];
            while (reader.TryRow(out var next)) rows.Add(next);
            end = reader.Position;
        }
        if (rows is null || rows.Count == 0) throw new NotSupportedException("Owned face-control initializer layout is unbound.");
        var result = new List<FalloutFaceControlBinding>();
        var group = 0; var index = 0; var previous = rows[0].Offset - 16;
        foreach (var row in rows)
        {
            if (row.Offset != previous + 16) { group += 2; index = 0; }
            if (group > 2) throw new NotSupportedException("Owned control aggregate has another domain.");
            if (row.Setting is not null)
            {
                if (row.Page is < 0 or >= 20) throw new NotSupportedException("Owned face control targets an unbound menu page.");
                result.Add(new(group, index, row.Page, row.Setting, row.Minimum, row.Maximum));
            }
            else if (row.Page != 20) throw new InvalidDataException("An active face control has no source label.");
            previous = row.Offset; index++;
        }
        if (group != 2) throw new NotSupportedException("Owned face controls lack a separate texture aggregate.");

        // Tone controls are emitted explicitly in a different order from their
        // coefficient indices. Read the getter's constant (domain,symmetry,index)
        // call sites immediately after the declarations, before the function's
        // structured exception epilogue. The same callee must own every sample.
        var tail = code.Span[end..];
        ReadOnlySpan<byte> epilogue = [0x8b, 0x4d, 0xf4, 0x64, 0x89, 0x0d, 0, 0, 0, 0];
        var extent = tail.IndexOf(epilogue);
        if (extent < 0) throw new NotSupportedException("Owned control initializer has no admitted function boundary.");
        tail = tail[..extent];
        var order = new List<int>(); int? getter = null;
        for (var at = 0; at <= tail.Length - 18; at++)
        {
            var input = tail[at..];
            if (input[0] != 0x6a || input[2] != 0x6a || input[3] != 0 || input[4] != 0x6a || input[5] != 1 ||
                input[6] != 0x8d || (input[7] & 0xc7) != 0x85 || input[12] != 0x50 + ((input[7] >> 3) & 7) || input[13] != 0xe8) continue;
            var target = end + at + 18 + BinaryPrimitives.ReadInt32LittleEndian(input[14..]);
            if (getter is not null && getter != target) throw new NotSupportedException("Owned control sampling has multiple owners.");
            getter = target;
            if (!order.Contains(input[1])) order.Add(input[1]);
        }
        if (order.Count == 0 || result.Where(row => row.Group == 2).Any(row => !order.Contains(row.Index)) ||
            order.Any(index => !result.Any(row => row.Group == 2 && row.Index == index)))
            throw new NotSupportedException("Owned texture control order is incomplete.");
        return new(result, order);
    }

    private sealed record ControlInitializer(int Offset, int Page, string? Setting, FalloutControlLimit Minimum, FalloutControlLimit Maximum);

    private sealed class ControlInitializerReader(ReadOnlyMemory<byte> code, int offset,
        IReadOnlyDictionary<uint, string> settings, Func<uint, float> constant)
    {
        internal int Position { get; private set; } = offset;
        private ReadOnlySpan<byte> Input => code.Span[Position..];

        internal bool TryRow(out ControlInitializer row)
        {
            var start = Position; row = null!;
            if (!StoreImmediate(out var stack, out var page)) return false;
            string? label = null;
            if (Setting(out var key))
            {
                label = key;
                if (!Store(0x89, 0x85, 0x45, out var destination) || destination != stack + 4) { Position = start; return false; }
            }
            else if (!StoreImmediate(out var destination, out var value) || destination != stack + 4 || value != 0)
            { Position = start; return false; }
            if (!Limit(stack + 8, out var minimum) || !Limit(stack + 12, out var maximum)) { Position = start; return false; }
            row = new(stack, page, label, minimum, maximum); return true;
        }

        private bool Setting(out string setting)
        {
            setting = "";
            if (Input.Length < 10 || Input[0] != 0xb9 || Input[5] != 0xe8 ||
                !settings.TryGetValue(U32(Input, 1), out var key)) return false;
            setting = key; Position += 10; return true;
        }

        private bool Limit(int destination, out FalloutControlLimit limit)
        {
            limit = null!; var start = Position;
            if (Setting(out var key))
            {
                if (!key.StartsWith('f')) { Position = start; return false; }
                limit = new(key, 0);
            }
            else if (Input.Length >= 6 && Input[0] == 0xd9 && Input[1] == 0x05)
            { limit = new(null, constant(U32(Input, 2))); Position += 6; }
            else if (Input.Length >= 2 && Input[0] == 0xd9 && Input[1] is 0xee or 0xe8)
            { limit = new(null, Input[1] == 0xee ? 0 : 1); Position += 2; }
            else return false;
            if (!Store(0xd9, 0x9d, 0x5d, out var stack) || stack != destination || !float.IsFinite(limit.Constant))
            { Position = start; return false; }
            return true;
        }

        private bool StoreImmediate(out int destination, out int value)
        {
            var start = Position; value = 0;
            if (!Store(0xc7, 0x85, 0x45, out destination)) return false;
            if (Input.Length < 4) { Position = start; return false; }
            value = BinaryPrimitives.ReadInt32LittleEndian(Input); Position += 4; return true;
        }

        private bool Store(byte opcode, byte wide, byte shortForm, out int destination)
        {
            destination = 0;
            if (Input.Length >= 6 && Input[0] == opcode && Input[1] == wide)
            { destination = BinaryPrimitives.ReadInt32LittleEndian(Input[2..]); Position += 6; return true; }
            if (Input.Length >= 3 && Input[0] == opcode && Input[1] == shortForm)
            { destination = unchecked((sbyte)Input[2]); Position += 3; return true; }
            return false;
        }
    }
}
