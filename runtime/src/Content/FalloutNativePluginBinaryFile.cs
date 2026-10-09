using System.Buffers.Binary;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;
using OpenNV.Runtime.Compatibility.NativePlugins;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutNativeBinaryBufferRange(uint Offset, uint SourceOffset, uint Length);
internal sealed record FalloutNativeBinaryFileSnapshot(string Plugin, string SourceSha256, string RuntimeSha256,
    string ConstructionOwner, uint BufferCapacity, uint BinaryOffset, uint BackendOffset, uint LogicalOffset,
    uint CachedSize, uint BufferBytes, uint BufferConsumed, byte[] WrittenBuffer, uint WrittenExtent,
    IReadOnlyList<FalloutNativeBinaryBufferRange> BufferSources, long Revision, bool Good);

// The actual selected binary reader owns backend, buffered and logical cursors
// separately. It exposes no original CRT FILE* or guessed native class image.
internal sealed partial class FalloutNativePluginBinaryFile : IDisposable
{
    private readonly FalloutPluginStack _records;
    private readonly FalloutPluginContext _context;
    private readonly FalloutNativeBinaryFileConstruction _construction;
    private readonly FileStream _input;
    private readonly FileStream _runtime;
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private readonly byte[] _buffer;
    private readonly uint[] _origins;
    private uint _binaryOffset, _backendOffset, _logicalOffset, _cachedSize;
    private uint _valid, _consumed, _written;
    private long _revision;
    private bool _good, _disposed;
    private Exception? _fault;
    private FalloutNativeBinaryTransfer? _pending;
    private ulong _nextTransfer;

    internal string SourceSha256 => _context.Sha256;
    internal string Plugin => _context.Plugin.Name;
    internal string SourcePath => _context.Plugin.Path;
    internal SafeFileHandle SourceHandle { get { RequireCurrent(); return _input.SafeFileHandle; } }
    internal ulong SourceLength { get { RequireCurrent(); return checked((ulong)_input.Length); } }
    internal uint Capacity => checked((uint)_buffer.Length);
    internal uint LogicalOffset { get { RequireCurrent(); return _logicalOffset; } }
    internal bool Good { get { RequireCurrent(); return _good; } }
    internal FalloutNativeBinaryFileConstruction Construction => _construction;

    internal FalloutNativePluginBinaryFile(FalloutPluginStack records, FalloutPluginContext context,
        FalloutNativeBinaryFileConstruction construction, uint bufferCapacity)
    {
        _records = records; _context = context;
        construction.Require();
        _construction = construction with { InitialFields = construction.InitialFields.Select(field =>
            new FalloutNativeBinaryInitialField(field.Offset, field.Bytes.ToArray())).ToArray() };
        if (!records.Plugins.Contains(context) || !context.Plugin.NativeSourceAvailable)
            throw new InvalidDataException("Binary file is not an actual selected contributor reader.");
        if (context.Bytes is < 0 or > uint.MaxValue || bufferCapacity > int.MaxValue)
            throw new NotSupportedException("Selected binary extent/buffer has no admitted UInt32/native allocation owner.");
        if (construction.ReadMode != 0)
            throw new NotSupportedException("Selected binary constructor is missing its read-only mode/open association.");
        _input = new(context.Plugin.Path, FileMode.Open, FileAccess.Read, FileShare.Read);
        FileStream? runtime = null;
        try
        {
            if (_input.Length != context.Bytes || !StringComparer.OrdinalIgnoreCase.Equals(
                Convert.ToHexString(SHA256.HashData(_input)), context.Sha256))
                throw new InvalidDataException("Binary reader changed the exact original contributor bytes.");
            runtime = new(construction.RuntimePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (!StringComparer.OrdinalIgnoreCase.Equals(Convert.ToHexString(SHA256.HashData(runtime)), construction.RuntimeSha256))
                throw new InvalidDataException("Binary constructor source changed before reader publication.");
            _runtime = runtime;
            if (new[] { 4, 0x14, 0x18, 0x1c, 0x150, 0x154 }.Any(at => construction.Word(at) != 0))
                throw new NotSupportedException("Selected binary construction needs its nonempty initial cursor/buffer producer.");
            _buffer = new byte[checked((int)bufferCapacity)];
            _origins = new uint[checked((int)bufferCapacity)];
            _good = true; RequireCurrent();
        }
        catch (Exception first)
        {
            var failures = new List<Exception> { first };
            try { runtime?.Dispose(); } catch (Exception cleanup) { failures.Add(cleanup); }
            try { _input.Dispose(); } catch (Exception cleanup) { failures.Add(cleanup); }
            if (failures.Count != 1) throw new AggregateException("Binary source construction/retirement failed.", failures);
            throw;
        }
    }

    internal void RequireCurrent()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (Environment.CurrentManagedThreadId != _thread || !_records.Plugins.Contains(_context) ||
            !_context.Plugin.NativeSourceAvailable || _input.SafeFileHandle.IsClosed || _runtime.SafeFileHandle.IsClosed || _input.Length != _context.Bytes)
            throw new InvalidOperationException("Selected binary file thread/source/reader lifetime changed.");
        if (_fault is { } fault) throw new InvalidOperationException("Selected binary reader retains an unfinished native prefix.", fault);
        if (_valid > Capacity || _consumed > _valid || _written > Capacity || _valid > _written)
            throw new InvalidDataException("Binary buffer ownership is internally inconsistent.");
    }

