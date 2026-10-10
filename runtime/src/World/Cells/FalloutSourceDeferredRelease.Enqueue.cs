namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutSourceDeferredRelease
{
    internal FalloutDeferredReleaseDisposition SubmitRaw(IFalloutSourceDeferredRaw value)
    {
        lock (_gate)
        {
            Require(allowObjectChild: true); EnterOwner();
            try
            {
                RequireObject(value);
                if (value.Source.Role is not (FalloutDeferredReleaseRole.RawA or FalloutDeferredReleaseRole.RawB))
                    throw new InvalidDataException("Raw release entered a different original destructor queue.");
                var queue = _queues[value.Source.Role];
                if (queue.Slots.Any(slot => slot is not null && ReferenceEquals(slot.Owner, value)))
                    return FalloutDeferredReleaseDisposition.AlreadyQueued;
                var loan = NewLoan(value);
                // Only raw-A has the reached detach/child/controller preamble.
                // The actual raw-B source writer has no equivalent consumer.
                if (value.Source.Role == FalloutDeferredReleaseRole.RawA)
                    Child(loan, FalloutDeferredReleaseStage.Preparing, FalloutDeferredReleaseStage.Prepared,
                        value.PrepareForDeferredRelease);
                if (DeferRawOrForm())
                {
                    Append(queue, loan);
                    loan.Receipt = loan.Receipt with { Stage = FalloutDeferredReleaseStage.Queued, Changed = Next(), QueueStored = true };
                    return FalloutDeferredReleaseDisposition.Deferred;
                }
                DestroyRaw(loan); ForgetReturned(loan);
                return FalloutDeferredReleaseDisposition.Immediate;
            }
            catch (Exception failure) { Retain(failure); throw; }
            finally { ExitOwner(); }
        }
    }

    internal FalloutDeferredReleaseDisposition SubmitReference(IFalloutSourceDeferredReference value)
    {
        lock (_gate)
        {
            Require(allowObjectChild: true); EnterOwner();
            Loan? loan = null;
            var temporaryReleaseEntered = false;
            try
            {
                RequireObject(value);
                if (value.Source.Role is not (FalloutDeferredReleaseRole.ReferenceA or
                    FalloutDeferredReleaseRole.ReferenceB or FalloutDeferredReleaseRole.EmbeddedReference))
                    throw new InvalidDataException("Reference release entered a different original ownership queue.");
                // Source disabled writers return without acquiring a reference.
                // An immediate Release here would invent ownership and can free
                // the actual caller's object while the drain is still running.
                if (_enabled == 0) return FalloutDeferredReleaseDisposition.DisabledNoAcquire;
                loan = NewLoan(value);
                if (value.Source.Role != FalloutDeferredReleaseRole.EmbeddedReference)
                {
                    Child(loan, FalloutDeferredReleaseStage.AcquiringTemporary, FalloutDeferredReleaseStage.TemporaryAcquired,
                        value.AcquireReference);
                    loan.Receipt = loan.Receipt with { TemporaryReference = true, Changed = Next() };
                    Reserve(_queues[value.Source.Role]);
                    // Compiler template/TLS initialization is a separate real
                    // source producer. A factory without it must refuse; native
                    // node presence or a runtime empty list cannot supply it.
                    Child(loan, FalloutDeferredReleaseStage.Preparing, FalloutDeferredReleaseStage.Prepared,
                        value.RequireInitializedTemplate);
                }
                if (value.Source.Role == FalloutDeferredReleaseRole.EmbeddedReference)
                {
                    Child(loan, FalloutDeferredReleaseStage.AcquiringQueue, FalloutDeferredReleaseStage.QueueAcquired,
                        value.AcquireReference);
                    loan.Receipt = loan.Receipt with { QueueReference = true, Changed = Next() };
                    Append(_queues[value.Source.Role], loan);
                    loan.Receipt = loan.Receipt with { QueueStored = true, Changed = Next() };
                }
                else
                {
                    // The actual smart slot stores its pointer before its own
                    // increment. A thrown acquire must retain that pointer and
                    // the potentially committed native reference separately.
                    Append(_queues[value.Source.Role], loan);
                    loan.Receipt = loan.Receipt with { QueueStored = true, Changed = Next() };
                    Child(loan, FalloutDeferredReleaseStage.AcquiringQueue, FalloutDeferredReleaseStage.QueueAcquired,
                        value.AcquireReference);
                    loan.Receipt = loan.Receipt with { QueueReference = true, Changed = Next() };
                }
                if (loan.Receipt.TemporaryReference)
                {
                    temporaryReleaseEntered = true;
                    Child(loan, FalloutDeferredReleaseStage.ReleasingTemporary, FalloutDeferredReleaseStage.Queued,
                        value.ReleaseReference);
                    loan.Receipt = loan.Receipt with { TemporaryReference = false, Changed = Next() };
                }
                loan.Receipt = loan.Receipt with { Stage = FalloutDeferredReleaseStage.Queued, Changed = Next() };
                return FalloutDeferredReleaseDisposition.Deferred;
            }
            catch (Exception failure)
            {
                Retain(failure);
                // The temporary smart reference is a known separately acquired
                // scope even when template/slot construction fails. Releasing
                // it does not retry an uncertain queue increment or publish a
                // successful enqueue. Both failures remain visible.
                if (loan?.Receipt.TemporaryReference == true && !temporaryReleaseEntered)
                {
                    try
                    {
                        Child(loan, FalloutDeferredReleaseStage.ReleasingTemporary, FalloutDeferredReleaseStage.Failed,
                            value.ReleaseReference, knownReferenceCleanup: true);
                        loan.Receipt = loan.Receipt with { TemporaryReference = false, Changed = Next() };
                    }
                    catch (Exception cleanup)
                    {
                        throw new AggregateException("Source reference construction and its known temporary release both failed.", failure, cleanup);
                    }
                }
                throw;
            }
            finally { ExitOwner(); }
        }
    }

    internal FalloutDeferredReleaseDisposition SubmitForm(IFalloutSourceDeferredForm value)
    {
        lock (_gate)
        {
            Require(allowObjectChild: true); EnterOwner();
            try
            {
                RequireObject(value);
                if (value.Source.Role != FalloutDeferredReleaseRole.Form)
                    throw new InvalidDataException("Virtual reference deletion entered a different original release queue.");
                if (_queues[FalloutDeferredReleaseRole.Form].Slots.Any(slot => slot is not null && ReferenceEquals(slot.Owner, value)))
                    throw new NotSupportedException("Repeated original reference detach/election requires its actual native form-map owner.");
                var loan = NewLoan(value);
                Child(loan, FalloutDeferredReleaseStage.Preparing, FalloutDeferredReleaseStage.Prepared,
                    value.DetachForDeferredRelease);
                if (DeferRawOrForm())
                {
                    Append(_queues[FalloutDeferredReleaseRole.Form], loan);
                    loan.Receipt = loan.Receipt with { QueueStored = true, Stage = FalloutDeferredReleaseStage.Queued, Changed = Next() };
                    return FalloutDeferredReleaseDisposition.Deferred;
                }
                Child(loan, FalloutDeferredReleaseStage.DeletingForm, FalloutDeferredReleaseStage.Returned,
                    () => value.Delete(true));
                ForgetReturned(loan); return FalloutDeferredReleaseDisposition.Immediate;
            }
            catch (Exception failure) { Retain(failure); throw; }
            finally { ExitOwner(); }
        }
    }

    // The original front-virtual deletion must remove its own queue entry.
    // This hook belongs inside the actual object's destructor callback. It
    // cannot be called by an unrelated form, a timer or a queued-free request.
    internal void RemoveDeletingForm(IFalloutSourceDeferredForm expected)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_retired, this);
            var queue = _queues[FalloutDeferredReleaseRole.Form];
            if (_enteredObjects.Count == 0 || _enteredObjects.Peek() is not { } loan ||
                !ReferenceEquals(loan.Owner, expected) || loan.Receipt.Stage != FalloutDeferredReleaseStage.DeletingForm ||
                _ownerThread != System.Environment.CurrentManagedThreadId || !loan.Receipt.QueueStored ||
                queue.Slots.Count == 0 || !ReferenceEquals(queue.Slots[0], loan))
            {
                _forbiddenReentry = checked(_forbiddenReentry + 1);
                throw new InvalidOperationException("Form deletion cannot remove another allocation or synthesize source queue progress.");
            }
            queue.Slots.RemoveAt(0);
            loan.Receipt = loan.Receipt with { QueueStored = false, SelfRemoved = true, Changed = Next() };
        }
    }

    private void Append(Queue queue, Loan loan)
    {
        Reserve(queue);
        queue.Slots.Add(loan); _ = Next();
    }

    private void Reserve(Queue queue)
    {
        var next = checked((uint)queue.Slots.Count + 1);
        if (next > queue.Capacity)
        {
            if (queue.Role == FalloutDeferredReleaseRole.EmbeddedReference)
                queue.Capacity = queue.Capacity == 0 ? 4 : checked(queue.Capacity <= 1024 ? queue.Capacity * 2 : queue.Capacity + 1024);
            else
            {
                var growth = queue.Role == FalloutDeferredReleaseRole.Form ? _source.FormGrowth :
                    queue.Role is FalloutDeferredReleaseRole.RawA or FalloutDeferredReleaseRole.RawB ? _source.RawGrowth : _source.ReferenceGrowth;
                var capacity = checked(queue.Capacity + growth);
                if (capacity > ushort.MaxValue || next > ushort.MaxValue)
                    throw new NotSupportedException("Original UInt16 deferred queue extent/capacity overflow has no admitted source allocation.");
                queue.Capacity = capacity;
            }
            _ = Next();
        }
    }

    private void DestroyRaw(Loan loan)
    {
        if (loan.Owner is not IFalloutSourceDeferredRaw raw)
            throw new InvalidDataException("Raw destructor loan has no actual allocation consumer.");
        Child(loan, FalloutDeferredReleaseStage.Destructing, FalloutDeferredReleaseStage.Destructed, raw.Destruct);
        Child(loan, FalloutDeferredReleaseStage.FreeingAllocation, FalloutDeferredReleaseStage.Returned, raw.FreeAllocation);
    }

    private void ForgetReturned(Loan loan)
    {
        if (loan.Receipt.Stage != FalloutDeferredReleaseStage.Returned || loan.Receipt.Error is not null ||
            loan.Receipt.TemporaryReference || loan.Receipt.QueueReference || loan.Receipt.QueueStored)
            throw new InvalidOperationException("Deferred allocation/reference is still owned after its real returned consumer.");
        _retained.Remove(loan.Receipt.Loan);
        if (!_retained.Values.Any(other => other.Receipt.Source.Identity == loan.Receipt.Source.Identity))
            _objects.Remove(loan.Receipt.Source.Identity);
        _ = Next();
    }
}
