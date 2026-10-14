using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal enum FalloutDataHandlerLoadingIngress
{ Construction, PackageCleanup, RecordLoad, TesTeardown, DataHandlerUnload, TransientFileEnter, TransientFileReturn, CopyOrLoad, NativeCallback, ScriptCallback }
internal sealed record FalloutDataHandlerLoadingInvocation(Guid Call, Guid RuntimeProcess, Guid CommonProcess,
    FalloutCombatActorIdentity? Actor, long Epoch, int ThreadId, FalloutDataHandlerLoadingIngress Ingress,
    Guid? PackageCommand = null);
internal sealed record FalloutDataHandlerLoadingRead(string Source, string Stack, Guid RuntimeProcess,
    Guid Process, Guid Factory, Guid Constructor, long Sequence, byte Value,
    FalloutDataHandlerLoadingInvocation Invocation);
internal sealed record FalloutDataHandlerLoadingPublication(Guid RuntimeProcess, Guid Process, Guid Factory,
    Guid Constructor, Guid? PreviousProcess = null, Guid? PreviousFactory = null, long PublishedSequence = 0);
internal sealed record FalloutDataHandlerRecordLoadPrefix(Guid Receipt, Guid RuntimeProcess, Guid Factory,
    string RecordsSource, long Changed, string Boundary, byte BeforeValue = 0,
    bool AvailableBefore = true, string? BoundaryBefore = null);
internal sealed record FalloutDataHandlerLoadingWriter(FalloutDataHandlerLoadingInvocation Invocation,
    Guid Factory, long Changed, byte Before, byte After, bool AvailableBefore, bool AvailableAfter, string? Boundary);
internal sealed record FalloutDataHandlerLoadingSnapshot(string Schema, FalloutActorPackageAssignmentSource Source,
    string Stack, Guid RuntimeProcess, Guid Process, Guid Factory, Guid Constructor, long Sequence,
    byte Value, bool Available, string? Boundary, IReadOnlyList<FalloutDataHandlerLoadingPublication> Publications,
    IReadOnlyList<FalloutDataHandlerLoadingWriter> Writers,
    IReadOnlyList<FalloutDataHandlerRecordLoadPrefix>? RecordPrefixes = null,
    IReadOnlyList<FalloutDataHandlerRecordPreservation>? RecordPreservations = null,
    IReadOnlyList<FalloutDataHandlerIndexedRecordsBinding>? RecordBindings = null);

// This is the one source DataHandler byte. Original record loading is not
// admitted merely because OpenNV's record reader returned or TES exists.
internal sealed partial class FalloutSourceDataHandlerLoadingState
{
    internal const string Schema = "opennv-source-DataHandler-loading/v2";
    internal const string RecordLoadBoundary = "source-DataHandler-loading-pre-command-record-load-writer-prefix-unqualified";
    private readonly Action<FalloutDataHandlerLoadingInvocation> _requireActual;
    private readonly List<FalloutDataHandlerLoadingPublication> _publications = [];
    private readonly List<FalloutDataHandlerLoadingWriter> _writers = [];
    private readonly List<FalloutDataHandlerRecordLoadPrefix> _recordPrefixes = [];
    private readonly HashSet<Guid> _ids = [];
    private byte _value;
    private bool _available = true, _busy;
    private string? _boundary;
    private long _sequence, _fault;
    private int? _thread;
    internal FalloutActorPackageAssignmentSource Source { get; }
    internal string Stack { get; }
    internal Guid RuntimeProcess { get; }
    internal Guid Process { get; } = Guid.NewGuid();
    internal Guid Factory { get; } = Guid.NewGuid();
    internal Guid Constructor { get; }
    internal string? SaveBlocker => _busy ? "source-DataHandler-loading-writer-entered" : _boundary;

