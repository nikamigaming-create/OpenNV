using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

internal static partial class SourceMainScriptCallerContracts
{
    internal static void RunSourceDeferredReleaseContracts()
    {
        StandaloneDeferredCurrentAndCold(); StandaloneDeferredOwnedQueueOrder(); StandaloneDeferredUnknownAllocation();
        Console.WriteLine("OPENNV_FO3_SOURCE_DEFERRED_RELEASE_PASS authoredOnly=true actualPendingMain=true sixOrderedQueues=true " +
            "sourceInitialEnableOne=true realReferenceAndAllocationConsumers=true selfRemovalRequired=true " +
            "opaqueFailureRetained=true mandatoryFreshCold=true FnvTlsAndNativeFactories=UNOWNED nativeGameplay=UNEXECUTED");
    }

    private static void StandaloneDeferredCurrentAndCold()
    {
        using var warm = new StandaloneFixture();
        var first = warm.Owner.MainPlayerPendingConsumers.CurrentDeferredRelease.Capture();
        Require(first.Enabled == 1 && first.Last is null && first.Queues.Count == 6 &&
            first.Queues.All(queue => queue.End == 0 && queue.Capacity == 0 && queue.Occupied == 0),
            "Actual standalone module initial data did not construct its own enabled empty release containers.");
        StoreStandaloneEmptyTransfer(warm); ExecuteStandalonePending(warm);
        var runtime = warm.Owner.Capture(); var pending = runtime.StandaloneMain!.PlayerCell.PendingConsumers;
        var release = pending.DeferredRelease!;
        Require(release.Last is { Complete: true, CompletedQueues: 6, ReturnedObjects: 0, Retirement: false } &&
            release.Last.Main == runtime.StandaloneMain.MainCaller.LastCall!.Invocation && !warm.Slot.Pending &&
            runtime.StandaloneMain.PlayerCell.LastCall!.Children.Single(child =>
                child.Step == FalloutMainPlayerCellStep.PendingDeferredDestruction).Returned is not null,
            "Empty global release flush did not actually return before the source payload null store.");
        Reject(() => FalloutMainPlayerPendingState.Validate(pending with { DeferredRelease = null }));
        Reject(() => FalloutSourceDeferredRelease.Validate(release with { Enabled = 0 }));
        Reject(() => FalloutSourceDeferredRelease.Validate(release with { Queues = release.Queues.Skip(1).ToArray() }));
        Reject(() => FalloutSourceDeferredRelease.Validate(release with { Source = release.Source with { InitialEnabled = 0 } }));
        using var cold = new StandaloneFixture(runtime, warm.Fade.Capture());
        var next = cold.Owner.MainPlayerPendingConsumers.CurrentDeferredRelease.Capture();
        Require(next.Constructor != release.Constructor && next.Process != release.Process && next.Last is null &&
            next.Cold?.PreviousProcess == release.Process && cold.Host.Log.Count == 0,
            "Cold global release construction promoted a previous native pointer/thread/returned flush or replayed Main.");
    }

    private static void StandaloneDeferredOwnedQueueOrder()
    {
        using var fixture = new StandaloneFixture(); var manager = fixture.Owner.MainPlayerPendingConsumers.CurrentDeferredRelease;
        var source = manager.Capture(); var log = new List<string>();
        AuthoredDeferredObject Make(FalloutDeferredReleaseRole role) => new(new(Guid.NewGuid(), source.Process,
            source.Source.Contract, role, "authored-source-allocation-and-reference-factory"), manager, log);
        var rawA = Make(FalloutDeferredReleaseRole.RawA); var rawB = Make(FalloutDeferredReleaseRole.RawB);
        var referenceA = Make(FalloutDeferredReleaseRole.ReferenceA); var referenceB = Make(FalloutDeferredReleaseRole.ReferenceB);
        var form = Make(FalloutDeferredReleaseRole.Form); var embedded = Make(FalloutDeferredReleaseRole.EmbeddedReference);
        Require(manager.SubmitForm(form) == FalloutDeferredReleaseDisposition.Deferred &&
            manager.SubmitReference(embedded) == FalloutDeferredReleaseDisposition.Deferred &&
            manager.SubmitRaw(rawB) == FalloutDeferredReleaseDisposition.Deferred &&
            manager.SubmitReference(referenceB) == FalloutDeferredReleaseDisposition.Deferred &&
            manager.SubmitRaw(rawA) == FalloutDeferredReleaseDisposition.Deferred &&
            manager.SubmitReference(referenceA) == FalloutDeferredReleaseDisposition.Deferred &&
            manager.SubmitRaw(rawA) == FalloutDeferredReleaseDisposition.AlreadyQueued,
            "Source enqueue lost actual role, enabled/Main gate or duplicate-before-detach semantics.");
        Require(rawA.Preparations == 1 && rawB.Preparations == 0 && referenceA.References == 2 &&
            referenceB.References == 2 && embedded.References == 2 && embedded.TemplateQueries == 0,
            "Distinct source queue preparation and real temporary/slot/embedded references were coalesced.");
        Reject(() => manager.Capture()); log.Clear();
        StoreStandaloneEmptyTransfer(fixture); ExecuteStandalonePending(fixture);
        Require(log.SequenceEqual(new[] { "ReferenceA:release", "RawA:destruct", "RawA:free", "RawB:destruct",
            "RawB:free", "ReferenceB:release", "Form:delete", "EmbeddedReference:release" }) &&
            rawA.Freed && rawB.Freed && form.Deleted && referenceA.References == 1 &&
            referenceB.References == 1 && embedded.References == 1,
            "Global flush changed actual destructor/allocation/reference order or failed to consume the original front entry.");
        var done = manager.Capture();
        Require(done.Enabled == 1 && done.Last is { Complete: true, CompletedQueues: 6, ReturnedObjects: 6 } &&
            done.RetainedObjects.Count == 0 && done.Queues.All(queue => queue.Capacity == 0 && queue.End == 0),
            "Returned source children still own storage or an incomplete queue was presented as a successful flush.");
    }

