using System.Globalization;
using System.Security.Cryptography;
using OpenNV.Runtime.Content;

internal static partial class QuestGraphAuditContracts
{
    private static void SourceBsaEncodingAdmission(string directory)
    {
        var root = Path.Combine(directory, "source-bsa-encoding-inputs");
        Directory.CreateDirectory(root);
        var normal = "authored archive body"u8.ToArray();
        var fixedBody = FixedFrame(normal);
        var storedWriter = new CompressionBits();
        StoredBlock(storedWriter, true, normal);
        var storedBody = storedWriter.ToArray();
        var cases = new[]
        {
            new BsaEncodingCase("declared-empty", PrefixFrame(0, []), [], FalloutBsaMemberEncoding.PrefixOnlyEmpty),
            new BsaEncodingCase("framed-empty", PrefixFrame(0, WrapFrame([3, 0], [])), [], FalloutBsaMemberEncoding.ZlibFramed),
            new BsaEncodingCase("header-fixed-empty", PrefixFrame(0, Join([0x78, 0x9c], [3, 0])), [], FalloutBsaMemberEncoding.ZlibHeaderDeflate),
            new BsaEncodingCase("header-fixed", PrefixFrame(normal.Length, Join([0x78, 0x9c], fixedBody)), normal, FalloutBsaMemberEncoding.ZlibHeaderDeflate),
            new BsaEncodingCase("header-stored", PrefixFrame(normal.Length, Join([0x78, 0x9c], storedBody)), normal, FalloutBsaMemberEncoding.ZlibHeaderDeflate),
            new BsaEncodingCase("header-dynamic", PrefixFrame(4, Join([0x78, 0x9c], DynamicLiteralFrame())), "ABBA"u8.ToArray(), FalloutBsaMemberEncoding.ZlibHeaderDeflate),
            new BsaEncodingCase("header-multiple", PrefixFrame(6, Join([0x78, 0x9c], MultipleFrame())), "ABABBA"u8.ToArray(), FalloutBsaMemberEncoding.ZlibHeaderDeflate),
            new BsaEncodingCase("small-window-header-fixed", PrefixFrame(normal.Length, Join([0x08, 0x1d], fixedBody)), normal, FalloutBsaMemberEncoding.ZlibHeaderDeflate),
            new BsaEncodingCase("uncompressed", normal, normal, FalloutBsaMemberEncoding.Stored),
            new BsaEncodingCase("uncompressed-empty", [], [], FalloutBsaMemberEncoding.Stored),
        };
        foreach (var test in cases)
        {
            foreach (var archiveDefaultCompressed in new[] { false, true })
            foreach (var embeddedNames in new[] { false, true })
            {
                var path = Path.Combine(root, test.Name + "-" + archiveDefaultCompressed + "-" + embeddedNames + ".bsa");
                WriteCompressionBsa(path, test.Stored, archiveDefaultCompressed, embeddedNames,
                    memberCompressed: test.Encoding != FalloutBsaMemberEncoding.Stored);
                var hash = SHA256.HashData(File.ReadAllBytes(path));
                using (var archive = new FalloutBsaArchive(path))
                {
                    var read = archive.ReadWithEncoding("TEXTURES/payload.bin");
                    Require(read.Encoding == test.Encoding && read.Data.SequenceEqual(test.Expected) &&
                        archive.Read("textures/payload.bin").SequenceEqual(test.Expected),
                        "Actual BSA encoding/wrapper/bytes changed: " + test.Name);
                }
                Require(hash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(path))),
                    "Actual BSA encoding admission changed source bytes: " + test.Name);
            }
        }

        var empty = WrapFrame([3, 0], []);
        var badAdler = empty.ToArray(); badAdler[^1] ^= 1;
        var badAdlerNormal = WrapFrame(fixedBody, normal); badAdlerNormal[^1] ^= 1;
        var headerBody = Join([0x78, 0x9c], fixedBody);
        var windowHistory = Enumerable.Repeat((byte)0x5a, 32768).ToArray();
        var negatives = new List<(string Name, byte[] Stored)>
        {
            ("missing-prefix", []),
            ("short-prefix", [0, 0, 0]),
            ("absent-nonempty", PrefixFrame(1, [])),
            ("declared-empty-extra-nonheader", PrefixFrame(0, [0])),
            ("header-with-no-body", PrefixFrame(0, [0x78, 0x9c])),
            ("header-nonfinal-empty", PrefixFrame(0, [0x78, 0x9c, 2, 0])),
            ("header-trailing-empty", PrefixFrame(0, [0x78, 0x9c, 3, 0, 0])),
            ("header-concatenated-empty", PrefixFrame(0, [0x78, 0x9c, 3, 0, 3, 0])),
            ("framed-bad-adler", PrefixFrame(0, badAdler)),
            ("framed-nonempty-bad-adler", PrefixFrame(normal.Length, badAdlerNormal)),
            ("framed-trailing", PrefixFrame(0, Join(empty, [0x7f]))),
            ("framed-concatenated", PrefixFrame(0, Join(empty, empty))),
            ("header-truncated-body", PrefixFrame(normal.Length, headerBody[..^1])),
            ("header-size-too-small", PrefixFrame(normal.Length - 1, headerBody)),
            ("header-size-too-large", PrefixFrame(normal.Length + 1, headerBody)),
            ("header-invalid-fcheck", PrefixFrame(0, [0x78, 0x9d, 3, 0])),
            ("header-dictionary", PrefixFrame(0, [0x78, 0x20, 3, 0])),
            ("header-match-beyond-declared-window", PrefixFrame(33026, Join([0x08, 0x1d], MaximumDistanceFrame(windowHistory)))),
        };
        for (var partialChecksumBytes = 1; partialChecksumBytes < 4; ++partialChecksumBytes)
            negatives.Add(("header-partial-adler-" + partialChecksumBytes,
                PrefixFrame(0, empty[..(empty.Length - 4 + partialChecksumBytes)])));
        foreach (var test in IncompleteHuffmanCases())
            negatives.Add(("header-incomplete-" + test.Name, PrefixFrame(test.Expected.Length, Join([0x78, 0x9c], test.Body))));
        foreach (var (name, stored) in negatives)
        {
            foreach (var archiveDefaultCompressed in new[] { false, true })
            foreach (var embeddedNames in new[] { false, true })
            {
                var path = Path.Combine(root, name + "-" + archiveDefaultCompressed + "-" + embeddedNames + ".bsa");
                WriteCompressionBsa(path, stored, archiveDefaultCompressed, embeddedNames);
                var hash = SHA256.HashData(File.ReadAllBytes(path));
                using (var archive = new FalloutBsaArchive(path))
                {
                    for (var attempt = 0; attempt < 2; ++attempt)
                    {
                        var refused = false;
                        try { _ = archive.ReadWithEncoding("textures/payload.bin"); }
                        catch (InvalidDataException) { refused = true; }
                        Require(refused, "A malformed archive encoding acquired bytes/typed success: " + name);
                    }
                }
                Require(hash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(path))),
                    "Malformed BSA encoding refusal changed source bytes: " + name);
            }
        }
        TypedSourcePayloadEncodingCache();
        Console.WriteLine("OPENNV_BSA_SOURCE_ENCODINGS_PASS declaredEmpty=true exactHeaderBody=true framedChecksum=strict rawAlphabetOwner=actual-inflater stored=true embeddedNames=true compressionDefaultAndOverride=true malformedBodiesRefused=true sourceBytes=unchanged runtimeReadiness=uninspected");
    }

    private static void TypedSourcePayloadEncodingCache()
    {
        var cache = new RuntimeLiveContentSource.SourcePayloadCache(4);
        var emptyReads = 0;
        RuntimeLiveContentSource.SourcePayload Empty(string _)
        {
            ++emptyReads;
            return new([], FalloutBsaMemberEncoding.PrefixOnlyEmpty);
        }
        Require(cache.GetOrAddWithEncoding("empty", Empty).ArchiveEncoding == FalloutBsaMemberEncoding.PrefixOnlyEmpty &&
            cache.GetOrAddWithEncoding("empty", Empty).ArchiveEncoding == FalloutBsaMemberEncoding.PrefixOnlyEmpty && emptyReads == 1,
            "A zero-byte successful source-cache hit lost its actual encoding or reread the source.");
        var failedReads = 0;
        for (var attempt = 0; attempt < 2; ++attempt)
        {
            var refused = false;
            try
            {
                _ = cache.GetOrAddWithEncoding("failed", _ =>
                {
                    ++failedReads;
                    throw new InvalidDataException("authored original source refusal");
                });
            }
            catch (InvalidDataException) { refused = true; }
            Require(refused, "A failed typed source read became a cached successful payload.");
        }
        Require(failedReads == 2, "Typed payload caching reused a failed read.");
        _ = cache.GetOrAddWithEncoding("body", _ => new([1, 2], FalloutBsaMemberEncoding.ZlibHeaderDeflate));
        _ = cache.GetOrAddWithEncoding("replacement", _ => new([3, 4, 5, 6], FalloutBsaMemberEncoding.Stored));
        Require(cache.Bytes == 4 && cache.GetOrAddWithEncoding("empty", Empty).ArchiveEncoding == FalloutBsaMemberEncoding.PrefixOnlyEmpty &&
            emptyReads == 2, "Budget eviction lost the actual encoding owner on a genuine zero-byte reread.");
        var legacy = cache.GetOrAdd("legacy", _ => [7, 8, 9, 10]);
        Require(legacy.SequenceEqual(new byte[] { 7, 8, 9, 10 }) &&
            cache.GetOrAddWithEncoding("legacy", _ => throw new InvalidOperationException("Legacy hit must not reread")).ArchiveEncoding is null,
            "The established byte-array cache API invented archive encoding or lost its retained payload.");
        cache.Clear();
        Require(cache.Bytes == 0, "Typed source payload retirement retained bytes.");
    }

    // The CLI admits arbitrary explicit archive/member/encoding/length/digest
    // tuples; product readers have no game, path, name or success exception.
    internal static void RunOwnedBsaEncodingAdmission(IReadOnlyList<string> arguments)
    {
        if (arguments.Count == 0 || arguments.Count % 5 != 0)
            throw new ArgumentException("Owned BSA encoding tuples require archive, member, encoding, byte count and decoded SHA256.");
        var originalHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < arguments.Count; index += 5)
        {
            var path = Path.GetFullPath(arguments[index]);
            var logical = arguments[index + 1];
            if (!Enum.TryParse<FalloutBsaMemberEncoding>(arguments[index + 2], ignoreCase: false, out var expectedEncoding) ||
                !Enum.IsDefined(expectedEncoding) ||
                !int.TryParse(arguments[index + 3], NumberStyles.None, CultureInfo.InvariantCulture, out var expectedBytes) || expectedBytes < 0)
                throw new ArgumentException("Owned BSA tuple has an invalid encoding/byte-count declaration.");
            var expectedHash = arguments[index + 4];
            if (expectedHash.Length != 64 || expectedHash.Any(value => !Uri.IsHexDigit(value)))
                throw new ArgumentException("Owned BSA tuple requires a complete decoded SHA256.");
            if (!originalHashes.ContainsKey(path))
                originalHashes.Add(path, HashOwnedBsa(path));
            using (var archive = new FalloutBsaArchive(path))
            {
                var read = archive.ReadWithEncoding(logical);
                var hash = Convert.ToHexString(SHA256.HashData(read.Data)).ToLowerInvariant();
                Require(read.Encoding == expectedEncoding && read.Data.Length == expectedBytes &&
                    hash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase),
                    "Owned BSA tuple lost its exact encoding/declared bytes/digest: " + logical);
                Console.WriteLine("OPENNV_OWNED_BSA_MEMBER_ENCODING_PASS source=" + path + "::" + logical +
                    " encoding=" + read.Encoding + " bytes=" + read.Data.Length + " sha256=" + hash +
                    " containerSha256=" + originalHashes[path]);
            }
        }
        foreach (var (path, hash) in originalHashes)
            Require(HashOwnedBsa(path) == hash, "Owned BSA encoding read changed original source bytes: " + path);
        Console.WriteLine("OPENNV_OWNED_BSA_ENCODING_PASS members=" + arguments.Count / 5 +
            " originalContainers=unchanged execution=uninspected native=uninspected parity=uninspected");
    }

    private static string HashOwnedBsa(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private sealed record BsaEncodingCase(string Name, byte[] Stored, byte[] Expected, FalloutBsaMemberEncoding Encoding);
}
