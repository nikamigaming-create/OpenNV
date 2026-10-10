namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutSourceTaskManager
{
    private void ResetCore(Guid? main, bool retirement, Action requireInvocation)
    {
        lock (_gate)
        {
            Require(allowFailure: retirement); ArgumentNullException.ThrowIfNull(requireInvocation);
            FalloutSourceTaskManagerThread thread;
            try { thread = AcquireThread(); }
            catch (Exception failure) { Retain(failure); throw; }
            _last = new(main, retirement, _constructor, thread.ManagedThread, 0, [], 0, false, Next(), null, null);
            _busy = true; var reentry = _reentry;
            try
            {
                RequireCohortAccounted();
                for (var index = 0; index < _source.BucketCount; index++)
                {
                    foreach (var task in _buckets[index].ToArray())
                    {
                        RequireTask(task);
                        if (_source.Bucket(task.Key) != index)
                            throw new InvalidDataException("Source task key changed without real manager removal/reinsertion.");
                        // Acquire the iterator's actual task reference before
                        // the virtual child and release after that child. A
                        // failed release stays owned instead of disappearing
                        // into GC or a false reset-complete receipt.
                        var iteratorReference = AcquireReference(task);
                        _last = _last with { Visited = _last.Visited.Append(task.Identity).ToArray(), Changed = Next() };
                        Exception? primary = null;
                        try
                        {
                            CancelTask(task); requireInvocation();
                            if (reentry != _reentry) throw new InvalidOperationException("Source task child swallowed an actual manager reentry refusal.");
                            _last = _last with { Returned = checked(_last.Returned + 1), Changed = Next() };
                        }
                        catch (Exception failure) { primary = failure; }
                        try { ReleaseReference(iteratorReference); }
                        catch (Exception release)
                        {
                            if (primary is not null) throw new AggregateException("Source task cancellation and iterator release both failed.", primary, release);
                            throw;
                        }
                        if (primary is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(primary).Throw();
                    }
                    _last = _last with { ExaminedBuckets = index + 1, Changed = Next() };
                }
                RequireCohortAccounted(); requireInvocation();
                _last = _last with { Complete = true, Changed = Next() };
            }
            catch (Exception failure)
            {
                Retain(failure);
                _last = _last with
                {
                    FailureType = failure.GetType().FullName ?? failure.GetType().Name,
                    Error = string.IsNullOrWhiteSpace(failure.Message) ? failure.GetType().Name : failure.Message,
                    Changed = Next()
                };
                throw;
            }
            finally { _busy = false; }
        }
    }

    private void RequireCohortAccounted()
    {
        var values = ReadCohort().Tasks;
        var byIdentity = values.ToDictionary(value => value.Identity);
        var mapped = _buckets.SelectMany(bucket => bucket).ToDictionary(task => task.Identity);
        foreach (var live in values.Where(value => !value.Retired))
        {
            if (live.Failure is not null || live.Boundary is not null || live.State is null)
                throw new NotSupportedException(live.Failure ?? live.Boundary ?? "Actual source task state/cancellation remains unowned.");
            if (!mapped.TryGetValue(live.Identity, out var task))
                throw new NotSupportedException("actual-source-task-manager-membership-and-virtual-cancel-unowned:" + live.Identity);
            RequireTask(task);
            if (task.Key != live.Key || task.ReadState() != live.State)
                throw new InvalidDataException("Original source-task manager and real task field owner disagree.");
        }
        foreach (var task in mapped.Values)
            if (!byIdentity.ContainsKey(task.Identity))
                throw new InvalidDataException("Task manager retained a value absent from the complete current source-factory cohort.");
    }

    private void CancelTask(IFalloutSourceManagerTask task)
    {
        while (true)
        {
            var state = task.ReadState();
            if (state < 0) throw new InvalidDataException("Actual source task has a negative state word.");
            if (state > 5) return;
            if (state is 2 or 3 or 4)
            {
                // The original rechecks live state; states three/four call
                // the actual zero-yield worker wait. No timeout or completed
                // I/O receipt invents a task-state transition.
                task.WaitForSourceStateChange(state, state is 3 or 4); continue;
            }
            if (task.CompareExchangeState(6, state) != state) continue;
            if (state != 1) { task.CancelVirtual(state, null); return; }
            var schedulerReference = AcquireReference(task);
            Exception? primary = null;
            try
            {
                var key = task.Key; var index = task.ReadSchedulerIndex(key);
                if (index < 0) throw new InvalidDataException("Source scheduler returned a negative cancellation counter index.");
                if (task.RemoveSchedulerKey(key)) task.DecrementSchedulerCounters(index);
                task.CancelVirtual(1, null);
            }
            catch (Exception failure) { primary = failure; }
            try { ReleaseReference(schedulerReference); }
            catch (Exception release)
            {
                if (primary is not null) throw new AggregateException("Source state-one cancellation and retained-reference release both failed.", primary, release);
                throw;
            }
            if (primary is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(primary).Throw();
            return;
        }
    }

    internal void Retire(Action requireNativeMainClosed)
    {
        ArgumentNullException.ThrowIfNull(requireNativeMainClosed);
        lock (_gate)
        {
            if (_retired) return; Require(allowFailure: true); requireNativeMainClosed();
            if (_heldReferences.Count != 0)
                throw new NotSupportedException("Failed task iterator release retains its real native/refcount ownership.");
            ResetCore(null, retirement: true, requireNativeMainClosed);
            if (_buckets.Any(bucket => bucket.Count != 0) || ReadCohort().Tasks.Any(task => !task.Retired))
                throw new NotSupportedException("Task-manager cancellation returned before actual task/map/provider retirement.");
            _retired = true;
            // Original failures and task/thread receipts remain diagnostics;
            // cancellation is never promoted to a native task destruction.
        }
    }

    internal void RequireRetiredBeforeProviders()
    {
        lock (_gate)
        {
            if (!_retired || _busy || _heldReferences.Count != 0 || _buckets.Any(bucket => bucket.Count != 0))
                throw new InvalidOperationException("Actual source task manager must retire before its task/cohort providers.");
        }
    }
}
