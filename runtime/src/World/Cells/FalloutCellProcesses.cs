using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

// Actual source load/attachment/retirement operations own phase writes. A
// resident set or draw count never synthesizes an original CELL phase.
internal sealed partial class FalloutCellProcesses : IDisposable
{
    internal const string Schema = "opennv-source-cell-processes/v1";
    private readonly FalloutCellProcessDeclaration _declaration;
    private readonly FalloutCellProcessSource _source;
    private readonly string _stack;
    private readonly Guid _process = Guid.NewGuid();
    private readonly Dictionary<FalloutFormKey, FalloutCellProcessEntry> _cells = new(FalloutFormKeyComparer.Instance);
    private readonly Dictionary<Guid, FalloutCellProcessAttachment> _attachments = [];
    private readonly List<FalloutCellProcessTransition> _transitions = [];
    private FalloutCellProcessHandoff? _cold;
    private long _sequence;
    private bool _busy, _disposed;
    internal FalloutCellProcesses(FalloutCellProcessDeclaration declaration, FalloutPluginStack records,
        string stack, FalloutCellProcessesSnapshot? restore = null,
        Func<FalloutPlacedReference, FalloutReferencePlacement>? currentPlacement = null)
    {
        declaration.Validate(); ArgumentException.ThrowIfNullOrWhiteSpace(stack);
        if (records.OwnedSource is { } owned && owned.StackId != stack)
            throw new InvalidDataException("CELL process selection differs from its actual immutable record source.");
        _declaration = declaration; _source = new(records, currentPlacement); _stack = stack;
        if (restore is not null) Restore(restore);
    }
    internal string? SaveBlocker => _busy ? "source-cell-process-operation-in-flight" :
        _cold?.AwaitingNativeAttachments.Count > 0 ? "source-cell-cold-native-attachment-not-rebound" :
        _cells.Values.FirstOrDefault(cell => cell.Failure is not null) is { } failed ?
            "source-cell-process:" + failed.Source.Cell + ":" + failed.Failure :
        _cells.Values.FirstOrDefault(cell => cell.Phase is FalloutCellProcessPhase.LoadingData or FalloutCellProcessPhase.ReleasingData or
            FalloutCellProcessPhase.Attaching or FalloutCellProcessPhase.Detaching) is { } working ?
            "source-cell-operation-incomplete:" + working.Source.Cell + ":" + working.Phase :
        _attachments.Values.FirstOrDefault(attachment => !attachment.Retired &&
            (!attachment.RootPublished || attachment.Failure is not null || attachment.Children.Any(child =>
                child.Phase is FalloutCellProcessChildPhase.Pending or FalloutCellProcessChildPhase.Failed))) is { } pending ?
            "source-cell-native-attachment-incomplete:" + pending.Identity : null;
    internal object State => new { source = _declaration.Contract, process = _process, _sequence,
        cells = _cells.Values.ToArray(), attachments = _attachments.Values.ToArray(), transitions = _transitions.ToArray(),
        cold = _cold, saveBlocker = SaveBlocker, parity = "unmeasured" };
    internal FalloutBaseObjectDefinition ReadChildBase(FalloutCellProcessReference child) => _source.ReadChildBase(child);

