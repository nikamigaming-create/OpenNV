using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace OpenNV.Runtime.Content;

internal enum FalloutNativeSourceReadKind { Complete, TruncatedWithTerminator, Empty }
internal sealed record FalloutNativeSourceRead(FalloutNativeSourceReadKind Kind, ReadOnlyMemory<byte> Bytes,
    uint SourceBytes, uint ConsumedBytes, string? Diagnostic);
internal sealed record FalloutNativeSourceFileSnapshot(string Plugin, string Sha256, long Revision,
    long? RecordHeaderOffset, uint? RawFormId, byte[]? RecordHeader, string? BodySha256,
    uint DataOffset, uint ChunkType, uint ChunkBytes, uint BytesRead, bool AtEnd);

// Actual selected reader state, not a copied TESFile image or a gameplay
// authority. The native adapter must bind this same owner before exposing any
// file/parser method. Missing BSFile, alias/map and loaded-field producers are
// independent publication refusals.
internal sealed class FalloutNativePluginSourceFile : IDisposable
{
    private readonly FalloutPluginStack _records;
    private readonly FalloutPluginContext _context;
    private readonly FileStream _lease;
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private FalloutPluginRecord? _record;
    private ReadOnlyMemory<byte> _body;
    private IReadOnlyDictionary<int, SourceChunk> _chunks = new Dictionary<int, SourceChunk>();
    private byte[]? _header;
    private string? _bodySha;
    private int _offset;
    private uint _chunkType, _chunkBytes, _bytesRead;
    private bool _end, _disposed;
    private long _revision;

    private sealed record SourceChunk(uint Type, int HeaderOffset, ReadOnlyMemory<byte> Bytes);
    internal FalloutPluginContext Context => _context;
    internal string SourceSha256 => _context.Sha256;
    internal long Length => _lease.Length;
    internal bool HasCurrentRecord { get { RequireCurrent(); return _record is not null; } }

    internal FalloutNativePluginSourceFile(FalloutPluginStack records, FalloutPluginContext context)
    {
        _records = records; _context = context;
        if (!records.Plugins.Contains(context) || !context.Plugin.NativeSourceAvailable)
            throw new InvalidDataException("Native file state is not an actual selected input reader.");
        _lease = new(context.Plugin.Path, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            if (!StringComparer.OrdinalIgnoreCase.Equals(Convert.ToHexString(SHA256.HashData(_lease)), context.Sha256))
                throw new InvalidDataException("Native file state changed the exact selected input bytes.");
            RequireCurrent();
        }
        catch { _lease.Dispose(); throw; }
    }

