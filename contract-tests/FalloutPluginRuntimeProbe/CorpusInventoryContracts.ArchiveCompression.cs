using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;

internal static partial class CorpusInventoryContracts
{
    private static void ArchiveCompressionRefusals(string directory)
    {
        var game = Game(directory, "compressed-member-ledger");
        var archivePath = Path.Combine(game, "Data", "FalloutNV.bsa");
        var genuineEmpty = CorpusCompressedFrame([]);
        Require(genuineEmpty.Length >= 8 && (genuineEmpty[2] & 1) != 0,
            "The authored empty compression control has no first final block.");
        var nonfinal = (byte[])genuineEmpty.Clone();
        nonfinal[2] &= 0xfe;
        var normal = Encoding.UTF8.GetBytes("ordinary compressed corpus sibling");
        var checksumAbsent = CorpusCompressedFrame(normal)[..^4];
        var badAdler = genuineEmpty.ToArray(); badAdler[^1] ^= 1;
        (string Name, byte[] Stored, bool Refused, byte[] Expected, FalloutBsaMemberEncoding? Encoding)[] members =
        [
            // Preserve the original absent-zero member as an explicit typed
            // archive empty encoding; independent malformed empties remain below.
            ("absent.bin", CorpusCompressedMember([], 0), false, [], FalloutBsaMemberEncoding.PrefixOnlyEmpty),
            ("missing-prefix.bin", [], true, [], null),
            ("absent-nonempty.bin", CorpusCompressedMember([], 1), true, [], null),
            ("nonfinal.bin", CorpusCompressedMember(nonfinal, 0), true, [], null),
            ("trailing.bin", CorpusCompressedMember([.. genuineEmpty, 0x7f], 0), true, [], null),
            ("empty-nonheader.bin", CorpusCompressedMember([0x7f], 0), true, [], null),
            ("bad-adler.bin", CorpusCompressedMember(badAdler, 0), true, [], null),
            ("checksum-absent-trailing.bin", CorpusCompressedMember([.. checksumAbsent, 0x7f], (uint)normal.Length), true, [], null),
            ("empty.bin", CorpusCompressedMember(genuineEmpty, 0), false, [], FalloutBsaMemberEncoding.ZlibFramed),
            ("normal.bin", CorpusCompressedMember(CorpusCompressedFrame(normal), (uint)normal.Length), false, normal, FalloutBsaMemberEncoding.ZlibFramed),
            ("checksum-absent.bin", CorpusCompressedMember(checksumAbsent, (uint)normal.Length), false, normal, FalloutBsaMemberEncoding.ZlibHeaderDeflate),
        ];
        WriteCorpusCompressedArchive(archivePath, members.Select(member => (member.Name, member.Stored)).ToArray());
        var unchanged = FileHashes(game);
        var archiveHash = Hash(File.ReadAllBytes(archivePath));
        using (var source = Open(game))
        using (var records = FalloutPluginStack.Load(source.PluginSources))
        {
            var observed = new List<(string Logical, string Source, byte[] Bytes)>();
            source.ResourceReadObserver = (logical, identity, bytes) => observed.Add((logical, identity, bytes.ToArray()));
            // The same ordinary source owner is reused. A failed payload lookup must
            // remain a failure rather than install an empty entry in its payload cache.
            for (var attempt = 0; attempt < 2; ++attempt)
            {
                var report = Path.Combine(directory, "compressed-member-ledger-report-" + attempt);
                Require(CorpusInventory.Run(records, source, report) == 1,
                    "Malformed compressed members earned a successful full corpus source-read exit.");
                var resourceRows = Rows(report, "winning-resources.jsonl");
                var failures = Rows(report, "failure-instances.jsonl");
                var extentRows = Rows(report, "archive-stored-extents.jsonl");
                Require(extentRows.Length == 11 && failures.Length == 7,
                    "Malformed member byte refusals were confused with archive discovery or valid sibling failures.");
                foreach (var member in members)
                {
                    var logical = "textures\\" + member.Name;
                    var row = resourceRows.Single(value => value.GetProperty("logical").GetString() == logical);
                    var extent = extentRows.Single(value => value.GetProperty("member").GetString() == logical);
                    Require(row.GetProperty("winningSource").GetString() == archivePath + "::" + logical &&
                        extent.GetProperty("containerSha256").GetString() == archiveHash &&
                        extent.GetProperty("storedBytes").GetInt32() == member.Stored.Length &&
                        extent.GetProperty("Compressed").GetBoolean(),
                        "Compressed member evidence changed its actual original container, extent or ordinary winner.");
                    if (member.Refused)
                    {
                        Require(row.GetProperty("byteOutcome").GetString() == "failed" &&
                            row.GetProperty("bytes").ValueKind == JsonValueKind.Null &&
                            row.GetProperty("sha256").ValueKind == JsonValueKind.Null &&
                            row.GetProperty("readKind").ValueKind == JsonValueKind.Null &&
                            row.GetProperty("archiveEncoding").ValueKind == JsonValueKind.Null &&
                            failures.Count(value => value.GetProperty("lane").GetString() == "winning-resource-bytes" &&
                                value.GetProperty("owner").GetProperty("logical").GetString() == logical) == 1,
                            "A malformed member acquired byte/encoding evidence or lost its exact refusal instance.");
                    }
                    else
                    {
                        var expected = member.Expected;
                        Require(row.GetProperty("byteOutcome").GetString() == "read" &&
                            row.GetProperty("bytes").GetInt64() == expected.LongLength &&
                            row.GetProperty("sha256").GetString() == Hash(expected) &&
                            row.GetProperty("originalFileSha256").GetString() == archiveHash &&
                            row.GetProperty("archiveEncoding").GetString() == member.Encoding.ToString() &&
                            row.GetProperty("decoding").GetString() == "uninspected",
                            "A malformed sibling suppressed a genuine archive encoding or promoted decoder acceptance.");
                    }
                }
                using var summary = JsonDocument.Parse(File.ReadAllText(Path.Combine(report, "summary.json")));
                var state = summary.RootElement;
                Require(state.GetProperty("sourceReadOutcome").GetString() == "failed" &&
                    state.GetProperty("resourceDiscoveryComplete").GetBoolean() &&
                    state.GetProperty("failedInstances").GetInt64() == 7 &&
                    state.GetProperty("winningResources").GetInt64() == 13 &&
                    state.GetProperty("winningResourceByteEvidence").GetInt64() == 6 &&
                    !state.GetProperty("runtimeReady").GetBoolean(),
                    "A fully known directory claimed all winning bytes complete or converted an enumeration into runtime acceptance.");
            }

            // Test the selection-domain digest owner independently without changing
            // the corpus Run API or reflecting its private shared-cache overload.
            using var reuse = new CorpusByteEvidenceCache();
            var failedReads = 0;
            var successfulReads = 0;
            for (var attempt = 0; attempt < 2; ++attempt)
            {
                foreach (var member in members)
                {
                    var logical = "textures\\" + member.Name;
                    Require(source.TryResolve(logical, null, out var identity), "An authored member disappeared from its ordinary source index.");
                    var extent = source.ResourceExtent(identity);
                    (byte[] Data, string? ArchiveEncoding) Read()
                    {
                        if (member.Refused) ++failedReads; else ++successfulReads;
                        Require(source.TryRead(logical, null, out var payload, out var readSource, out var encoding) && readSource == identity,
                            "A digest attempt bypassed the ordinary resolved source owner.");
                        Require(encoding == member.Encoding, "The ordinary source cache lost the actual successful archive encoding.");
                        return (payload, encoding?.ToString());
                    }
                    if (member.Refused) Reject(() => reuse.Archive(extent.File, logical, extent.Offset, extent.StoredBytes, extent.Compressed, Read));
                    else
                    {
                        var evidence = reuse.Archive(extent.File, logical, extent.Offset, extent.StoredBytes, extent.Compressed, Read);
                        var expected = member.Expected;
                        Require(evidence.Bytes == expected.LongLength && evidence.Sha256 == Hash(expected) && evidence.FileSha256 == archiveHash &&
                            evidence.ArchiveEncoding == member.Encoding.ToString(),
                            "Reused member evidence lost its complete original source bytes.");
                    }
                }
            }
            var cache = JsonSerializer.SerializeToElement(reuse.State);
            Require(failedReads == 14 && successfulReads == 4 &&
                cache.GetProperty("memberByteReads").GetInt64() == 4 &&
                cache.GetProperty("memberDigestHits").GetInt64() == 4 &&
                cache.GetProperty("retainedMemberDigests").GetInt32() == 4,
                "A failed compressed read was cached/reused as empty evidence or discarded a genuine sibling digest.");
            foreach (var member in members)
            {
                var logical = "textures\\" + member.Name;
                var events = observed.Where(value => value.Logical == logical).ToArray();
                Require(events.Length == (member.Refused ? 0 : 3) && events.All(value =>
                    value.Source == archivePath + "::" + logical && value.Bytes.SequenceEqual(member.Expected)),
                    "Typed source-cache reuse changed successful observer count/winner/bytes or emitted a failed read.");
            }
            source.ResourceReadObserver = null;
        }
        Require(unchanged.SequenceEqual(FileHashes(game)), "Compressed member refusal changed original authored source bytes.");
        OrdinaryStoredAndLooseEncodings(directory);
        Console.WriteLine("OPENNV_CORPUS_COMPRESSED_MEMBER_REFUSAL_PASS absentZeroCase=explicitly-migrated missingPrefixAndNonemptyAbsentRefused=true nonfinal=true trailing=true badAdler=strict siblings=declared-empty-framed-empty-normal-checksum-absent actualEncodingEvidence=true failedEvidence=none cacheRefusal=not-reused observer=preserved source=unchanged runtimeReady=false");
    }