    internal void RequireIdle()
    {
        RequireCurrent();
        if (_pending is not null) throw new InvalidOperationException("Binary source still owns an incomplete output transfer.");
    }

    internal uint Size()
    {
        RequireIdle();
        // The source size query preserves the current backend/buffer position.
        // An empty source remains a genuine zero-size measurement.
        _cachedSize = checked((uint)_input.Length); _revision = checked(_revision + 1);
        return _cachedSize;
    }

    internal void Seek(uint argument, uint sourceOrigin)
    {
        SeekMember(argument, sourceOrigin);
    }

    internal FalloutNativeBinaryTransfer Read(uint requested) => ReadCore(requested, true);

    private FalloutNativeBinaryTransfer ReadCore(uint requested, bool updateLogical)
    {
        RequireIdle();
        var pieces = new List<BinaryPiece>(); var count = 0U;
        try
        {
            if (_good)
            {
                var available = checked(_valid - _consumed);
                if (requested <= available)
                {
                    if (requested != 0) pieces.Add(BinaryPiece.Memory(_buffer.AsMemory(checked((int)_consumed), checked((int)requested)).ToArray()));
                    _consumed = checked(_consumed + requested); count = requested;
                }
                else
                {
                    if (available != 0)
                    {
                        pieces.Add(BinaryPiece.Memory(_buffer.AsMemory(checked((int)_consumed), checked((int)available)).ToArray()));
                        count = available;
                    }
                    _valid = _consumed = 0;
                    var remaining = checked(requested - available);
                    if (remaining > Capacity)
                    {
                        var actual = Readable(_backendOffset, remaining);
                        if (actual != 0) pieces.Add(BinaryPiece.Source(_backendOffset, actual));
                        _backendOffset = unchecked(_backendOffset + actual); count = checked(count + actual);
                    }
                    else
                    {
                        var actual = Readable(_backendOffset, Capacity);
                        if (actual != 0) ReadOriginal(_backendOffset, _buffer.AsSpan(0, checked((int)actual)));
                        for (var index = 0U; index < actual; ++index) _origins[checked((int)index)] = checked(_backendOffset + index);
                        _backendOffset = unchecked(_backendOffset + actual); _valid = actual;
                        _written = Math.Max(_written, actual); _consumed = Math.Min(remaining, actual);
                        if (_consumed != 0) pieces.Add(BinaryPiece.Memory(_buffer.AsMemory(0, checked((int)_consumed)).ToArray()));
                        count = checked(count + _consumed);
                    }
                }
                if (updateLogical) _logicalOffset = unchecked(_logicalOffset + count); _revision = checked(_revision + 1);
            }
            var transfer = new FalloutNativeBinaryTransfer(this, checked(++_nextTransfer), requested, count, pieces);
            _pending = transfer; return transfer;
        }
        catch (Exception error) { _fault = error; throw; }
    }

