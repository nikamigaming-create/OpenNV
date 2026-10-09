using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed class FalloutQueuedTaskPriorities : IDisposable
{
    private const string Schema = "opennv-source-task-priority/v1";
    private readonly object _gate = new();
    private readonly FalloutMainFrameDeclaration _source;
    private readonly string _stack;
    private readonly Guid _process = Guid.NewGuid();
    private readonly Dictionary<Guid, FalloutQueuedTaskPriorityEntry> _tasks = [];
    private readonly IFalloutSourceTaskPriorityConsumer? _consumer;
    private FalloutActorProcessRuntimeHandoff? _cold;
    private long _sequence, _callbackFault;
    private bool _busy, _disposed;
    internal FalloutQueuedTaskPriorities(FalloutMainFrameDeclaration source, string stack,
        FalloutQueuedTaskPrioritySnapshot? restore = null, IFalloutSourceTaskPriorityConsumer? consumer = null)
    {
        source.Validate(); ArgumentException.ThrowIfNullOrWhiteSpace(stack);
        _source = source; _stack = stack; _consumer = consumer;
        if (restore is not null)
        {
            Validate(restore);
            if (restore.Contract != source.Contract || restore.Stack != stack || restore.CapturedProcess == _process)
                throw new InvalidDataException("Source task priority restore lost its selected new process.");
            // Every currently admitted cold task is already destroyed. Active
            // task graphs are an explicit boundary, not a recreated work item.
            if (restore.Tasks.Any(value => !value.Retired))
                throw new NotSupportedException("Actual queued task graph has no admitted active new-process handoff.");
            foreach (var task in restore.Tasks) _tasks.Add(task.Identity, task);
            _sequence = restore.Sequence; _cold = new(restore.CapturedProcess, _process, Next());
        }
    }
    internal void Construct(Guid identity, byte priority)
    {
        lock (_gate)
        {
            RequireMutation();
            if (identity == Guid.Empty || _tasks.ContainsKey(identity)) throw new InvalidDataException("Source task constructor is not a fresh exact queued object.");
            // Both originals zero the entire key and state before writing the
            // caller's low byte into the key. TLS context is a separate field.
            _tasks.Add(identity, new(identity, (ulong)priority << 16, 0, [], false, Next(), null, null));
        }
    }
    internal byte ReadPriority(Guid identity)
    {
        lock (_gate)
        {
            var task = Require(identity);
            if (task.Boundary is { } boundary) throw new NotSupportedException(boundary);
            if (task.Failure is { } failure) throw new InvalidOperationException(failure);
            return unchecked((byte)(task.Key >> 16));
        }
    }
    internal void Reprioritize(Guid identity, int priority)
    {
        lock (_gate)
        {
            RequireMutation(); _busy = true;
            try { Visit(identity, priority, new HashSet<Guid>()); }
            catch (Exception error)
            {
                var task = Require(identity); _tasks[identity] = task with
                { Failure = task.Failure ?? Message(error), Changed = Next() };
                throw;
            }
            finally { _busy = false; }
        }
    }
    private void Visit(Guid identity, int requested, HashSet<Guid> ancestors)
    {
        var task = Require(identity);
        if (task.Retired || task.Failure is not null || task.Boundary is not null)
            throw new NotSupportedException(task.Failure ?? task.Boundary ?? "Actual source task was retired.");
        if (!ancestors.Add(identity)) throw new InvalidDataException("Actual source task priority tree contains a cycle.");
        try
        {
            // Null slots and duplicate nonnull children retain original order.
            foreach (var child in task.Children) if (child is { } value) Visit(value, requested, ancestors);
            var replacement = ReplacePriority(task.Key, requested);
            if (_consumer is null)
            {
                if (task.State != 0) throw new NotSupportedException("Actual queued task manager state/removal/requeue consumer is unowned.");
                _tasks[identity] = task with { Key = replacement, Changed = Next() };
                return;
            }
            var consumer = new GuardedConsumer(this, _consumer);
            var result = ChangePriority(identity, task.Key, requested, consumer);
            var state = consumer.ReadState(identity);
            _tasks[identity] = task with { Key = result.After, State = state, Changed = Next() };
        }
        finally { ancestors.Remove(identity); }
    }
    internal static ulong ReplacePriority(ulong before, int requested) =>
        unchecked((before & 0xffffffffff00ffffUL) + (ulong)((long)requested * 65536L));
    internal static FalloutSourceTaskKeyChange ChangePriority(Guid identity, ulong before, int requested,
        IFalloutSourceTaskPriorityConsumer consumer)
    {
        ArgumentNullException.ThrowIfNull(consumer);
        var after = ReplacePriority(before, requested); var state = consumer.ReadState(identity);
        if (state == 0)
        {
            consumer.StoreKey(identity, after); return new(identity, requested, before, after, state, true, false, false);
        }
        if (state >= 3 || consumer.CompareExchangeState(identity, 2, state) != state)
            return new(identity, requested, before, before, state, false, false, false);
        consumer.Remove(identity); consumer.StoreKey(identity, after);
        var requeue = consumer.CompareExchangeState(identity, 0, 2) == 2;
        if (requeue) consumer.Enqueue(identity);
        return new(identity, requested, before, after, state, true, true, requeue);
    }
    internal void SourceDispatchEntered(Guid identity, string owner)
    {
        lock (_gate)
        {
            RequireMutation(); ArgumentException.ThrowIfNullOrWhiteSpace(owner); var task = Require(identity);
            if (task.Retired || task.State != 0 || task.Boundary is not null || task.Failure is not null)
                throw new NotSupportedException(task.Failure ?? task.Boundary ?? "Source task dispatch has no fresh constructor-owned state.");
            // Native/reference preparation can construct further task graphs
            // and a TLS context. Its entered state is never guessed from the
            // first-party read Task. Keep that real source boundary visible.
            _tasks[identity] = task with { State = null,
                Boundary = task.Boundary ?? "actual-source-task-dispatch-state-and-child-producers-unowned:" + owner, Changed = Next() };
        }
    }
    internal void Retire(Guid identity)
    {
        lock (_gate)
        {
            RequireMutation(); var task = Require(identity);
            if (task.Retired) return;
            if (task.Children.Any(child => child is { } value && !Require(value).Retired))
                throw new NotSupportedException("Source priority provider still owns unretired actual child tasks.");
            // Called only after the exact queued-reference caller/map/real
            // consumers have retired; do not claim an original success state.
            _tasks[identity] = task with { Retired = true, Changed = Next() };
        }
    }
    internal string? SaveBlocker
    {
        get { lock (_gate) return _busy ? "actual-source-task-priority-consumer-entered" :
            _tasks.Values.FirstOrDefault(value => !value.Retired) is { } task ?
                task.Failure ?? task.Boundary ?? "actual-source-task-object-still-owned:" + task.Identity : null; }
    }
    internal string? RuntimeBoundary
    {
        get { lock (_gate) return _tasks.Values.FirstOrDefault(value => value.Boundary is not null)?.Boundary; }
    }
    internal object State
    {
        get { lock (_gate) return new { contract = _source.Contract, process = _process, sequence = _sequence,
            tasks = _tasks.Values.ToArray(), cold = _cold, runtimeBoundary = RuntimeBoundary, saveBlocker = SaveBlocker }; }
    }
    internal FalloutQueuedTaskPrioritySnapshot Capture()
    {
        lock (_gate)
        {
            RequireMutation();
            if (SaveBlocker is { } blocker) throw new NotSupportedException(blocker);
            return new(Schema, _source.Contract, _stack, _process, _sequence, _tasks.Values.ToArray(), _cold);
        }
    }
    internal static void Validate(FalloutQueuedTaskPrioritySnapshot saved)
    {
        if (saved is null || saved.Schema != Schema || saved.Contract is not { Length: 64 } || !saved.Contract.All(Uri.IsHexDigit) ||
            string.IsNullOrWhiteSpace(saved.Stack) || saved.CapturedProcess == Guid.Empty || saved.Sequence < 0 || saved.Tasks is null ||
            saved.Tasks.Any(value => value is null) || saved.Tasks.Select(value => value.Identity).Distinct().Count() != saved.Tasks.Count)
            throw new InvalidDataException("Current task priority snapshot lost its exact actual objects.");
        var identities = saved.Tasks.Select(value => value.Identity).ToHashSet();
        foreach (var task in saved.Tasks)
            if (task.Identity == Guid.Empty || task.Changed < 1 || task.Changed > saved.Sequence || task.Children is null ||
                task.Children.Any(child => child is { } value && (!identities.Contains(value) || value == task.Identity)) ||
                task.State is null && task.Boundary is null || task.State is not null && task.State != 0 ||
                task.Failure is not null && string.IsNullOrWhiteSpace(task.Failure) ||
                task.Boundary is not null && string.IsNullOrWhiteSpace(task.Boundary))
                throw new InvalidDataException("Source task priority snapshot invented a native state/child/constructor.");
        if (saved.ColdHandoff is { } cold && (cold.PreviousProcess == Guid.Empty || cold.CurrentProcess != saved.CapturedProcess ||
            cold.PreviousProcess == cold.CurrentProcess || cold.Sequence < 1 || cold.Sequence > saved.Sequence))
            throw new InvalidDataException("Source task priorities lost their actual cold process handoff.");
    }
    private FalloutQueuedTaskPriorityEntry Require(Guid identity)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _tasks.TryGetValue(identity, out var value) ? value : throw new InvalidDataException("Priority task has no actual source constructor.");
    }
    private void RequireMutation()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_busy)
        {
            _callbackFault = checked(_callbackFault + 1);
            throw new InvalidOperationException("Task priority consumer reentered its source owner.");
        }
    }
    private sealed class GuardedConsumer(FalloutQueuedTaskPriorities owner, IFalloutSourceTaskPriorityConsumer consumer) : IFalloutSourceTaskPriorityConsumer
    {
        private T Call<T>(Func<T> call)
        {
            var faults = owner._callbackFault; var value = call();
            if (faults != owner._callbackFault) throw new InvalidOperationException("Source task consumer caught a forbidden priority-owner reentry.");
            return value;
        }
        public int ReadState(Guid task) => Call(() => consumer.ReadState(task));
        public int CompareExchangeState(Guid task, int replacement, int expected) => Call(() => consumer.CompareExchangeState(task, replacement, expected));
        public void StoreKey(Guid task, ulong key) => Call(() => { consumer.StoreKey(task, key); return true; });
        public void Remove(Guid task) => Call(() => { consumer.Remove(task); return true; });
        public void Enqueue(Guid task) => Call(() => { consumer.Enqueue(task); return true; });
    }
    private long Next() => _sequence = checked(_sequence + 1);
    private static string Message(Exception error) => string.IsNullOrWhiteSpace(error.Message) ? error.GetType().Name : error.Message;
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return; RequireMutation();
            if (SaveBlocker is { } blocker) throw new NotSupportedException(blocker);
            _disposed = true; _tasks.Clear();
        }
    }
}
