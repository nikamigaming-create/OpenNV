namespace OpenNV.Runtime.Content;

// One actual contributor reader belongs to the campaign, rather than to each
// original module's projection. Native image/caller generations stay in the
// execution domain; this owner retains the real shared source-file lifetime.
internal sealed partial class FalloutNativePluginLoadedFiles : IDisposable
{
    internal sealed class Reader(FalloutPluginContext context, FalloutNativePluginLoadedFile file, ulong generation)
    {
        internal readonly FalloutPluginContext Context = context;
        internal readonly FalloutNativePluginLoadedFile File = file;
        internal readonly ulong Generation = generation;
        internal int Retainers;
        internal int Calls;
        internal bool Retired;
    }

    private readonly FalloutPluginStack _records;
    private readonly FalloutNativeBinaryFileConstruction _construction;
    private readonly Dictionary<FalloutPluginContext, Reader> _readers = [];
    private readonly List<Exception> _retirementFailures = [];
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private ulong _generation;
    private bool _disposed;

    internal int ReaderCount { get { RequireCurrent(); return _readers.Count; } }
    internal bool HasRetainedReaders => _readers.Count != 0;
    internal IReadOnlyList<Exception> RetirementFailures => _retirementFailures.AsReadOnly();
    internal string RuntimeSha256 => _construction.RuntimeSha256;

    internal FalloutNativePluginLoadedFiles(FalloutPluginStack records)
        : this(records, FalloutExecutableStringTable.ReadBinaryFileConstruction(
            records.OwnedSource?.FalloutExecutablePath ??
                throw new NotSupportedException("Loaded contributor construction needs the actual selected executable source."))) { }

    // Authored contracts may provide their own genuine first-party neutral
    // declaration and hashed source. Product graph construction uses the
    // source-detected overload above, never a package-name/default selector.
    internal FalloutNativePluginLoadedFiles(FalloutPluginStack records, FalloutNativeBinaryFileConstruction construction)
    {
        ArgumentNullException.ThrowIfNull(records); ArgumentNullException.ThrowIfNull(construction);
        construction.Require(); _records = records;
        _construction = construction with
        {
            InitialFields = construction.InitialFields.Select(field =>
                new FalloutNativeBinaryInitialField(field.Offset, field.Bytes.ToArray())).ToArray()
        };
    }

    internal Lease Retain(FalloutPluginContext context)
    {
        RequireCurrent(); RequireContext(context);
        if (_readers.TryGetValue(context, out var existing))
        {
            RequireReader(existing); existing.Retainers = checked(existing.Retainers + 1);
            return new(this, existing);
        }
        var generation = checked(_generation + 1);
        var file = new FalloutNativePluginLoadedFile(_records, context, _construction, _construction.ConstructorBufferCapacity);
        try
        {
            // Genuine selected TES4 construction consumes this contributor's
            // own original header/body, including its backend buffer progress.
            // A winning script/record is never substituted for loader state.
            file.SelectRecord(context.Plugin.Records.Single(record => record.Signature == "TES4"));
            if (!file.CurrentBody().IsEmpty)
                do { _ = file.NextChunk(); _ = file.ReadChunk(0); } while (file.AdvanceChunk());
            var reader = new Reader(context, file, generation) { Retainers = 1 };
            _readers.Add(context, reader); _generation = generation;
            return new(this, reader);
        }
        catch (Exception primary)
        {
            try { file.Dispose(); }
            catch (Exception cleanup)
            {
                var failure = new AggregateException("Loaded contributor construction/retirement failed.", primary, cleanup);
                _retirementFailures.Add(failure); throw failure;
            }
            throw;
        }
    }

    private void RequireCurrent()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (Environment.CurrentManagedThreadId != _thread)
            throw new InvalidOperationException("Loaded contributor collection left its actual campaign owner thread.");
        if (_retirementFailures.Count != 0)
            throw new AggregateException("Loaded contributor collection retains retirement failure.", _retirementFailures);
    }
    private void RequireContext(FalloutPluginContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!_records.Plugins.Contains(context) || !context.Plugin.NativeSourceAvailable)
            throw new InvalidDataException("Loaded contributor identity is not the exact selected source reader.");
    }
    private void RequireReader(Reader reader)
    {
        RequireCurrent(); RequireContext(reader.Context);
        if (reader.Retired || reader.Retainers <= 0 || !_readers.TryGetValue(reader.Context, out var actual) ||
            !ReferenceEquals(actual, reader) || reader.Generation == 0)
            throw new InvalidOperationException("Loaded contributor lease has a foreign or retired process generation.");
        reader.File.RequireCurrent();
    }
    private void Release(Reader reader)
    {
        // Cleanup does not invoke the healthy-file guard: a source/output
        // refusal still owns its handles and needs independent retirement.
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (Environment.CurrentManagedThreadId != _thread || reader.Retired || reader.Retainers <= 0 ||
            !_readers.TryGetValue(reader.Context, out var actual) || !ReferenceEquals(actual, reader))
            throw new InvalidOperationException("Loaded contributor lease retirement is foreign or repeated.");
        if (reader.Calls != 0)
            throw new InvalidOperationException("Loaded contributor cannot retire during its actual source invocation.");
        --reader.Retainers;
        if (reader.Retainers != 0) return;
        reader.Retired = true; _readers.Remove(reader.Context);
        try { reader.File.Dispose(); }
        catch (Exception failure) { _retirementFailures.Add(failure); throw; }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            if (_retirementFailures.Count != 0)
                throw new AggregateException("Loaded contributor collection retains retirement failure.", _retirementFailures);
            return;
        }
        if (Environment.CurrentManagedThreadId != _thread)
            throw new InvalidOperationException("Loaded contributor collection retirement changed its owner thread.");
        if (_readers.Values.Any(reader => reader.Calls != 0))
            throw new InvalidOperationException("Loaded contributor collection has an active source invocation.");
        var failures = new List<Exception>(_retirementFailures);
        foreach (var reader in _readers.Values.ToArray())
        {
            reader.Retired = true;
            try { reader.File.Dispose(); } catch (Exception failure) { failures.Add(failure); }
        }
        _readers.Clear(); _disposed = true;
        _retirementFailures.Clear(); _retirementFailures.AddRange(failures);
        if (failures.Count != 0) throw new AggregateException("Loaded contributor collection retired with retained failures.", failures);
    }
}