    internal IReadOnlyList<NativeNvseDataField> NativeFields()
    {
        RequireCurrent();
        // Callable pointers, real native buffer, CRT FILE*, allocation padding
        // and untouched path tails have separate missing owners. No whole
        // object publication can turn these partial fields into a complete class.
        var fields = new List<NativeNvseDataField>
        {
            Word(4, _binaryOffset, "actual-binary-seek-offset"), Word(0x10, Capacity, "actual-owned-buffer-capacity"),
            Word(0x14, _valid, "actual-read-buffer-bytes"), Word(0x18, _consumed, "actual-read-buffer-consumption"),
            Word(0x1c, _backendOffset, "actual-read-only-backend-position"),
            Word(0x28, _construction.ReadMode, "selected-source-read-mode"),
            new(0x2c, new byte[] { _good ? (byte)1 : (byte)0 }, "actual-read-only-open-result"),
            Word(0x150, _logicalOffset, "actual-binary-logical-read-position"),
            Word(0x154, _cachedSize, "actual-cursor-preserving-size-query"),
        };
        foreach (var at in new[] { 0x30, 0x34, 0x38, 0x3c, 0x40, 0x148, 0x14c })
            fields.Add(new(at, _construction.InitialFields.Single(field => field.Offset == at).Bytes.ToArray(),
                _construction.DeclarationOwner + ":actual-initial-scalar/no-admitted-mutator"));
        var name = AnsiPath(_context.Plugin.Path);
        fields.Add(new(0x44, name.Concat(new byte[] { 0 }).ToArray(), "actual-selected-terminated-binary-path-prefix"));
        return fields;
    }

    internal ReadOnlyMemory<byte> WrittenBuffer()
    { RequireCurrent(); return _buffer.AsMemory(0, checked((int)_written)).ToArray(); }

    internal FalloutNativeBinaryFileSnapshot Capture()
    {
        RequireIdle(); return new(Plugin, SourceSha256, _construction.RuntimeSha256, _construction.DeclarationOwner,
            Capacity, _binaryOffset, _backendOffset, _logicalOffset, _cachedSize, _valid, _consumed,
            WrittenBuffer().ToArray(), _written, BufferRanges(), _revision, _good);
    }

    internal void Restore(FalloutNativeBinaryFileSnapshot snapshot)
    {
        RequireIdle(); ArgumentNullException.ThrowIfNull(snapshot);
        if (!StringComparer.OrdinalIgnoreCase.Equals(snapshot.Plugin, Plugin) ||
            !StringComparer.OrdinalIgnoreCase.Equals(snapshot.SourceSha256, SourceSha256) ||
            !StringComparer.OrdinalIgnoreCase.Equals(snapshot.RuntimeSha256, _construction.RuntimeSha256) ||
            snapshot.ConstructionOwner != _construction.DeclarationOwner || snapshot.BufferCapacity != Capacity ||
            snapshot.Revision < 0 || !snapshot.Good || snapshot.WrittenBuffer is null ||
            snapshot.BufferSources is null ||
            snapshot.WrittenExtent != snapshot.WrittenBuffer.Length || snapshot.WrittenExtent > Capacity ||
            snapshot.BufferBytes > snapshot.WrittenExtent || snapshot.BufferConsumed > snapshot.BufferBytes ||
            snapshot.CachedSize != 0 && snapshot.CachedSize != _input.Length)
            throw new InvalidDataException("Binary cold state changed source/construction/buffer/cursor ownership.");
        var candidate = snapshot.WrittenBuffer.ToArray(); var candidateOrigins = new uint[checked((int)snapshot.WrittenExtent)];
        var next = 0U;
        foreach (var range in snapshot.BufferSources)
        {
            if (range is null || range.Length == 0 || range.Offset != next || range.Length > snapshot.WrittenExtent - next ||
                (ulong)range.SourceOffset + range.Length > SourceLength)
                throw new InvalidDataException("Binary retained buffer lost exact original byte provenance.");
            var actual = new byte[checked((int)range.Length)]; ReadOriginal(range.SourceOffset, actual);
            if (!actual.AsSpan().SequenceEqual(candidate.AsSpan(checked((int)range.Offset), actual.Length)))
                throw new InvalidDataException("Binary cold written buffer differs from its complete original source range.");
            for (var index = 0U; index < range.Length; ++index)
                candidateOrigins[checked((int)(range.Offset + index))] = checked(range.SourceOffset + index);
            next = checked(next + range.Length);
        }
        if (next != snapshot.WrittenExtent) throw new InvalidDataException("Binary cold buffer has unaccounted written bytes.");
        // The complete written buffer is persisted because consumed prefixes
        // and old refill tails are real retained state. Active output leases
        // and process-local pointers never enter this snapshot.
        if (snapshot.BufferBytes != 0)
        {
            if (snapshot.BackendOffset < snapshot.BufferBytes) throw new InvalidDataException("Binary buffer precedes its actual source.");
            var original = new byte[checked((int)snapshot.BufferBytes)];
            ReadOriginal(checked(snapshot.BackendOffset - snapshot.BufferBytes), original);
            if (!original.AsSpan().SequenceEqual(snapshot.WrittenBuffer.AsSpan(0, original.Length)))
                throw new InvalidDataException("Binary retained live buffer differs from original source bytes.");
            var logical = unchecked(snapshot.BackendOffset - snapshot.BufferBytes + snapshot.BufferConsumed);
            if (logical != snapshot.LogicalOffset) throw new InvalidDataException("Binary cold logical/buffer positions disagree.");
        }
        else if (snapshot.LogicalOffset != snapshot.BackendOffset)
            throw new InvalidDataException("Binary cold direct/backend positions disagree.");
        // All validation completes before any owner state changes.
        candidate.CopyTo(_buffer, 0); candidateOrigins.CopyTo(_origins, 0); _written = snapshot.WrittenExtent;
        _binaryOffset = snapshot.BinaryOffset; _backendOffset = snapshot.BackendOffset;
        _logicalOffset = snapshot.LogicalOffset; _cachedSize = snapshot.CachedSize;
        _valid = snapshot.BufferBytes; _consumed = snapshot.BufferConsumed; _revision = snapshot.Revision; _good = snapshot.Good;
    }