    private static void StandaloneDeferredUnknownAllocation()
    {
        var fixture = new StandaloneFixture(); var manager = fixture.Owner.MainPlayerPendingConsumers.CurrentDeferredRelease;
        var source = manager.Capture(); var raw = new AuthoredDeferredObject(new(Guid.NewGuid(), source.Process,
            source.Source.Contract, FalloutDeferredReleaseRole.RawB, "authored-failed-real-allocation-release"), manager, [])
        { FailFree = true };
        manager.SubmitRaw(raw); StoreStandaloneEmptyTransfer(fixture); var original = fixture.Slot.Next;
        Reject(() => ExecuteStandalonePending(fixture));
        var retained = fixture.Slot.Capture();
        Require(fixture.Slot.Pending && fixture.Slot.Next is null && retained.Pending == original && retained.Completion is null &&
            raw.Destructed && !raw.Freed && raw.DestructorCalls == 1 && raw.FreeCalls == 1 && manager.SaveBlocker is not null,
            "Failed allocation release was hidden, replayed or allowed to null-store the still-owned pending payload.");
        Reject(() => manager.Capture()); Reject(() => manager.Retire());
        Require(raw.DestructorCalls == 1 && raw.FreeCalls == 1,
            "Opaque failed native release was called again without a real committed-ownership receipt.");
        try { fixture.Dispose(); throw new InvalidOperationException("Failed allocation unexpectedly reported clean source retirement."); }
        catch (AggregateException error)
        {
            Require(error.ToString().Contains("authored-allocation-free-failed", StringComparison.Ordinal),
                "Independent retirement lost the actual original allocation failure.");
        }
    }

    private sealed class AuthoredDeferredObject(FalloutDeferredReleaseObjectSource source,
        FalloutSourceDeferredRelease manager, List<string> log) : IFalloutSourceDeferredRaw,
        IFalloutSourceDeferredReference, IFalloutSourceDeferredForm
    {
        public FalloutDeferredReleaseObjectSource Source => source;
        internal int References = 1, Preparations, TemplateQueries, DestructorCalls, FreeCalls;
        internal bool Destructed, Freed, Deleted, FailFree;
        public void RequireCurrentFactory()
        {
            if (Freed || Deleted) throw new InvalidOperationException("Actual authored object factory is retired.");
        }
        public void PrepareForDeferredRelease() { RequireCurrentFactory(); Preparations++; }
        public void DetachForDeferredRelease() { RequireCurrentFactory(); Preparations++; }
        public void RequireInitializedTemplate()
        {
            if (source.Role == FalloutDeferredReleaseRole.EmbeddedReference)
                throw new InvalidOperationException("Embedded reference called a source-absent compiler-template child.");
            TemplateQueries++;
        }
        public void AcquireReference() { RequireCurrentFactory(); References++; }
        public void ReleaseReference()
        {
            RequireCurrentFactory(); if (References <= 1) throw new InvalidOperationException("Caller reference was released without an actual queue acquisition.");
            References--; log.Add(source.Role + ":release");
        }
        public void Destruct()
        {
            RequireCurrentFactory(); if (Destructed) throw new InvalidOperationException("Destructor prefix replayed.");
            DestructorCalls++; Destructed = true; log.Add(source.Role + ":destruct");
        }
        public void FreeAllocation()
        {
            FreeCalls++;
            if (!Destructed || Freed) throw new InvalidOperationException("Allocation released outside its actual destructor lifetime.");
            if (FailFree) throw new InvalidOperationException("authored-allocation-free-failed");
            Freed = true; log.Add(source.Role + ":free");
        }
        public void Delete(bool freeAllocation)
        {
            RequireCurrentFactory(); if (!freeAllocation) throw new InvalidOperationException("Source delete lost its original true argument.");
            manager.RemoveDeletingForm(this); Deleted = true; log.Add(source.Role + ":delete");
        }
    }
}
