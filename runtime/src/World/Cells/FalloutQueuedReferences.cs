using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

// All map and lease mutations use one C# lock. The original's lock-free
// container is an implementation mechanism; its key/value retention, unique
// insertion and matched retirement are the observable contract here.
internal sealed partial class FalloutQueuedReferences : IDisposable
{
    internal const string Schema = "opennv-source-queued-references/v1";
    private readonly object _gate = new();
    private readonly FalloutActorProcessQueueDeclaration _source;
    private readonly string _stack;
    private readonly Func<FalloutFormKey, FalloutQueuedReferenceSource> _identity;
    private readonly Action<Guid, int>? _reprioritize;
    private readonly Guid _process = Guid.NewGuid();
    private readonly Dictionary<Guid, FalloutQueuedReferenceEntry> _objects = [];
    private readonly Dictionary<FalloutFormKey, Guid> _map = new(FalloutFormKeyComparer.Instance);
    private FalloutActorProcessRuntimeHandoff? _cold;
    private long _sequence, _callbackFault;
    private bool _callback, _retiring, _disposed;

    internal FalloutQueuedReferences(FalloutActorProcessQueueDeclaration source, string stack,
        Func<FalloutFormKey, FalloutQueuedReferenceSource> identity, FalloutQueuedReferencesSnapshot? restore = null,
        Action<Guid, int>? reprioritize = null)
    {
        source.Validate(); ArgumentException.ThrowIfNullOrWhiteSpace(stack); ArgumentNullException.ThrowIfNull(identity);
        _source = source; _stack = stack; _identity = identity; _reprioritize = reprioritize;
        ConstructTaskPriorities(restore);
        // Both selected loader constructors create an empty actual map. No
        // actor cohort or current scene is inserted as a substitute.
        if (restore is not null) Restore(restore);
    }

    internal FalloutActorProcessFact<bool> Membership(FalloutFormKey reference)
    {
        lock (_gate)
        {
            RequireAlive();
            if (!_map.TryGetValue(reference, out var value))
                return new(false, "actual-source-loader-reference-map/" + _sequence);
            var item = _objects[value];
            if (item.Boundary is not null)
                return new(null, "actual-source-loader-reference-map/" + value, item.Boundary);
            return new(true, "actual-source-loader-reference-map/" + value, item.Failure);
        }
    }
    internal FalloutFormKey ReferenceOf(Guid identity)
    {
        lock (_gate) { RequireAlive(); return Require(identity).Source.Reference; }
    }
    internal string? SaveBlocker
    {
        get
        {
            lock (_gate)
            {
                if (_callback) return "actual-queued-reference-source-callback-in-flight";
                if (_retiring) return "actual-queued-reference-loader-retirement-entered";
                return _objects.Values.FirstOrDefault(item => item.Phase != FalloutQueuedReferencePhase.Destroyed ||
                    item.Failure is not null || item.Boundary is not null) is { } item ?
                    "actual-queued-reference:" + item.Source.Reference + ":" + (item.Failure ?? item.Boundary ?? item.Phase.ToString()) : TaskPriorities.SaveBlocker;
            }
        }
    }
    internal object State
    {
        get
        {
            lock (_gate) return new
            {
                source = _source.Contract,
                process = _process,
                _sequence,
                map = _map.Select(item => new FalloutQueuedReferenceMapEntry(item.Key, item.Value)).ToArray(),
                objects = _objects.Values.ToArray(),
                tasks = TaskPriorities.State,
                cold = _cold,
                saveBlocker = SaveBlocker
            };
        }
    }

