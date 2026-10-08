using System.Security.Cryptography;
using System.Text;
using OpenNV.Runtime.Content;

internal static partial class QuestGraphAuditContracts
{
    private static void StrictBsaCompressedFraming(string directory)
    {
        var root = Path.Combine(directory, "strict-bsa-inputs"); Directory.CreateDirectory(root);
        var positives = new[]
        {
            new CompressionFrameCase("fixed-empty", [3, 0], [], 1),
            new CompressionFrameCase("stored-empty", [1, 0, 0, 255, 255], [], 1),
            new CompressionFrameCase("dynamic-empty", DynamicEmptyFrame(), [], 1),
            new CompressionFrameCase("fixed-literals", FixedFrame("record"u8.ToArray()), "record"u8.ToArray(), 1),
            new CompressionFrameCase("dynamic-literals", DynamicLiteralFrame(), "ABBA"u8.ToArray(), 1),
            new CompressionFrameCase("multi-block", MultipleFrame(), "ABABBA"u8.ToArray(), 3),
        };
        foreach (var test in positives)
        {
            var framed = WrapFrame(test.Body, test.Expected);
            var result = FalloutDeflateExtent.ValidateZlibFrame(framed, (uint)test.Expected.Length);
            Require(result.ConsumedBytes == test.Body.Length && result.DecodedBytes == test.Expected.Length && result.Blocks == test.Blocks,
                "Reusable zlib envelope validation lost BSA boundary facts.");
            foreach (var archiveDefaultCompressed in new[] { false, true })
            {
                var path = Path.Combine(root, test.Name + "-default-" + archiveDefaultCompressed + ".bsa");
                WriteCompressionBsa(path, PrefixFrame(test.Expected.Length, framed), archiveDefaultCompressed);
                var hash = SHA256.HashData(File.ReadAllBytes(path));
                using (var archive = new FalloutBsaArchive(path))
                    Require(archive.StoredExtent("textures/payload.bin").Compressed &&
                        archive.Read("TEXTURES/payload.bin").SequenceEqual(test.Expected),
                        "Actual compressed BSA reader refused/misdecoded a genuine framed member: " + test.Name);
                Require(hash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(path))), "Compressed BSA positive mutated original bytes.");
            }
        }
        var emptyFrame = WrapFrame([3, 0], []);
        // The original absent-envelope/zero declaration is now an explicit
        // source-proven archive encoding, independent of framed empty streams.
        foreach (var archiveDefaultCompressed in new[] { false, true })
        {
            var path = Path.Combine(root, "declared-empty-default-" + archiveDefaultCompressed + ".bsa");
            WriteCompressionBsa(path, PrefixFrame(0, []), archiveDefaultCompressed);
            var hash = SHA256.HashData(File.ReadAllBytes(path));
            using (var archive = new FalloutBsaArchive(path))
            {
                var read = archive.ReadWithEncoding("textures/payload.bin");
                Require(read.Data.Length == 0 && read.Encoding == FalloutBsaMemberEncoding.PrefixOnlyEmpty,
                    "The retained absent-zero source case lost its typed archive empty encoding.");
            }
            Require(hash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(path))), "Declared-empty BSA admission mutated source bytes.");
        }
        var nonFinal = emptyFrame.ToArray(); nonFinal[2] &= 0xfe;
        var badAdler = emptyFrame.ToArray(); badAdler[^1] ^= 1;
        var invalidWindow = emptyFrame.ToArray(); invalidWindow[0] = 0x88;
        invalidWindow[1] = (byte)((31 - (invalidWindow[0] << 8) % 31) % 31);
        var negatives = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["absent-nonempty"] = PrefixFrame(1, []),
            ["missing-size-prefix"] = [],
            ["empty-nonheader-payload"] = PrefixFrame(0, [0x7f]),
            ["header-only"] = PrefixFrame(0, emptyFrame[..2]),
            ["missing-body"] = PrefixFrame(0, Join(emptyFrame[..2], emptyFrame[^4..])),
            ["nonfinal-empty"] = PrefixFrame(0, nonFinal),
            ["truncated-trailer"] = PrefixFrame(0, emptyFrame[..^1]),
            ["invalid-window"] = PrefixFrame(0, invalidWindow),
            ["trailing-before-adler"] = PrefixFrame(0, WrapFrame([3, 0, 0], [])),
            ["trailing-after-adler"] = PrefixFrame(0, Join(emptyFrame, [0])),
            ["concatenated-members"] = PrefixFrame(0, Join(emptyFrame, emptyFrame)),
            ["bad-adler-strict"] = PrefixFrame(0, badAdler),
            ["declared-output-mismatch"] = PrefixFrame(1, emptyFrame),
        };
        foreach (var (name, payload) in negatives)
        {
            var path = Path.Combine(root, name + ".bsa"); WriteCompressionBsa(path, payload, archiveDefaultCompressed: false);
            var hash = SHA256.HashData(File.ReadAllBytes(path));
            using (var archive = new FalloutBsaArchive(path))
            {
                try { _ = archive.Read("textures/payload.bin"); }
                catch (Exception failure) when (failure is InvalidDataException or IOException)
                {
                    Require(hash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(path))), "Compressed BSA refusal mutated original bytes.");
                    continue;
                }
                throw new InvalidDataException("Actual BSA reader accepted a malformed framed member: " + name);
            }
        }
        Console.WriteLine("OPENNV_STRICT_COMPRESSED_BSA_FRAMING_PASS genuineEmpty=true declaredEmptyCase=explicitly-migrated fixedStoredDynamic=true multipleBlocks=true archiveCompressionDefaultAndOverride=true malformedNonfinalTruncatedTrailingRefused=true badAdlerPolicy=strict originalBytes=unchanged storedByteEvidenceIsNotDecodeProof=true");
    }

    private static void WriteCompressionBsa(string path, byte[] content, bool archiveDefaultCompressed,
        bool embeddedNames = false, bool memberCompressed = true)
    {
        // Reuse the existing authored 104 single-member directory layout.
        // The per-member bit toggles the archive's compression default.
        var folder = Encoding.ASCII.GetBytes("textures\0");
        var name = Encoding.ASCII.GetBytes("payload.bin\0");
        var payload = content;
        if (embeddedNames)
        {
            var embedded = Encoding.UTF8.GetBytes("textures/payload.bin");
            payload = Join([(byte)embedded.Length], embedded, content);
        }
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write(0x00415342u); writer.Write(104u); writer.Write(36u);
        writer.Write((archiveDefaultCompressed ? 7u : 3u) | (embeddedNames ? 0x100u : 0u));
        writer.Write(1u); writer.Write(1u); writer.Write((uint)folder.Length); writer.Write((uint)name.Length); writer.Write(0u);
        writer.Write(0ul); writer.Write(1u); writer.Write((uint)(52 + name.Length));
        writer.Write((byte)folder.Length); writer.Write(folder);
        writer.Write(0ul); writer.Write((uint)payload.Length | (archiveDefaultCompressed != memberCompressed ? 0x40000000u : 0u));
        writer.Write((uint)(52 + 1 + folder.Length + 16 + name.Length));
        writer.Write(name); writer.Write(payload);
    }
}