    internal void RequireCurrent()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (Environment.CurrentManagedThreadId != _thread || !_records.Plugins.Contains(_context) ||
            !_context.Plugin.NativeSourceAvailable || _lease.SafeFileHandle.IsClosed ||
            _lease.Length != _context.Bytes)
            throw new InvalidOperationException("Native selected-file thread/source/reader lifetime changed.");
        if (_record is { } record && (!ReferenceEquals(record.Plugin, _context.Plugin) || !_context.Plugin.Records.Contains(record)))
            throw new InvalidOperationException("Native current record left its actual contributor reader.");
    }

    // Contributor parsing admits that contributor's real record, including an
    // overridden or deleted declaration. Winning identity is a separate form
    // publication join; it never substitutes the winner's bytes here.
    internal void SelectRecord(FalloutPluginRecord record)
    {
        RequireCurrent();
        if (!ReferenceEquals(record.Plugin, _context.Plugin) || !_context.Plugin.Records.Contains(record))
            throw new InvalidDataException("Native current record is foreign to its selected source reader.");
        var fields = record.ReadSubrecords().ToArray();
        ReadOnlyMemory<byte> body;
        var chunks = new Dictionary<int, SourceChunk>();
        if (fields.Length == 0)
            body = record.ReadData();
        else
        {
            if (!MemoryMarshal.TryGetArray(fields[0].Data, out var first) || first.Array is null)
                throw new InvalidDataException("Actual source subrecord owner has no complete transport extent.");
            body = first.Array;
            foreach (var field in fields)
            {
                if (!MemoryMarshal.TryGetArray(field.Data, out var span) || !ReferenceEquals(span.Array, first.Array) ||
                    span.Offset < FalloutPlugin.SubrecordHeaderSize || span.Count != field.Data.Length)
                    throw new InvalidDataException("Source subrecord left its complete original decoded body.");
                var at = checked(span.Offset - FalloutPlugin.SubrecordHeaderSize);
                var type = BinaryPrimitives.ReadUInt32LittleEndian(body.Span[at..]);
                if (!chunks.TryAdd(at, new(type, at, field.Data)))
                    throw new InvalidDataException("Native source subrecord header identity repeats.");
            }
        }
        var header = _context.Plugin.ReadAt(record.HeaderOffset, FalloutPlugin.RecordHeaderSize, "native current record header");
        if (BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(12)) != record.RawFormId ||
            BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(8)) != record.Flags ||
            BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(4)) != record.StoredSize)
            throw new InvalidDataException("Native current record differs from the actual indexed header.");
        var sha = Convert.ToHexString(SHA256.HashData(body.Span));
        RequireCurrent();
        _record = record; _body = body; _chunks = chunks; _header = header; _bodySha = sha;
        _offset = 0; _chunkType = 0; _chunkBytes = 0; _bytesRead = 0; _end = false;
        _revision = checked(_revision + 1);
    }

    internal uint NextChunk()
    {
        RequireRecord();
        if (_chunkType != 0) return _chunkType;
        if (_end || _offset >= _body.Length)
            throw new NotSupportedException("Original next-header at an exhausted record needs its actual next-file/group producer.");
        // XXXX transport belongs to the existing complete subrecord decoder.
        // Its following actual header is retained by the source extent join.
        if (!_chunks.TryGetValue(_offset, out var chunk))
        {
            var next = _chunks.Values.Where(row => row.HeaderOffset >= _offset).OrderBy(row => row.HeaderOffset).FirstOrDefault();
            if (next is null || next.HeaderOffset - _offset != 10 ||
                !_body.Span.Slice(_offset, 4).SequenceEqual("XXXX"u8))
                throw new NotSupportedException("Native parser position has no exact admitted source subrecord header.");
            chunk = next; _offset = chunk.HeaderOffset;
        }
        _chunkType = chunk.Type; _chunkBytes = checked((uint)chunk.Bytes.Length); _bytesRead = 0;
        _revision = checked(_revision + 1); return _chunkType;
    }

    // The original API's 'has more' operation advances the parser. It is not a
    // pure availability query, and skipping an unread tail still changes state.
    internal bool AdvanceChunk()
    {
        RequireRecord();
        _offset = checked(_offset + FalloutPlugin.SubrecordHeaderSize + (int)_chunkBytes);
        ClearHeader(); _revision = checked(_revision + 1);
        if (_offset >= _body.Length) { _end = true; return false; }
        _bytesRead = 0;
        return NextChunk() != 0;
    }

    internal FalloutNativeSourceRead ReadChunk(uint capacity)
    {
        RequireRecord();
        // The original operation returns true immediately for a size-zero
        // header, including after terminal advance cleared that header. It
        // writes no caller memory and preserves the previous read count.
        if (_chunkBytes == 0)
            return new(FalloutNativeSourceReadKind.Empty, ReadOnlyMemory<byte>.Empty, 0, _bytesRead, null);
        if (_chunkType == 0)
            throw new InvalidOperationException("Native chunk read has no actual current header.");
        var chunk = _chunks[_offset];
        // Each call starts at the current chunk's first byte, including after
        // an earlier partial/full read. It does not append to the old cursor.
        var truncated = capacity != 0 && capacity < _chunkBytes;
        var copied = truncated ? checked((int)capacity - 1) : chunk.Bytes.Length;
        ReadOnlyMemory<byte> output;
        if (truncated)
        {
            var terminated = new byte[checked((int)capacity)];
            chunk.Bytes.Span[..copied].CopyTo(terminated); output = terminated;
        }
        else output = chunk.Bytes;
        _bytesRead = checked((uint)copied); _revision = checked(_revision + 1);
        return new(truncated ? FalloutNativeSourceReadKind.TruncatedWithTerminator : FalloutNativeSourceReadKind.Complete,
            output, _chunkBytes, _bytesRead, truncated ?
                $"Native source chunk {_chunkType:x8} contains {_chunkBytes} bytes; caller capacity {capacity} truncates to {copied} bytes and a terminator." : null);
    }

    internal FalloutNativeSourceFileSnapshot Capture()
    {
        RequireCurrent();
        return new(_context.Plugin.Name, _context.Sha256, _revision, _record?.HeaderOffset, _record?.RawFormId,
            _header?.ToArray(), _bodySha, checked((uint)_offset), _chunkType, _chunkBytes, _bytesRead, _end);
    }
    internal void Restore(FalloutNativeSourceFileSnapshot saved)
    {
        RequireCurrent(); ArgumentNullException.ThrowIfNull(saved);
        if (!StringComparer.OrdinalIgnoreCase.Equals(saved.Plugin, _context.Plugin.Name) ||
            !StringComparer.OrdinalIgnoreCase.Equals(saved.Sha256, _context.Sha256) || saved.Revision < 0)
            throw new InvalidDataException("Cold native parser changed its actual selected contributor/source identity.");
        if (saved.RecordHeaderOffset is null)
        {
            if (saved.RawFormId is not null || saved.RecordHeader is not null || saved.BodySha256 is not null ||
                saved.DataOffset != 0 || saved.ChunkType != 0 || saved.ChunkBytes != 0 || saved.BytesRead != 0 || saved.AtEnd)
                throw new InvalidDataException("Cold source parser invents fields without an original current record.");
            _record = null; _body = default; _chunks = new Dictionary<int, SourceChunk>(); _header = null; _bodySha = null;
            _offset = 0; _chunkType = 0; _chunkBytes = 0; _bytesRead = 0; _end = false; _revision = saved.Revision; return;
        }
        var record = _context.Plugin.Records.SingleOrDefault(row => row.HeaderOffset == saved.RecordHeaderOffset) ??
            throw new InvalidDataException("Cold source parser current record is absent from its original contributor.");
        // Reopen through the full existing source decoder, not a persisted
        // derivative. Overridden/deleted records keep this contributor's bytes.
        var before = (_record, _body, _chunks, _header, _bodySha, _offset, _chunkType, _chunkBytes, _bytesRead, _end, _revision);
        try
        {
        SelectRecord(record);
        if (saved.RawFormId != record.RawFormId || saved.RecordHeader is null || !_header!.AsSpan().SequenceEqual(saved.RecordHeader) ||
            !StringComparer.OrdinalIgnoreCase.Equals(saved.BodySha256, _bodySha) || saved.DataOffset > int.MaxValue)
            throw new InvalidDataException("Cold native parser changed its original record header or complete decoded body.");
        var at = checked((int)saved.DataOffset);
        if (saved.AtEnd)
        {
            var tail = _chunks.Values.OrderBy(row => row.HeaderOffset).LastOrDefault();
            if (at < (_body.IsEmpty ? FalloutPlugin.SubrecordHeaderSize : _body.Length) || (at - _body.Length) % FalloutPlugin.SubrecordHeaderSize != 0 ||
                saved.ChunkType != 0 || saved.ChunkBytes != 0 || saved.BytesRead > (tail?.Bytes.Length ?? 0))
                throw new InvalidDataException("Cold exhausted parser has no exact terminal source cursor/read count.");
        }
        else if (saved.ChunkType == 0)
        {
            if (at != 0 || saved.ChunkBytes != 0 || saved.BytesRead != 0)
                throw new InvalidDataException("Cold unread parser has no actual initial-header state.");
        }
        else if (!_chunks.TryGetValue(at, out var chunk) || saved.ChunkType != chunk.Type ||
            saved.ChunkBytes != chunk.Bytes.Length || saved.BytesRead > chunk.Bytes.Length)
            throw new InvalidDataException("Cold native parser header/read fields differ from their actual source subrecord.");
        _offset = at; _chunkType = saved.ChunkType; _chunkBytes = saved.ChunkBytes; _bytesRead = saved.BytesRead;
        _end = saved.AtEnd; _revision = saved.Revision;
        }
        catch
        {
            (_record, _body, _chunks, _header, _bodySha, _offset, _chunkType, _chunkBytes, _bytesRead, _end, _revision) = before;
            throw;
        }
    }
    internal IReadOnlyList<OpenNV.Runtime.Compatibility.NativePlugins.NativeNvseDataField> NativeFields()
    {
        RequireRecord();
        var result = new List<OpenNV.Runtime.Compatibility.NativePlugins.NativeNvseDataField>();
        result.Add(new(0x240, _header!.ToArray(), "actual-contributor-current-record-header"));
        Add(0x258, _chunkType, "actual-source-parser-current-subrecord-type");
        Add(0x25c, _chunkBytes, "actual-source-parser-extended-subrecord-size");
        Add(0x264, checked((uint)_record!.HeaderOffset), "actual-source-record-header-file-offset");
        Add(0x268, checked((uint)_offset), "actual-source-parser-decoded-data-offset");
        Add(0x26c, _bytesRead, "actual-source-parser-last-read-byte-count");
        return result;
        void Add(int at, uint word, string owner)
        {
            var bytes = new byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(bytes, word); result.Add(new(at, bytes, owner));
        }
    }
    // The decompressed body is the original byte decoder's actual extent.
    // A caller may publish it in its own native generation, but never persist
    // it as another launch input or replace its current-record provenance.
    internal ReadOnlyMemory<byte> CurrentBody() { RequireRecord(); return _body; }
    internal bool CurrentRecordCompressed { get { RequireRecord(); return _record!.IsCompressed; } }
    private void RequireRecord()
    { RequireCurrent(); if (_record is null) throw new InvalidOperationException("Native parser has no actual selected source record."); }
    private void ClearHeader() { _chunkType = 0; _chunkBytes = 0; }
    public void Dispose()
    {
        if (_disposed) return;
        if (Environment.CurrentManagedThreadId != _thread)
            throw new InvalidOperationException("Native selected-file retirement changed its owner thread.");
        _disposed = true; _lease.Dispose(); _record = null; _body = default;
        _chunks = new Dictionary<int, SourceChunk>(); _header = null; _bodySha = null;
    }
}
