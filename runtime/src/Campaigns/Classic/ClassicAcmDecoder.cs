using System.Buffers.Binary;

namespace OpenNV.Runtime.Campaigns.Classic;

internal sealed record ClassicAcmHeader(int Samples, int Channels, int SampleRate, int Level, int Rows);

/// <summary>Streaming PCM from owned classic ACM bytes. All history is per voice.</summary>
internal sealed class ClassicAcmDecoder
{
    private readonly byte[] _source;
    private long _bit;
    private readonly int[] _block;
    private readonly int[][] _history;
    private readonly int _columns;
    private int _position;
    private int _available;
    private int _remaining;
    private string? _failure;
    internal ClassicAcmHeader Header { get; }
    internal long ZeroPaddingBits => Math.Max(0, _bit - (long)_source.Length * 8);

    internal ClassicAcmDecoder(byte[] source)
    {
        if (source.Length < 14 || source[0] != 0x97 || source[1] != 0x28 || source[2] != 3 || source[3] != 1)
            throw new InvalidDataException("Classic ACM header or version is invalid.");
        var samples = BinaryPrimitives.ReadUInt32LittleEndian(source.AsSpan(4));
        var channels = BinaryPrimitives.ReadUInt16LittleEndian(source.AsSpan(8));
        var rate = BinaryPrimitives.ReadUInt16LittleEndian(source.AsSpan(10));
        var layout = BinaryPrimitives.ReadUInt16LittleEndian(source.AsSpan(12));
        var level = layout & 15; var rows = layout >> 4;
        if (samples > int.MaxValue || channels == 0 || rate == 0 || rows == 0)
            throw new InvalidDataException("Classic ACM sample layout is invalid.");
        Header = new((int)samples, channels, rate, level, rows);
        _source = source; _bit = 112; _remaining = Header.Samples; _columns = 1 << level;
        _block = new int[checked(_columns * rows)];
        _history = Enumerable.Range(0, level).Select(stage => new int[(_columns >> (stage + 1)) * 2]).ToArray();
    }

    internal int Read(Span<short> destination)
    {
        if (_failure is not null) throw new InvalidDataException(_failure);
        var written = 0;
        while (written < destination.Length && _remaining > 0)
        {
            if (_available == 0)
            {
                try { DecodeBlock(); Synthesize(); }
                catch (InvalidDataException error) { _failure = error.Message; throw; }
                _position = 0; _available = Math.Min(_block.Length, _remaining);
            }
            var count = Math.Min(destination.Length - written, _available);
            for (var index = 0; index < count; index++)
                destination[written + index] = unchecked((short)(_block[_position + index] >> Header.Level));
            _position += count; _available -= count; _remaining -= count; written += count;
        }
        return written;
    }

    private int Bits(int count)
    {
        var value = 0;
        // The original input owner supplies zeros at EOF. This is needed by
        // valid final blocks; ZeroPaddingBits keeps that consumption observable.
        for (var index = 0; index < count; index++, _bit++)
            if (_bit < (long)_source.Length * 8)
                value |= ((_source[(int)(_bit >> 3)] >> (int)(_bit & 7)) & 1) << index;
        return value;
    }

    private void DecodeBlock()
    {
        var amplitudeCount = 1 << Bits(4); var step = Bits(16);
        for (var column = 0; column < _columns; column++)
        {
            var selector = Bits(5);
            if (selector is 1 or 2 or 25 or 28 or 30 or 31)
                throw new InvalidDataException($"Classic ACM column selector {selector} is invalid.");
            var row = 0;
            void Emit(int symbol)
            {
                if (symbol < -amplitudeCount || symbol >= amplitudeCount)
                    throw new InvalidDataException("Classic ACM symbol exceeds its amplitude table.");
                if (row < Header.Rows) _block[row++ * _columns + column] = unchecked((short)(symbol * step));
            }
            while (row < Header.Rows)
            {
                if (selector == 0) { Emit(0); continue; }
                if (selector <= 16) { Emit(Bits(selector) - (1 << (selector - 1))); continue; }
                if (selector is 19 or 22 or 29)
                {
                    var radix = selector == 19 ? 3 : selector == 22 ? 5 : 11;
                    var digits = selector == 29 ? 2 : 3;
                    var value = Bits(selector == 19 ? 5 : 7);
                    var limit = digits == 2 ? radix * radix : radix * radix * radix;
                    if (value >= limit) throw new InvalidDataException("Classic ACM packed symbol is invalid.");
                    for (var index = 0; index < digits && row < Header.Rows; index++)
                    { Emit(value % radix - radix / 2); value /= radix; }
                    continue;
                }
                var pairedZeros = selector is 17 or 20 or 23 or 26;
                if (Bits(1) == 0)
                {
                    Emit(0); if (pairedZeros && row < Header.Rows) Emit(0);
                    continue;
                }
                if (pairedZeros && Bits(1) == 0) { Emit(0); continue; }
                if (selector is 17 or 18) { Emit(Bits(1) == 0 ? -1 : 1); continue; }
                if (selector is 23 or 24)
                {
                    if (Bits(1) == 0) { Emit(Bits(1) == 0 ? -1 : 1); continue; }
                    var value = Bits(2); Emit(value < 2 ? value - 3 : value); continue;
                }
                var width = selector is 20 or 21 ? 2 : 3;
                var half = 1 << (width - 1); var encoded = Bits(width);
                Emit(encoded < half ? encoded - half : encoded - half + 1);
            }
        }
    }

    private void Synthesize()
    {
        if (Header.Level == 0) return;
        var chunkRows = Math.Max(1, 2048 / _columns - 2);
        for (var row = 0; row < Header.Rows; row += chunkRows)
        {
            var offset = row * _columns;
            var length = Math.Min(chunkRows, Header.Rows - row) * 2;
            var stride = _columns / 2;
            for (var stage = 0; stage < Header.Level; stage++, stride /= 2, length *= 2)
            {
                var history = _history[stage];
                for (var column = 0; column < stride; column++)
                {
                    var a = history[column * 2]; var b = history[column * 2 + 1];
                    for (var pair = 0; pair < length; pair += 2)
                    {
                        var at = offset + column + pair * stride;
                        var x = _block[at]; var y = _block[at + stride];
                        _block[at] = unchecked(a + 2 * b + x);
                        _block[at + stride] = unchecked(2 * x - b - y);
                        a = x; b = y;
                    }
                    history[column * 2] = stage == 0 ? unchecked((short)a) : a;
                    history[column * 2 + 1] = stage == 0 ? unchecked((short)b) : b;
                }
                if (stage == 0)
                    for (var index = 0; index < length; index++) _block[offset + index * stride] = unchecked(_block[offset + index * stride] + 1);
            }
        }
    }
}
