using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal enum FalloutDeferredReleaseRole { ReferenceA, RawA, RawB, ReferenceB, Form, EmbeddedReference }
internal enum FalloutDeferredReleaseStage
{
    Created, Preparing, Prepared, AcquiringTemporary, TemporaryAcquired, AcquiringQueue,
    QueueAcquired, ReleasingTemporary, Queued, Destructing, Destructed, FreeingAllocation,
    ReleasingReference, DeletingForm, Returned, Failed,
}
internal enum FalloutDeferredReleaseDisposition { Deferred, Immediate, AlreadyQueued, DisabledNoAcquire }
internal sealed record FalloutDeferredReleaseObjectSource(Guid Identity, Guid Process, string Contract,
    FalloutDeferredReleaseRole Role, string Owner);
internal sealed record FalloutDeferredReleaseObjectReceipt(Guid Loan, FalloutDeferredReleaseObjectSource Source,
    FalloutDeferredReleaseStage Stage, long Changed, bool TemporaryReference, bool QueueReference,
    bool QueueStored, bool SelfRemoved, string? FailureType, string? Error);
internal sealed record FalloutDeferredReleaseQueueSnapshot(FalloutDeferredReleaseRole Role, uint Capacity,
    uint End, uint Occupied, IReadOnlyList<Guid?> Slots);
internal sealed record FalloutDeferredReleaseFlush(Guid? Main, bool Retirement, Guid Constructor,
    int ManagedThread, byte EnabledBefore, int CompletedQueues, int ReturnedObjects, long Changed,
    bool Complete, string? FailureType, string? Error);
internal sealed record FalloutSourceDeferredReleaseSnapshot(string Schema,
    FalloutSourceDeferredReleaseDeclaration Source, FalloutMainPlayerCellSource Player, string Stack,
    Guid Process, Guid Constructor, long Sequence, byte Enabled,
    IReadOnlyList<FalloutDeferredReleaseQueueSnapshot> Queues,
    IReadOnlyList<FalloutDeferredReleaseObjectReceipt> RetainedObjects,
    FalloutDeferredReleaseFlush? Last, string? FailureType, string? Error,
    FalloutActorProcessRuntimeHandoff? Cold);

// A source object factory supplies its real allocation/reference/virtual
// consumers. Godot node presence, CLR Tasks and a callback registration cannot
// create any of these source ownership facts.
internal interface IFalloutSourceDeferredObject
{
    FalloutDeferredReleaseObjectSource Source { get; }
    void RequireCurrentFactory();
}
internal interface IFalloutSourceDeferredRaw : IFalloutSourceDeferredObject
{
    void PrepareForDeferredRelease();
    void Destruct();
    void FreeAllocation();
}
internal interface IFalloutSourceDeferredReference : IFalloutSourceDeferredObject
{
    void RequireInitializedTemplate();
    void AcquireReference();
    void ReleaseReference();
}
internal interface IFalloutSourceDeferredForm : IFalloutSourceDeferredObject
{
    void DetachForDeferredRelease();
    void Delete(bool freeAllocation);
}