    internal void Construct(FalloutFormKey cell)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_cells.ContainsKey(cell)) return;
        var identity = _source.ReadIdentity(cell);
        _cells.Add(cell, new(identity, FalloutCellProcessPhase.Constructed, 1, null, null));
        Write(cell, FalloutCellProcessOperation.Construct, FalloutCellProcessPhase.Constructed, null, "actual-winning-TESObjectCELL-constructor");
    }
    internal FalloutActorProcessFact<byte> ReadPhase(FalloutFormKey? cell)
    {
        if (cell is null) return new(0, "actual-null-CELL-process-predicate");
        Construct(cell.Value); var state = Require(cell.Value);
        if (_cold?.AwaitingNativeAttachments.Any(identity => _attachments[identity].NativeRoot == 0 &&
            _attachments[identity].CellEpochs.ContainsKey(cell.Value)) == true)
            return new(null, "actual-cold-CELL-native-attachment-not-rebound", state.Failure);
        return new((byte)state.Phase, "original-CELL-phase:" + cell + "/" + state.Epoch, state.Failure);
    }
    internal void LoadSource(FalloutFormKey cell)
    {
        Construct(cell);
        if (RequireHealthy(cell).Data is not null) return;
        Operation([cell], "actual-winning-CELL-source-data-load", () =>
        {
            var state = RequireHealthy(cell);
            if (state.Phase != FalloutCellProcessPhase.Constructed)
                throw new NotSupportedException("Original CELL load entry has a still-owned unmatched phase.");
            Write(cell, FalloutCellProcessOperation.BeginLoad, FalloutCellProcessPhase.LoadingData, null, "actual-CELL-reader-entered");
            var data = _source.Read(cell);
            _cells[cell] = Require(cell) with { Data = data };
            Write(cell, FalloutCellProcessOperation.CompleteLoad, FalloutCellProcessPhase.DataLoaded, null, "actual-CELL-reader-returned");
        });
    }
    internal Guid BeginAttachment(FalloutCellScene scene, IReadOnlyList<FalloutFormKey> sourceCells, ulong actualRoot)
    {
        if (actualRoot == 0) throw new InvalidDataException("CELL attachment has no actual native root identity.");
        foreach (var cell in sourceCells) LoadSource(cell);
        Guid identity = default;
        Operation(sourceCells, "actual-native-CELL-attachment-entered", () =>
        {
            RequireExclusiveNativeRoot(actualRoot);
            var children = _source.ValidateCurrentScene(scene, sourceCells);
            foreach (var cell in sourceCells)
                if (RequireHealthy(cell).Phase != FalloutCellProcessPhase.DataLoaded ||
                    _attachments.Values.Any(attachment => !attachment.Retired && attachment.CellEpochs.ContainsKey(cell)))
                    throw new NotSupportedException("Original overlapping/shared CELL attachment arbitration is unowned; existing owners retained.");
            identity = Guid.NewGuid();
            var epochs = sourceCells.ToDictionary(cell => cell, cell => checked(Require(cell).Epoch + 1), FalloutFormKeyComparer.Instance);
            _attachments.Add(identity, new(identity, _process, epochs, actualRoot,
                children.Select(child => new FalloutCellProcessChild(child.Source, child.Placement.Copy(),
                    FalloutCellProcessChildPhase.Pending, [], null, null)).ToArray(),
                false, false, null));
            foreach (var (cell, epoch) in epochs)
            {
                _cells[cell] = Require(cell) with { Epoch = epoch };
                Write(cell, FalloutCellProcessOperation.BeginAttach, FalloutCellProcessPhase.Attaching, identity, "actual-native-source-child-work-entered");
            }
        });
        return identity;
    }
    // Cold restoration retains original source state but owns no previous
    // process's Godot IDs. Real new child work, not deserialization, enters5.
    internal void BeginColdAttachment(Guid identity, FalloutCellScene scene,
        IReadOnlyList<FalloutFormKey> sourceCells, ulong actualRoot)
    {
        var previous = RequireAttachment(identity);
        if (_cold is null || !_cold.AwaitingNativeAttachments.Contains(identity) || actualRoot == 0)
            throw new InvalidDataException("CELL cold binding has no pending original attachment/new native root.");
        Operation(sourceCells, "actual-new-process-native-CELL-attachment", () =>
        {
            RequireExclusiveNativeRoot(actualRoot);
            if (previous.Retired || previous.NativeRoot != 0 || previous.RootPublished || previous.Failure is not null ||
                !previous.CellEpochs.Keys.ToHashSet(FalloutFormKeyComparer.Instance).SetEquals(sourceCells))
                throw new InvalidDataException("CELL cold attachment crossed a retained source/failure owner.");
            var children = _source.ValidateCurrentScene(scene, sourceCells);
            if (!children.Select(child => child.Source).SequenceEqual(previous.Children.Select(child => child.Source)))
                throw new InvalidDataException("CELL cold attachment changed its exact original selected child work.");
            var epochs = new Dictionary<FalloutFormKey, long>(FalloutFormKeyComparer.Instance);
            foreach (var (cell, oldEpoch) in previous.CellEpochs)
            {
                var state = RequireHealthy(cell);
                if (state.Epoch != oldEpoch || state.Phase != FalloutCellProcessPhase.Attached || state.Data is null)
                    throw new InvalidDataException("CELL cold attachment has no complete source phase/epoch to rebind.");
                epochs.Add(cell, checked(oldEpoch + 1));
            }
            _attachments[identity] = previous with { NativeRoot = actualRoot, CellEpochs = epochs,
                Children = children.Select(child => new FalloutCellProcessChild(child.Source, child.Placement.Copy(),
                    FalloutCellProcessChildPhase.Pending, [], null, null)).ToArray() };
            foreach (var (cell, epoch) in epochs)
            {
                _cells[cell] = Require(cell) with { Epoch = epoch };
                Write(cell, FalloutCellProcessOperation.AttachmentRebind, FalloutCellProcessPhase.Attaching, identity,
                    "actual-new-process-child-attachment-entered");
            }
        });
    }
    internal void ReleaseSource(FalloutFormKey cell)
    {
        var state = RequireHealthy(cell);
        Operation([cell], "actual-CELL-source-data-release", () =>
        {
            if (_attachments.Values.Any(attachment => !attachment.Retired && attachment.CellEpochs.ContainsKey(cell)))
                throw new InvalidOperationException("CELL data release still owns actual native child/attachment consumers.");
            if (state.Phase is not (FalloutCellProcessPhase.DataLoaded or FalloutCellProcessPhase.Constructed))
                throw new NotSupportedException("Original CELL source release entry has an unmatched live operation.");
            Write(cell, FalloutCellProcessOperation.BeginRelease, FalloutCellProcessPhase.ReleasingData, null, "actual-source-CELL-release-entered");
            _source.ReleaseMetadata(cell); _cells[cell] = Require(cell) with { Data = null };
            Write(cell, FalloutCellProcessOperation.CompleteRelease, FalloutCellProcessPhase.Constructed, null, "actual-source-CELL-data-retired");
        });
    }
    private FalloutCellProcessEntry Require(FalloutFormKey cell)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _cells.TryGetValue(cell, out var state) ? state : throw new NotSupportedException("Actual CELL constructor is absent.");
    }
    private void RequireExclusiveNativeRoot(ulong actualRoot)
    {
        if (_attachments.Values.Any(attachment => !attachment.Retired &&
            (attachment.NativeRoot == actualRoot || attachment.Children.Any(child => child.NativeObjects.Contains(actualRoot)))))
            throw new InvalidDataException("CELL native root is still owned by another source attachment or child.");
    }
    private FalloutCellProcessEntry RequireHealthy(FalloutFormKey cell)
    {
        var state = Require(cell);
        if (state.Failure is { } failure) throw new NotSupportedException(failure);
        return state;
    }
    private void Write(FalloutFormKey cell, FalloutCellProcessOperation operation, FalloutCellProcessPhase after,
        Guid? attachment, string owner)
    {
        var state = Require(cell); var sequence = Next();
        _cells[cell] = state with { Phase = after };
        _transitions.Add(new(sequence, cell, operation, state.Phase, after, state.Epoch, attachment, owner));
    }
    private void Operation(IReadOnlyList<FalloutFormKey> cells, string owner, Action action)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_busy) throw new InvalidOperationException("Actual CELL lifecycle producer reentered its operation.");
        _busy = true;
        try { action(); }
        catch (Exception error)
        {
            var failure = owner + ":" + (string.IsNullOrWhiteSpace(error.Message) ? error.GetType().Name : error.Message);
            foreach (var cell in cells)
                if (_cells.TryGetValue(cell, out var state)) _cells[cell] = state with { Failure = state.Failure ?? failure };
            throw;
        }
        finally { _busy = false; }
    }
    private long Next() => _sequence = checked(_sequence + 1);
    public void Dispose()
    {
        if (_disposed) return;
        if (_busy || _attachments.Values.Any(attachment => !attachment.Retired &&
            (attachment.NativeRoot != 0 || attachment.Children.Any(child => child.NativeObjects.Count != 0) ||
                _cold?.AwaitingNativeAttachments.Contains(attachment.Identity) != true)))
            throw new NotSupportedException("Actual CELL lifecycle still owns live native attachment consumers.");
        // An unpublished cold intent owns no new-process native objects. Its
        // cancellation releases only read-only C# metadata; previous native
        // receipts remain evidence and are never relabeled as destroyed.
        _source.ReleaseAllMetadata();
        _disposed = true; _cells.Clear();
    }
}
