using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutSourceTaskManager
{
    internal const string Schema = "opennv-source-task-manager/v1";
    private readonly object _gate = new();
    private readonly FalloutSourceTaskManagerDeclaration _source;
    private readonly FalloutMainPlayerCellSource _player;
    private readonly string _stack;
    private readonly Guid _process, _constructor = Guid.NewGuid();
    private readonly Func<FalloutSourceTaskCohort> _cohort;
    private readonly List<IFalloutSourceManagerTask>[] _buckets;
    private readonly Dictionary<int, FalloutSourceTaskManagerThread> _threads = [];
    private sealed class IteratorReference(IFalloutSourceManagerTask task)
    {
        internal readonly IFalloutSourceManagerTask Task = task;
        internal bool Acquired, ReleaseFailed;
    }
    private readonly Dictionary<Guid, IteratorReference> _heldReferences = [];
    private FalloutSourceTaskManagerAttempt? _last;
    private FalloutActorProcessRuntimeHandoff? _cold;
    private long _sequence, _reentry;
    private bool _busy, _retired;
    private string? _failureType, _error;

    internal FalloutSourceTaskManager(FalloutMainPlayerCellSource player, string stack, Guid process,
        Func<FalloutSourceTaskCohort> cohort, FalloutSourceTaskManagerSnapshot? saved = null)
    {
        player.Validate(); ArgumentException.ThrowIfNullOrWhiteSpace(stack); ArgumentNullException.ThrowIfNull(cohort);
        if (process == Guid.Empty) throw new InvalidDataException("Actual task manager has no current source process.");
        _player = player; _source = FalloutSourceTaskManagerDeclaration.Read(player); _stack = stack;
        _process = process; _cohort = cohort;
        // This is the selected constructor's genuine empty map, not a query
        // that equates background indexing, CELL residency or ready I/O with
        // absence of original source tasks.
        _buckets = Enumerable.Range(0, _source.BucketCount).Select(_ => new List<IFalloutSourceManagerTask>()).ToArray();
        if (saved is not null) Restore(saved);
    }

    internal void RegisterSourceTask(IFalloutSourceManagerTask task)
    {
        lock (_gate)
        {
            Require(); ArgumentNullException.ThrowIfNull(task); _busy = true; var reentry = _reentry;
            try
            {
                RequireTask(task);
                var cohort = ReadCohort();
                var actual = cohort.Tasks.SingleOrDefault(value => value.Identity == task.Identity);
                if (actual is null || actual.Retired || actual.Key != task.Key ||
                    _buckets.Any(bucket => bucket.Any(value => value.Identity == task.Identity)))
                    throw new InvalidDataException("Task manager insertion lost its exact current source factory/key/object.");
                if (reentry != _reentry) throw new InvalidOperationException("Source task insertion swallowed a manager reentry refusal.");
                // A concrete scheduler/task factory must perform this source
                // insertion. A queued-reference map row cannot invent membership
                // in this independently owned task-manager map.
                _buckets[_source.Bucket(task.Key)].Insert(0, task); _ = Next();
            }
            catch (Exception failure) { Retain(failure); throw; }
            finally { _busy = false; }
        }
    }

    internal void RemoveSourceTask(IFalloutSourceManagerTask expected)
    {
        lock (_gate)
        {
            Require(allowFailure: true); ArgumentNullException.ThrowIfNull(expected); _busy = true; var reentry = _reentry;
            try
            {
                RequireTask(expected);
                if (_heldReferences.Values.Any(pin => pin.Task.Identity == expected.Identity))
                    throw new InvalidOperationException("Source task removal precedes release of its actual iterator reference.");
                var bucket = _buckets.SingleOrDefault(values => values.Any(task => ReferenceEquals(task, expected))) ??
                    throw new InvalidDataException("Task removal changed the exact retained map value.");
                if (ReadCohort().Tasks.SingleOrDefault(value => value.Identity == expected.Identity) is not { Retired: true })
                    throw new NotSupportedException("Source task factory/native consumers have not retired the actual task.");
                if (reentry != _reentry) throw new InvalidOperationException("Source task removal swallowed a manager reentry refusal.");
                bucket.Remove(expected); _ = Next();
            }
            catch (Exception failure) { Retain(failure); throw; }
            finally { _busy = false; }
        }
    }

    internal void Reset(FalloutMainPlayerCellInvocation invocation)
    {
        ArgumentNullException.ThrowIfNull(invocation); invocation.Require(FalloutMainPlayerCellStep.PendingWorldPrelude);
        if (invocation.Main.Process != _process || invocation.Owner.MainPlayerCellSource != _player)
            throw new InvalidDataException("Task reset changed the real entered source Main/Player process.");
        ResetCore(invocation.Main.Identity, retirement: false,
            () => invocation.Require(FalloutMainPlayerCellStep.PendingWorldPrelude));
    }

    private FalloutSourceTaskCohort ReadCohort()
    {
        var wasBusy = _busy; _busy = true; var before = _reentry;
        try
        {
            var value = _cohort() ?? throw new InvalidDataException("Task manager has no actual source-factory cohort.");
            if (before != _reentry) throw new InvalidOperationException("Task cohort swallowed a manager reentry refusal.");
            if (value.Stack != _stack || value.Executable != _source.Executable || value.Tasks is null ||
                value.Tasks.Any(task => task is null || task.Identity == Guid.Empty || task.State is < 0 ||
                    task.State is null && task.Boundary is null || task.Failure is not null && string.IsNullOrWhiteSpace(task.Failure) ||
                    task.Boundary is not null && string.IsNullOrWhiteSpace(task.Boundary)) ||
                value.Tasks.Select(task => task.Identity).Distinct().Count() != value.Tasks.Count)
                throw new InvalidDataException("Task manager source cohort changed its exact stack/factory/state ownership.");
            return value;
        }
        finally { _busy = wasBusy; }
    }

    private void RequireTask(IFalloutSourceManagerTask task)
    {
        if (task.Identity == Guid.Empty || task.Process != _process || task.Contract != _source.Contract ||
            string.IsNullOrWhiteSpace(task.Owner))
            throw new InvalidDataException("Actual manager task has a foreign source factory/process lifetime.");
    }

    internal void RequireBinding(FalloutMainPlayerCellSource player, string stack, Guid process)
    {
        lock (_gate)
        {
            Require(allowFailure: true);
            if (_player != player || _stack != stack || _process != process)
                throw new InvalidDataException("Pending Player binding changed the actual task-manager constructor/source epoch.");
        }
    }

    private Guid AcquireReference(IFalloutSourceManagerTask task)
    {
        var identity = Guid.NewGuid(); var pin = new IteratorReference(task);
        // Retain the potential ownership before calling an opaque refcount
        // child. A thrown acquire may have committed its physical increment.
        _heldReferences.Add(identity, pin);
        task.AcquireIteratorReference(); pin.Acquired = true; return identity;
    }

    private void ReleaseReference(Guid identity)
    {
        var pin = _heldReferences[identity];
        if (!pin.Acquired || pin.ReleaseFailed)
            throw new NotSupportedException("Failed task reference child requires its actual committed-ownership receipt before retry.");
        try { pin.Task.ReleaseIteratorReference(); _heldReferences.Remove(identity); }
        catch { pin.ReleaseFailed = true; throw; }
    }

    private void Require(bool allowFailure = false)
    {
        ObjectDisposedException.ThrowIf(_retired, this);
        if (_busy) { _reentry = checked(_reentry + 1); throw new InvalidOperationException("Source task manager reentered its current child."); }
        if (!allowFailure && _error is not null)
            throw new InvalidOperationException("Source task manager retains its failed reset prefix: " + _error);
    }

    private FalloutSourceTaskManagerThread AcquireThread()
    {
        var thread = Environment.CurrentManagedThreadId;
        if (_threads.TryGetValue(thread, out var present)) return present;
        // Slot zero is the constructor's empty slot. The observed source
        // registration increments before testing the two-slot capacity.
        var slot = checked(_threads.Count + 1);
        if (slot >= _source.IteratorSlots)
            throw new NotSupportedException("Actual task-manager iterator thread capacity is exhausted; original worker/TLS registration is unowned.");
        var result = new FalloutSourceTaskManagerThread(slot, thread, _process, _constructor, Next());
        _threads.Add(thread, result); return result;
    }

    private long Next() => _sequence = checked(_sequence + 1);
    private void Retain(Exception error)
    {
        _failureType ??= error.GetType().FullName ?? error.GetType().Name;
        _error ??= string.IsNullOrWhiteSpace(error.Message) ? error.GetType().Name : error.Message;
    }

    internal string? SaveBlocker
    {
        get
        {
            lock (_gate)
            {
                if (_retired) return "actual-source-task-manager-retired";
                if (_busy) return "actual-source-task-manager-reset-entered";
                if (_error is not null) return "actual-source-task-manager:" + _error;
                if (_heldReferences.Count != 0 || _buckets.Any(bucket => bucket.Count != 0))
                    return "actual-source-task-manager-current-native-task-cold-reconstruction-unowned";
                return ReadCohort().Tasks.Any(task => !task.Retired) ?
                    "actual-source-task-manager-live-factory-task-cold-reconstruction-unowned" : null;
            }
        }
    }

    internal object State
    {
        get
        {
            lock (_gate) return new
            {
                source = _source,
                stack = _stack,
                process = _process,
                constructor = _constructor,
                sequence = _sequence,
                threads = _threads.Values.ToArray(),
                last = _last,
                cold = _cold,
                buckets = _buckets.Select((values, index) => new { index, tasks = values.Select(task => task.Identity).ToArray() }).ToArray(),
                heldIteratorReferences = _heldReferences.Select(value => new
                { reference = value.Key, task = value.Value.Task.Identity, value.Value.Acquired, value.Value.ReleaseFailed }).ToArray(),
                failure = _error,
                retired = _retired,
                sourceNativeWorkerTls = "independent-source-thread-and-task-virtual-consumers-required",
                saveBlocker = SaveBlocker
            };
        }
    }
}
