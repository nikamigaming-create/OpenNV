using System.Buffers.Binary;
using OpenNV.Runtime.Compatibility.NativePlugins;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutNativeLoadedFileSnapshot(FalloutNativeSourceFileSnapshot Parser,
    FalloutNativeBinaryFileSnapshot Binary, FalloutNativeFileMetadataSnapshot Metadata, long Revision);

// One contributor lifetime joins its real parser, retained binary buffer and
// actual Windows metadata. It supplies no whole ModInfo/BSFile image or CRT FILE*.
internal sealed partial class FalloutNativePluginLoadedFile : IDisposable
{
    private readonly FalloutPluginStack _records;
    private readonly FalloutPluginContext _context;
    private readonly FalloutNativePluginSourceFile _parser;
    private readonly FalloutNativePluginBinaryFile _binary;
    private readonly FalloutNativePluginLoadedFileMetadata _metadata;
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private FalloutPluginRecord? _record;
    private Exception? _fault;
    private long _revision;
    private bool _disposed;

    internal FalloutPluginContext Context => _context;
    internal FalloutNativePluginBinaryFile Binary { get { RequireCurrent(); return _binary; } }
    internal string SourceSha256 => _context.Sha256;
    internal bool HasCurrentRecord { get { RequireCurrent(); return _parser.HasCurrentRecord; } }
    internal bool CurrentRecordCompressed { get { RequireCurrent(); return _parser.CurrentRecordCompressed; } }
    internal ReadOnlyMemory<byte> CurrentBody() { RequireCurrent(); return _parser.CurrentBody(); }

    internal FalloutNativePluginLoadedFile(FalloutPluginStack records, FalloutPluginContext context,
        FalloutNativeBinaryFileConstruction construction, uint actualBufferCapacity)
    {
        _records = records; _context = context;
        var binary = new FalloutNativePluginBinaryFile(records, context, construction, actualBufferCapacity);
        FalloutNativePluginSourceFile? parser = null;
        try
        {
            var metadata = new FalloutNativePluginLoadedFileMetadata(binary);
            parser = new(records, context);
            _binary = binary; _parser = parser; _metadata = metadata;
            // This is a measured size query, not the complete-source record
            // count, stored container extent or a fabricated loaded flag.
            _ = _binary.Size(); RequireCurrent();
        }
        catch (Exception first)
        {
            var failures = new List<Exception> { first };
            try { parser?.Dispose(); } catch (Exception cleanup) { failures.Add(cleanup); }
            try { binary.Dispose(); } catch (Exception cleanup) { failures.Add(cleanup); }
            if (failures.Count != 1) throw new AggregateException("Loaded contributor construction/retirement failed.", failures);
            throw;
        }
    }

