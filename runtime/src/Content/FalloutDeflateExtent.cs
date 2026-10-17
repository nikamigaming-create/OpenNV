namespace OpenNV.Runtime.Content;

internal readonly record struct FalloutDeflateExtentResult(
    long FinalBitOffset, int ConsumedBytes, uint DecodedBytes, int Blocks);

/// <summary>
/// Validates one complete RFC1951 body before framework inflation. No decoded
/// bytes are copied or retained; Adler32 policy belongs to the framed reader.
/// </summary>
internal static class FalloutDeflateExtent
{
    private static ReadOnlySpan<int> LengthBases =>
        [3, 4, 5, 6, 7, 8, 9, 10, 11, 13, 15, 17, 19, 23, 27, 31, 35, 43, 51, 59, 67, 83, 99, 115, 131, 163, 195, 227, 258];
    private static ReadOnlySpan<int> LengthExtraBits =>
        [0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2, 3, 3, 3, 3, 4, 4, 4, 4, 5, 5, 5, 5, 0];
    private static ReadOnlySpan<int> DistanceBases =>
        [1, 2, 3, 4, 5, 7, 9, 13, 17, 25, 33, 49, 65, 97, 129, 193, 257, 385, 513, 769, 1025, 1537, 2049, 3073, 4097, 6145, 8193, 12289, 16385, 24577];
    private static ReadOnlySpan<int> DistanceExtraBits =>
        [0, 0, 0, 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 6, 7, 7, 8, 8, 9, 9, 10, 10, 11, 11, 12, 12, 13, 13];
    private static ReadOnlySpan<int> CodeLengthOrder =>
        [16, 17, 18, 0, 8, 7, 9, 6, 10, 5, 11, 4, 12, 3, 13, 2, 14, 1, 15];
    private static readonly Huffman FixedLiteralLengths = FixedLiterals();
    private static readonly Huffman FixedDistances = new(Enumerable.Repeat((byte)5, 32).ToArray());

    internal static FalloutDeflateExtentResult ValidateZlibFrame(ReadOnlySpan<byte> framed, uint expectedOutputBytes)
    {
        const int headerBytes = 2, trailerBytes = 4;
        if (framed.Length < headerBytes + trailerBytes || (framed[0] & 15) != 8 || (framed[0] >> 4) > 7 ||
            (((framed[0] << 8) | framed[1]) % 31) != 0 || (framed[1] & 0x20) != 0)
            throw new InvalidDataException("Invalid zlib envelope for a DEFLATE extent: CMF/FCHECK/CINFO/dictionary declaration is unsupported or absent.");
        return Validate(framed[headerBytes..^trailerBytes], expectedOutputBytes, 1 << ((framed[0] >> 4) + 8));
    }

    internal static FalloutDeflateExtentResult Validate(ReadOnlySpan<byte> compressed,
        uint expectedOutputBytes, int windowBytes = 32768)
    {
        if (windowBytes is < 1 or > 32768) throw new ArgumentOutOfRangeException(nameof(windowBytes));
        var input = new Bits(compressed);
        long output = 0;
        var blocks = 0;
        bool final;
        do
        {
            final = input.Read(1) != 0;
            var type = input.Read(2);
            blocks++;
            switch (type)
            {
                case 0:
                    input.AlignByte();
                    var length = input.Read(16);
                    var complement = input.Read(16);
                    if ((length ^ complement) != 0xffff) throw input.Error("stored LEN/NLEN disagree");
                    input.SkipAlignedBytes(length);
                    CountOutput(ref output, length, expectedOutputBytes, input.Position);
                    break;
                case 1:
                    ReadCompressed(ref input, FixedLiteralLengths, FixedDistances, ref output, expectedOutputBytes, windowBytes);
                    break;
                case 2:
                    var (literals, distances) = ReadDynamicTrees(ref input);
                    ReadCompressed(ref input, literals, distances, ref output, expectedOutputBytes, windowBytes);
                    break;
                default:
                    throw input.Error("reserved block type");
            }
        } while (!final);
        var consumed = checked((int)((input.Position + 7) / 8));
        if (consumed != compressed.Length) throw input.Error("bytes follow the final DEFLATE block");
        if (output != expectedOutputBytes) throw input.Error($"decoded length {output} differs from declaration {expectedOutputBytes}");
        return new(input.Position, consumed, (uint)output, blocks);
    }

    private static void ReadCompressed(ref Bits input, Huffman literals, Huffman distances,
        ref long output, uint expectedOutputBytes, int windowBytes)
    {
        while (true)
        {
            var symbol = literals.Read(ref input);
            if (symbol < 256)
            {
                CountOutput(ref output, 1, expectedOutputBytes, input.Position);
                continue;
            }
            if (symbol == 256) return;
            if (symbol is < 257 or > 285) throw input.Error("reserved literal/length symbol");
            var index = symbol - 257;
            var length = LengthBases[index] + input.Read(LengthExtraBits[index]);
            var distanceSymbol = distances.Read(ref input);
            if (distanceSymbol > 29) throw input.Error("reserved distance symbol");
            var distance = DistanceBases[distanceSymbol] + input.Read(DistanceExtraBits[distanceSymbol]);
            if (distance > output || distance > windowBytes)
                throw input.Error("match distance exceeds preceding output or the declared window");
            // Overlapping matches and references to preceding blocks retain
            // the same history count; no reconstructed byte buffer is needed.
            CountOutput(ref output, length, expectedOutputBytes, input.Position);
        }
    }

