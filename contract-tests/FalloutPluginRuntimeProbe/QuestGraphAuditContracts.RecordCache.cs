using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using OpenNV.Runtime.Content;

internal static partial class QuestGraphAuditContracts
{
    internal static void RecordCache()
    {
        var root = Path.Combine(Path.GetTempPath(), $"opennv-record-cache-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var observer = FalloutPluginRecord.ReadObserver;
        try
        {
            var path = Path.Combine(root, "Source.esm");
            var payload = Field("DATA", [1, 2, 3, 4]);
            using var compressed = new MemoryStream();
            using (var codec = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true)) codec.Write(payload);
            File.WriteAllBytes(path, Join(Header(), Record("FUTR", 0x100, 0, payload),
                Record("FUTR", 0x101, FalloutPluginRecord.CompressedFlag, Join(BitConverter.GetBytes((uint)payload.Length), compressed.ToArray())),
                Record("FUTR", 0x102, FalloutPluginRecord.CompressedFlag, [1, 0, 0, 0, 0])));
            using (var plugin = FalloutPlugin.Open(path))
            {
                var plain = plugin.Records[1];
                var packed = plugin.Records[2];
                var before = plugin.RecordCacheState.Decodes;
                var reads = new byte[32][];
                Parallel.For(0, reads.Length, index => reads[index] = packed.ReadData());
                Require(plugin.RecordCacheState.Decodes == before + 1 && reads.All(data => data.SequenceEqual(payload)),
                    "Concurrent reads did not reuse one validated compressed payload.");
                reads[0][0] = 0;
                Require(reads.Skip(1).All(data => data.SequenceEqual(payload)) && packed.ReadData().SequenceEqual(payload),
                    "A returned byte array mutated another read or the canonical cache.");
                var observed = packed.ReadDataForObservation(); observed[0] = 0;
                Require(packed.ReadDataForObservation().SequenceEqual(payload), "Observation bytes escaped the cache's ownership.");
                var expectedHash = Convert.ToHexString(SHA256.HashData(payload));
                var firstHash = packed.ReadDataHash();
                var hashes = new string[32];
                Parallel.For(0, hashes.Length, index => hashes[index] = packed.ReadDataHash());
                Require(firstHash == expectedHash && hashes.All(hash => ReferenceEquals(hash, firstHash)),
                    "Concurrent source digest requests did not reuse their bounded canonical record entry.");
                var calls = 0;
                FalloutPluginRecord.ReadObserver = (record, bytes) =>
                {
                    if (record != packed) return;
                    calls++;
                    Require(MemoryMarshal.TryGetArray(bytes, out var array), "Read observer lost its array-backed payload.");
                    array.Array![array.Offset] = 0;
                };
                Require(packed.ReadData()[0] == 0 && calls == 1 && packed.ReadDataForObservation().SequenceEqual(payload),
                    "Cached reads changed observer dispatch or let its mutation corrupt a later read.");
                var mutatedPayload = payload.ToArray(); mutatedPayload[0] = 0;
                Require(packed.ReadDataHash() == Convert.ToHexString(SHA256.HashData(mutatedPayload)) && calls == 2,
                    "Source digest bypassed observer delivery or ignored its private mutation.");
                FalloutPluginRecord.ReadObserver = (_, _) => throw new InvalidOperationException("digest-observer-refusal");
                try { _ = packed.ReadDataHash(); throw new InvalidDataException("Source digest suppressed observer refusal."); }
                catch (InvalidOperationException error) when (error.Message == "digest-observer-refusal") { }
                FalloutPluginRecord.ReadObserver = null;
                Require(ReferenceEquals(packed.ReadDataHash(), firstHash), "Observed digest contaminated the canonical hash.");
                Require(packed.ReadSubrecords().Single().Data.Span.SequenceEqual(new byte[] { 1, 2, 3, 4 }) &&
                    plain.ReadData().SequenceEqual(payload), "Cached subrecords or uncompressed records changed.");
                for (var attempt = 0; attempt < 2; attempt++)
                    Reject(() => plugin.Records[3].ReadData(), "invalid");
                var otherPath = Path.Combine(root, "Other.esm");
                File.WriteAllBytes(otherPath, Join(Header(), Record("FUTR", 0x100, 0, Field("DATA", [9]))));
                using (var other = FalloutPlugin.Open(otherPath, "Source.esm"))
                    Require(other.Records[1].ReadData()[^1] == 9 && plain.ReadData().SequenceEqual(payload),
                        "A record cache escaped its owning plugin instance.");
                plugin.Dispose();
                Require(plugin.RecordCacheState is { Entries: 0, Bytes: 0 }, "Disposal retained decoded payloads.");
                try { packed.ReadData(); throw new InvalidDataException("A cached record remained readable after disposal."); }
                catch (ObjectDisposedException) { }
                try { packed.ReadDataHash(); throw new InvalidDataException("Source digest bypassed disposal."); }
                catch (ObjectDisposedException) { }
                try { packed.ReadDataForObservation(); throw new InvalidDataException("Observation bypassed source disposal."); }
                catch (ObjectDisposedException) { }
            }

            // Empty records need an entry bound as well as a decoded-byte bound.
            using (var output = File.Create(path))
            {
                output.Write(Header());
                for (var index = 0; index <= FalloutPlugin.RecordCacheCapacity; index++)
                    output.Write(Record("FUTR", checked((uint)(0x100 + index)), 0));
            }
            using (var plugin = FalloutPlugin.Open(path))
            {
                var evictedHash = plugin.Records[1].ReadDataHash();
                foreach (var record in plugin.Records) _ = record.ReadData();
                Require(plugin.RecordCacheState.Entries == FalloutPlugin.RecordCacheCapacity,
                    "Empty decoded records exceeded the entry bound.");
                var before = plugin.RecordCacheState.Decodes;
                var reloadedHash = plugin.Records[1].ReadDataHash();
                Require(reloadedHash == evictedHash && !ReferenceEquals(reloadedHash, evictedHash),
                    "Source digest survived its decoded-record LRU eviction.");
                Require(plugin.RecordCacheState.Decodes == before + 1, "The least recently used record was not evicted.");
            }

            var chunk = new byte[1024 * 1024];
            using (var output = File.Create(path))
            {
                output.Write(Header());
                for (var index = 0; index <= FalloutPlugin.RecordCacheBudgetBytes / chunk.Length; index++)
                    output.Write(Record("FUTR", checked((uint)(0x100 + index)), 0, chunk));
                output.Write(Record("FUTR", 0x200, 0, new byte[FalloutPlugin.RecordCacheBudgetBytes + 1]));
            }
            using (var plugin = FalloutPlugin.Open(path))
            {
                foreach (var record in plugin.Records.SkipLast(1)) _ = record.ReadData();
                Require(plugin.RecordCacheState.Bytes == FalloutPlugin.RecordCacheBudgetBytes,
                    "Decoded record bytes exceeded the memory budget or evicted too early.");
                var before = plugin.RecordCacheState;
                Require(plugin.Records[^1].ReadData().Length == FalloutPlugin.RecordCacheBudgetBytes + 1 &&
                    plugin.RecordCacheState.Bytes == before.Bytes && plugin.RecordCacheState.Entries == before.Entries,
                    "An oversized record was rejected or retained beyond the memory budget.");
                _ = plugin.Records[^1].ReadData();
                Require(plugin.RecordCacheState.Decodes == before.Decodes + 2, "An oversized payload entered the cache.");
            }
            Console.WriteLine("OPENNV_RECORD_CACHE_PASS compressed=true concurrent=true independentBuffers=true observer=true instanceOwned=true bounded=true eviction=true disposed=true");
        }
        finally
        {
            FalloutPluginRecord.ReadObserver = observer;
            Directory.Delete(root, recursive: true);
        }
    }
}