    internal void RequireCurrent()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (Environment.CurrentManagedThreadId != _thread || !_records.Plugins.Contains(_context))
            throw new InvalidOperationException("Loaded contributor left its exact campaign/thread lifetime.");
        if (_fault is { } fault) throw new InvalidOperationException("Loaded contributor retains a failed source/native prefix.", fault);
        _parser.RequireCurrent(); _binary.RequireCurrent();
        if (_record is { } record && (!ReferenceEquals(record.Plugin, _context.Plugin) || !_context.Plugin.Records.Contains(record)))
            throw new InvalidOperationException("Loaded current record left its actual contributor.");
    }
    internal void RequireIdle() { RequireCurrent(); _binary.RequireIdle(); }

    internal void SelectRecord(FalloutPluginRecord record)
    {
        RequireIdle();
        if (!ReferenceEquals(record.Plugin, _context.Plugin) || !_context.Plugin.Records.Contains(record))
            throw new InvalidDataException("Loaded contributor cannot select a winning/foreign substitute record.");
        if (record.HeaderOffset < 0 || record.StoredSize < 0 ||
            (ulong)record.HeaderOffset + FalloutPlugin.RecordHeaderSize + checked((uint)record.StoredSize) > _binary.SourceLength)
            throw new InvalidDataException("Loaded current record has no complete original stored extent.");
        try
        {
            _binary.Seek(checked((uint)record.HeaderOffset), _binary.Construction.SeekSet);
            var header = _context.Plugin.ReadAt(record.HeaderOffset, FalloutPlugin.RecordHeaderSize, "loaded contributor current header");
            Consume(header);
            // Full decoding stays in the existing reader. A compressed record
            // consumes its complete original stored body once; subrecord reads
            // thereafter use that actual decoded extent and do not seek the file.
            if (record.IsCompressed) ConsumeStored(checked((uint)(record.HeaderOffset + FalloutPlugin.RecordHeaderSize)), checked((uint)record.StoredSize));
            _parser.SelectRecord(record); _record = record; _revision = checked(_revision + 1);
        }
        catch (Exception error) { _fault = error; throw; }
    }

    internal uint NextChunk()
    {
        RequireIdle(); var before = _parser.Capture();
        try
        {
            var result = _parser.NextChunk();
            if (before.ChunkType == 0 && !_parser.CurrentRecordCompressed)
            {
                var after = _parser.Capture();
                ConsumeHeader(before.DataOffset, after.DataOffset);
            }
            _revision = checked(_revision + 1); return result;
        }
        catch (Exception error) { _fault = error; throw; }
    }

    internal bool AdvanceChunk()
    {
        RequireIdle(); var before = _parser.Capture();
        try
        {
            var result = _parser.AdvanceChunk();
            if (result && !_parser.CurrentRecordCompressed)
            {
                var start = checked(before.DataOffset + FalloutPlugin.SubrecordHeaderSize + before.ChunkBytes);
                _binary.Seek(FileDataOffset(start), _binary.Construction.SeekSet);
                ConsumeHeader(start, _parser.Capture().DataOffset);
            }
            // An exhausted advance retains the actual last backend position and
            // read count. It does not manufacture a read of the skipped tail.
            _revision = checked(_revision + 1); return result;
        }
        catch (Exception error) { _fault = error; throw; }
    }

    internal FalloutNativeSourceRead ReadChunk(uint capacity)
    {
        RequireIdle(); var before = _parser.Capture();
        try
        {
            var result = _parser.ReadChunk(capacity);
            if (before.ChunkBytes != 0 && !_parser.CurrentRecordCompressed)
            {
                var position = FileDataOffset(checked(before.DataOffset + FalloutPlugin.SubrecordHeaderSize));
                if (before.BytesRead != 0) _binary.Seek(position, _binary.Construction.SeekSet);
                else if (_binary.LogicalOffset != position)
                    throw new InvalidOperationException("Loaded subrecord/binary cursor lost its actual header consumption.");
                Consume(result.Bytes.Span[..checked((int)result.ConsumedBytes)]);
            }
            _revision = checked(_revision + 1); return result;
        }
        catch (Exception error) { _fault = error; throw; }
    }

    internal IReadOnlyList<NativeNvseDataField> NativeFields()
    {
        RequireCurrent();
        var fields = new List<NativeNvseDataField>();
        if (_parser.HasCurrentRecord) fields.AddRange(_parser.NativeFields());
        fields.AddRange(_metadata.NativeFields());
        fields.Add(Word(0x22c, _binary.Capacity, "actual-selected-contributor-buffer-argument"));
        fields.Add(Word(0x260, _binary.Capture().CachedSize, "actual-contributor-cursor-preserving-file-size"));
        return fields;
        // Status, record-load counts, group/maps, loaded flags, original native
        // child pointers, metadata tails and decompressed native allocation
        // still need their own producers before complete class publication.
    }

    private void ConsumeHeader(uint start, uint header)
    {
        if (header < start) throw new InvalidDataException("Loaded subrecord header moved before its complete transport prefix.");
        var body = _parser.CurrentBody(); var bytes = checked(header - start + FalloutPlugin.SubrecordHeaderSize);
        if ((ulong)start + bytes > checked((ulong)body.Length) || _binary.LogicalOffset != FileDataOffset(start))
            throw new InvalidDataException("Loaded header lost its exact contributor/body/binary extent.");
        Consume(body.Span.Slice(checked((int)start), checked((int)bytes)));
    }
    private uint FileDataOffset(uint offset)
    {
        if (_record is null) throw new InvalidOperationException("Loaded binary cursor has no actual current record.");
        return checked((uint)_record.HeaderOffset + FalloutPlugin.RecordHeaderSize + offset);
    }

    private void Consume(ReadOnlySpan<byte> expected)
    {
        if (expected.IsEmpty) return;
        var transfer = _binary.Read(checked((uint)expected.Length));
        try
        {
            if (transfer.Actual != expected.Length) throw new EndOfStreamException("Loaded contributor did not read its complete declared extent.");
            var at = 0U;
            while (at < transfer.Actual)
            {
                var count = Math.Min(1024U * 1024, transfer.Actual - at); var actual = transfer.Slice(at, count);
                if (!actual.AsSpan().SequenceEqual(expected.Slice(checked((int)at), actual.Length)))
                    throw new InvalidDataException("Loaded binary output differs from the exact original decoder input.");
                at = checked(at + count);
            }
            transfer.Complete();
        }
        catch (Exception error) { transfer.Fail(error); throw; }
    }
    private void ConsumeStored(uint offset, uint size)
    {
        if (size == 0) return;
        if (_binary.LogicalOffset != offset) throw new InvalidDataException("Compressed contributor body has no actual post-header position.");
        var transfer = _binary.Read(size);
        try
        {
            if (transfer.Actual != size) throw new EndOfStreamException("Compressed contributor lost its complete original stored body.");
            var at = 0U;
            while (at < size)
            {
                var count = Math.Min(1024U * 1024, size - at); var actual = transfer.Slice(at, count);
                var original = _context.Plugin.ReadAt(checked((long)offset + at), checked((int)count), "loaded compressed contributor body");
                if (!actual.AsSpan().SequenceEqual(original))
                    throw new InvalidDataException("Compressed binary output differs from the complete original source transport.");
                at = checked(at + count);
            }
            transfer.Complete();
        }
        catch (Exception error) { transfer.Fail(error); throw; }
    }
    private static NativeNvseDataField Word(int at, uint value, string owner)
    { var bytes = new byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(bytes, value); return new(at, bytes, owner); }

    internal void RetainFailure(Exception error)
    {
        ArgumentNullException.ThrowIfNull(error); ObjectDisposedException.ThrowIf(_disposed, this);
        if (Environment.CurrentManagedThreadId != _thread)
            throw new InvalidOperationException("Loaded contributor failure changed its actual owner thread.");
        _fault = _fault is null || ReferenceEquals(_fault, error) ? error :
            new AggregateException("Loaded contributor retains source and native output failures.", _fault, error);
    }

    public void Dispose()
    {
        if (_disposed) return;
        if (Environment.CurrentManagedThreadId != _thread) throw new InvalidOperationException("Loaded contributor retirement changed its actual owner thread.");
        var errors = new List<Exception>();
        if (_fault is { } fault) errors.Add(fault);
        try { _parser.Dispose(); } catch (Exception error) { errors.Add(error); }
        try { _binary.Dispose(); } catch (Exception error) { errors.Add(error); }
        _disposed = true; _record = null;
        if (errors.Count != 0) throw new AggregateException("Loaded contributor retained parser/binary/cleanup failures.", errors);
    }
}