    private static void OrdinaryStoredAndLooseEncodings(string directory)
    {
        foreach (var looseWinner in new[] { false, true })
        {
            var game = Game(directory, "ordinary-encoding-" + looseWinner);
            var expected = Encoding.UTF8.GetBytes("base fixture");
            var identity = Path.Combine(game, "Data", "FalloutNV.bsa") + "::textures\\shared.dds";
            if (looseWinner)
            {
                var textures = Path.Combine(game, "Data", "textures");
                Directory.CreateDirectory(textures);
                identity = Path.Combine(textures, "shared.dds");
                expected = "authored loose winner"u8.ToArray();
                File.WriteAllBytes(identity, expected);
            }
            var unchanged = FileHashes(game);
            FalloutBsaMemberEncoding? expectedEncoding = looseWinner ? null : FalloutBsaMemberEncoding.Stored;
            using (var source = Open(game))
            using (var records = FalloutPluginStack.Load(source.PluginSources))
            {
                var observed = 0;
                source.ResourceReadObserver = (logical, owner, bytes) =>
                {
                    if (logical != "textures\\shared.dds") return;
                    Require(owner == identity && bytes.Span.SequenceEqual(expected), "Stored/loose source observer changed winner or bytes.");
                    ++observed;
                };
                for (var attempt = 0; attempt < 2; ++attempt)
                {
                    Require(source.TryRead("textures\\shared.dds", null, out var payload, out var owner, out var encoding) &&
                        owner == identity && payload.SequenceEqual(expected) &&
                        encoding == expectedEncoding,
                        "Actual ordinary stored/loose reads lost their distinct encoding owner across payload-cache reuse.");
                }
                var output = Path.Combine(directory, "ordinary-encoding-report-" + looseWinner);
                Require(CorpusInventory.Run(records, source, output) == 0, "Valid ordinary stored/loose byte evidence failed.");
                var row = Rows(output, "winning-resources.jsonl").Single(value => value.GetProperty("logical").GetString() == "textures\\shared.dds");
                Require(row.GetProperty("winningSource").GetString() == identity && row.GetProperty("sha256").GetString() == Hash(expected) &&
                    (looseWinner ? row.GetProperty("archiveEncoding").ValueKind == JsonValueKind.Null :
                        row.GetProperty("archiveEncoding").GetString() == nameof(FalloutBsaMemberEncoding.Stored)) &&
                    observed == (looseWinner ? 2 : 3),
                    "Corpus stored/loose metadata invented an archive encoding, lost actual identity or changed the existing read observer lane.");
                source.ResourceReadObserver = null;
            }
            Require(unchanged.SequenceEqual(FileHashes(game)), "Ordinary stored/loose encoding inspection changed authored source bytes.");
        }
    }

