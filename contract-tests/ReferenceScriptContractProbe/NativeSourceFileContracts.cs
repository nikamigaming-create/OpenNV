using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using OpenNV.Runtime.Content;

internal static class NativeSourceFileContracts
{
    internal static void Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "opennv-native-source-file-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var basePath = Path.Combine(root, "Base.esm"); var overlayPath = Path.Combine(root, "Overlay.esp");
            var header = Fields(("HEDR", new byte[12]));
            var large = Enumerable.Range(0, 70000).Select(index => checked((byte)(index % 251))).ToArray();
            var body = Fields(("EDID", Encoding.ASCII.GetBytes("fixture\0")), ("DATA", new byte[] { 0x11, 0x22, 0x33, 0x44 }),
                ("ZERO", []), ("LONG", large));
            Write(basePath, Record("TES4", 0, 1, header), Record("GMST", 0x800, 0, body),
                Record("GMST", 0x801, 0, []), Record("GMST", 0x802, 0, new byte[] { (byte)'D', (byte)'A', (byte)'T', (byte)'A', 8, 0, 1 }));
            Write(overlayPath, Record("TES4", 0, 0, Fields(("HEDR", new byte[12]), ("MAST", Encoding.ASCII.GetBytes("Base.esm\0")),
                ("DATA", new byte[8]))), Record("GMST", 0x800, 0x20, Fields(("EDID", Encoding.ASCII.GetBytes("deleted\0")))));
            var originalBase = Hash(basePath); var originalOverlay = Hash(overlayPath);
            using var records = FalloutPluginStack.Load(root, ["Base.esm", "Overlay.esp"]);
            var context = records.Plugins.Single(row => row.Plugin.Name == "Base.esm");
            var source = context.Plugin.Records.Single(row => row.RawFormId == 0x800);
            var overlay = records.Plugins.Single(row => row.Plugin.Name == "Overlay.esp");
            using var parser = new FalloutNativePluginSourceFile(records, context);
            Refuse(() => parser.NextChunk(), "no original current record");
            parser.SelectRecord(source);
            Require(parser.NextChunk() == Tag("EDID"), "original declaring header");
            var truncated = parser.ReadChunk(3);
            Require(truncated.Kind == FalloutNativeSourceReadKind.TruncatedWithTerminator &&
                truncated.Bytes.Span.SequenceEqual(new byte[] { (byte)'f', (byte)'i', 0 }) && truncated.ConsumedBytes == 2 &&
                truncated.Diagnostic is not null, "real truncation/count/diagnostic");
            var whole = parser.ReadChunk(0);
            Require(whole.Bytes.Span.SequenceEqual(Encoding.ASCII.GetBytes("fixture\0")), "repeat starts at the source chunk's first byte");
            var current = parser.Capture();
            Require(current.RawFormId == source.RawFormId && current.RecordHeaderOffset == source.HeaderOffset &&
                current.BytesRead == 8 && Word(parser.NativeFields().Single(row => row.Offset == 0x264).Bytes.Span) == source.HeaderOffset,
                "original header-offset and consumed-byte fields");
            Require(parser.ReadChunk(1).Bytes.Span.SequenceEqual(new byte[1]) && parser.Capture().BytesRead == 0,
                "one-byte capacity is the terminator with zero consumed source bytes");
            parser.Restore(current);
            Require(parser.Capture().Revision == current.Revision && parser.Capture().BytesRead == 8, "cold exact read state");
            Require(parser.AdvanceChunk() && parser.NextChunk() == Tag("DATA"), "advancing predicate owns next header");
            Require(parser.ReadChunk(4).Bytes.Span.SequenceEqual(new byte[] { 0x11, 0x22, 0x33, 0x44 }), "actual UInt32 bytes");
            Require(parser.AdvanceChunk() && parser.NextChunk() == Tag("ZERO"), "zero-size field is retained");
            Require(parser.ReadChunk(4).Kind == FalloutNativeSourceReadKind.Empty && parser.Capture().BytesRead == 0, "empty field write/count");
            Require(parser.AdvanceChunk() && parser.NextChunk() == Tag("LONG") && parser.Capture().ChunkBytes == 70000,
                "full XXXX following-header and UInt32 extent");
            Require(parser.ReadChunk(0).Bytes.Span.SequenceEqual(large), "complete large source bytes without a transport cap");
            var longState = parser.Capture();
            Require(!parser.AdvanceChunk() && parser.Capture().BytesRead == 70000 && parser.Capture().ChunkType == 0,
                "terminal predicate preserves its last actual read count");
            var end = parser.Capture();
            Require(parser.ReadChunk(4).Bytes.IsEmpty && parser.Capture().BytesRead == 70000,
                "cleared terminal zero-size header is a no-write operation with retained count");
            using (var cold = new FalloutNativePluginSourceFile(records, context))
            {
                cold.Restore(end); Require(cold.Capture().DataOffset == end.DataOffset && cold.Capture().AtEnd, "cold exhausted extent");
                cold.Restore(longState); Require(cold.ReadChunk(0).Bytes.Span.SequenceEqual(large), "cold complete original body");
            }
            Refuse(() => parser.NextChunk(), "unknown next-file/group producer after the reached record end");
            Refuse(() => parser.Restore(end with { RawFormId = 0x999 }), "foreign cold raw identity");
            Require(parser.Capture().DataOffset == end.DataOffset && parser.Capture().Revision == end.Revision, "failed restore is atomic");
            Refuse(() => parser.Restore(end with { Sha256 = new string('0', 64) }), "changed cold source hash");
            Refuse(() => parser.Restore(longState with { ChunkBytes = 69999 }), "changed cold extended size");
            Refuse(() => parser.SelectRecord(overlay.Plugin.Records.Single(row => row.RawFormId == 0x800)), "different original contributor");
            Refuse(() => parser.SelectRecord(context.Plugin.Records.Single(row => row.RawFormId == 0x802)), "malformed complete decoder refusal");
            Require(parser.Capture().DataOffset == end.DataOffset, "failed select preserves current source state");
            using (var deleted = new FalloutNativePluginSourceFile(records, overlay))
            {
                var originalDeleted = overlay.Plugin.Records.Single(row => row.RawFormId == 0x800);
                Require(originalDeleted.IsDeleted, "independent actual deleted override source");
                deleted.SelectRecord(originalDeleted);
                Require(deleted.NextChunk() == Tag("EDID") && deleted.ReadChunk(0).Bytes.Span.SequenceEqual(Encoding.ASCII.GetBytes("deleted\0")),
                    "deleted disposition retains its declaring bytes, never substitutes a winner");
            }
            parser.SelectRecord(context.Plugin.Records.Single(row => row.RawFormId == 0x801));
            Require(!parser.AdvanceChunk() && parser.Capture().DataOffset == 6, "empty-record advancing primitive");
            Refuse(() => parser.Restore(parser.Capture() with { DataOffset = 0 }), "unreachable empty terminal cursor");
            parser.Restore(parser.Capture());
            Require(Hash(basePath) == originalBase && Hash(overlayPath) == originalOverlay, "read-only original fixture inputs");
            Console.WriteLine("OPENNV_NATIVE_SOURCE_FILE_CONTRACT_PASS");
        }
        finally { Directory.Delete(root, recursive: true); }
    }
    private static uint Word(ReadOnlySpan<byte> bytes) => BinaryPrimitives.ReadUInt32LittleEndian(bytes);
    private static uint Tag(string text) => Word(Encoding.ASCII.GetBytes(text));
    private static string Hash(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }
    private static void Write(string path, params byte[][] records) => File.WriteAllBytes(path, records.SelectMany(value => value).ToArray());
    private static byte[] Record(string signature, uint id, uint flags, byte[] body)
    {
        var header = new byte[24]; Encoding.ASCII.GetBytes(signature).CopyTo(header, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), checked((uint)body.Length));
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(8), flags); BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(12), id);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(20), 15); return header.Concat(body).ToArray();
    }
    private static byte[] Fields(params (string Name, byte[] Bytes)[] rows)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        foreach (var row in rows)
        {
            if (row.Bytes.Length > ushort.MaxValue)
            { writer.Write(Encoding.ASCII.GetBytes("XXXX")); writer.Write((ushort)4); writer.Write(checked((uint)row.Bytes.Length)); }
            writer.Write(Encoding.ASCII.GetBytes(row.Name)); writer.Write(row.Bytes.Length > ushort.MaxValue ? (ushort)0 : checked((ushort)row.Bytes.Length));
            writer.Write(row.Bytes);
        }
        return stream.ToArray();
    }
    private static void Require(bool value, string reason) { if (!value) throw new InvalidOperationException("Source file contract: " + reason); }
    private static void Refuse(Action action, string reason)
    {
        try { action(); } catch (Exception error) when (error is IOException or NotSupportedException or InvalidOperationException) { return; }
        throw new InvalidOperationException("Source file contract falsely admitted " + reason);
    }
}