    private IReadOnlyList<FalloutNativeBinaryBufferRange> BufferRanges()
    {
        var ranges = new List<FalloutNativeBinaryBufferRange>(); var at = 0U;
        while (at < _written)
        {
            var length = 1U; var source = _origins[checked((int)at)];
            while (at + length < _written && (ulong)source + length <= uint.MaxValue &&
                _origins[checked((int)(at + length))] == source + length) ++length;
            ranges.Add(new(at, source, length)); at = checked(at + length);
        }
        return ranges;
    }

    private uint Readable(uint at, uint requested)
        => at >= _input.Length ? 0 : checked((uint)Math.Min((long)requested, _input.Length - at));
    private void ReadOriginal(uint offset, Span<byte> output)
    {
        RequireCurrent();
        if ((ulong)offset + checked((uint)output.Length) > checked((ulong)_input.Length))
            throw new InvalidDataException("Binary read exceeds the original selected source extent.");
        var done = 0;
        while (done < output.Length)
        {
            var actual = RandomAccess.Read(_input.SafeFileHandle, output[done..], checked((long)offset + done));
            if (actual == 0) throw new EndOfStreamException("Selected binary input lost a declared byte extent.");
            done = checked(done + actual);
        }
    }
    private static NativeNvseDataField Word(int at, uint value, string owner)
    { var bytes = new byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(bytes, value); return new(at, bytes, owner); }
    internal static byte[] AnsiPath(string path)
    {
        if (path.Length >= 260 || path.Any(character => character is '\0' or > (char)127))
            throw new NotSupportedException("Binary native path needs the selected ANSI/long-path conversion owner.");
        return System.Text.Encoding.ASCII.GetBytes(path);
    }
    public void Dispose()
    {
        if (_disposed) return;
        if (Environment.CurrentManagedThreadId != _thread) throw new InvalidOperationException("Binary source retirement changed its owner thread.");
        var failures = new List<Exception>();
        if (_fault is { } fault) failures.Add(fault);
        if (_pending is { Completed: false } transfer)
            failures.Add(new InvalidOperationException($"Binary retirement retains incomplete transfer {transfer.Id}/{transfer.Position}/{transfer.Actual}."));
        try { _input.Dispose(); } catch (Exception error) { failures.Add(error); }
        try { _runtime.Dispose(); } catch (Exception error) { failures.Add(error); }
        _disposed = true; _good = false;
        if (failures.Count != 0) throw new AggregateException("Binary source retired with retained output/cleanup failures.", failures);
    }
}
