using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

// One shared authority for reference modification masks. A package never
// creates a replacement map or treats source loading as an unconditional skip.
internal sealed partial class FalloutSourceReferenceChangeManager
{
    internal const string Schema = "opennv-source-reference-change-map/v2";
    private readonly Action<FalloutReferencePositionInvocation> _requireActual;
    private readonly Dictionary<uint, FalloutReferenceChangeEntry> _entries = [];
    private readonly List<FalloutReferenceChangeEntry> _pending = [];
    private readonly Dictionary<Guid, FalloutReferenceChangeData> _data = [];
    private readonly List<Guid>[] _buckets = Enumerable.Range(0, FalloutReferencePositionSource.ChangeBucketCount).Select(_ => new List<Guid>()).ToArray();
    private readonly List<FalloutReferenceChangePublication> _publications = [];
    private readonly List<FalloutReferenceChangeCall> _calls = [];
    private uint _managerFlags = FalloutReferencePositionSource.InitialChangeManagerFlags;
    private long _sequence, _fault;
    private int? _thread;
    private Scope? _current;
    private string? _writerBoundary;
    private bool _retired;
    internal FalloutReferencePositionSource Source { get; }
    internal string Stack { get; }
    internal Guid Process { get; }
    internal Guid Factory { get; } = Guid.NewGuid();
    internal Guid Constructor { get; }
    internal FalloutReferenceChangeCall? StartupFlagClear { get; private set; }
    internal string? SaveBlocker => _current is not null ? "source-reference-change-map-in-flight" :
        _retired ? "source-reference-change-map-retired" : _writerBoundary ?? _failure;

    internal FalloutSourceReferenceChangeManager(FalloutReferencePositionSource source, string stack,
        Guid process, Guid constructor, Action<FalloutReferencePositionInvocation> requireActual,
        FalloutReferenceChangeSnapshot? saved = null, Action? retainCurrentFrameThreadRead = null)
    {
        source.Validate(); ArgumentException.ThrowIfNullOrWhiteSpace(stack); ArgumentNullException.ThrowIfNull(requireActual);
        if (process == Guid.Empty || constructor == Guid.Empty)
            throw new InvalidDataException("Reference change manager lacks its actual shared constructor/process.");
        Source = source; Stack = stack; Process = process; Constructor = constructor; _requireActual = requireActual;
        _retainFrameThreadRead = retainCurrentFrameThreadRead;
        if (saved is not null)
        {
            Validate(saved, source, stack);
            if (saved.Process == process || saved.Factory == Factory || saved.Constructor == constructor || saved.Retired)
                throw new InvalidDataException("Cold change manager reused its process, constructor or retirement.");
            _sequence = saved.Sequence; _managerFlags = saved.ManagerFlags; _writerBoundary = saved.WriterBoundary;
            _publications.AddRange(saved.Publications); _calls.AddRange(saved.Calls.Select(CloneCall));
            _frameReceipts.Restore(saved.FrameRuns); _failure = _calls.FirstOrDefault(value => !value.Returned)?.Failure;
            foreach (var entry in saved.Entries) _entries.Add(entry.SourceFormId, entry);
            _pending.AddRange(saved.PendingEntries ?? []);
            foreach (var data in saved.Data ?? []) _data.Add(data.Identity, new(data));
            for (var index = 0; index < _buckets.Length; index++) _buckets[index].AddRange(saved.Buckets[index]);
            StartupFlagClear = _calls.LastOrDefault(value => value.Kind == "startup-manager-flag-one-clear");
        }
        _publications.Add(new(Process, Factory, Constructor, saved?.Process, saved?.Factory));
    }

    internal FalloutReferenceChangeCall ClearStartupSuppression(FalloutReferencePositionInvocation invocation)
    {
        if (StartupFlagClear is not null) throw new InvalidOperationException("Startup change-manager flag clear cannot replay.");
        if (invocation.Ingress != FalloutReferencePositionIngress.ConfiguredStartup)
            throw new InvalidOperationException("Startup change flag has another original caller.");
        return Run(invocation, "startup-manager-flag-one-clear", 0, false, scope =>
        {
            scope.Step = FalloutReferenceChangeStep.ManagerFlagStore;
            _managerFlags &= ~1u; Store(scope, FalloutReferenceChangeStep.ManagerFlagStore, _managerFlags);
        });
    }

