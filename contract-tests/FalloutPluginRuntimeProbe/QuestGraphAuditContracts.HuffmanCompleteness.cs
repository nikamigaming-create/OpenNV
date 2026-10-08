using System.Security.Cryptography;
using OpenNV.Runtime.Content;

internal static partial class QuestGraphAuditContracts
{
    private static void StrictHuffmanReaderAdmission(string directory)
    {
        var root = Path.Combine(directory, "strict-huffman-reader-inputs");
        Directory.CreateDirectory(root);
        var cases = IncompleteHuffmanCases();
        var sourceBodies = cases.Select(test => SHA256.HashData(test.Body)).ToArray();
        var helperAdmitted = new List<string>();
        foreach (var test in cases)
        {
            FalloutDeflateExtentResult? extent = null;
            try { extent = FalloutDeflateExtent.Validate(test.Body, (uint)test.Expected.Length); }
            catch (InvalidDataException) { }
            if (extent is not { } observed)
                continue;
            Require(observed.FinalBitOffset == test.FinalBits && observed.ConsumedBytes == test.Body.Length &&
                observed.DecodedBytes == test.Expected.Length && observed.Blocks == 1,
                "A helper-admitted incomplete alphabet lost its authored boundary: " + test.Name);
            helperAdmitted.Add(test.Name);
        }

        // Legal one-bit EOB and absent distance codes remain reader controls.
        var validEmpty = WrapFrame(DynamicEmptyFrame(), []);
        var badChecksumEmpty = validEmpty.ToArray();
        badChecksumEmpty[^1] ^= 1;
        var originalRecords = new List<byte[]>
        {
            Header(),
            Record("FUTR", 0x700, FalloutPluginRecord.CompressedFlag, PrefixFrame(0, validEmpty)),
            Record("FUTR", 0x701, FalloutPluginRecord.CompressedFlag, PrefixFrame(0, badChecksumEmpty)),
        };
        var malformedIds = new List<(uint Id, string Name)>();
        uint nextId = 0x702;
        foreach (var test in cases)
        {
            var framed = WrapFrame(test.Body, test.Expected);
            foreach (var badChecksum in new[] { false, true })
            {
                var payload = framed.ToArray();
                if (badChecksum)
                    payload[^1] ^= 1;
                malformedIds.Add((nextId, test.Name + (badChecksum ? "-bad-adler" : "-normal-adler")));
                originalRecords.Add(Record("FUTR", nextId++, FalloutPluginRecord.CompressedFlag,
                    PrefixFrame(test.Expected.Length, payload)));
            }
        }
        var pluginPath = Path.Combine(root, "Huffman.esm");
        File.WriteAllBytes(pluginPath, Join(originalRecords.ToArray()));
        var pluginHash = SHA256.HashData(File.ReadAllBytes(pluginPath));
        using (var records = FalloutPluginStack.Load(root, ["Huffman.esm"]))
        {
            Require(records.GetEffective(new("Huffman.esm", 0x700)).ReadData().Length == 0,
                "A legal one-bit EOB/absent-distance original record was refused.");
            Require(records.GetEffective(new("Huffman.esm", 0x701)).ReadData().Length == 0,
                "The original-record bad-Adler compatibility control was refused.");
            foreach (var (id, name) in malformedIds)
            {
                // A failed read must remain refused when the same source is read again.
                for (var attempt = 0; attempt < 2; ++attempt)
                    RejectIncompleteHuffmanRecord(records.GetEffective(new("Huffman.esm", id)), name);
            }
        }
        Require(pluginHash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(pluginPath))),
            "Incomplete Huffman original-record refusal changed source bytes.");
        Console.WriteLine("OPENNV_HUFFMAN_RECORD_READERS_PASS incompleteCodeLiteralDistanceRefused=true normalAndBadAdler=true refusalReread=true legalOneBitAndNoDistance=true badAdlerControl=accepted originalBytes=unchanged");

        foreach (var archiveDefaultCompressed in new[] { false, true })
        {
            var controlPath = Path.Combine(root, "legal-empty-default-" + archiveDefaultCompressed + ".bsa");
            WriteCompressionBsa(controlPath, PrefixFrame(0, validEmpty), archiveDefaultCompressed);
            var controlHash = SHA256.HashData(File.ReadAllBytes(controlPath));
            using (var archive = new FalloutBsaArchive(controlPath))
                Require(archive.Read("textures/payload.bin").Length == 0,
                    "A legal one-bit EOB/absent-distance BSA member was refused.");
            Require(controlHash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(controlPath))),
                "Legal Huffman BSA admission changed source bytes.");

            var checksumPath = Path.Combine(root, "legal-empty-bad-adler-default-" + archiveDefaultCompressed + ".bsa");
            WriteCompressionBsa(checksumPath, PrefixFrame(0, badChecksumEmpty), archiveDefaultCompressed);
            RequireIncompleteHuffmanBsaRefusal(checksumPath, "legal-tree-bad-adler-strict-control");

            foreach (var test in cases)
            {
                var framed = WrapFrame(test.Body, test.Expected);
                foreach (var badChecksum in new[] { false, true })
                {
                    var payload = framed.ToArray();
                    if (badChecksum)
                        payload[^1] ^= 1;
                    var name = test.Name + (badChecksum ? "-bad-adler" : "-normal-adler");
                    var path = Path.Combine(root, name + "-default-" + archiveDefaultCompressed + ".bsa");
                    WriteCompressionBsa(path, PrefixFrame(test.Expected.Length, payload), archiveDefaultCompressed);
                    RequireIncompleteHuffmanBsaRefusal(path, name);
                }
            }
        }
        for (var index = 0; index < cases.Length; ++index)
            Require(sourceBodies[index].SequenceEqual(SHA256.HashData(cases[index].Body)),
                "Huffman admission mutated authored source bits.");
        Console.WriteLine("OPENNV_HUFFMAN_BSA_READERS_PASS incompleteCodeLiteralDistanceRefused=true normalAndBadAdler=true archiveDefaultAndOverride=true refusalReread=true legalOneBitAndNoDistance=true badAdlerControl=refused originalBytes=unchanged");
        // The helper may delegate incomplete alphabets to the real inflater.
        // Its successful extent count is never decoded-byte or execution evidence.
        Console.WriteLine("OPENNV_HUFFMAN_JOINT_ADMISSION_PASS helperAdmitted=" + helperAdmitted.Count +
            " helperCases=" + string.Join(",", helperAdmitted) +
            " helperIsNotFullAlphabetOrDecodeProof=true actualReadersRefused=true runtimeReadiness=uninspected");
    }

    private static void RejectIncompleteHuffmanRecord(FalloutPluginRecord record, string name)
    {
        try { _ = record.ReadData(); }
        catch (FalloutPluginFormatException failure)
        {
            Require(failure.Message.Contains("Huffman.esm", StringComparison.Ordinal) &&
                failure.Message.Contains("FUTR", StringComparison.Ordinal) &&
                failure.Message.Contains(record.RawFormId.ToString("x8"), StringComparison.OrdinalIgnoreCase),
                "Incomplete Huffman record refusal lost original source identity: " + name);
            return;
        }
        throw new InvalidDataException("The actual original-record reader accepted an incomplete Huffman alphabet: " + name);
    }

    private static void RequireIncompleteHuffmanBsaRefusal(string path, string name)
    {
        var hash = SHA256.HashData(File.ReadAllBytes(path));
        using (var archive = new FalloutBsaArchive(path))
        {
            for (var attempt = 0; attempt < 2; ++attempt)
            {
                var refused = false;
                try { _ = archive.Read("TEXTURES/payload.bin"); }
                catch (InvalidDataException) { refused = true; }
                Require(refused, "The actual BSA reader accepted an incomplete alphabet/strict checksum refusal: " + name);
            }
        }
        Require(hash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(path))),
            "Incomplete Huffman BSA refusal changed source bytes: " + name);
    }

    private sealed record IncompleteHuffmanCase(string Name, byte[] Body, byte[] Expected, long FinalBits);

    private static IncompleteHuffmanCase[] IncompleteHuffmanCases() =>
    [
        // Final dynamic block; complete code-length alphabet {0:1,2:1};
        // literal alphabet {EOB:2}, no distance; emits the declared EOB prefix 00.
        new("literal-eob-length2", Convert.FromHexString(
            "0580010400000040000000000000000000000000000000000000000000000000000000000000000002"), [], 325),
        // Final dynamic block; incomplete code-length alphabet {0:2,1:2};
        // legal literal alphabet {EOB:1}, no distance; uses declared prefixes.
        new("code-length-alphabet-incomplete", Convert.FromHexString(
            "05C0010800000000200000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000001"), [], 588),
        // Final dynamic block; complete code-length/literal alphabets;
        // literals {A:2,B:2,EOB:2,length3:2}, distance {1:2};
        // emits A, length3/distance1, EOB, with overlapping output AAAA.
        new("distance-symbol-length2", Convert.FromHexString(
            "0D8001040000004000000000000000000C0000000000000000000000000000000000000000000000CE04"), "AAAA"u8.ToArray(), 332),
    ];
}
