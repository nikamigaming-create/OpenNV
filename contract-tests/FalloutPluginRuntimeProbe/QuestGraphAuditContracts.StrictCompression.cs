using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using OpenNV.Runtime.Content;

internal static partial class QuestGraphAuditContracts
{
    private static void StrictCompressedFraming(string directory)
    {
        var fixedEmpty = new byte[] { 3, 0 };
        var storedEmpty = new byte[] { 1, 0, 0, 255, 255 };
        var alphabet = Enumerable.Range(0, 256).Select(value => (byte)value).ToArray();
        var positives = new List<CompressionFrameCase>
        {
            new("fixed-empty", fixedEmpty, [], 1),
            new("stored-empty", storedEmpty, [], 1),
            new("nonzero-final-padding", [3, 0xfc], [], 1),
            new("stored-alignment-ignored", [0xf9, 0, 0, 255, 255], [], 1),
            new("fixed-all-literal-ranges", FixedFrame(alphabet), alphabet, 1),
            new("dynamic-no-distances-repeat17-repeat18", DynamicLiteralFrame(), "ABBA"u8.ToArray(), 1),
            new("dynamic-one-symbol-empty", DynamicEmptyFrame(), [], 1),
            new("dynamic-repeat16-full-literal-alphabet", DynamicAlphabetFrame(), alphabet, 1),
            new("dynamic-repeat16-crosses-alphabets", CrossAlphabetRepeatFrame(), [255], 1),
            new("stored-fixed-dynamic-multiple-blocks", MultipleFrame(), "ABABBA"u8.ToArray(), 3),
            new("cross-block-history", HistoryFrame(), "abcabc"u8.ToArray(), 2),
            new("overlapping-match", OverlapFrame(), "AAAA"u8.ToArray(), 1),
            new("cross-block-overlapping-match", HistoryOverlapFrame(), Enumerable.Repeat((byte)65, 259).ToArray(), 2),
        };
        var maximumHistory = Enumerable.Repeat((byte)0x5a, 32768).ToArray();
        var maximumExpected = maximumHistory.Concat(Enumerable.Repeat((byte)0x5a, 258)).ToArray();
        var maximumFrame = MaximumDistanceFrame(maximumHistory);
        positives.Add(new("maximum-window-and-match", maximumFrame, maximumExpected, 2));
        // Public framework outputs supplement the independent bit writers;
        // they do not choose which block families the authored cases cover.
        foreach (var data in new[] { Array.Empty<byte>(), alphabet, maximumExpected })
        {
            using var output = new MemoryStream();
            using (var compressor = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true)) compressor.Write(data);
            positives.Add(new("framework-generated-" + data.Length, output.ToArray()[2..^4], data, 0));
        }
        foreach (var test in positives)
        {
            var before = SHA256.HashData(test.Body);
            var extent = FalloutDeflateExtent.Validate(test.Body, (uint)test.Expected.Length);
            Require(extent.ConsumedBytes == test.Body.Length && extent.DecodedBytes == test.Expected.Length &&
                extent.FinalBitOffset <= (long)test.Body.Length * 8 && extent.FinalBitOffset > (long)(test.Body.Length - 1) * 8 &&
                (test.Blocks == 0 || extent.Blocks == test.Blocks), "Strict framing lost an independent complete boundary: " + test.Name);
            Require(before.SequenceEqual(SHA256.HashData(test.Body)), "Strict framing mutated input bits.");
        }
        RejectStrictBody(HistoryFrame(), 6, window: 2);
        RejectStrictBody(maximumFrame, (uint)maximumExpected.Length, window: 32767);
        RejectStrictBody(fixedEmpty, 1);
        var nonFinalEmpty = fixedEmpty.ToArray(); nonFinalEmpty[0] &= 0xfe;
        var negatives = new List<CompressionFrameCase>
        {
            new("nonfinal-empty", nonFinalEmpty, [], 0),
            new("stored-nonfinal-empty", [0, 0, 0, 255, 255], [], 0),
            new("reserved-block-type", [7, 0], [], 0),
            new("stored-len-nlen", [1, 1, 0, 255, 255, 65], [65], 0),
            new("stored-truncated-data", [1, 1, 0, 254, 255], [65], 0),
            new("literal-before-final-end-truncated", FixedFrame([65])[..^1], [65], 0),
            new("fixed-reserved-literal", ReservedLiteralFrame(), [], 0),
            new("fixed-reserved-distance", ReservedDistanceFrame(), "AAAA"u8.ToArray(), 0),
            new("distance-before-output", MatchBeforeOutputFrame(), "AAA"u8.ToArray(), 0),
            new("dynamic-repeat-without-previous", InvalidCodeLengthsFrame(0), [], 0),
            new("dynamic-repeat-overflow", InvalidCodeLengthsFrame(1), [], 0),
            new("dynamic-oversubscribed-tree", InvalidCodeLengthsFrame(2), [], 0),
            new("dynamic-reserved-literal-count", InvalidCodeLengthsFrame(3), [], 0),
            new("dynamic-empty-code-length-alphabet", InvalidCodeLengthsFrame(4), [], 0),
            new("dynamic-missing-end-symbol", DynamicEmptyFrame(missingEnd: true), [], 0),
            new("dynamic-unused-prefix", DynamicEmptyFrame(unusedPrefix: true), [], 0),
            new("dynamic-missing-distance", MissingDistanceFrame(), "AAAA"u8.ToArray(), 0),
        };
        foreach (var test in positives)
        {
            if ((test.Body[0] & 1) != 0)
            {
                var unfinished = test.Body.ToArray(); unfinished[0] &= 0xfe;
                negatives.Add(test with { Name = test.Name + "-nonfinal", Body = unfinished, Blocks = 0 });
            }
            foreach (var trailing in new byte[] { 0, 0x7f, 0xff })
                negatives.Add(test with { Name = test.Name + "-trailing-" + trailing, Body = Join(test.Body, [trailing]), Blocks = 0 });
            negatives.Add(test with { Name = test.Name + "-second-member", Body = Join(test.Body, fixedEmpty), Blocks = 0 });
            negatives.Add(test with { Name = test.Name + "-truncated", Body = test.Body[..^1], Blocks = 0 });
        }
        foreach (var test in negatives) RejectStrictBody(test.Body, (uint)test.Expected.Length);

