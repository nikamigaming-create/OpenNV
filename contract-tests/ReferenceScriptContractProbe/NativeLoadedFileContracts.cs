using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using OpenNV.Runtime.Content;

internal static partial class NativeLoadedFileContracts
{
    internal static void Run()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "opennv-loaded-file-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "Current.esm"); var overlayPath = Path.Combine(root, "Later.esp");
            var runtimePath = Path.Combine(root, "authored-construction.bin");
            File.WriteAllBytes(runtimePath, Encoding.ASCII.GetBytes("First-party neutral binary construction fixture. No native class or executable code."));
            var runtimeHash = Hash(runtimePath); var large = Enumerable.Range(0, 70000).Select(at => (byte)(at % 251)).ToArray();
            var ordinary = Fields(("EDID", "buffer-fixture\0"u8.ToArray()), ("DATA", new byte[] { 17, 34, 51, 68 }),
                ("ZERO", []), ("LONG", large));
            var compressedBody = Fields(("EDID", "compressed-fixture\0"u8.ToArray()), ("DATA", new byte[] { 9, 8, 7, 6 }));
            File.WriteAllBytes(path, Record("TES4", 0, 1, Fields(("HEDR", new byte[12])))
                .Concat(Record("GMST", 0x800, 0, ordinary)).Concat(Record("GMST", 0x801, 0x40000, Compress(compressedBody))).ToArray());
            File.WriteAllBytes(overlayPath, Record("TES4", 0, 0,
                Fields(("HEDR", new byte[12]), ("MAST", "Current.esm\0"u8.ToArray()), ("DATA", new byte[8])))
                .Concat(Record("GMST", 0x800, 0x20, Fields(("EDID", "actual-deleted-override\0"u8.ToArray())))).ToArray());
            var original = File.ReadAllBytes(path); var hash = Hash(path); var overlayHash = Hash(overlayPath);
            var construction = FixtureConstruction(runtimePath, runtimeHash);
            using var records = FalloutPluginStack.Load(root, ["Current.esm", "Later.esp"]);
            var context = records.Plugins.Single(item => item.Plugin.Name == "Current.esm");
            CheckSharedContributors(root, records, context, construction);
            CheckSourceContinuations(records, context, construction);

            foreach (var argument in new[] { 3U, unchecked((uint)-3) })
            {
                using var end = new FalloutNativePluginBinaryFile(records, context, construction, 8);
                _ = Read(end, 3);
                end.Seek(argument, construction.SeekEnd);
                var sought = end.Capture();
                var logical = unchecked((uint)original.Length - argument);
                var backend = checked((uint)(original.Length + unchecked((int)argument)));
                Require(sought.LogicalOffset == logical && sought.BackendOffset == backend &&
                    sought.BinaryOffset == backend && sought.BufferBytes == 0 && sought.BufferConsumed == 0,
                    "source End retains unsigned derived subtraction and unchanged signed inherited argument");
                var bytes = Read(end, 2);
                Require(argument == 3 ? bytes.Length == 0 : bytes.AsSpan().SequenceEqual(original.AsSpan(original.Length - 3, 2)),
                    "actual End backend independently owns EOF or tail bytes");
                Require(end.LogicalOffset == unchecked(logical + (uint)bytes.Length),
                    "actual read advances the independent derived cursor without reconciling End roles");
            }