    internal FalloutReferenceChangeCall ReferenceChanged(FalloutReferencePositionInvocation invocation,
        uint mask, bool force, IFalloutReferenceChangeConsumers consumers) => Run(invocation, "reference-change", mask, force, scope =>
    {
        ArgumentNullException.ThrowIfNull(consumers);
        if (consumers.Factory == Guid.Empty) throw new InvalidDataException("Reference change has no actual source consumer.");
        consumers.RequireCurrent(invocation); Guard(scope);
        if (_writerBoundary is not null) throw new NotSupportedException(_writerBoundary);
        scope.Step = FalloutReferenceChangeStep.ThreadFlags;
        scope.ThreadFlags = consumers.ReadThreadFlags(invocation); consumers.RequireCurrent(invocation); Guard(scope);
        var thread = scope.ThreadFlags;
        if (thread.Source != Source.Identity || thread.Process != Process || thread.Factory == Guid.Empty || thread.ConsumerFactory != consumers.Factory ||
            thread.Invocation != invocation.Call || thread.Thread != invocation.ThreadId || thread.Initialized != 1 || thread.Sequence < 1)
            throw new InvalidDataException("Reference change borrowed another source thread flag owner.");
        scope.Step = FalloutReferenceChangeStep.ManagerFlags;
        if (!force && ((thread.Flags & 1) != 0 || (_managerFlags & 1) != 0))
        { scope.Suppressed = true; return; }
        scope.Step = FalloutReferenceChangeStep.ReferenceFlags;
        scope.Flags = consumers.ReadReferenceFlags(invocation); consumers.RequireCurrent(invocation); Guard(scope);
        if (scope.Flags.Source != FalloutReferenceScriptExtraInitializerSource.Reviewed(Source.Fields).Identity || scope.Flags.Stack != Stack ||
            scope.Flags.Actor != invocation.Actor || scope.Flags.Process != invocation.CommonProcess || scope.Flags.Epoch != invocation.Epoch ||
            scope.Flags.Constructor != invocation.ReferenceConstructor || scope.Flags.Factory == Guid.Empty || scope.Flags.FieldsFactory == Guid.Empty)
            throw new InvalidDataException("Reference change borrowed another reference flag authority.");
        if ((scope.Flags.Flags & 0x4000) != 0) { scope.Suppressed = true; return; }
        scope.FormId = consumers.ReadSourceFormId(invocation); consumers.RequireCurrent(invocation); Guard(scope);
        if (scope.FormId == 0) throw new InvalidDataException("Reference change omitted its original source ID.");
        if ((scope.Flags.Flags & 0x20) != 0)
        {
            scope.Step = FalloutReferenceChangeStep.DataHandler;
            scope.DataHandler = consumers.ReadDataHandlerSuppression(invocation, scope.FormId.Value);
            consumers.RequireCurrent(invocation); Guard(scope);
            if (scope.DataHandler == true) { scope.Suppressed = true; return; }
        }
        scope.Suppressed = false; scope.Step = FalloutReferenceChangeStep.Lookup;
        _entries.TryGetValue(scope.FormId.Value, out var entry); scope.Before = entry;
        if (entry is null)
        {
            entry = new(Guid.NewGuid(), scope.FormId.Value, invocation.Actor.Reference, 0, null);
            _pending.Add(entry); scope.After = entry;
            Store(scope, FalloutReferenceChangeStep.EntryConstructed, 0, entry.Identity);
            Guard(scope);
            _entries.Add(entry.SourceFormId, entry); _buckets[Bucket(entry.SourceFormId)].Insert(0, entry.Identity);
            _pending.Remove(entry); Store(scope, FalloutReferenceChangeStep.Inserted, (uint)_entries.Count, entry.Identity);
        }
        else if (entry.Reference != invocation.Actor.Reference)
            throw new InvalidDataException("Reference change reused a source ID for another actual reference.");
        if (entry.SerializedData is null)
        {
            entry = entry with { Flags = entry.Flags | mask }; _entries[entry.SourceFormId] = entry;
            scope.After = entry; Store(scope, FalloutReferenceChangeStep.MaskStore, entry.Flags, entry.Identity);
        }
        else
        {
            if (!_data.ContainsKey(entry.SerializedData.Value)) throw new NotSupportedException("source-current-serialized-change-data-lease-unbound");
            scope.After = entry;
        }
        Guard(scope);
    });

    // Real retained data publication, separate from the original serializer's
    // broader layout and return. A nonnull pointer is never an arbitrary Guid.
    internal FalloutReferenceChangeCall PublishSerializedData(FalloutReferencePositionInvocation invocation,
        uint sourceFormId, FalloutReferenceChangeData data) => Run(invocation, "serialized-change-data-publication", 0, false, scope =>
        {
            ArgumentNullException.ThrowIfNull(data); scope.FormId = sourceFormId;
            if (!_entries.TryGetValue(sourceFormId, out var entry) || entry.Reference != invocation.Actor.Reference)
                throw new InvalidOperationException("Serialized change data has no actual current reference record.");
            scope.Before = entry;
            if (entry.SerializedData is not null)
                throw new NotSupportedException("source-current-serialized-change-data-replacement-and-retirement-unbound");
            if (_data.TryGetValue(data.Identity, out var prior) && !ReferenceEquals(prior, data))
                throw new InvalidOperationException("Serialized change data replaced its actual retained object.");
            _data[data.Identity] = data; entry = entry with { SerializedData = data.Identity };
            _entries[sourceFormId] = entry; scope.After = entry;
            Store(scope, FalloutReferenceChangeStep.DataStore, entry: entry.Identity, data: data.Identity);
        });

