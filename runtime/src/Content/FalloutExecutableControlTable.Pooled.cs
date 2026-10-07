namespace OpenNV.Runtime.Content;

internal static partial class FalloutExecutableStringTable
{
    private static int I32(ReadOnlySpan<byte> code, int offset) => unchecked((int)U32(code, offset));
    // Another compiler emits the same aggregates through pooled registers and
    // frame slots, then constructs tone sliders directly. Resolve typed values
    // through those assignments and their consumers; source labels alone cannot
    // establish either an index, a limit or a menu-page association.
    private static FalloutFaceControlTable ReadPooledFaceControls(ReadOnlyMemory<byte> source,
        IReadOnlyDictionary<uint, string> settings, Func<uint, float> constant)
    {
        var code = source.Span;
        FalloutFaceControlTable? result = null;
        for (var at = 0; at <= code.Length - 13; at++)
        {
            var input = code[at..];
            if (input[0] != 0xa1 || !settings.TryGetValue(U32(input, 1) - 4, out var name) || name != "sRSMShapeOption01" ||
                !input.Slice(5, 3).SequenceEqual(new byte[] { 0xf3, 0x0f, 0x10 })) continue;
            if (result is not null) throw new InvalidDataException("Owned face-control declarations are ambiguous.");
            var reader = new PooledControlReader(source, at, settings, constant);
            while (reader.Move()) { }
            if (reader.Position >= code.Length || code[reader.Position] != 0xe8)
                throw new NotSupportedException("Pooled face-control assignments have an unbound continuation.");
            var starts = reader.Frame.Where(row => row.Value?.Setting == "sRSMShapeOption01").Select(row => row.Key - 4).ToArray();
            if (starts.Length != 1) throw new InvalidDataException("Pooled geometry aggregate has no unique source start.");
            var rows = new List<FalloutFaceControlBinding>();
            var last = reader.Frame.Keys.Max();
            if ((last - starts[0] + 4) % 16 != 0)
                throw new InvalidDataException("Pooled geometry aggregate has an incomplete trailing row.");
            var count = (last - starts[0] + 4) / 16;
            for (var index = 0; index < count; index++)
            {
                var slot = starts[0] + index * 16;
                var page = reader.Integer(slot);
                var label = reader.Value(slot + 4);
                var minimum = reader.Limit(slot + 8); var maximum = reader.Limit(slot + 12);
                if (label.Setting is { } labelSetting && labelSetting.StartsWith('s') && page is >= 0 and < 20)
                    rows.Add(new(0, index, page, labelSetting, minimum, maximum));
                else if (page != 20 || label.Setting is not null || label.Bits != 0 ||
                    minimum.Setting is not null || minimum.Constant != 0 || maximum.Setting is not null || maximum.Constant != 0)
                    throw new InvalidDataException("Pooled geometry row has incomplete active or hidden fields.");
            }
            var tail = code[reader.Position..];
            ReadOnlySpan<byte> epilogue = [0x8b, 0x4d, 0xf4, 0x64, 0x89, 0x0d, 0, 0, 0, 0];
            var extent = tail.IndexOf(epilogue);
            if (extent < 0) throw new NotSupportedException("Pooled face controls have no admitted function boundary.");
            tail = tail[..extent];
            var consumer = PooledGeometryConsumer(tail, reader.Position, starts[0]);
            var order = new List<int>();
            int? constructor = null;
            for (var offset = consumer.End; offset <= tail.Length - 18; offset++)
            {
                var sample = tail[offset..];
                if (sample[0] != 0x6a || sample[2] != 0x6a || sample[3] != 0) continue;
                var lea = 4;
                if (sample.Length >= 25 && sample.Slice(4, 3).SequenceEqual(new byte[] { 0xc7, 0x40, 8 }))
                {
                    if (order.Count == 0 || U32(sample, 7) != order[^1])
                        throw new InvalidDataException("Tone sampling lost its preceding coefficient index.");
                    lea += 7;
                }
                if (sample.Length < lea + 14 || !sample.Slice(lea, 2).SequenceEqual(new byte[] { 0x8d, 0x85 }) ||
                    !sample.Slice(lea + 6, 4).SequenceEqual(new byte[] { 0x6a, 1, 0x50, 0xe8 })) continue;
                var end = offset + lea + 14;
                if (reader.Position + end + I32(sample, lea + 10) != consumer.Getter || I32(sample, lea + 2) != consumer.Buffer)
                    throw new NotSupportedException("Pooled tone sampling has a foreign source owner.");
                if (order.Contains(sample[1])) throw new InvalidDataException("Pooled tone coefficients are declared more than once.");
                var declaration = PooledToneDeclaration(tail[end..], reader.Position + end, reader, consumer.Pages, ref constructor);
                rows.Add(new(2, sample[1], declaration.Page, declaration.Label, declaration.Minimum, declaration.Maximum));
                order.Add(sample[1]); offset = end - 1;
            }
            if (count == 0 || order.Count == 0) throw new NotSupportedException("Pooled face-control domains are incomplete.");
            result = new(rows, order, count);
        }
        return result ?? throw new NotSupportedException("Owned face-control initializer layout is unbound.");
    }