    internal FalloutSourceDataHandlerLoadingState(FalloutActorPackageAssignmentSource source, string stack,
        Guid actualRuntimeProcess, Guid actualConstructor, Action<FalloutDataHandlerLoadingInvocation> requireActual,
        FalloutDataHandlerLoadingSnapshot? saved = null)
    {
        source.Validate(); ArgumentException.ThrowIfNullOrWhiteSpace(stack); ArgumentNullException.ThrowIfNull(requireActual);
        if (actualRuntimeProcess == Guid.Empty || actualConstructor == Guid.Empty)
            throw new InvalidDataException("DataHandler loading needs its actual source runtime and constructor owners.");
        Source = source; Stack = stack; RuntimeProcess = actualRuntimeProcess; Constructor = actualConstructor; _requireActual = requireActual;
        if (saved is not null)
        {
            Validate(saved, source, stack);
            if (saved.Process == Process || saved.RuntimeProcess == RuntimeProcess || saved.Constructor == Constructor)
                throw new InvalidDataException("Cold DataHandler loading reused its original runtime/field/constructor lifetime.");
            _value = saved.Value; _available = saved.Available; _boundary = saved.Boundary; _sequence = saved.Sequence;
            _publications.AddRange(saved.Publications); _writers.AddRange(saved.Writers);
            _recordPrefixes.AddRange(saved.RecordPrefixes ?? []);
            _recordPreservations.AddRange(saved.RecordPreservations ?? []);
            _recordBindings.AddRange(saved.RecordBindings ?? []);
            foreach (var writer in _writers) _ids.Add(writer.Invocation.Call);
            foreach (var prefix in _recordPrefixes) _ids.Add(prefix.Receipt);
            foreach (var preservation in _recordPreservations) _ids.Add(preservation.Admission);
        }
        _publications.Add(new(RuntimeProcess, Process, Factory, Constructor, saved?.Process, saved?.Factory, _sequence));
    }