            using (var reader = new FalloutNativePluginBinaryFile(records, context, construction, 8))
            {
                Require(Read(reader, 3).AsSpan().SequenceEqual(original.AsSpan(0, 3)), "real initial buffered bytes");
                var first = reader.Capture();
                Require(first.BinaryOffset == 0 && first.BackendOffset == 8 && first.LogicalOffset == 3 &&
                    first.BufferBytes == 8 && first.BufferConsumed == 3, "independent actual cursor roles");
                Require(Read(reader, 2).AsSpan().SequenceEqual(original.AsSpan(3, 2)), "read consumes retained bytes");
                reader.Seek(1, construction.SeekSet);
                Require(reader.Capture().BackendOffset == 8 && reader.Capture().BufferConsumed == 1 &&
                    reader.Capture().BinaryOffset == unchecked((uint)-4), "absolute rewind reuses buffer and independent unsigned offset");
                Require(Read(reader, 2).AsSpan().SequenceEqual(original.AsSpan(1, 2)), "actual repeated buffered bytes");
                reader.Seek(checked((uint)original.Length - 3), construction.SeekSet);
                Require(reader.LogicalOffset == original.Length - 3 &&
                    Read(reader, 2).AsSpan().SequenceEqual(original.AsSpan(original.Length - 3, 2)), "source Set transport reaches actual tail bytes");
                var retained = reader.Capture();
                Require(retained.WrittenExtent == 8 && retained.BufferBytes == 3 && retained.BufferSources.Count == 2 &&
                    retained.WrittenBuffer.AsSpan(3).SequenceEqual(original.AsSpan(3, 5)), "short refill retains original written tail provenance");
                using var cold = new FalloutNativePluginBinaryFile(records, context, construction, 8);
                cold.Restore(retained);
                Require(Read(cold, 7).AsSpan().SequenceEqual(original.AsSpan(original.Length - 1)), "partial EOF preserves actual byte count");
                var before = reader.Capture(); var altered = retained.WrittenBuffer.ToArray(); altered[0] ^= 0x80;
                Refuse(() => reader.Restore(retained with { WrittenBuffer = altered }), "changed original retained byte");
                Refuse(() => reader.Restore(retained with { BufferConsumed = retained.BufferBytes + 1 }), "unowned consumed extent");
                Refuse(() => reader.Restore(retained with { BufferSources = retained.BufferSources.Skip(1).ToArray() }), "missing tail/source prefix");
                Require(Same(before, reader.Capture()), "failed buffer restore is atomic");
                reader.Seek(0, 0x11223344);
                Require(Same(before, reader.Capture()), "unknown origin changes no owner");
            }
            using (var loaded = new FalloutNativePluginLoadedFile(records, context, construction, 8))
            {
                var record = context.Plugin.Records.Single(item => item.RawFormId == 0x800); loaded.SelectRecord(record);
                Require(loaded.Binary.LogicalOffset == record.HeaderOffset + 24 && loaded.NextChunk() == Tag("EDID"), "actual contributor/header consumption");
                Require(loaded.ReadChunk(3).Bytes.Span.SequenceEqual("bu\0"u8), "real truncation prefix/terminator");
                Require(loaded.ReadChunk(0).Bytes.Span.SequenceEqual("buffer-fixture\0"u8), "repeated chunk rewind joins buffered state");
                Require(loaded.ReadChunk(1).ConsumedBytes == 0 && loaded.ReadChunk(0).Bytes.Span.SequenceEqual("buffer-fixture\0"u8),
                    "zero-byte truncation and following reread retain source position");
                Require(loaded.AdvanceChunk() && loaded.ReadChunk(4).Bytes.Span.SequenceEqual(new byte[] { 17, 34, 51, 68 }), "next source data");
                Require(loaded.AdvanceChunk() && loaded.ReadChunk(4).Bytes.IsEmpty && loaded.AdvanceChunk(), "empty sibling and XXXX transport");
                Require(loaded.ReadChunk(0).Bytes.Span.SequenceEqual(large), "complete extended chunk through actual binary reader");
                var settled = loaded.Capture(); Require(!loaded.AdvanceChunk(), "terminal advance");
                using var cold = new FalloutNativePluginLoadedFile(records, context, construction, 8); cold.Restore(settled);
                Require(cold.ReadChunk(0).Bytes.Span.SequenceEqual(large), "cold joint parser/binary owner");
                var before = cold.Capture();
                Refuse(() => cold.Restore(settled with { Parser = settled.Parser with { RawFormId = 0x9988 } }), "foreign cold parser identity");
                Require(Same(before.Binary, cold.Capture().Binary) && before.Parser.Revision == cold.Capture().Parser.Revision,
                    "joint refusal leaves valid sibling/state intact");
                loaded.SelectRecord(context.Plugin.Records.Single(item => item.RawFormId == 0x801));
                var binaryEnd = loaded.Binary.LogicalOffset;
                Require(loaded.NextChunk() == Tag("EDID") && loaded.ReadChunk(0).Bytes.Span.SequenceEqual("compressed-fixture\0"u8) &&
                    loaded.AdvanceChunk() && loaded.ReadChunk(4).Bytes.Span.SequenceEqual(new byte[] { 9, 8, 7, 6 }) &&
                    loaded.Binary.LogicalOffset == binaryEnd, "compressed siblings use decoded original extent without invented file seeks");
                var native = loaded.NativeFields();
                Require(native.Any(field => field.Offset == 0x260) && native.Any(field => field.Offset == 0x29c) &&
                    !native.Any(field => field.Offset == 0x10 || field.Offset == 0 || field.Offset == 0x3e8),
                    "actual metadata/size fields do not manufacture child/status/loaded flags");
            }
            var failed = new FalloutNativePluginBinaryFile(records, context, construction, 0);
            var unfinished = failed.Read(16); Require(unfinished.Slice(0, 4).AsSpan().SequenceEqual(original.AsSpan(0, 4)), "actual output prefix");
            Refuse(() => failed.Size(), "active output reentry");
            unfinished.Fail(new IOException("Authored native destination refusal after real prefix."));
            Refuse(() => unfinished.Complete(), "faulted output cannot finish/replay");
            Refuse(failed.Dispose, "retirement reports retained original failure");
            // The actual runtime lease retires even after a failed destination.
            // Merely opening this authored input with write access changes no byte.
            using (var retiredLease = new FileStream(runtimePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                Require(retiredLease.Length != 0, "failed retirement closed exact owned runtime lease");
            Require(Hash(path) == hash && Hash(overlayPath) == overlayHash && Hash(runtimePath) == runtimeHash, "unchanged original authored inputs");
            Console.WriteLine("OPENNV_NATIVE_LOADED_FILE_CONTRACT_PASS component=parser-buffer-metadata nativeClassPublication=unowned NOT_DLL_COMPATIBILITY");
        }
        finally
        {
            var expected = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!root.StartsWith(expected, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(root).StartsWith("opennv-loaded-file-", StringComparison.Ordinal))
                throw new InvalidOperationException("Authored file cleanup left its exact temporary output boundary.");
            Directory.Delete(root, recursive: true);
        }
    }
    private static FalloutNativeBinaryFileConstruction FixtureConstruction(string path, string sha)
    {
        var fields = new List<FalloutNativeBinaryInitialField>();
        foreach (var at in new[] { 4, 0x14, 0x18, 0x1c, 0x34, 0x38, 0x3c, 0x40, 0x148, 0x14c, 0x150, 0x154 })
        {
            var bytes = new byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(bytes, at == 0x38 ? uint.MaxValue : 0);
            fields.Add(new(at, bytes));
        }
        fields.Add(new(0x30, new byte[1]));
        return new(path, sha, "authored-neutral-source-construction-fixture", 0, 0, 1, 2, 8, fields);
    }
    private static byte[] Read(FalloutNativePluginBinaryFile file, uint requested)
    {
        var transfer = file.Read(requested); using var bytes = new MemoryStream();
        while (transfer.Position < transfer.Actual)
            bytes.Write(transfer.Slice(transfer.Position, Math.Min(3U, transfer.Actual - transfer.Position)));
        transfer.Complete(); return bytes.ToArray();
    }
    private static bool Same(FalloutNativeBinaryFileSnapshot left, FalloutNativeBinaryFileSnapshot right)
        => left.BinaryOffset == right.BinaryOffset && left.BackendOffset == right.BackendOffset && left.LogicalOffset == right.LogicalOffset &&
        left.BufferBytes == right.BufferBytes && left.BufferConsumed == right.BufferConsumed && left.Revision == right.Revision &&
        left.WrittenBuffer.AsSpan().SequenceEqual(right.WrittenBuffer) && left.BufferSources.SequenceEqual(right.BufferSources);
    private static byte[] Compress(byte[] body)
    {
        using var output = new MemoryStream(); using var writer = new BinaryWriter(output, Encoding.ASCII, leaveOpen: true);
        writer.Write(body.Length);
        using (var zlib = new ZLibStream(output, CompressionLevel.SmallestSize, leaveOpen: true)) zlib.Write(body);
        return output.ToArray();
    }
    private static byte[] Record(string type, uint id, uint flags, byte[] body)
    {
        var header = new byte[24]; Encoding.ASCII.GetBytes(type).CopyTo(header, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), checked((uint)body.Length));
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(8), flags); BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(12), id);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(20), 15); return header.Concat(body).ToArray();
    }
    private static byte[] Fields(params (string Type, byte[] Bytes)[] fields)
    {
        using var output = new MemoryStream(); using var writer = new BinaryWriter(output);
        foreach (var field in fields)
        {
            if (field.Bytes.Length > ushort.MaxValue) { writer.Write("XXXX"u8); writer.Write((ushort)4); writer.Write(field.Bytes.Length); }
            writer.Write(Encoding.ASCII.GetBytes(field.Type));
            writer.Write(field.Bytes.Length > ushort.MaxValue ? (ushort)0 : checked((ushort)field.Bytes.Length)); writer.Write(field.Bytes);
        }
        return output.ToArray();
    }
    private static uint Tag(string text) => BinaryPrimitives.ReadUInt32LittleEndian(Encoding.ASCII.GetBytes(text));
    private static string Hash(string path) { using var input = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(input)); }
    private static void Require(bool value, string reason) { if (!value) throw new InvalidOperationException("Loaded file contract: " + reason); }
    private static void Refuse(Action action, string reason)
    {
        try { action(); } catch (Exception error) when (error is IOException or InvalidDataException or InvalidOperationException or NotSupportedException or AggregateException) { return; }
        throw new InvalidOperationException("Loaded file contract falsely admitted " + reason);
    }
}
