namespace OpenNV.Runtime.World.Cells;

// Retains only actual admitted source-factory work, not every asset Task. The
// native retirement callback remains owned through failures and is consumed
// on the constructing thread after the real Task has returned.
internal sealed partial class FalloutQueuedReferenceWorkRegistry(FalloutQueuedReferences source) : IDisposable
{
    private sealed class Work(Action retire)
    {
        internal readonly Action Retire = retire;
        internal IFalloutQueuedReferenceRead? Read;
        internal Exception? Failure;
    }
    private readonly Dictionary<Guid, Work> _work = [];
    private int _thread = Environment.CurrentManagedThreadId;
    private long _reentry;
    private bool _busy, _stopping, _disposed;
    internal FalloutQueuedReferenceRead<T> Begin<T>(Guid identity, string owner,
        Func<CancellationToken, Task<T>> read, Action retireEnteredNative, CancellationToken cancellation)
    {
        RequireThread();
        if (_stopping || _work.ContainsKey(identity)) throw new InvalidOperationException("Actual queued-reference read is already owned or its registry is retiring.");
        ArgumentNullException.ThrowIfNull(read); ArgumentNullException.ThrowIfNull(retireEnteredNative);
        var entry = new Work(retireEnteredNative); _work.Add(identity, entry);
        _busy = true; var reentry = _reentry;
        try
        {
            var value = new FalloutQueuedReferenceRead<T>(source, identity, owner, read, cancellation); entry.Read = value;
            if (reentry != _reentry) throw new InvalidOperationException("Actual source reader caught a queue registry reentry refusal.");
            return value;
        }
        catch (Exception error)
        {
            entry.Failure ??= error;
            source.RetainFailure(identity, error);
            // Even constructor failure keeps the exact map value. Disposal
            // can distinguish never-entered work from an actually held Task.
            throw;
        }
        finally { _busy = false; }
    }
    internal IReadOnlyList<Task> StopAndReadTasks()
    {
        RequireThread(); _stopping = true; source.StopNewWork();
        var errors = new List<Exception>();
        foreach (var entry in _work.Values)
            try { entry.Read?.RequestCancellation(); } catch (Exception error) { entry.Failure ??= error; errors.Add(error); }
        if (errors.Count != 0) throw new AggregateException("Actual queued read cancellation retains every entered consumer.", errors);
        return _work.Values.Where(entry => entry.Read is not null).Select(entry => entry.Read!.ReadTask).ToArray();
    }
    internal void RetireReturnedWork()
    {
        RequireThread(); var errors = new List<Exception>(); _busy = true;
        try
        {
            foreach (var (identity, entry) in _work.ToArray())
            {
                var reentry = _reentry;
                try
                {
                    if (entry.Read is null) source.RetireUnstarted(identity, "actual-queued-read-construction-retirement");
                    else if (!entry.Read.Retired) entry.Read.RetireAfterReadReturned(entry.Retire);
                    if (reentry != _reentry) throw new InvalidOperationException("Actual native queued retirement caught an ownership reentry refusal.");
                    _work.Remove(identity);
                }
                catch (Exception error) { entry.Failure ??= error; source.RetainFailure(identity, error); errors.Add(error); }
            }
        }
        finally { _busy = false; }
        if (errors.Count != 0) throw new AggregateException("Actual queued work retains original failures and still-owned native/task consumers.", errors);
    }
    private void RequireThread()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (Environment.CurrentManagedThreadId != _thread) throw new InvalidOperationException("Actual queued work registry has a foreign native owning thread.");
        if (_busy) { _reentry = checked(_reentry + 1); throw new InvalidOperationException("Actual queued work registry reentered an owned consumer."); }
    }
    public void Dispose()
    {
        if (_disposed) return;
        _ = StopAndReadTasks(); RetireReturnedWork(); _disposed = true; _nativePresentationOwner = null;
    }
}
