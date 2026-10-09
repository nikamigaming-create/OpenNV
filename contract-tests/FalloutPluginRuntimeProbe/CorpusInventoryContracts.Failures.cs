using System.Buffers.Binary;
using System.Text.Json;
using OpenNV.Runtime.Content;

internal static partial class CorpusInventoryContracts
{
    private static void SourceFailures(string directory)
    {
        var game = Game(directory, "negative");
        var data = Path.Combine(game, "Data");
        // Both original bytes and actual source refusals are required. These are
        // authored malformed data, not a mocked exception or a reflected implementation.
        Append(data, "FalloutNV.esm", Record("STAT", 0x50, FalloutPluginRecord.DeletedFlag, new byte[] { 0x41 }),
            Record("SCPT", 0x51, 0, Field("SCHR", new byte[20]), Field("SLSD", new byte[24])));
        var archive = Path.Combine(data, "FalloutNV.bsa");
        ModContentContracts.WriteArchive(archive, "\u0010\0\0\0not-zlib");
        var badBytes = File.ReadAllBytes(archive);
        var rawSizeOffset = 52 + 1 + "textures\0".Length + sizeof(ulong);
        var rawSize = BinaryPrimitives.ReadUInt32LittleEndian(badBytes.AsSpan(rawSizeOffset));
        BinaryPrimitives.WriteUInt32LittleEndian(badBytes.AsSpan(rawSizeOffset), rawSize | 0x40000000u);
        File.WriteAllBytes(archive, badBytes);
        var before = FileHashes(game);
        var output = Path.Combine(directory, "negative-report");
        using (var source = Open(game))
        using (var records = FalloutPluginStack.Load(source.PluginSources))
        {
            Require(CorpusInventory.Run(records, source, output) == 1, "Malformed owned bytes returned a successful source-read exit.");
            var failures = Rows(output, "failure-instances.jsonl");
            Require(failures.Any(row => row.GetProperty("lane").GetString() == "record-layout" &&
                    row.GetProperty("owner").GetProperty("disposition").GetString() == "deleted") &&
                failures.Any(row => row.GetProperty("lane").GetString() == "script-locals") &&
                failures.Any(row => row.GetProperty("lane").GetString() == "winning-resource-bytes" &&
                    row.GetProperty("owner").GetProperty("logical").GetString() == "textures\\shared.dds"),
                "Deleted layout, script-local or actual compressed-byte refusal disappeared.");
            var resources = Rows(output, "winning-resources.jsonl");
            var failed = resources.Single(row => row.GetProperty("logical").GetString() == "textures\\shared.dds");
            Require(failed.GetProperty("byteOutcome").GetString() == "failed" &&
                failed.GetProperty("storedExtent").GetProperty("Compressed").GetBoolean(),
                "A stored compressed extent became a completed byte read.");
            using var summary = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "summary.json")));
            Require(summary.RootElement.GetProperty("failedInstances").GetInt64() == failures.LongLength &&
                summary.RootElement.GetProperty("sourceReadOutcome").GetString() == "failed", "The summary lost refusal instances.");
        }
        Require(before.SequenceEqual(FileHashes(game)), "A failed audit mutated its authored source bytes.");

        // A malformed selected archive directory leaves the remaining namespace unknown.
        var discoveryGame = Game(directory, "bad-directory");
        File.WriteAllBytes(Path.Combine(discoveryGame, "Data", "FalloutNV.bsa"), [0, 1, 2]);
        var discoveryBefore = FileHashes(discoveryGame);
        var discoverySource = Open(discoveryGame);
        try
        {
            using var records = FalloutPluginStack.Load(discoverySource.PluginSources);
            var report = Path.Combine(directory, "bad-directory-report");
            Require(CorpusInventory.Run(records, discoverySource, report) == 1, "Unknown archive member denominator returned success.");
            var failures = Rows(report, "failure-instances.jsonl");
            Require(failures.Any(row => row.GetProperty("lane").GetString() == "archive-member-discovery") &&
                failures.Any(row => row.GetProperty("lane").GetString() == "ordinary-archive-index"),
                "An actual archive-directory or ordinary-index failure was hidden.");
            using var summary = JsonDocument.Parse(File.ReadAllText(Path.Combine(report, "summary.json")));
            Require(!summary.RootElement.GetProperty("resourceDiscoveryComplete").GetBoolean() &&
                summary.RootElement.GetProperty("resourceInventory").GetProperty("remainingDenominator").GetString() == "unknown",
                "A failed directory claimed a complete resource denominator.");
        }
        finally
        {
            // Retirement drains the actual background reader and retains its original
            // malformed-source failure after closing the remaining source owners.
            Exception? warmupFailure = null;
            try { discoverySource.ArchiveWarmup.GetAwaiter().GetResult(); }
            catch (Exception error) { warmupFailure = error; }
            Exception? retirementFailure = null;
            try { discoverySource.Dispose(); }
            catch (Exception error) { retirementFailure = error; }
            Require(warmupFailure is AggregateException aggregate &&
                aggregate.Flatten().InnerExceptions.All(error => error is InvalidDataException) &&
                ReferenceEquals(retirementFailure, warmupFailure),
                "Malformed archive retirement lost or replaced its actual background reader failure.");
        }
        Require(discoveryBefore.SequenceEqual(FileHashes(discoveryGame)),
            "Malformed archive retirement mutated its authored source bytes.");
    }

    private static void ByteReuseAndChanges(string directory)
    {
        var root = Path.Combine(directory, "reuse");
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "source.bsa");
        ModContentContracts.WriteArchive(path, "original member fixture");
        var original = File.ReadAllBytes(path);
        var mtime = File.GetLastWriteTimeUtc(path);
        using var reuse = new CorpusByteEvidenceCache();
        var reads = 0;
        using (var archive = new FalloutBsaArchive(path))
        {
            var extent = archive.StoredExtent("textures/shared.dds");
            byte[] Read() { ++reads; return archive.Read("textures/shared.dds"); }
            var first = reuse.Archive(path, "textures\\shared.dds", extent.Offset, extent.Bytes, extent.Compressed, Read);
            var second = reuse.Archive(path, "textures\\shared.dds", extent.Offset, extent.Bytes, extent.Compressed, Read);
            Require(reads == 1 && first.Sha256 == second.Sha256 && first.Bytes == second.Bytes &&
                second.ReadKind == "reused-exact-original-member-evidence", "An unchanged exact member did not reuse genuine byte evidence.");
        }
        for (var index = 0; index < 65; ++index)
        {
            var small = Path.Combine(root, $"small-{index}.bin");
            File.WriteAllBytes(small, BitConverter.GetBytes(index));
            _ = reuse.File(small);
        }
        var retained = reuse.File(path);
        Require(retained.ReadKind is "unchanged-read-lease" or "rehashed-read-lease",
            "Interleaved cheaper sources evicted the more expensive original read lease.");
        // More genuinely larger originals than the lease bound evict the first lease, not its
        // denominator. A same-length, same-mtime byte mutation must still refuse.
        for (var index = 0; index < 65; ++index)
        {
            var file = Path.Combine(root, $"other-{index}.bin");
            var larger = new byte[original.Length + index + 1];
            BitConverter.GetBytes(index).CopyTo(larger, 0);
            File.WriteAllBytes(file, larger);
            _ = reuse.File(file);
        }
        var changed = (byte[])original.Clone(); changed[^1] ^= 1;
        File.WriteAllBytes(path, changed); File.SetLastWriteTimeUtc(path, mtime);
        Reject(() => reuse.File(path));
        Require(Hash(File.ReadAllBytes(path)) == Hash(changed), "The refusal repaired or rewrote original fixture bytes.");
        reuse.Dispose();
        var refused = false;
        try { _ = reuse.File(path); }
        catch (ObjectDisposedException) { refused = true; }
        Require(refused, "Retired corpus byte evidence accepted another source query.");
    }
}
