using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutCellProcesses
{
    private readonly List<FalloutCellSharedGraphChange> _sharedGraphs = [];
    private string? SharedGraphSaveBlocker => _sharedGraphs.FirstOrDefault(change => change.Failure is not null ||
        change.Phase is not (FalloutCellSharedGraphPhase.Complete or FalloutCellSharedGraphPhase.Cancelled or
            FalloutCellSharedGraphPhase.RootRetired)) is { } pending ?
        "actual-shared-CELL-graph:" + pending.Identity + ":" + (pending.Failure ?? pending.Phase.ToString()) : null;

    internal Guid BeginSharedGraph(Guid attachment, ulong actualRoot, FalloutCellScene before,
        FalloutCellScene after, IReadOnlyList<FalloutFormKey> sourceCells)
    {
        var owner = RequireAttachment(attachment);
        if (sourceCells.Count == 0 || sourceCells.Distinct(FalloutFormKeyComparer.Instance).Count() != sourceCells.Count)
            throw new InvalidDataException("Shared CELL extension duplicated or omitted its target source set.");
        _source.ValidateRetainedScene(before, owner);
        foreach (var cell in sourceCells) LoadSource(cell);
        var target = _source.ValidateCurrentScene(after, sourceCells);
        var targetSources = sourceCells.Select(_source.ReadNativeCellSource).ToArray();
        Guid identity = default;
        Operation(owner.CellEpochs.Keys.Union(sourceCells, FalloutFormKeyComparer.Instance).ToArray(),
            "actual-shared-native-CELL-graph-extension-entered", () =>
        {
            if (owner.Retired || !owner.RootPublished || owner.NativeRoot != actualRoot || actualRoot == 0 ||
                owner.Failure is not null || ActiveSharedGraph(attachment) is not null ||
                owner.CellEpochs.Any(pair => RequireHealthy(pair.Key).Epoch != pair.Value ||
                    Require(pair.Key).Phase != FalloutCellProcessPhase.Attached) ||
                before.Cell.Worldspace is null || before.Cell.Worldspace != after.Cell.Worldspace)
                throw new InvalidDataException("Shared CELL extension has no complete same-world native attachment.");
            RequireNativeCellsPublished(owner);
            var oldChildren = owner.Children.ToDictionary(child => child.Source.Reference, FalloutFormKeyComparer.Instance);
            foreach (var child in target)
                if (oldChildren.TryGetValue(child.Source.Reference, out var retained) && retained.Source != child.Source)
                    throw new InvalidDataException("Shared CELL extension changed an immutable winning reference.");
            var epochs = new Dictionary<FalloutFormKey, long>(FalloutFormKeyComparer.Instance);
            foreach (var cell in sourceCells)
            {
                if (owner.CellEpochs.TryGetValue(cell, out var retained)) { epochs.Add(cell, retained); continue; }
                if (RequireHealthy(cell).Phase != FalloutCellProcessPhase.DataLoaded ||
                    _attachments.Values.Any(value => !value.Retired && value.CellEpochs.ContainsKey(cell)))
                    throw new InvalidOperationException("Incoming shared CELL still owns another native attachment.");
                epochs.Add(cell, checked(Require(cell).Epoch + 1));
            }
            var unionEpochs = owner.CellEpochs.ToDictionary(pair => pair.Key, pair => pair.Value, FalloutFormKeyComparer.Instance);
            foreach (var (cell, epoch) in epochs) unionEpochs[cell] = epoch;
            var children = owner.Children.Concat(target.Where(child => !oldChildren.ContainsKey(child.Source.Reference))
                .Select(child => new FalloutCellProcessChild(child.Source, child.Placement.Copy(),
                    FalloutCellProcessChildPhase.Pending, [], null, null))).ToArray();
            var consumers = owner.CellConsumers.Concat(sourceCells.Where(cell => !owner.CellEpochs.ContainsKey(cell))
                .Select(ConstructNativeCell)).ToArray();
            identity = Guid.NewGuid(); var entered = Next();
            _sharedGraphs.Add(new(identity, attachment, _process, actualRoot, before.Cell.FormKey, after.Cell.FormKey,
                CopyEpochs(owner.CellEpochs), CopyEpochs(epochs), CopyChildren(owner.Children),
                target.Select(CopyPlacedChild).ToArray(), targetSources, CopyNativeCells(owner.CellConsumers), [], [], [], [],
                FalloutCellSharedGraphPhase.Preparing, entered, entered, null));
            _attachments[attachment] = owner with { CellEpochs = unionEpochs, Children = children,
                CellConsumers = consumers, RootPublished = false };
            foreach (var (cell, epoch) in epochs.Where(pair => !owner.CellEpochs.ContainsKey(pair.Key)))
            {
                _cells[cell] = Require(cell) with { Epoch = epoch };
                Write(cell, FalloutCellProcessOperation.BeginAttach, FalloutCellProcessPhase.Attaching, attachment,
                    "actual-incoming-shared-CELL-native-work-entered");
            }
        });
        return identity;
    }

    internal void BeginSharedGraphPublication(Guid attachment, ulong actualRoot, FalloutCellScene target)
    {
        var owner = RequireAttachment(attachment); var change = RequireActiveSharedGraph(attachment);
        Operation(owner.CellEpochs.Keys.ToArray(), "actual-shared-CELL-graph-publication-entered", () =>
        {
            RequireGraphRoot(owner, change, actualRoot, FalloutCellSharedGraphPhase.Preparing);
            var current = _source.ValidateCurrentScene(target, change.AfterEpochs.Keys.ToArray());
            if (target.Cell.FormKey != change.AfterActive || !SamePlacedChildren(current, change.TargetChildren))
                throw new NotSupportedException("Shared CELL placement changed during entered source/native work; the target must be rebuilt.");
            if (change.TargetChildren.Any(targetChild => owner.Children.Single(child => child.Source == targetChild.Source).Phase is
                    FalloutCellProcessChildPhase.Failed) || owner.Failure is not null)
                throw new NotSupportedException("Shared CELL publication retains a failed source child.");
            StoreGraph(change with { Phase = FalloutCellSharedGraphPhase.Publishing });
            foreach (var cell in change.BeforeEpochs.Keys.Where(cell => !change.AfterEpochs.ContainsKey(cell)))
                Write(cell, FalloutCellProcessOperation.BeginDetach, FalloutCellProcessPhase.Detaching, attachment,
                    "actual-outgoing-shared-CELL-native-retirement-entered");
        });
    }

    internal void BeginSharedGraphCancellation(Guid attachment, ulong actualRoot)
    {
        var owner = RequireAttachment(attachment); var change = RequireActiveSharedGraph(attachment);
        if (change.Phase == FalloutCellSharedGraphPhase.Cancelling) return;
        Operation(owner.CellEpochs.Keys.ToArray(), "actual-prepared-shared-CELL-cancellation-entered", () =>
        {
            RequireGraphRoot(owner, change, actualRoot, FalloutCellSharedGraphPhase.Preparing);
            StoreGraph(change with { Phase = FalloutCellSharedGraphPhase.Cancelling });
            foreach (var cell in change.AfterEpochs.Keys.Where(cell => !change.BeforeEpochs.ContainsKey(cell)))
                Write(cell, FalloutCellProcessOperation.BeginDetach, FalloutCellProcessPhase.Detaching, attachment,
                    "actual-cancelled-incoming-CELL-native-retirement-entered");
        });
    }

    internal void RetireSharedChild(Guid attachment, FalloutFormKey reference, string originalOwner)
    {
        var owner = RequireAttachment(attachment); var change = RequireActiveSharedGraph(attachment);
        Operation(owner.CellEpochs.Keys.ToArray(), originalOwner, () =>
        {
            if (!SharedRetiringReferences(change).Contains(reference, FalloutFormKeyComparer.Instance))
                throw new InvalidOperationException("Shared graph tried to destroy a retained source reference.");
            var index = owner.Children.ToList().FindIndex(child => child.Source.Reference == reference);
            if (index < 0) throw new InvalidDataException("Shared graph destruction has no exact source child.");
            var children = owner.Children.ToArray();
            children[index] = children[index] with { Phase = FalloutCellProcessChildPhase.Retired, NativeObjects = [], Owner = originalOwner };
            _attachments[attachment] = owner with { Children = children };
            StoreGraph(change with { DestroyedReferences = change.DestroyedReferences.Append(reference)
                .Distinct(FalloutFormKeyComparer.Instance).ToArray() });
        });
    }

    internal void CompleteSharedGraph(Guid attachment, ulong actualRoot)
    {
        var owner = RequireAttachment(attachment); var change = RequireActiveSharedGraph(attachment);
        Operation(owner.CellEpochs.Keys.ToArray(), "actual-shared-CELL-graph-consumers-returned", () =>
        {
            if (change.Phase is not (FalloutCellSharedGraphPhase.Publishing or FalloutCellSharedGraphPhase.Cancelling) ||
                owner.NativeRoot != actualRoot || change.NativeRoot != actualRoot || owner.Retired || actualRoot == 0)
                throw new InvalidDataException("Shared CELL completion has no entered actual publication/cancellation.");
            var cancelling = change.Phase == FalloutCellSharedGraphPhase.Cancelling;
            var epochs = cancelling ? change.BeforeEpochs : change.AfterEpochs;
            var target = cancelling ? change.BeforeChildren.Select(child => new FalloutCellProcessPlacedChild(child.Source, child.Placement)) : change.TargetChildren;
            var targetChildren = target.Select(child =>
            {
                if (!cancelling) _source.RequireCurrentChildPlacement(child);
                var actual = owner.Children.Single(value => value.Source == child.Source);
                if (actual.Phase is FalloutCellProcessChildPhase.Pending or FalloutCellProcessChildPhase.Failed or FalloutCellProcessChildPhase.Retired || actual.Failure is not null)
                    throw new NotSupportedException("Actual shared graph still owns an unreturned target reference consumer.");
                return actual with { Placement = child.Placement.Copy() };
            }).ToArray();
            var retiredReferences = SharedRetiringReferences(change);
            var retiredCells = SharedRetiringCells(change);
            if (!change.DestroyedReferences.ToHashSet(FalloutFormKeyComparer.Instance).SetEquals(retiredReferences) ||
                !change.DestroyedCellConsumers.ToHashSet(FalloutFormKeyComparer.Instance).SetEquals(retiredCells) ||
                owner.Children.Where(child => retiredReferences.Contains(child.Source.Reference, FalloutFormKeyComparer.Instance))
                    .Any(child => child.Phase != FalloutCellProcessChildPhase.Retired || child.NativeObjects.Count != 0) ||
                owner.CellConsumers.Where(cell => retiredCells.Contains(cell.Source.Cell.Cell, FalloutFormKeyComparer.Instance))
                    .Any(cell => cell.Phase != FalloutCellProcessChildPhase.Retired || cell.NativeObjects.Count != 0))
                throw new InvalidOperationException("Shared CELL completion discarded actual outgoing native ownership.");
            var targetConsumers = epochs.Keys.Select(cell => owner.CellConsumers.Single(value => value.Source.Cell.Cell == cell)).ToArray();
            var candidate = owner with { CellEpochs = CopyEpochs(epochs), Children = targetChildren, CellConsumers = targetConsumers, RootPublished = true };
            RequireNativeCellsPublished(candidate);
            foreach (var cell in epochs.Keys)
                if (Require(cell).Epoch != epochs[cell] || Require(cell).Phase is not (FalloutCellProcessPhase.Attached or FalloutCellProcessPhase.Attaching))
                    throw new InvalidDataException("Shared graph completion changed an actual CELL phase/epoch.");
            foreach (var cell in retiredCells)
                if (Require(cell).Phase != FalloutCellProcessPhase.Detaching)
                    throw new InvalidDataException("Shared CELL release has no consumed actual detach prefix.");
            foreach (var cell in retiredCells)
                Write(cell, FalloutCellProcessOperation.CompleteDetach, FalloutCellProcessPhase.DataLoaded, attachment,
                    "actual-outgoing-shared-CELL-consumers-destroyed");
            foreach (var cell in epochs.Keys.Where(cell => Require(cell).Phase == FalloutCellProcessPhase.Attaching))
                Write(cell, FalloutCellProcessOperation.CompleteAttach, FalloutCellProcessPhase.Attached, attachment,
                    "actual-incoming-shared-CELL-consumers-published");
            _attachments[attachment] = candidate;
            StoreGraph(change with { Phase = cancelling ? FalloutCellSharedGraphPhase.Cancelled : FalloutCellSharedGraphPhase.Complete,
                PublishedChildren = CopyChildren(targetChildren), PublishedCellConsumers = CopyNativeCells(targetConsumers) });
        });
    }

    internal void FailSharedGraph(Guid attachment, Exception error)
    {
        ArgumentNullException.ThrowIfNull(error); var owner = RequireAttachment(attachment);
        var failure = string.IsNullOrWhiteSpace(error.Message) ? error.GetType().Name : error.Message;
        if (ActiveSharedGraph(attachment) is { } change) StoreGraph(change with { Failure = change.Failure ?? failure });
        _attachments[attachment] = owner with { Failure = owner.Failure ?? failure };
        foreach (var cell in owner.CellEpochs.Keys) _cells[cell] = Require(cell) with { Failure = Require(cell).Failure ?? failure };
    }

    internal FalloutCellSharedGraphChange? ReadSharedGraph(Guid attachment) => ActiveSharedGraph(attachment);
    private FalloutCellSharedGraphChange? ActiveSharedGraph(Guid attachment) => _sharedGraphs.LastOrDefault(change =>
        change.Attachment == attachment && change.Process == _process && change.Phase is
            FalloutCellSharedGraphPhase.Preparing or FalloutCellSharedGraphPhase.Publishing or FalloutCellSharedGraphPhase.Cancelling or FalloutCellSharedGraphPhase.RetiringRoot);
    private FalloutCellSharedGraphChange RequireActiveSharedGraph(Guid attachment) => ActiveSharedGraph(attachment) ??
        throw new InvalidOperationException("Actual shared CELL graph has no entered retained operation.");
    private void StoreGraph(FalloutCellSharedGraphChange change)
    {
        var index = _sharedGraphs.FindIndex(value => value.Identity == change.Identity);
        if (index < 0) throw new InvalidDataException("Shared graph update lost its exact retained invocation.");
        _sharedGraphs[index] = change with { Changed = Next() };
    }
    private static void RequireGraphRoot(FalloutCellProcessAttachment owner, FalloutCellSharedGraphChange change,
        ulong root, FalloutCellSharedGraphPhase phase)
    {
        if (owner.Retired || owner.NativeRoot != root || change.NativeRoot != root || root == 0 || change.Phase != phase)
            throw new InvalidDataException("Shared CELL work crossed the actual native root/entered phase.");
    }
    private static FalloutFormKey[] SharedRetiringReferences(FalloutCellSharedGraphChange change) => change.Phase switch
    {
        FalloutCellSharedGraphPhase.Publishing => change.BeforeChildren.Select(child => child.Source.Reference)
            .Except(change.TargetChildren.Select(child => child.Source.Reference), FalloutFormKeyComparer.Instance).ToArray(),
        FalloutCellSharedGraphPhase.Cancelling => change.TargetChildren.Select(child => child.Source.Reference)
            .Except(change.BeforeChildren.Select(child => child.Source.Reference), FalloutFormKeyComparer.Instance).ToArray(),
        _ => throw new InvalidOperationException("Shared reference destruction has no publication/cancellation owner."),
    };
    private static FalloutFormKey[] SharedRetiringCells(FalloutCellSharedGraphChange change) => change.Phase switch
    {
        FalloutCellSharedGraphPhase.Publishing => change.BeforeEpochs.Keys.Except(change.AfterEpochs.Keys, FalloutFormKeyComparer.Instance).ToArray(),
        FalloutCellSharedGraphPhase.Cancelling => change.AfterEpochs.Keys.Except(change.BeforeEpochs.Keys, FalloutFormKeyComparer.Instance).ToArray(),
        _ => throw new InvalidOperationException("Shared CELL destruction has no publication/cancellation owner."),
    };
    private static IReadOnlyDictionary<FalloutFormKey, long> CopyEpochs(IReadOnlyDictionary<FalloutFormKey, long> epochs) =>
        epochs.ToDictionary(pair => pair.Key, pair => pair.Value, FalloutFormKeyComparer.Instance);
    private static FalloutCellProcessPlacedChild CopyPlacedChild(FalloutCellProcessPlacedChild child) => child with { Placement = child.Placement.Copy() };
    private static FalloutCellProcessChild[] CopyChildren(IEnumerable<FalloutCellProcessChild> children) =>
        children.Select(child => child with { Placement = child.Placement.Copy(), NativeObjects = child.NativeObjects.ToArray() }).ToArray();
    private static FalloutCellNativeConsumers[] CopyNativeCells(IEnumerable<FalloutCellNativeConsumers> cells) =>
        cells.Select(cell => cell with { NativeObjects = cell.NativeObjects.ToArray() }).ToArray();
    private static bool SamePlacedChildren(IEnumerable<FalloutCellProcessPlacedChild> left, IEnumerable<FalloutCellProcessPlacedChild> right) =>
        left.Zip(right, (a, b) => a.Source == b.Source && a.Placement.Cell == b.Placement.Cell &&
            a.Placement.Position.SequenceEqual(b.Placement.Position) && a.Placement.RotationRadians.SequenceEqual(b.Placement.RotationRadians)).All(value => value) &&
        left.Count() == right.Count();
}