    internal FalloutQueuedReferenceRequest Request(FalloutFormKey reference, int priority,
        FalloutQueuedReferenceFactoryInputs inputs)
    {
        lock (_gate)
        {
            RequireCreation(); ArgumentNullException.ThrowIfNull(inputs); ArgumentException.ThrowIfNullOrWhiteSpace(inputs.Owner);
            if (inputs.MainForced is null || inputs.MainPermitsForcedQueue is null || inputs.SourceBaseExcluded is null ||
                inputs.SourceReferenceHasQueueFlag is null || inputs.SourceQueueFlags is null)
                throw new InvalidDataException("Queued reference factory lost its independent original inputs.");
            // These guards precede the actual map query. Later unknown fields
            // are not consumed after a genuine earlier refusal.
            if (inputs.MainForced.Require() && !inputs.MainPermitsForcedQueue.Require() || inputs.SourceBaseExcluded.Require() ||
                inputs.SourceReferenceHasQueueFlag.Require() && (inputs.SourceQueueFlags.Require() & 2u) != 0)
                return new(FalloutQueuedReferenceRequestDisposition.SourceRefused, null, false, inputs.Owner);
            var source = Identity(reference);
            if (_map.TryGetValue(reference, out var existing))
            {
                var item = _objects[existing];
                if (item.Source != source || !item.Mapped || item.Phase >= FalloutQueuedReferencePhase.MapRemoved)
                    throw new InvalidDataException("Source queued-reference map contains a foreign or retired value.");
                if (item.Boundary is not null) throw new NotSupportedException(item.Boundary);
                var currentPriority = TaskPriorities.ReadPriority(existing);
                if (currentPriority != item.Priority) throw new InvalidDataException("Queued map byte and actual source task key disagree.");
                var changed = currentPriority != priority;
                if (changed)
                {
                    // The actual source constructor/key owner admits state0
                    // and refuses an entered opaque dispatch. A separately
                    // bound consumer remains ordered after that real owner.
                    try { Callback(() => { TaskPriorities.Reprioritize(existing, priority); _reprioritize?.Invoke(existing, priority); return true; }); }
                    catch (Exception error)
                    {
                        _objects[existing] = item with { Failure = item.Failure ?? Message(error), Changed = Next() }; throw;
                    }
                    _objects[existing] = item with { Priority = TaskPriorities.ReadPriority(existing), Changed = Next() };
                }
                return new(FalloutQueuedReferenceRequestDisposition.Existing, existing, changed, inputs.Owner);
            }
            var identity = Guid.NewGuid(); var sequence = Next();
            TaskPriorities.Construct(identity, unchecked((byte)priority));
            var queued = new FalloutQueuedReferenceEntry(identity, source, unchecked((byte)priority), FalloutQueuedReferencePhase.Constructed,
                true, true, false, [], sequence, sequence, inputs.Owner, null, null, false);
            _objects.Add(identity, queued);
            // There is exactly one retained map value per reference. No
            // replacement is performed when a second request arrives.
            _map.Add(reference, identity);
            return new(FalloutQueuedReferenceRequestDisposition.Constructed, identity, false, inputs.Owner);
        }
    }

    internal Guid EnterConsumer(Guid identity, FalloutQueuedReferenceConsumerKind kind, string owner)
    {
        lock (_gate)
        {
            RequireMutation(); ArgumentException.ThrowIfNullOrWhiteSpace(owner); var item = Require(identity);
            if (kind != FalloutQueuedReferenceConsumerKind.Cancellation) RequireCreation();
            if (!Enum.IsDefined(kind) || item.Boundary is not null && (kind != FalloutQueuedReferenceConsumerKind.Cancellation || item.OpaqueOwnership) ||
                item.Phase >= FalloutQueuedReferencePhase.ConsumersRetired ||
                item.Failure is not null && kind != FalloutQueuedReferenceConsumerKind.Cancellation ||
                item.Consumers.Any(consumer => !consumer.Returned &&
                    (kind != FalloutQueuedReferenceConsumerKind.Cancellation || consumer.Kind == FalloutQueuedReferenceConsumerKind.Cancellation)))
                throw new NotSupportedException(item.Failure ?? item.Boundary ?? "Queued reference retains another entered consumer.");
            var phase = kind switch
            {
                FalloutQueuedReferenceConsumerKind.Read when item.Phase == FalloutQueuedReferencePhase.Constructed => FalloutQueuedReferencePhase.Reading,
                FalloutQueuedReferenceConsumerKind.Assembly when item.Phase == FalloutQueuedReferencePhase.ReadReturned => FalloutQueuedReferencePhase.Assembling,
                FalloutQueuedReferenceConsumerKind.Publication when item.Phase == FalloutQueuedReferencePhase.Assembling => FalloutQueuedReferencePhase.Assembling,
                FalloutQueuedReferenceConsumerKind.Cancellation when item.CancelRequested => FalloutQueuedReferencePhase.Cancelling,
                _ => throw new InvalidDataException("Queued-reference consumer entered outside its actual source work order.")
            };
            var consumer = Guid.NewGuid(); var sequence = Next();
            _objects[identity] = item with
            {
                Phase = phase,
                Changed = sequence,
                Consumers = item.Consumers.Append(new(consumer, kind, owner, _process, sequence, sequence, false, null)).ToArray()
            };
            return consumer;
        }
    }

