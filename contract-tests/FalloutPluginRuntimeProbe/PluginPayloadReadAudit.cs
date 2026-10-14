using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;

internal static class PluginPayloadReadAudit
{
    // Uses the ordinary reader on every retained original record. The digest
    // covers decoded bytes and order; no owned payload is written to disk.
    internal static void Run(FalloutPluginStack stack)
    {
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var previousObserver = FalloutPluginRecord.ReadObserver;
        FalloutPluginRecord? current = null;
        long records = 0, compressed = 0, subrecords = 0, decodedBytes = 0;
        long compressedTicks = 0, uncompressedTicks = 0, observations = 0;
        var total = Stopwatch.StartNew();
        Span<byte> prefix = stackalloc byte[24];
        try
        {
            FalloutPluginRecord.ReadObserver = (record, bytes) =>
            {
                if (!ReferenceEquals(record, current))
                    throw new InvalidOperationException("Payload audit observed a different source read.");
                previousObserver?.Invoke(record, bytes);
                digest.AppendData(bytes.Span);
                decodedBytes = checked(decodedBytes + bytes.Length);
                observations++;
            };
            foreach (var context in stack.Plugins)
            {
                var name = Encoding.UTF8.GetBytes(context.Plugin.Name);
                BinaryPrimitives.WriteInt32LittleEndian(prefix, name.Length);
                digest.AppendData(prefix[..sizeof(int)]);
                digest.AppendData(name);
                foreach (var record in context.Plugin.Records)
                {
                    current = record;
                    BinaryPrimitives.WriteInt64LittleEndian(prefix, record.HeaderOffset);
                    BinaryPrimitives.WriteUInt32LittleEndian(prefix[8..], record.RawFormId);
                    BinaryPrimitives.WriteUInt32LittleEndian(prefix[12..], record.Flags);
                    BinaryPrimitives.WriteUInt16LittleEndian(prefix[16..], record.FormVersion);
                    BinaryPrimitives.WriteInt32LittleEndian(prefix[18..], record.StoredSize);
                    prefix[22..].Clear();
                    digest.AppendData(prefix);
                    digest.AppendData(Encoding.ASCII.GetBytes(record.Signature));
                    var started = Stopwatch.GetTimestamp();
                    subrecords = checked(subrecords + record.ReadSubrecords().LongCount());
                    var elapsed = Stopwatch.GetTimestamp() - started;
                    if (record.IsCompressed) { compressed++; compressedTicks += elapsed; }
                    else uncompressedTicks += elapsed;
                    records++;
                }
            }
            if (observations != records)
                throw new InvalidOperationException("Payload audit lost or duplicated a source read.");
            Console.WriteLine("OPENNV_PLUGIN_PAYLOAD_READ_AUDIT_PASS " + JsonSerializer.Serialize(new
            {
                plugins = stack.Plugins.Count,
                records,
                compressed,
                subrecords,
                decodedBytes,
                digest = Convert.ToHexString(digest.GetHashAndReset()).ToLowerInvariant(),
                elapsedMilliseconds = total.Elapsed.TotalMilliseconds,
                compressedMilliseconds = compressedTicks * (1000.0 / Stopwatch.Frequency),
                uncompressedMilliseconds = uncompressedTicks * (1000.0 / Stopwatch.Frequency),
                gameplay = false,
                parity = false,
                recording = false
            }));
        }
        finally { FalloutPluginRecord.ReadObserver = previousObserver; }
    }
}
