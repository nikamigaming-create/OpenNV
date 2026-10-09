using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

// Selected source map: 37 buckets, unsigned key modulo bucket count and new
// entries at the bucket head. This map belongs to original CELL loader callers;
// presentation look-ahead and unrelated decoder Tasks never populate it.
internal sealed partial class FalloutExteriorCellLoaderState
{
    private const int Buckets = 37;
    private readonly FalloutMainPlayerPendingSource _source;
    private readonly string _stack;
    private readonly Guid _process;
    private readonly List<IFalloutExteriorCellLoaderTask>[] _buckets = Enumerable.Range(0, Buckets)
        .Select(_ => new List<IFalloutExteriorCellLoaderTask>()).ToArray();
    private readonly List<IFalloutExteriorCellLoaderTask> _failedConstructions = [];
    private long _sequence, _callbackFault;
    private bool _busy, _retired;
    private string? _failureType, _error;
    private FalloutExteriorCellLoaderCancellation? _last;
    private FalloutActorProcessRuntimeHandoff? _cold;
    internal string? SaveBlocker => _busy ? "source-exterior-loader-map-consumer-entered" :
        _error is not null ? "source-exterior-loader-map:" + _error :
        _buckets.Any(bucket => bucket.Count != 0) || _failedConstructions.Count != 0 ? "source-exterior-loader-task-cold-native-continuation-unowned" : null;
    internal object State => new
    {
        source = _source.Contract,
        stack = _stack,
        process = _process,
        sequence = _sequence,
        buckets = _buckets.Select((bucket, index) => new { index, tasks = bucket.Select(task => task.Source).ToArray() }),
        failedConstructionOwners = _failedConstructions.Select(task => task.Source).ToArray(),
        cancellation = _last,
        failure = _error,
        cold = _cold,
        retired = _retired,
        blocker = SaveBlocker
    };
    internal FalloutExteriorCellLoaderState(FalloutMainPlayerPendingSource source, string stack, Guid process,
        FalloutExteriorCellLoaderSnapshot? saved)
    {
        source.Validate(); ArgumentException.ThrowIfNullOrWhiteSpace(stack);
        if (process == Guid.Empty) throw new InvalidDataException("Exterior loader constructor has no source process.");
        _source = source; _stack = stack; _process = process;
        if (saved is not null) Restore(saved);
    }
    internal FalloutExteriorCellLoaderTaskSource ReadOrCreate(FalloutMainPlayerCellInvocation invocation,
        FalloutCellProcessIdentity cell, int x, int y, Func<uint, IFalloutExteriorCellLoaderTask> factory)
    {
        Require(); ArgumentNullException.ThrowIfNull(factory);
        invocation.Require(invocation.Step);
        if (invocation.Step is not (FalloutMainPlayerCellStep.WorldLoad or FalloutMainPlayerCellStep.PendingDestination) ||
            invocation.Main.Process != _process)
            throw new InvalidOperationException("Source exterior loader task lacks its actual entered original CELL caller.");
        var key = _source.PackCellKey(x, y);
        var bucket = _buckets[key % Buckets];
        var existing = bucket.SingleOrDefault(value => value.Source.Key == key);
        // Lookup precedes the source task constructor. Suppressing a duplicate
        // may not allocate a second owned task and abandon it afterward.
        if (existing is not null) return existing.Source;
        _busy = true; var faults = _callbackFault;
        IFalloutExteriorCellLoaderTask? constructed = null;
        try
        {
            var task = constructed = factory(key) ?? throw new InvalidDataException("Source CELL loader constructor returned no owned task.");
            invocation.Require(invocation.Step); ValidateTask(task.Source, _source);
            if (_callbackFault != faults || task.Retired || task.Source.Invocation != invocation.Main.Identity ||
                task.Source.Key != key || task.Source.Cell != cell || task.Source.X != x || task.Source.Y != y ||
                _buckets.SelectMany(value => value).Any(value => value.Source.Identity == task.Source.Identity))
                throw new InvalidDataException("Source task factory changed its actual entered CELL/key/epoch or swallowed map reentry.");
            bucket.Insert(0, task); _ = Next(); return task.Source;
        }
        catch (Exception failure)
        {
            if (constructed is not null && !_buckets.Any(values => values.Contains(constructed)) && !_failedConstructions.Contains(constructed))
                _failedConstructions.Add(constructed);
            _failureType ??= Type(failure); _error ??= Message(failure); throw;
        }
        finally { _busy = false; }
    }
    internal void RetireReturnedTask(IFalloutExteriorCellLoaderTask task)
    {
        Require(allowFailure: true); ArgumentNullException.ThrowIfNull(task);
        var bucket = _buckets.SingleOrDefault(values => values.Contains(task));
        if (bucket is null && !_failedConstructions.Contains(task) || !task.Retired)
            throw new InvalidOperationException("Exterior loader removal precedes actual task/provider retirement.");
        if (bucket is not null) bucket.Remove(task); else _failedConstructions.Remove(task);
        _ = Next();
    }
    internal void CancelAll(FalloutMainPlayerCellInvocation invocation)
    {
        Require(); invocation.Require(FalloutMainPlayerCellStep.PendingWorldPrelude);
        if (invocation.Main.Process != _process) throw new InvalidDataException("Exterior reset changed the shared Main process.");
        // Source iteration retains each live task while TaskManager cancellation
        // executes. Concurrent native mutation needs its own admitted map/TLS
        // producer; swallowing an owner reentry cannot certify an empty map.
        var tasks = _buckets.SelectMany(bucket => bucket).ToArray();
        _last = new(invocation.Main.Identity, tasks.Select(task => task.Source.Identity).ToArray(), 0, null, null, Next());
        _busy = true; var faults = _callbackFault;
        try
        {
            foreach (var task in tasks)
            {
                if (task.Retired) throw new InvalidOperationException("Source map still owns an already retired task.");
                task.RequestSourceCancellation(invocation);
                invocation.Require(FalloutMainPlayerCellStep.PendingWorldPrelude);
                if (_callbackFault != faults) throw new InvalidOperationException("Exterior cancellation swallowed a real map mutation reentry.");
                _last = _last with { Returned = checked(_last.Returned + 1), Changed = Next() };
            }
        }
        catch (Exception failure)
        {
            _failureType ??= Type(failure); _error ??= Message(failure);
            _last = _last with { Changed = Next(), FailureType = Type(failure), Error = Message(failure) }; throw;
        }
        finally { _busy = false; }
    }
    internal void Retire()
    {
        if (_retired) return;
        Require(allowFailure: true);
        if (_buckets.Any(bucket => bucket.Count != 0) || _failedConstructions.Count != 0)
            throw new InvalidOperationException("Original exterior loader manager still owns actual source tasks.");
        _retired = true;
    }
    private void Require(bool allowFailure = false)
    {
        ObjectDisposedException.ThrowIf(_retired, this);
        if (_busy) { _callbackFault = checked(_callbackFault + 1); throw new InvalidOperationException("Source exterior loader map reentered its current child."); }
        if (!allowFailure && _error is not null) throw new InvalidOperationException("Exterior loader map retains its original failed prefix: " + _error);
    }
    private long Next() => _sequence = checked(_sequence + 1);
    private static string Type(Exception failure) => failure.GetType().FullName ?? failure.GetType().Name;
    private static string Message(Exception failure) => string.IsNullOrWhiteSpace(failure.Message) ? failure.GetType().Name : failure.Message;
}