    internal FalloutReferenceChangeCall ObserveUnownedWriter(FalloutReferencePositionInvocation invocation, string boundary) =>
        Run(invocation, "unowned-change-manager-writer", 0, false, scope =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(boundary); scope.Writer = boundary;
            scope.Step = FalloutReferenceChangeStep.UnownedWriter; _writerBoundary = boundary;
        });

    private FalloutReferenceChangeCall Run(FalloutReferencePositionInvocation invocation, string kind,
        uint mask, bool force, Action<Scope> action)
    {
        ObjectDisposedException.ThrowIf(_retired, this);
        if (_current is not null) { _fault++; throw new InvalidOperationException("Shared reference change manager reentered."); }
        PrepareFrameChange(invocation, kind, mask, force);
        var scope = new Scope(invocation, kind, mask, force, ++_sequence, _managerFlags, _fault); _current = scope;
        try { Guard(scope); action(scope); Guard(scope); scope.Step = FalloutReferenceChangeStep.Returned; return Complete(scope, true, null); }
        catch (Exception error) { Complete(scope, false, string.IsNullOrWhiteSpace(error.Message) ? error.GetType().Name : error.Message); throw; }
        finally { _current = null; }
    }
    private FalloutReferenceChangeCall Complete(Scope scope, bool returned, string? failure)
    {
        var call = new FalloutReferenceChangeCall(scope.Invocation, Source.Identity, Factory, scope.Entered,
            checked(++_sequence), scope.Step, scope.Mask, scope.Force, scope.ManagerBefore, _managerFlags,
            scope.Flags, scope.ThreadFlags, scope.FormId, scope.Before, scope.After,
            Array.AsReadOnly(scope.Stores.ToArray()), returned, failure, scope.DataHandler, scope.Suppressed, scope.Writer, scope.Kind);
        RetainChangeCall(call); _failure ??= failure;
        if (scope.Kind == "startup-manager-flag-one-clear") StartupFlagClear = call; return call;
    }
    private void Store(Scope scope, FalloutReferenceChangeStep step, uint? bits = null, Guid? entry = null, Guid? data = null)
    { scope.Step = step; scope.Stores.Add(new(step, checked(++_sequence), bits, entry, data)); }
    private static int Bucket(uint formId) => checked((int)(formId % FalloutReferencePositionSource.ChangeBucketCount));
    private void Guard(Scope scope)
    {
        var invocation = scope.Invocation; invocation.Actor.Validate(); invocation.ValidatePackageCaller();
        if (_retired || _current != scope || _fault != scope.Fault || invocation.Call == Guid.Empty || invocation.ReferenceProcess != Process ||
            invocation.ReferenceConstructor == Guid.Empty || invocation.CommonProcess == Guid.Empty || invocation.Epoch < 1 ||
            invocation.ThreadId != Environment.CurrentManagedThreadId || _thread is { } thread && thread != invocation.ThreadId)
            throw new InvalidOperationException("Reference change lost its shared source owner, actual reference or current thread.");
        _requireActual(invocation);
        if (_retired || _current != scope || _fault != scope.Fault)
            throw new InvalidOperationException("Reference change guard swallowed reentry or replaced its actual manager.");
        _thread ??= invocation.ThreadId;
    }
    private sealed class Scope(FalloutReferencePositionInvocation invocation, string kind, uint mask, bool force,
        long entered, uint managerBefore, long fault)
    {
        internal FalloutReferencePositionInvocation Invocation = invocation;
        internal string Kind = kind;
        internal uint Mask = mask, ManagerBefore = managerBefore;
        internal bool Force = force;
        internal long Entered = entered, Fault = fault;
        internal FalloutReferenceChangeStep Step;
        internal FalloutReferenceFlagRead? Flags;
        internal FalloutSourceThreadReferenceFlagsRead? ThreadFlags;
        internal uint? FormId;
        internal FalloutReferenceChangeEntry? Before, After;
        internal bool? DataHandler, Suppressed;
        internal string? Writer;
        internal List<FalloutReferenceChangeStore> Stores = [];
    }
}