    internal void RequestCancellation(Guid identity, string owner)
    {
        lock (_gate)
        {
            RequireMutation(); ArgumentException.ThrowIfNullOrWhiteSpace(owner); var item = Require(identity);
            if (item.Phase >= FalloutQueuedReferencePhase.ConsumersRetired || !item.CallerOwned)
                throw new InvalidDataException("Cancellation has no still-owned actual queued-reference work.");
            // Cancelling a token is only a request. The active Task/assembly
            // lease remains entered until its real consumer actually returns.
            if (!item.CancelRequested) _objects[identity] = item with { CancelRequested = true, Changed = Next() };
        }
    }
    internal void ReturnConsumer(Guid identity, Guid consumer, string owner, Exception? failure = null)
    {
        lock (_gate)
        {
            RequireMutation(); var item = Require(identity);
            var current = item.Consumers.SingleOrDefault(value => value.Identity == consumer) ??
                throw new InvalidDataException("Queued-reference return has no exact entered consumer.");
            if (current.Returned || current.Process != _process || current.Owner != owner)
                throw new InvalidDataException("Queued-reference return belongs to a different process or invocation.");
            if (current.Kind == FalloutQueuedReferenceConsumerKind.Cancellation &&
                item.Consumers.Any(value => value.Identity != consumer && !value.Returned))
                throw new NotSupportedException("Cancellation return still owns an entered real queued child.");
            var error = failure is null ? null : Message(failure);
            var sequence = Next();
            var returned = current with { Returned = true, Failure = error, Changed = sequence };
            var phase = error is not null ? item.Phase : current.Kind switch
            {
                FalloutQueuedReferenceConsumerKind.Read => FalloutQueuedReferencePhase.ReadReturned,
                FalloutQueuedReferenceConsumerKind.Assembly => FalloutQueuedReferencePhase.Assembling,
                FalloutQueuedReferenceConsumerKind.Publication => FalloutQueuedReferencePhase.PublicationReturned,
                FalloutQueuedReferenceConsumerKind.Cancellation => FalloutQueuedReferencePhase.ConsumersRetired,
                _ => throw new InvalidDataException("Queued-reference consumer kind is unknown.")
            };
            _objects[identity] = item with
            {
                Phase = phase,
                Failure = item.Failure ?? error,
                Changed = sequence,
                Consumers = item.Consumers.Select(value => value.Identity == consumer ? returned : value).ToArray()
            };
        }
    }

