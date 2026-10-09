using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    private FalloutQueuedReferencePriority? _sourceQueuePriority;
    private Func<(FalloutActorProcessFact<int> X, FalloutActorProcessFact<int> Y)>? _sourceQueueGrid;
    private object? _sourceQueueGridLease;
    internal object? SourceMainFrameState => new
    {
        main = ActualProcessRuntimeState,
        priority = _sourceQueuePriority?.State,
        tasks = _queuedReferences?.TaskPriorities.State,
        actualGridProducerBound = _sourceQueueGridLease is not null
    };
    internal IDisposable BindSourceQueuedPriorityGrid(Func<(FalloutActorProcessFact<int> X, FalloutActorProcessFact<int> Y)> read)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); ArgumentNullException.ThrowIfNull(read);
        if (_sourceQueueGridLease is not null) throw new InvalidOperationException("Actual source grid priority producer is already bound.");
        var lease = new object(); _sourceQueueGridLease = lease; _sourceQueueGrid = read;
        return new SourceQueueGridLease(this, lease);
    }
    private sealed class SourceQueueGridLease(FalloutReferenceWorld world, object lease) : IDisposable
    {
        public void Dispose()
        {
            if (!ReferenceEquals(world._sourceQueueGridLease, lease)) return;
            world._sourceQueueGridLease = null; world._sourceQueueGrid = null;
        }
    }
    private void ConstructSourceFrameDispatch(string stack, FalloutSourceFrameDispatchSnapshot? restore)
    {
        if (_sourceQueuePriority is not null)
            throw new InvalidOperationException("Source frame/task dispatch already has actual owners.");
        var declaration = FalloutMainFrameDeclaration.ForExecutable(_processRuntimeDeclaration!.ExecutableSha256);
        if (restore is not null) ValidateSourceFrameDispatch(restore);
        _sourceQueuePriority = new(declaration, stack, restore?.Priority);
    }
    private FalloutQueuedTaskPriorities SourceTaskPriorities => QueuedReferences.TaskPriorities;
    internal int ReadSourceNativeLoadPriority(FalloutFormKey reference)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var cell = CurrentActorOrReferenceCell(reference);
        CellProcesses.Construct(cell);
        var priorityCell = CellProcesses.ReadSourcePriorityCell(cell);
        return (_sourceQueuePriority ?? throw new NotSupportedException("Actual original caller priority owner is absent."))
            .Read(priorityCell, () =>
            {
                var source = records.OwnedSource ?? throw new NotSupportedException("Actual source queued caller has no selected installation.");
                // An interior caller does not read the exterior grid anchor.
                // The source setting is still read and all integer arms stay
                // exact, including zero/large UInt32 diameters.
                var grid = priorityCell.Interior ?
                    (new FalloutActorProcessFact<int>(null, "source-interior-priority-does-not-consume-grid-x"),
                     new FalloutActorProcessFact<int>(null, "source-interior-priority-does-not-consume-grid-y")) :
                    _sourceQueueGrid?.Invoke() ??
                    (new FalloutActorProcessFact<int>(null, "actual-original-TES-grid-x-producer-unowned"),
                     new FalloutActorProcessFact<int>(null, "actual-original-TES-grid-y-producer-unowned"));
                return new(() => ReadSourceGridDiameter(source), grid.Item1, grid.Item2, null,
                    "actual-reference-native-Load3D-null-position-caller");
            });
    }
    private static FalloutActorProcessFact<uint> ReadSourceGridDiameter(RuntimeLiveContentSource source)
    {
        // This consumer owns the Main collection's actual descriptor. The
        // NVSE preferences-first lookup is a different source call.
        var value = FalloutInstallationSettings.Read(source).NumericIni.Find(FalloutIniCollection.Main, "uGridsToLoad:General") ??
            throw new NotSupportedException("Selected original Main grid-setting declaration is absent.");
        if (value.Declaration.Kind != 'u' || value.Number is not { } number || !double.IsFinite(number) ||
            number < 0 || number > uint.MaxValue || number != Math.Truncate(number))
            throw new InvalidDataException("Original Main grid-setting payload has no UInt32 owner.");
        return new((uint)number, "actual-Main-uGridsToLoad:General/" + value.Origin);
    }
    private FalloutFormKey CurrentActorOrReferenceCell(FalloutFormKey reference) => reference == _enginePlayer ?
        CurrentActorProcessCell(reference) ?? throw new NotSupportedException("Actual current Player CELL is null in native Load3D caller.") :
        Placement(reference).Cell;
    internal FalloutQueuedReferenceRead<T> BeginSourceNativeLoad<T>(FalloutFormKey reference, string owner,
        Func<CancellationToken, Task<T>> read, Action retireEnteredNative, CancellationToken cancellation)
    {
        var priority = ReadSourceNativeLoadPriority(reference);
        var request = RequestSourceQueuedReference(reference, priority, owner);
        if (request.Disposition == FalloutQueuedReferenceRequestDisposition.SourceRefused)
            throw new NotSupportedException("Actual native Load3D factory source guard refused this reference.");
        if (request.Disposition != FalloutQueuedReferenceRequestDisposition.Constructed || request.Identity is not { } identity)
            throw new NotSupportedException("Native Load3D caller must reuse the already-owned exact source queued work, not construct a second reader.");
        var source = FalloutMainFrameDeclaration.ForExecutable(_processRuntimeDeclaration!.ExecutableSha256);
        _ = FalloutSourceQueuedReferenceDispatch.Read(source, false,
            () => new(null, "ordinary-native-caller-does-not-consume-current-thread"),
            () => new(null, "ordinary-native-caller-does-not-consume-main-thread"),
            () => new(null, "ordinary-native-caller-does-not-consume-inline-gate"), owner);
        SourceTaskPriorities.SourceDispatchEntered(identity, owner);
        return BeginSourceQueuedRead(identity, owner, read, retireEnteredNative, cancellation);
    }
    internal void ExecuteSourceMainQueueWindow(bool full, IFalloutMainQueueFrameConsumers consumers) =>
        ProcessRuntime.ExecuteMainQueueWindow(full, consumers);
    private void RetireSourceFrameDispatch()
    {
        // Map/caller/read/native retirement has already returned. Provider
        // retirement cannot discard a retained real failure or active graph.
        _sourceQueuePriority?.Dispose(); _sourceQueuePriority = null;
        _sourceQueueGrid = null; _sourceQueueGridLease = null;
    }
}