    // The owned-file reader can already have returned before this independent
    // source field is composed. This receipt records that real input boundary;
    // it never asserts an executed original loader entry or loader return.
    internal FalloutDataHandlerRecordLoadPrefix RetainUnqualifiedRecordLoadPrefix(Guid actualReceipt,
        string recordsSource, Action requireActualRecordPrefix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recordsSource); ArgumentNullException.ThrowIfNull(requireActualRecordPrefix);
        if (_busy) { ++_fault; throw new InvalidOperationException("DataHandler loading reentered its input prefix."); }
        if (actualReceipt == Guid.Empty || _ids.Contains(actualReceipt))
            throw new InvalidOperationException("DataHandler input prefix omitted or replayed its actual receipt.");
        _busy = true; var fault = _fault;
        try
        {
            requireActualRecordPrefix();
            if (_fault != fault) throw new InvalidOperationException("DataHandler input-prefix guard swallowed a real reentry failure.");
            var prefix = new FalloutDataHandlerRecordLoadPrefix(actualReceipt, RuntimeProcess, Factory, recordsSource, ++_sequence,
                RecordLoadBoundary, _value, _available, _boundary);
            _ids.Add(actualReceipt); _recordPrefixes.Add(prefix); _available = false; _boundary = prefix.Boundary; return prefix;
        }
        finally { _busy = false; }
    }

    internal FalloutDataHandlerLoadingRead Read(FalloutDataHandlerLoadingInvocation invocation)
    {
        Guard(invocation);
        if (invocation.Ingress != FalloutDataHandlerLoadingIngress.PackageCleanup || invocation.PackageCommand is null)
            throw new InvalidOperationException("DataHandler loading read lost its reached package-cleanup child.");
        if (!_available) throw new NotSupportedException(_boundary);
        var read = new FalloutDataHandlerLoadingRead(Source.Identity, Stack, RuntimeProcess, Process, Factory,
            Constructor, _sequence, _value, invocation);
        Guard(invocation); return read;
    }

    internal FalloutDataHandlerLoadingWriter ObserveEnteredWriter(FalloutDataHandlerLoadingInvocation invocation)
    {
        if (invocation.Ingress is FalloutDataHandlerLoadingIngress.PackageCleanup or FalloutDataHandlerLoadingIngress.Construction)
            throw new InvalidOperationException("A loading read/constructor cannot invalidate another source writer.");
        return Write(invocation, () =>
        {
            // Qualified literal writers retain the literal before their next
            // child. Record/copy/native/script ingress has no current literal.
            return invocation.Ingress switch
            {
                FalloutDataHandlerLoadingIngress.TesTeardown or FalloutDataHandlerLoadingIngress.DataHandlerUnload or
                    FalloutDataHandlerLoadingIngress.TransientFileEnter => ((byte)1, true, (string?)null),
                FalloutDataHandlerLoadingIngress.TransientFileReturn => ((byte)0, true, (string?)null),
                FalloutDataHandlerLoadingIngress.RecordLoad => (_value, false, RecordLoadBoundary),
                _ => (_value, false, "source-DataHandler-loading-" + invocation.Ingress + "-writer-unowned")
            };
        });
    }

    private FalloutDataHandlerLoadingWriter Write(FalloutDataHandlerLoadingInvocation invocation,
        Func<(byte Value, bool Available, string? Boundary)> operation)
    {
        if (_busy) { ++_fault; throw new InvalidOperationException("DataHandler loading reentered its current writer."); }
        Guard(invocation);
        if (!_ids.Add(invocation.Call)) throw new InvalidOperationException("DataHandler loading writer replayed its original call.");
        _busy = true; var fault = _fault;
        try
        {
            Guard(invocation, true); var after = operation();
            if (_fault != fault) throw new InvalidOperationException("DataHandler loading guard swallowed a real reentry failure.");
            var writer = new FalloutDataHandlerLoadingWriter(invocation, Factory, ++_sequence, _value, after.Value,
                _available, after.Available, after.Boundary);
            _value = after.Value; _available = after.Available; _boundary = after.Boundary; _writers.Add(writer); return writer;
        }
        finally { _busy = false; }
    }
    private void Guard(FalloutDataHandlerLoadingInvocation invocation, bool entered = false)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        if (_busy && !entered) { ++_fault; throw new InvalidOperationException("DataHandler loading borrowed an entered source writer."); }
        if (invocation.Call == Guid.Empty || invocation.RuntimeProcess != RuntimeProcess || invocation.CommonProcess == Guid.Empty ||
            invocation.Epoch < 1 || invocation.ThreadId != Environment.CurrentManagedThreadId || !Enum.IsDefined(invocation.Ingress) ||
            invocation.PackageCommand == Guid.Empty || (invocation.Ingress == FalloutDataHandlerLoadingIngress.PackageCleanup) != (invocation.PackageCommand is not null) ||
            invocation.Ingress == FalloutDataHandlerLoadingIngress.PackageCleanup && invocation.Actor is null ||
            _thread is { } thread && thread != invocation.ThreadId)
            throw new InvalidOperationException("DataHandler loading lost its actual independent runtime/Common/thread/caller owners.");
        var fault = _fault; _requireActual(invocation);
        if (_fault != fault) throw new InvalidOperationException("DataHandler loading callback caught a real reentry failure.");
        _thread ??= invocation.ThreadId;
    }

    internal FalloutDataHandlerLoadingSnapshot Capture()
    {
        if (_busy) { ++_fault; throw new InvalidOperationException("Entered loading writer cannot be captured."); }
        var saved = new FalloutDataHandlerLoadingSnapshot(Schema, Source, Stack, RuntimeProcess, Process, Factory,
            Constructor, _sequence, _value, _available, _boundary, Array.AsReadOnly(_publications.ToArray()), Array.AsReadOnly(_writers.ToArray()),
            Array.AsReadOnly(_recordPrefixes.ToArray()), Array.AsReadOnly(_recordPreservations.ToArray()),
            Array.AsReadOnly(_recordBindings.ToArray()));
        Validate(saved, Source, Stack); return saved;
    }
    internal static void Validate(FalloutDataHandlerLoadingSnapshot saved, FalloutActorPackageAssignmentSource source, string stack)
    {
        source.Validate(); ArgumentNullException.ThrowIfNull(saved);
        if (saved.Schema != Schema || saved.Source != source || saved.Stack != stack || saved.Sequence < 0 ||
            saved.Publications is not { Count: > 0 } || saved.Writers is null)
            throw new InvalidDataException("DataHandler loading omitted its constructor/source/cold history.");
        var processes = new Dictionary<Guid, FalloutDataHandlerLoadingPublication>(); var factories = new HashSet<Guid>();
        var constructors = new HashSet<Guid>(); var runtimes = new HashSet<Guid>();
        FalloutDataHandlerLoadingPublication? previous = null;
        foreach (var publication in saved.Publications)
        {
            if (publication.RuntimeProcess == Guid.Empty || publication.Process == Guid.Empty || publication.Factory == Guid.Empty ||
                publication.Constructor == Guid.Empty || !constructors.Add(publication.Constructor) || !runtimes.Add(publication.RuntimeProcess) ||
                !processes.TryAdd(publication.Process, publication) || !factories.Add(publication.Factory) ||
                publication.PublishedSequence < (previous?.PublishedSequence ?? 0) || publication.PublishedSequence > saved.Sequence ||
                previous is null && publication.PublishedSequence != 0 ||
                publication.PreviousProcess != previous?.Process || publication.PreviousFactory != previous?.Factory)
                throw new InvalidDataException("DataHandler loading replaced its actual independent cold lifetime.");
            previous = publication;
        }
        if (previous!.RuntimeProcess != saved.RuntimeProcess || previous.Process != saved.Process || previous.Factory != saved.Factory || previous.Constructor != saved.Constructor)
            throw new InvalidDataException("DataHandler loading omitted its current actual publication.");
        byte value = 0; var available = true; string? boundary = null; long sequence = 0;
        var ids = new HashSet<Guid>();
        var prefixes = (saved.RecordPrefixes ?? []).ToDictionary(prefix => prefix.Changed);
        var writers = saved.Writers.ToDictionary(writer => writer.Changed);
        var preservations = (saved.RecordPreservations ?? []).ToDictionary(item => item.Changed);
        var publications = saved.Publications.ToDictionary(item => item.Factory);
        var publicationEnds = new Dictionary<Guid, long>();
        for (var index = 0; index < saved.Publications.Count; ++index)
            publicationEnds.Add(saved.Publications[index].Factory,
                index + 1 < saved.Publications.Count ? saved.Publications[index + 1].PublishedSequence : saved.Sequence);
        ValidateRecordBindings(saved, publications, publicationEnds);
        var bindings = (saved.RecordBindings ?? []).ToDictionary(item => item.Factory);
        var prefixReceipts = (saved.RecordPrefixes ?? []).ToDictionary(item => item.Receipt);
        var events = prefixes.Keys.Concat(writers.Keys).Concat(preservations.Keys).ToArray();
        if (events.Distinct().Count() != events.Length) throw new InvalidDataException("DataHandler loading reused a current mutation sequence.");
        foreach (var changed in events.Order())
        {
            if (prefixes.TryGetValue(changed, out var prefix))
            {
                if (writers.ContainsKey(changed) || prefix.Receipt == Guid.Empty || !ids.Add(prefix.Receipt) || prefix.Changed != ++sequence ||
                    string.IsNullOrWhiteSpace(prefix.RecordsSource) || prefix.Boundary != RecordLoadBoundary ||
                    !publications.TryGetValue(prefix.Factory, out var prefixPublication) || prefixPublication.RuntimeProcess != prefix.RuntimeProcess ||
                    prefix.Changed <= prefixPublication.PublishedSequence || prefix.Changed > publicationEnds[prefix.Factory] ||
                    prefix.BeforeValue != value || prefix.AvailableBefore != available || prefix.BoundaryBefore != boundary)
                    throw new InvalidDataException("DataHandler input prefix fabricated an original call or replaced its real input boundary.");
                available = false; boundary = prefix.Boundary; continue;
            }
            if (preservations.TryGetValue(changed, out var preservation))
            {
                ValidateRecordPreservation(preservation, saved, publications, publicationEnds, prefixReceipts, bindings,
                    sequence, value, available, boundary);
                if (!ids.Add(preservation.Admission)) throw new InvalidDataException("DataHandler records preservation replayed its admission.");
                var admittedPrefix = prefixReceipts[preservation.Prefix];
                available = admittedPrefix.AvailableBefore; boundary = admittedPrefix.BoundaryBefore; ++sequence; continue;
            }
            var writer = writers[changed];
            var invocation = writer.Invocation;
            if (!publications.TryGetValue(writer.Factory, out var publication) || invocation.Call == Guid.Empty || !ids.Add(invocation.Call) || invocation.RuntimeProcess != publication.RuntimeProcess ||
                writer.Changed <= publication.PublishedSequence || writer.Changed > publicationEnds[writer.Factory] ||
                invocation.CommonProcess == Guid.Empty || invocation.Epoch < 1 || invocation.ThreadId < 1 ||
                !Enum.IsDefined(invocation.Ingress) || invocation.Ingress is FalloutDataHandlerLoadingIngress.Construction or FalloutDataHandlerLoadingIngress.PackageCleanup ||
                invocation.PackageCommand is not null || writer.Changed != ++sequence || writer.Before != value || writer.AvailableBefore != available)
                throw new InvalidDataException("DataHandler loading reordered or replayed a source writer.");
            var literal = invocation.Ingress switch
            {
                FalloutDataHandlerLoadingIngress.TesTeardown or FalloutDataHandlerLoadingIngress.DataHandlerUnload or FalloutDataHandlerLoadingIngress.TransientFileEnter => (byte?)1,
                FalloutDataHandlerLoadingIngress.TransientFileReturn => (byte?)0,
                _ => null
            };
            if (literal is { } actual ? writer.After != actual || !writer.AvailableAfter || writer.Boundary is not null :
                writer.After != value || writer.AvailableAfter || string.IsNullOrWhiteSpace(writer.Boundary))
                throw new InvalidDataException("DataHandler loading invented a current literal or omitted an entered writer barrier.");
            value = writer.After; available = writer.AvailableAfter; boundary = writer.Boundary;
        }
        if (saved.Sequence != sequence || saved.Value != value || saved.Available != available || saved.Boundary != boundary)
            throw new InvalidDataException("DataHandler loading current byte differs from its actual constructor/writers.");
    }
}