    private static byte[] CorpusCompressedFrame(byte[] bytes)
    {
        using var stream = new MemoryStream();
        using (var compressor = new ZLibStream(stream, CompressionLevel.SmallestSize, leaveOpen: true)) compressor.Write(bytes);
        return stream.ToArray();
    }

    private static byte[] CorpusCompressedMember(byte[] framed, uint expected)
    {
        var stored = new byte[sizeof(uint) + framed.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(stored, expected);
        framed.CopyTo(stored, sizeof(uint));
        return stored;
    }

    private static void WriteCorpusCompressedArchive(string path, (string Name, byte[] Stored)[] members)
    {
        var folder = Encoding.ASCII.GetBytes("textures\0");
        var names = members.SelectMany(member => Encoding.ASCII.GetBytes(member.Name + "\0")).ToArray();
        var dataOffset = checked((uint)(52 + 1 + folder.Length + members.Length * 16 + names.Length));
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write(0x00415342u); writer.Write(104u); writer.Write(36u); writer.Write(3u);
        writer.Write(1u); writer.Write((uint)members.Length); writer.Write((uint)folder.Length); writer.Write((uint)names.Length); writer.Write(2u);
        writer.Write(0ul); writer.Write((uint)members.Length); writer.Write((uint)(52 + names.Length));
        writer.Write((byte)folder.Length); writer.Write(folder);
        foreach (var member in members)
        {
            writer.Write(0ul); writer.Write((uint)member.Stored.Length | 0x40000000u); writer.Write(dataOffset);
            dataOffset = checked(dataOffset + (uint)member.Stored.Length);
        }
        writer.Write(names);
        foreach (var member in members) writer.Write(member.Stored);
    }
}