    private static (int End, int Getter, int Buffer, int Pages) PooledGeometryConsumer(ReadOnlySpan<byte> code, int origin, int start)
    {
        var labelPointer = false; var stride = false; int? pages = null;
        for (var at = 0; at <= code.Length - 17; at++)
        {
            var input = code[at..];
            if (input[..2].SequenceEqual(new byte[] { 0x8d, 0xb5 }) && I32(input, 2) == start + 4) labelPointer = true;
            if (input[..3].SequenceEqual(new byte[] { 0x83, 0xc6, 0x10 })) stride = true;
            if (input[..3].SequenceEqual(new byte[] { 0x8b, 0x4c, 0x8b })) pages = input[3];
            else if (input[..3].SequenceEqual(new byte[] { 0x8b, 0x8c, 0x8b })) pages = I32(input, 3);
        }
        if (!labelPointer || !stride || pages is null || pages < 0)
            throw new NotSupportedException("Pooled geometry has no admitted indexed page consumer.");
        for (var at = 0; at <= code.Length - 17; at++)
        {
            var input = code[at..];
            if (!input[..5].SequenceEqual(new byte[] { 0x57, 0x6a, 0, 0x8d, 0x85 }) ||
                !input.Slice(9, 4).SequenceEqual(new byte[] { 0x6a, 0, 0x50, 0xe8 })) continue;
            return (at + 17, origin + at + 17 + I32(input, 13), I32(input, 5), pages.Value);
        }
        throw new NotSupportedException("Pooled geometry has no admitted coefficient getter.");
    }

    private static (int Page, string Label, FalloutControlLimit Minimum, FalloutControlLimit Maximum) PooledToneDeclaration(
        ReadOnlySpan<byte> code, int origin, PooledControlReader reader, int pages, ref int? constructor)
    {
        int? minimum = null, maximum = null, pageField = null, label = null, minRegister = null, maxRegister = null;
        var end = 0;
        while (end < code.Length && code[end] != 0xe8)
        {
            var input = code[end..];
            var length = ToneInstructionLength(input);
            if (input[..2].SequenceEqual(new byte[] { 0xff, 0xb5 }) && reader.Frame.TryGetValue(I32(input, 2), out var value) &&
                value?.Setting is { } key && key.StartsWith('s'))
            {
                if (label is not null) throw new InvalidDataException("Pooled tone constructor has multiple labels.");
                label = I32(input, 2);
            }
            if (length >= 3 && input[0] == 0x8b && input[1] == 0x4e) pageField = input[2];
            else if (length >= 6 && input[0] == 0x8b && input[1] == 0x8e) pageField = I32(input, 2);
            if (length == 8 && input[..2].SequenceEqual(new byte[] { 0xf3, 0x0f }) &&
                input[2] is 0x5d or 0x5f && (input[3] & 0xc7) == 0x85)
            {
                if (input[2] == 0x5d) { maximum = I32(input, 4); maxRegister = (input[3] >> 3) & 7; }
                else { minimum = I32(input, 4); minRegister = (input[3] >> 3) & 7; }
            }
            end += length;
        }
        if (end > code.Length - 5 || label is null) throw new NotSupportedException("Pooled tone label has no admitted constructor.");
        var target = origin + end + 5 + I32(code, end + 1);
        if (constructor is not null && constructor != target) throw new NotSupportedException("Pooled tone controls have different constructors.");
        constructor = target;
        if (minimum is null || maximum is null || pageField is null || minRegister != maxRegister ||
            pageField < pages || (pageField - pages) % 4 != 0 || (pageField - pages) / 4 >= 20)
            throw new NotSupportedException("Pooled tone limits or page association are unbound.");
        return ((pageField.Value - pages) / 4, reader.Value(label.Value).Setting!, reader.Limit(minimum.Value), reader.Limit(maximum.Value));
    }

    private static int ToneInstructionLength(ReadOnlySpan<byte> code)
    {
        var cursor = 1; var immediate = 0;
        if (code[0] is >= 0x50 and <= 0x57) return 1;
        if (code[0] is 0x6a or 0x68) { cursor = code[0] == 0x6a ? 2 : 5; }
        else
        {
            if (code.Length >= 4 && code[..2].SequenceEqual(new byte[] { 0xf3, 0x0f }) && code[2] is 0x10 or 0x11 or 0x2c or 0x59 or 0x5d or 0x5f) cursor = 3;
            else if (code[0] == 0x83) immediate = 1;
            else if (code[0] is not (0xd9 or 0x8b or 0x89 or 0xff))
                throw new NotSupportedException("Pooled tone constructor has an unbound instruction.");
            if (cursor >= code.Length) throw new InvalidDataException("Pooled tone instruction is truncated.");
            var modrm = code[cursor++]; var mode = modrm >> 6; var basis = modrm & 7;
            if (mode != 3 && basis == 4)
            {
                if (cursor >= code.Length) throw new InvalidDataException("Pooled tone address expression is truncated.");
                basis = code[cursor++] & 7;
            }
            cursor += mode == 1 ? 1 : mode == 2 || mode == 0 && basis == 5 ? 4 : 0;
            cursor += immediate;
        }
        return cursor <= code.Length ? cursor : throw new InvalidDataException("Pooled tone operand is truncated.");
    }