    internal void ConsumersRetired(Guid identity, string owner)
    {
        lock (_gate)
        {
            RequireMutation(); ArgumentException.ThrowIfNullOrWhiteSpace(owner); var item = Require(identity);
            if (item.Consumers.Any(value => !value.Returned) || item.Phase is not
                (FalloutQueuedReferencePhase.PublicationReturned or FalloutQueuedReferencePhase.Cancelling or FalloutQueuedReferencePhase.ConsumersRetired))
                throw new NotSupportedException("Queued reference still owns original I/O/assembly/publication consumers.");
            _objects[identity] = item with { Phase = FalloutQueuedReferencePhase.ConsumersRetired, Changed = Next() };
        }
    }
    internal bool RemoveMatched(FalloutFormKey reference, Guid expectedValue, string owner)
    {
        lock (_gate)
        {
            RequireMutation(); ArgumentException.ThrowIfNullOrWhiteSpace(owner);
            if (!_map.TryGetValue(reference, out var current) || current != expectedValue) return false;
            var item = Require(expectedValue);
            if (item.Source.Reference != reference || item.Consumers.Any(value => !value.Returned) ||
                item.Phase != FalloutQueuedReferencePhase.ConsumersRetired)
                throw new NotSupportedException("Matched map retirement preceded real queued-reference consumers.");
            _map.Remove(reference);
            _objects[expectedValue] = item with { Mapped = false, Phase = FalloutQueuedReferencePhase.MapRemoved, Changed = Next() };
            return true;
        }
    }
    internal void ReleaseCaller(Guid identity, string owner)
    {
        lock (_gate)
        {
            RequireMutation(); ArgumentException.ThrowIfNullOrWhiteSpace(owner); var item = Require(identity);
            if (!item.CallerOwned) return;
            if (item.Mapped || item.Consumers.Any(value => !value.Returned) || item.Phase != FalloutQueuedReferencePhase.MapRemoved)
                throw new NotSupportedException("Queued-reference caller cannot destroy a still-owned map or child.");
            TaskPriorities.Retire(identity);
            _objects[identity] = item with { CallerOwned = false, Phase = FalloutQueuedReferencePhase.Destroyed, Changed = Next() };
        }
    }
    internal void RetireUnstarted(Guid identity, string owner)
    {
        lock (_gate)
        {
            RequireMutation(); var item = Require(identity);
            if (item.Phase != FalloutQueuedReferencePhase.Constructed || item.Consumers.Count != 0 || item.OpaqueOwnership)
                throw new NotSupportedException("Unstarted queue retirement cannot replace an actual entered read or native child.");
            RequestCancellation(identity, owner);
            var consumer = EnterConsumer(identity, FalloutQueuedReferenceConsumerKind.Cancellation, owner);
            ReturnConsumer(identity, consumer, owner);
            ConsumersRetired(identity, owner);
            _ = RemoveMatched(item.Source.Reference, identity, owner);
            ReleaseCaller(identity, owner);
        }
    }
    internal void StopNewWork()
    {
        lock (_gate) { RequireMutation(); _retiring = true; }
    }
    internal void RetainBoundary(Guid identity, string owner)
    {
        lock (_gate)
        {
            RequireMutation(); ArgumentException.ThrowIfNullOrWhiteSpace(owner); var item = Require(identity);
            _objects[identity] = item with { Boundary = item.Boundary ?? owner, OpaqueOwnership = true, Changed = Next() };
        }
    }
    internal void RetainFailure(Guid identity, Exception error)
    {
        lock (_gate)
        {
            RequireMutation(); ArgumentNullException.ThrowIfNull(error); var item = Require(identity);
            _objects[identity] = item with { Failure = item.Failure ?? Message(error), Changed = Next() };
        }
    }
    private FalloutQueuedReferenceSource Identity(FalloutFormKey reference)
    {
        var source = Callback(() => _identity(reference)); ValidateSource(source);
        return source.Reference == reference ? source : throw new InvalidDataException("Queued-reference factory has a foreign source identity.");
    }
    private T Callback<T>(Func<T> callback)
    {
        _callback = true; var faults = _callbackFault;
        try
        {
            var result = callback();
            if (_callbackFault != faults) throw new InvalidOperationException("Source queued-reference callback caught actual map reentry.");
            return result;
        }
        finally { _callback = false; }
    }
    private FalloutQueuedReferenceEntry Require(Guid identity) => _objects.TryGetValue(identity, out var value) ? value :
        throw new InvalidDataException("Queued-reference object was never constructed by this loader.");
    private void RequireAlive() => ObjectDisposedException.ThrowIf(_disposed, this);
    private void RequireMutation()
    {
        RequireAlive();
        if (_callback) { _callbackFault = checked(_callbackFault + 1); throw new InvalidOperationException("Source queued-reference identity reentered its map."); }
    }
    private void RequireCreation()
    {
        RequireMutation();
        if (_retiring) throw new InvalidOperationException("Queued-reference loader retirement refuses new source work while retaining its existing consumers.");
    }
    private long Next() => _sequence = checked(_sequence + 1);
    private static string Message(Exception error) => string.IsNullOrWhiteSpace(error.Message) ? error.GetType().Name : error.Message;
    internal static void ValidateSource(FalloutQueuedReferenceSource value)
    {
        if (value is null || value.Reference.ObjectId == 0 || value.Base.ObjectId == 0 || string.IsNullOrWhiteSpace(value.Reference.OwnerPlugin) ||
            string.IsNullOrWhiteSpace(value.Base.OwnerPlugin) || !Hash(value.ReferenceSha256) || !Hash(value.BaseSha256) ||
            !Enum.IsDefined(value.Kind) || value.ReferenceSignature is not ("REFR" or "ACHR" or "ACRE" or "ENGINE_PLAYER") ||
            value.BaseSignature is not { Length: 4 } || value.EnginePlayer != (value.Kind == FalloutQueuedReferenceKind.Player) ||
            value.Kind is (FalloutQueuedReferenceKind.Reference or FalloutQueuedReferenceKind.Tree) && value.ReferenceSignature != "REFR" ||
            value.Kind == FalloutQueuedReferenceKind.Reference && value.BaseSignature is ("TREE" or "NPC_" or "CREA" or "SCPT") ||
            value.Kind == FalloutQueuedReferenceKind.Character && (value.ReferenceSignature != "ACHR" || value.BaseSignature != "NPC_") ||
            value.Kind == FalloutQueuedReferenceKind.Creature && (value.ReferenceSignature != "ACRE" || value.BaseSignature != "CREA") ||
            value.Kind == FalloutQueuedReferenceKind.Player && value.BaseSignature != "NPC_" ||
            value.EnginePlayer != (value.ReferenceSignature == "ENGINE_PLAYER") ||
            value.Kind == FalloutQueuedReferenceKind.Tree && value.BaseSignature != "TREE")
            throw new InvalidDataException("Queued-reference source lost its exact master/winner/base/factory class.");
    }
    private static bool Hash(string value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return; RequireMutation();
            _retiring = true;
            if (_objects.Values.FirstOrDefault(item => item.Mapped || item.CallerOwned || item.OpaqueOwnership ||
                item.Consumers.Any(value => !value.Returned)) is { } stillOwned)
                throw new NotSupportedException("Source queued loader retains an actual object/consumer: " + stillOwned.Source.Reference);
            _taskPriorities.Dispose();
            _disposed = true; _map.Clear(); _objects.Clear();
        }
    }
}
