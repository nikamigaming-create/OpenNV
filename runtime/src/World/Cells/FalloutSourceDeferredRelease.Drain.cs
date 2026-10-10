namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutSourceDeferredRelease
{
    internal void FlushPending(FalloutMainPlayerCellInvocation invocation)
    {
        invocation.Require(FalloutMainPlayerCellStep.PendingDeferredDestruction);
        if (invocation.Main.Process != _process || invocation.Owner.MainPlayerCellSource != _player)
            throw new InvalidDataException("Deferred drain changed its real entered Main/Player process.");
        Flush(invocation.Main.Identity, false,
            () => invocation.Require(FalloutMainPlayerCellStep.PendingDeferredDestruction));
    }

    private void Flush(Guid? main, bool retirement, Action requireCaller)
    {
        lock (_gate)
        {
            Require(); EnterOwner();
            var before = _enabled; var faults = _forbiddenReentry;
            _flushing = true;
            _last = new(main, retirement, _constructor, System.Environment.CurrentManagedThreadId,
                before, 0, 0, Next(), false, null, null);
            try
            {
                requireCaller();
                _enabled = 0; _ = Next();
                DrainReferences(_queues[FalloutDeferredReleaseRole.ReferenceA]); CompletedQueue();
                DrainRaw(_queues[FalloutDeferredReleaseRole.RawA]); CompletedQueue();
                DrainRaw(_queues[FalloutDeferredReleaseRole.RawB]); CompletedQueue();
                DrainReferences(_queues[FalloutDeferredReleaseRole.ReferenceB]); CompletedQueue();
                DrainForms(_queues[FalloutDeferredReleaseRole.Form]); CompletedQueue();
                DrainReferences(_queues[FalloutDeferredReleaseRole.EmbeddedReference]); CompletedQueue();
                requireCaller();
                if (_forbiddenReentry != faults)
                    throw new InvalidOperationException("Deferred drain swallowed an actual forbidden caller/capture reentry.");
                if (_retained.Count != 0)
                    throw new NotSupportedException("Actual deferred object/reference remains owned outside its returned source queue.");
                _last = _last with { Complete = true, Changed = Next() };
            }
            catch (Exception failure)
            {
                Retain(failure);
                _last = _last with { Changed = Next(), FailureType = Type(failure), Error = Message(failure) };
                throw;
            }
            finally
            {
                // The caller's actual enable cell and recursive lock are
                // independent cleanup owners. This restoration does not return
                // a failed child or clear any remaining queue allocation.
                _enabled = before; _ = Next(); _flushing = false; ExitOwner();
            }
        }
        void CompletedQueue() => _last = _last! with
        { CompletedQueues = _last.CompletedQueues + 1, Changed = Next() };
    }

    private void DrainRaw(Queue queue)
    {
        var end = queue.Slots.Count; // The original raw batch snapshots its end.
        for (var index = 0; index < end; index++)
        {
            var loan = queue.Slots[index]; if (loan is null) continue;
            RequireQueuedLoan(loan, queue.Role); DestroyRaw(loan); ReturnedObject();
        }
        // Original raw slots/count clear only after all destructor/free calls.
        // A later failure therefore retains the completed and failed prefix;
        // none of those freed allocations is called again on a retry.
        ClearReturnedQueue(queue);
    }

    private void DrainReferences(Queue queue)
    {
        for (var index = 0; index < queue.Slots.Count; index++)
        {
            var loan = queue.Slots[index]; if (loan is null) continue;
            RequireQueuedLoan(loan, queue.Role);
            if (loan.Owner is not IFalloutSourceDeferredReference reference || !loan.Receipt.QueueReference ||
                loan.Receipt.TemporaryReference)
                throw new NotSupportedException("Deferred queue release has no actually acquired source reference.");
            Child(loan, FalloutDeferredReleaseStage.ReleasingReference, FalloutDeferredReleaseStage.Returned,
                reference.ReleaseReference);
            loan.Receipt = loan.Receipt with { QueueReference = false, QueueStored = false, Changed = Next() };
            // The embedded buffer has no nullable smart-pointer slots and
            // clears its storage only after the final embedded child returned.
            if (queue.Role != FalloutDeferredReleaseRole.EmbeddedReference)
            {
                queue.Slots[index] = null; ForgetReturned(loan);
            }
            ReturnedObject();
        }
        ClearReturnedQueue(queue);
    }

    private void DrainForms(Queue queue)
    {
        while (queue.Slots.Count != 0)
        {
            var loan = queue.Slots[0] ?? throw new NotSupportedException("Original front-delete queue has a null non-progressing source entry.");
            RequireQueuedLoan(loan, queue.Role);
            if (loan.Owner is not IFalloutSourceDeferredForm form)
                throw new InvalidDataException("Deferred form has no actual virtual deletion consumer.");
            Child(loan, FalloutDeferredReleaseStage.DeletingForm, FalloutDeferredReleaseStage.Returned,
                () => form.Delete(true));
            if (!loan.Receipt.SelfRemoved || loan.Receipt.QueueStored || queue.Slots.Contains(loan))
                throw new NotSupportedException("Returned virtual deletion did not remove its own original source queue entry.");
            ForgetReturned(loan); ReturnedObject();
        }
        ClearReturnedQueue(queue);
    }

    private void RequireQueuedLoan(Loan loan, FalloutDeferredReleaseRole role)
    {
        if (loan.Receipt.Error is not null || loan.Receipt.Source.Role != role ||
            !_retained.TryGetValue(loan.Receipt.Loan, out var current) || current != loan ||
            !loan.Receipt.QueueStored || loan.Receipt.Stage != FalloutDeferredReleaseStage.Queued)
            throw new NotSupportedException("Source deferred queue has a foreign, failed or already consumed allocation/reference prefix.");
        RequireObject(loan.Owner);
    }

    private void ClearReturnedQueue(Queue queue)
    {
        foreach (var loan in queue.Slots.OfType<Loan>())
        {
            if (loan.Receipt.Stage != FalloutDeferredReleaseStage.Returned || loan.Receipt.Error is not null ||
                loan.Receipt.QueueReference || loan.Receipt.TemporaryReference)
                throw new NotSupportedException("Deferred buffer cannot release storage before its actual object/reference consumers returned.");
            loan.Receipt = loan.Receipt with { QueueStored = false, Changed = Next() }; ForgetReturned(loan);
        }
        queue.Slots.Clear(); queue.Capacity = 0; _ = Next();
    }

    private void ReturnedObject() => _last = _last! with
    { ReturnedObjects = checked(_last.ReturnedObjects + 1), Changed = Next() };

    internal void Retire()
    {
        lock (_gate)
        {
            if (_retired) return;
            Require(allowFailure: true);
            _requireNativeMainClosed();
            if (_error is not null)
            {
                // Opaque destructor/refcount failures can have committed. No
                // retirement retry may double-release or discard that handle.
                throw new NotSupportedException("Deferred-release retirement retains the actual failed source prefix: " + _error);
            }
            Flush(null, true, _requireNativeMainClosed);
            if (_retained.Count != 0 || _queues.Values.Any(queue => queue.Slots.Count != 0 || queue.Capacity != 0))
                throw new InvalidOperationException("Deferred manager still owns source objects/storage after retirement.");
            _retired = true; _ = Next();
        }
    }
}
