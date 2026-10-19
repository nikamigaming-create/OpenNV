using System.Buffers.Binary;
using OpenNV.Runtime.Campaigns.Classic;

internal static class ClassicAcmProbe
{
    internal static void Run()
    {
        Check(0, "", [0, 0, 0]);
        Check(3, "000 100 111", [-4, -3, 3]);
        Check(16, new string('0', 16) + new string('0', 15) + "1" + new string('1', 16), [-32768, 0, 32767]);
        Check(17, "0 10 110 111", [0, 0, 0, -1, 1]);
        Check(18, "0 10 11", [0, -1, 1]);
        Check(19, "10101", [-1, 0, 1]);
        Check(20, "0 10 1100 1110 1101 1111", [0, 0, 0, -2, -1, 1, 2]);
        Check(21, "0 100 110 101 111", [0, -2, -1, 1, 2]);
        Check(22, "1110110", [-2, -1, 0]); // 55 = 0 + 1*5 + 2*25.
        Check(23, "0 10 1100 1101 11100 11110 11101 11111", [0, 0, 0, -1, 1, -3, -2, 2, 3]);
        Check(24, "0 100 101 1100 1110 1101 1111", [0, -1, 1, -3, -2, 2, 3]);
        Check(26, "0 10 11000 11100 11010 11110 11001 11101 11011 11111", [0, 0, 0, -4, -3, -2, -1, 1, 2, 3, 4]);
        Check(27, "0 1000 1100 1010 1110 1001 1101 1011 1111", [0, -4, -3, -2, -1, 1, 2, 3, 4]);
        Check(29, "0110111", [3, 5]); // 118 = 8 + 10*11.
        Check(19, "10101", [-1]); // Packed groups consume their codeword even at a partial column end.
        foreach (var selector in new[] { 1, 2, 25, 28, 30, 31 }) Reject(Source(0, 1, 1, put => { put(1, 4); put(1, 16); put(selector, 5); }));
        Reject(Source(0, 3, 3, put => { put(2, 4); put(1, 16); put(19, 5); put(31, 5); }));
        var badHeader = new byte[14]; Reject(badHeader);
        var zeroRows = Source(0, 0, 1, _ => { }); Reject(zeroRows);
        var empty = new ClassicAcmDecoder(Source(0, 1, 0, _ => { }));
        if (empty.Read(new short[1]) != 0) throw new Exception("Empty ACM produced samples.");
        var padded = new ClassicAcmDecoder(Source(0, 3, 3, _ => { }));
        var silence = new short[3];
        if (padded.Read(silence) != 3 || silence.Any(value => value != 0) || padded.ZeroPaddingBits != 25)
            throw new Exception("Native ACM zero EOF extension changed.");
        var history = Source(1, 2, 8, put =>
        {
            for (var block = 0; block < 2; block++)
            {
                put(3, 4); put(1, 16);
                put(4, 5); put(9, 4); put(11, 4);
                put(4, 5); put(10, 4); put(12, 4);
            }
        });
        Verify(history, [1, 0, 4, 0, 6, -2, 4, 0]);
        var twoStages = Source(2, 1, 4, put =>
        {
            put(3, 4); put(1, 16);
            for (var value = 1; value <= 4; value++) { put(4, 5); put(value + 8, 4); }
        });
        Verify(twoStages, [0, 0, 1, -1]);
        var oddStereo = Source(0, 3, 3, put => { put(2, 4); put(1, 16); put(19, 5); put(21, 5); });
        BinaryPrimitives.WriteUInt16LittleEndian(oddStereo.AsSpan(8), 2);
        Verify(oddStereo, [-1, 0, 1]);
        Console.WriteLine("OPENNV_CLASSIC_ACM_PASS selectors=true invalidRejected=true retainedFault=true streaming=true history=true synthesis=true eofZero=true fixture=true playback=false");
    }

    private static void Check(int selector, string bits, short[] expected)
    {
        var source = Source(0, expected.Length, expected.Length, put =>
        {
            put(15, 4); put(1, 16); put(selector, 5);
            foreach (var bit in bits.Where(value => value != ' ')) put(bit - '0', 1);
        });
        Verify(source, expected);
    }

    private static void Verify(byte[] source, short[] expected)
    {
        foreach (var chunk in new[] { 1, 3, expected.Length })
        {
            var decoder = new ClassicAcmDecoder(source); var result = new List<short>();
            var buffer = new short[chunk];
            for (var read = decoder.Read(buffer); read != 0; read = decoder.Read(buffer)) result.AddRange(buffer.Take(read));
            if (!result.SequenceEqual(expected)) throw new Exception($"ACM PCM differs: {string.Join(',', result)} != {string.Join(',', expected)}");
        }
    }

    private static void Reject(byte[] source)
    {
        ClassicAcmDecoder decoder;
        try { decoder = new(source); }
        catch (InvalidDataException) { return; }
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try { decoder.Read(new short[32]); throw new Exception("Invalid ACM was accepted or its fault was lost."); }
            catch (InvalidDataException) { }
        }
    }

    private static byte[] Source(int level, int rows, int samples, Action<Action<int, int>> payload)
    {
        var bytes = new List<byte>(new byte[14]); var position = 112;
        payload((value, count) =>
        {
            for (var bit = 0; bit < count; bit++, position++)
            {
                if (position / 8 == bytes.Count) bytes.Add(0);
                bytes[position / 8] |= (byte)(((value >> bit) & 1) << (position & 7));
            }
        });
        var result = bytes.ToArray(); result[0] = 0x97; result[1] = 0x28; result[2] = 3; result[3] = 1;
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(4), samples);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(8), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(10), 22050);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(12), (ushort)(rows * 16 + level));
        return result;
    }
}