        var inputs = Path.Combine(directory, "strict-compressed-inputs"); Directory.CreateDirectory(inputs);
        var path = Path.Combine(inputs, "Strict.esm");
        var originalRecords = new List<byte[]> { Header() };
        uint id = 0x600;
        foreach (var test in positives)
            originalRecords.Add(Record("FUTR", id++, FalloutPluginRecord.CompressedFlag, PrefixFrame(test.Expected.Length, WrapFrame(test.Body, test.Expected))));
        var checksumFrame = WrapFrame(fixedEmpty, []); checksumFrame[^1] ^= 1;
        var checksumId = id++;
        originalRecords.Add(Record("FUTR", checksumId, FalloutPluginRecord.CompressedFlag, PrefixFrame(0, checksumFrame)));
        var afterChecksumId = id++;
        originalRecords.Add(Record("FUTR", afterChecksumId, FalloutPluginRecord.CompressedFlag, PrefixFrame(0, Join(WrapFrame(fixedEmpty, []), [0]))));
        var malformedIds = new List<uint>();
        foreach (var test in negatives)
        {
            malformedIds.Add(id);
            originalRecords.Add(Record("FUTR", id++, FalloutPluginRecord.CompressedFlag, PrefixFrame(test.Expected.Length, WrapFrame(test.Body, test.Expected))));
        }
        // The checksum fallback must not erase a structural trailing refusal.
        var badBoth = WrapFrame(Join(fixedEmpty, [0]), []); badBoth[^1] ^= 1;
        malformedIds.Add(id);
        originalRecords.Add(Record("FUTR", id++, FalloutPluginRecord.CompressedFlag, PrefixFrame(0, badBoth)));
        // Original record framing cannot concatenate an additional zlib member.
        malformedIds.Add(id);
        originalRecords.Add(Record("FUTR", id, FalloutPluginRecord.CompressedFlag,
            PrefixFrame(0, Join(WrapFrame(fixedEmpty, []), WrapFrame(fixedEmpty, [])))));
        File.WriteAllBytes(path, Join(originalRecords.ToArray()));
        var sourceHash = SHA256.HashData(File.ReadAllBytes(path));
        using (var records = FalloutPluginStack.Load(inputs, ["Strict.esm"]))
        {
            id = 0x600;
            foreach (var test in positives)
                Require(records.GetEffective(new("Strict.esm", id++)).ReadData().SequenceEqual(test.Expected),
                    "The complete original reader refused/misdecoded an independent framing positive: " + test.Name);
            Require(records.GetEffective(new("Strict.esm", checksumId)).ReadData().Length == 0,
                "Strict extent validation removed the intentional bad-Adler raw-DEFLATE compatibility path.");
            Reject(() => records.GetEffective(new("Strict.esm", afterChecksumId)).ReadData(), "invalid");
            foreach (var bad in malformedIds) Reject(() => records.GetEffective(new("Strict.esm", bad)).ReadData(), "invalid");
        }
        Require(sourceHash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(path))), "Compressed original-record admission changed source bytes.");
        Console.WriteLine("OPENNV_STRICT_COMPRESSED_RECORD_FRAMING_PASS fixed=true stored=true dynamic=true allRepeatCodes=true multiBlock=true crossBlockHistory=true overlappingMatches=true maximumWindow=true exactFinalExtent=true nonfinalTruncatedTrailingRefused=true genuineEmpty=true badAdlerCompatibility=true sourceBytes=unchanged runtimeReadiness=uninspected");
    }

    private sealed record CompressionFrameCase(string Name, byte[] Body, byte[] Expected, int Blocks);

    private static byte[] PrefixFrame(int size, byte[] framed)
    {
        var result = new byte[sizeof(uint) + framed.Length]; BinaryPrimitives.WriteUInt32LittleEndian(result, (uint)size);
        framed.CopyTo(result, sizeof(uint)); return result;
    }

    private static byte[] WrapFrame(byte[] body, byte[] expected)
    {
        uint first = 1, second = 0;
        foreach (var value in expected) { first = (first + value) % 65521; second = (second + first) % 65521; }
        var adler = new byte[4]; BinaryPrimitives.WriteUInt32BigEndian(adler, (second << 16) | first);
        return Join([0x78, 0x9c], body, adler);
    }

    private static void RejectStrictBody(byte[] body, uint expected, int window = 32768)
    {
        try { _ = FalloutDeflateExtent.Validate(body, expected, window); }
        catch (InvalidDataException) { return; }
        throw new InvalidOperationException("An incomplete/nonfinal/trailing/invalid DEFLATE body was accepted.");
    }

    private static byte[] FixedFrame(byte[] values)
    {
        var writer = new CompressionBits(); FixedBlock(writer, true, values); return writer.ToArray();
    }

    private static void FixedBlock(CompressionBits writer, bool final, byte[] values)
    {
        writer.Bits(final ? 1 : 0, 1); writer.Bits(1, 2);
        foreach (var value in values) FixedSymbol(writer, value);
        FixedSymbol(writer, 256);
    }

    private static void FixedSymbol(CompressionBits writer, int value)
    {
        if (value <= 143) writer.Code(value + 0x30, 8);
        else if (value <= 255) writer.Code(value - 144 + 0x190, 9);
        else if (value <= 279) writer.Code(value - 256, 7);
        else writer.Code(value - 280 + 0xc0, 8);
    }

    private static void StoredBlock(CompressionBits writer, bool final, byte[] values)
    {
        writer.Bits(final ? 1 : 0, 1); writer.Bits(0, 2); writer.Align();
        writer.Bits(values.Length, 16); writer.Bits(values.Length ^ 0xffff, 16);
        foreach (var value in values) writer.Bits(value, 8);
    }

    private static byte[] MultipleFrame()
    {
        var writer = new CompressionBits(); StoredBlock(writer, false, [65]); FixedBlock(writer, false, [66]);
        DynamicLiterals(writer, true); return writer.ToArray();
    }

    private static byte[] HistoryFrame()
    {
        var writer = new CompressionBits(); StoredBlock(writer, false, "abc"u8.ToArray());
        writer.Bits(1, 1); writer.Bits(1, 2); FixedSymbol(writer, 257); writer.Code(2, 5); FixedSymbol(writer, 256);
        return writer.ToArray();
    }

    private static byte[] OverlapFrame()
    {
        var writer = new CompressionBits(); writer.Bits(1, 1); writer.Bits(1, 2); FixedSymbol(writer, 65);
        FixedSymbol(writer, 257); writer.Code(0, 5); FixedSymbol(writer, 256); return writer.ToArray();
    }

    private static byte[] HistoryOverlapFrame()
    {
        var writer = new CompressionBits(); StoredBlock(writer, false, [65]);
        writer.Bits(1, 1); writer.Bits(1, 2); FixedSymbol(writer, 285); writer.Code(0, 5);
        FixedSymbol(writer, 256); return writer.ToArray();
    }

    private static byte[] MaximumDistanceFrame(byte[] history)
    {
        var writer = new CompressionBits(); StoredBlock(writer, false, history);
        writer.Bits(1, 1); writer.Bits(1, 2); FixedSymbol(writer, 285); writer.Code(29, 5); writer.Bits(8191, 13);
        FixedSymbol(writer, 256); return writer.ToArray();
    }

    private static byte[] ReservedLiteralFrame()
    {
        var writer = new CompressionBits(); writer.Bits(1, 1); writer.Bits(1, 2); FixedSymbol(writer, 286);
        FixedSymbol(writer, 256); return writer.ToArray();
    }

    private static byte[] ReservedDistanceFrame()
    {
        var writer = new CompressionBits(); writer.Bits(1, 1); writer.Bits(1, 2); FixedSymbol(writer, 65);
        FixedSymbol(writer, 257); writer.Code(30, 5); FixedSymbol(writer, 256); return writer.ToArray();
    }

    private static byte[] MatchBeforeOutputFrame()
    {
        var writer = new CompressionBits(); writer.Bits(1, 1); writer.Bits(1, 2); FixedSymbol(writer, 257);
        writer.Code(0, 5); FixedSymbol(writer, 256); return writer.ToArray();
    }

    private static byte[] DynamicLiteralFrame()
    {
        var writer = new CompressionBits(); DynamicLiterals(writer, true); return writer.ToArray();
    }

    private static void DynamicLiterals(CompressionBits writer, bool final)
    {
        writer.Bits(final ? 1 : 0, 1); writer.Bits(2, 2); writer.Bits(0, 5); writer.Bits(0, 5); writer.Bits(14, 4);
        // Code-length alphabet: 0=00, 1=01, 2=10, 17=110, 18=111.
        foreach (var length in new[] { 0, 3, 3, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 2, 0, 2 }) writer.Bits(length, 3);
        writer.Code(7, 3); writer.Bits(50, 7); // 61 zero lengths.
        writer.Code(6, 3); writer.Bits(1, 3); // Four zero lengths.
        writer.Code(2, 2); writer.Code(2, 2); // A and B have length two.
        writer.Code(7, 3); writer.Bits(127, 7); writer.Code(7, 3); writer.Bits(40, 7); // 189 zeros.
        writer.Code(1, 2); writer.Code(0, 2); // EOB length one; distance absent.
        writer.Code(2, 2); writer.Code(3, 2); writer.Code(3, 2); writer.Code(2, 2); writer.Code(0, 1);
    }

    private static byte[] DynamicEmptyFrame(bool missingEnd = false, bool unusedPrefix = false)
    {
        var writer = new CompressionBits(); writer.Bits(1, 1); writer.Bits(2, 2);
        writer.Bits(0, 5); writer.Bits(0, 5); writer.Bits(14, 4);
        foreach (var length in new[] { 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1 }) writer.Bits(length, 3);
        for (var symbol = 0; symbol < 256; ++symbol) writer.Code(0, 1);
        writer.Code(missingEnd ? 0 : 1, 1); writer.Code(0, 1);
        writer.Code(unusedPrefix ? 1 : 0, 1); return writer.ToArray();
    }

    private static byte[] DynamicAlphabetFrame()
    {
        var writer = new CompressionBits(); writer.Bits(1, 1); writer.Bits(2, 2);
        writer.Bits(0, 5); writer.Bits(0, 5); writer.Bits(14, 4);
        // Eight length-three codes for symbols 0,1,2,3,9,16,17,18.
        foreach (var length in new[] { 3, 3, 3, 3, 0, 0, 3, 0, 0, 0, 0, 0, 0, 3, 0, 3, 0, 3 }) writer.Bits(length, 3);
        writer.Code(4, 3); // Literal zero starts 256 length-nine symbols.
        for (var repeat = 0; repeat < 42; ++repeat) { writer.Code(5, 3); writer.Bits(3, 2); }
        writer.Code(5, 3); writer.Bits(0, 2); // Remaining three symbols.
        writer.Code(1, 3); writer.Code(1, 3); // EOB and a one-bit distance.
        for (var literal = 0; literal < 256; ++literal) writer.Code(256 + literal, 9);
        writer.Code(0, 1); return writer.ToArray();
    }

    private static byte[] InvalidCodeLengthsFrame(int kind)
    {
        var writer = new CompressionBits(); writer.Bits(1, 1); writer.Bits(2, 2);
        writer.Bits(kind == 3 ? 31 : 0, 5); writer.Bits(0, 5); writer.Bits(0, 4);
        if (kind == 0)
        {
            foreach (var length in new[] { 1, 0, 0, 1 }) writer.Bits(length, 3);
            writer.Code(1, 1); writer.Bits(0, 2);
        }
        else if (kind == 1)
        {
            foreach (var length in new[] { 0, 0, 1, 1 }) writer.Bits(length, 3);
            writer.Code(1, 1); writer.Bits(127, 7); writer.Code(1, 1); writer.Bits(127, 7);
        }
        else foreach (var length in kind == 4 ? new[] { 0, 0, 0, 0 } : new[] { 1, 1, 1, 0 }) writer.Bits(length, 3);
        return writer.ToArray();
    }

    private static byte[] CrossAlphabetRepeatFrame()
    {
        var writer = new CompressionBits(); writer.Bits(1, 1); writer.Bits(2, 2);
        writer.Bits(0, 5); writer.Bits(1, 5); writer.Bits(14, 4);
        // 0=0, 1=10, 16=11. Literal 255 and EOB each have a one-bit
        // code; both distances also have one-bit codes. The repeat starts
        // before EOB and legitimately crosses into the distance alphabet.
        foreach (var length in new[] { 2, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 2 }) writer.Bits(length, 3);
        for (var symbol = 0; symbol < 255; ++symbol) writer.Code(0, 1);
        writer.Code(2, 2); writer.Code(3, 2); writer.Bits(0, 2);
        writer.Code(0, 1); writer.Code(1, 1); return writer.ToArray();
    }

    private static byte[] MissingDistanceFrame()
    {
        var writer = new CompressionBits(); writer.Bits(1, 1); writer.Bits(2, 2);
        writer.Bits(1, 5); writer.Bits(0, 5); writer.Bits(14, 4);
        foreach (var length in new[] { 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 2, 0, 2 }) writer.Bits(length, 3);
        for (var symbol = 0; symbol < 65; ++symbol) writer.Code(0, 1);
        writer.Code(2, 2);
        for (var symbol = 66; symbol < 256; ++symbol) writer.Code(0, 1);
        writer.Code(3, 2); writer.Code(3, 2); writer.Code(0, 1);
        writer.Code(0, 1); writer.Code(3, 2); return writer.ToArray();
    }

    private sealed class CompressionBits
    {
        private readonly List<byte> _bytes = [];
        private int _bits;
        internal void Bits(int value, int count)
        {
            for (var bit = 0; bit < count; ++bit)
            {
                if ((_bits & 7) == 0) _bytes.Add(0);
                if (((value >> bit) & 1) != 0) _bytes[^1] |= (byte)(1 << (_bits & 7));
                _bits++;
            }
        }
        internal void Code(int value, int width)
        {
            for (var bit = width - 1; bit >= 0; --bit) Bits((value >> bit) & 1, 1);
        }
        internal void Align() { while ((_bits & 7) != 0) Bits(0, 1); }
        internal byte[] ToArray() => _bytes.ToArray();
    }
}