    private sealed record PooledControlValue(string? Setting, uint Bits);
    private sealed class PooledControlReader(ReadOnlyMemory<byte> code, int start,
        IReadOnlyDictionary<uint, string> settings, Func<uint, float> constant)
    {
        internal int Position { get; private set; } = start;
        internal Dictionary<int, PooledControlValue?> Frame { get; } = [];
        private readonly PooledControlValue?[] _registers = new PooledControlValue?[16];
        internal PooledControlValue Value(int slot) => Frame.TryGetValue(slot, out var value) && value is not null ? value :
            throw new InvalidDataException("Pooled face control uses an unowned frame value.");
        internal int Integer(int slot) => Value(slot) is { Setting: null } value ? unchecked((int)value.Bits) :
            throw new InvalidDataException("Pooled face control has no integer page declaration.");
        internal FalloutControlLimit Limit(int slot)
        {
            var value = Value(slot);
            if (value.Setting is { } name) return name.StartsWith('f') ? new(name, 0) :
                throw new InvalidDataException("Pooled face limit uses a nonnumeric setting.");
            var number = BitConverter.Int32BitsToSingle(unchecked((int)value.Bits));
            return float.IsFinite(number) ? new(null, number) : throw new InvalidDataException("Pooled face limit is non-finite.");
        }

        internal bool Move()
        {
            var input = code.Span[Position..];
            if (input.IsEmpty) return false;
            var cursor = 0; var scalar = input.Length >= 3 && input[..2].SequenceEqual(new byte[] { 0xf3, 0x0f });
            if (scalar) cursor = 2;
            var opcode = input[cursor++];
            if (!scalar && opcode is 0x68 or 0x6a || !scalar && opcode is >= 0x50 and <= 0x57)
            { Position += opcode == 0x68 ? 5 : opcode == 0x6a ? 2 : 1; return Position <= code.Length; }
            if (!scalar && opcode == 0xa1)
            { _registers[0] = Absolute(U32(input, cursor), false); Position += 5; return true; }
            if (scalar ? opcode is not (0x10 or 0x11) : opcode is not (0x8b or 0x89 or 0xc7 or 0x8d)) return false;
            if (cursor >= input.Length) throw new InvalidDataException("Pooled assignment is truncated.");
            var modrm = input[cursor++]; var reg = ((modrm >> 3) & 7) + (scalar ? 8 : 0);
            var operand = Operand(input, ref cursor, modrm);
            if (opcode == 0x8d) _registers[reg] = null;
            else if (opcode == 0xc7)
            {
                if ((modrm & 0x38) != 0) throw new NotSupportedException("Pooled immediate assignment has another operation.");
                Write(operand, new(null, U32(input, cursor))); cursor += 4;
            }
            else if (opcode is 0x8b or 0x10) _registers[reg] = Read(operand, scalar);
            else Write(operand, _registers[reg], scalar);
            Position += cursor; return true;
        }

        private static (int Kind, int Value) Operand(ReadOnlySpan<byte> input, ref int cursor, byte modrm)
        {
            var mode = modrm >> 6; var basis = modrm & 7;
            if (mode == 3) return (0, basis);
            if (mode == 0 && basis == 5) { var address = I32(input, cursor); cursor += 4; return (2, address); }
            if (basis != 5 || mode is not (1 or 2)) throw new NotSupportedException("Pooled assignment has an unbound address expression.");
            if (cursor >= input.Length) throw new InvalidDataException("Pooled frame displacement is truncated.");
            var slot = mode == 1 ? unchecked((sbyte)input[cursor]) : I32(input, cursor); cursor += mode == 1 ? 1 : 4;
            return (1, slot);
        }
        private PooledControlValue? Read((int Kind, int Value) operand, bool scalar) => operand.Kind switch
        {
            0 => _registers[operand.Value + (scalar ? 8 : 0)],
            1 => Frame.GetValueOrDefault(operand.Value),
            2 => Absolute(unchecked((uint)operand.Value), scalar),
            _ => throw new InvalidDataException("Pooled assignment operand is invalid."),
        };
        private void Write((int Kind, int Value) operand, PooledControlValue? value, bool scalar = false)
        {
            if (operand.Kind == 1) Frame[operand.Value] = value;
            else if (operand.Kind == 0) _registers[operand.Value + (scalar ? 8 : 0)] = value;
            else throw new NotSupportedException("Pooled assignment writes outside its owned frame.");
        }
        private PooledControlValue? Absolute(uint address, bool scalar)
        {
            if (settings.TryGetValue(address - 4, out var name))
                return name.StartsWith(scalar ? 'f' : 's') ? new(name, 0) :
                    throw new InvalidDataException("Pooled setting payload uses the wrong scalar lane.");
            if (!scalar) return null;
            var number = constant(address);
            if (!float.IsFinite(number)) throw new InvalidDataException("Pooled constant is non-finite.");
            return new(null, unchecked((uint)BitConverter.SingleToInt32Bits(number)));
        }
    }
}