    private static (Huffman Literals, Huffman Distances) ReadDynamicTrees(ref Bits input)
    {
        var literalCount = input.Read(5) + 257;
        var distanceCount = input.Read(5) + 1;
        var codeLengthCount = input.Read(4) + 4;
        if (literalCount > 286) throw input.Error("reserved dynamic literal/length count");
        var codeLengths = new byte[19];
        for (var index = 0; index < codeLengthCount; ++index)
            codeLengths[CodeLengthOrder[index]] = (byte)input.Read(3);
        var codeTree = new Huffman(codeLengths);
        var lengths = new byte[literalCount + distanceCount];
        var used = 0;
        while (used < lengths.Length)
        {
            var symbol = codeTree.Read(ref input);
            if (symbol <= 15) { lengths[used++] = (byte)symbol; continue; }
            int repeat;
            byte value;
            switch (symbol)
            {
                case 16:
                    if (used == 0) throw input.Error("repeat has no preceding code length");
                    value = lengths[used - 1]; repeat = input.Read(2) + 3;
                    break;
                case 17: value = 0; repeat = input.Read(3) + 3; break;
                case 18: value = 0; repeat = input.Read(7) + 11; break;
                default: throw input.Error("unknown code-length symbol");
            }
            if (repeat > lengths.Length - used) throw input.Error("code-length repeat exceeds both alphabets");
            lengths.AsSpan(used, repeat).Fill(value); used += repeat;
        }
        if (lengths[256] == 0) throw input.Error("dynamic literal tree has no end-of-block symbol");
        return (new(lengths.AsSpan(0, literalCount)), new(lengths.AsSpan(literalCount), allowEmpty: true));
    }

    private static void CountOutput(ref long output, int length, uint expected, long bit)
    {
        output += length;
        if (output > expected)
            throw new InvalidDataException($"Invalid DEFLATE extent: decoded output exceeds declaration {expected} (bit {bit}).");
    }

    private static Huffman FixedLiterals()
    {
        var lengths = new byte[288];
        lengths.AsSpan(0, 144).Fill(8); lengths.AsSpan(144, 112).Fill(9);
        lengths.AsSpan(256, 24).Fill(7); lengths.AsSpan(280, 8).Fill(8);
        return new(lengths);
    }

    private sealed class Huffman
    {
        private readonly int[] _counts = new int[16];
        private readonly int[] _firstCode = new int[16];
        private readonly int[] _firstSymbol = new int[16];
        private readonly int[] _symbols;
        private readonly int _maximum;

        internal Huffman(ReadOnlySpan<byte> lengths, bool allowEmpty = false)
        {
            foreach (var length in lengths)
            {
                if (length > 15) throw new InvalidDataException("Invalid DEFLATE Huffman code length.");
                if (length == 0) continue;
                _counts[length]++; _maximum = Math.Max(_maximum, length);
            }
            var available = 1;
            var total = 0;
            for (var length = 1; length <= 15; ++length)
            {
                available = available * 2 - _counts[length];
                if (available < 0) throw new InvalidDataException("Invalid DEFLATE Huffman alphabet is oversubscribed.");
                _firstCode[length] = (_firstCode[length - 1] + _counts[length - 1]) * 2;
                _firstSymbol[length] = total;
                total += _counts[length];
            }
            if (total == 0 && !allowEmpty) throw new InvalidDataException("Invalid DEFLATE Huffman alphabet is empty.");
            _symbols = new int[total];
            var written = new int[16];
            for (var symbol = 0; symbol < lengths.Length; ++symbol)
            {
                var length = lengths[symbol];
                if (length != 0) _symbols[_firstSymbol[length] + written[length]++] = symbol;
            }
        }

        internal int Read(ref Bits input)
        {
            var code = 0;
            for (var length = 1; length <= _maximum; ++length)
            {
                code = code * 2 + input.Read(1);
                var index = code - _firstCode[length];
                if (index >= 0 && index < _counts[length]) return _symbols[_firstSymbol[length] + index];
            }
            throw input.Error("Huffman prefix has no declared symbol");
        }
    }

    private ref struct Bits(ReadOnlySpan<byte> bytes)
    {
        internal long Position { get; private set; }
        private readonly ReadOnlySpan<byte> _bytes = bytes;

        internal int Read(int count)
        {
            if (count > (long)_bytes.Length * 8 - Position) throw Error("input ended before its final block completed");
            var result = 0;
            for (var index = 0; index < count; ++index)
            {
                result |= ((_bytes[(int)(Position / 8)] >> (int)(Position % 8)) & 1) << index;
                Position++;
            }
            return result;
        }

        internal void AlignByte() => Position = (Position + 7) & ~7L;

        internal void SkipAlignedBytes(int count)
        {
            if (count > _bytes.Length - Position / 8) throw Error("stored block payload is truncated");
            Position += (long)count * 8;
        }

        internal readonly InvalidDataException Error(string detail) =>
            new($"Invalid DEFLATE extent: {detail} (bit {Position}).");
    }
}
