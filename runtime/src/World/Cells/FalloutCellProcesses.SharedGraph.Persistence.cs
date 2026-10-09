using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutCellProcesses
{
    private void ValidateNativeCellContinuation(FalloutCellProcessAttachment attachment)
    {
        if (attachment.CellConsumers is null || attachment.CellConsumers.Count != attachment.CellEpochs.Count ||
            !attachment.CellConsumers.Select(cell => cell.Source.Cell.Cell).ToHashSet(FalloutFormKeyComparer.Instance)
                .SetEquals(attachment.CellEpochs.Keys))
            throw new InvalidDataException("CELL continuation omitted a current per-CELL native terrain scope.");
        foreach (var cell in attachment.CellConsumers)
        {
            if (cell.Source != _source.ReadNativeCellSource(cell.Source.Cell.Cell) || string.IsNullOrWhiteSpace(cell.Owner) ||
                !Enum.IsDefined(cell.Phase) || cell.NativeObjects is null || cell.NativeObjects.Any(value => value == 0) ||
                cell.NativeObjects.Distinct().Count() != cell.NativeObjects.Count ||
                cell.Phase is FalloutCellProcessChildPhase.Failed or FalloutCellProcessChildPhase.SourceDisabled ||
                cell.Phase == FalloutCellProcessChildPhase.Published && (cell.Source.Landscape is null || cell.NativeObjects.Count == 0) ||
                cell.Phase != FalloutCellProcessChildPhase.Published && cell.NativeObjects.Count != 0 ||
                cell.Phase == FalloutCellProcessChildPhase.SourceNoDraw && cell.Source.Landscape is not null ||
                attachment.Retired && cell.Phase != FalloutCellProcessChildPhase.Retired ||
                !attachment.Retired && attachment.RootPublished &&
                    cell.Phase is not (FalloutCellProcessChildPhase.Published or FalloutCellProcessChildPhase.SourceNoDraw))
                throw new InvalidDataException("CELL terrain continuation lost its exact winning source/native disposition.");
        }
        var objects = attachment.CellConsumers.SelectMany(cell => cell.NativeObjects)
            .Concat(attachment.Children.SelectMany(child => child.NativeObjects)).Prepend(attachment.NativeRoot)
            .Where(value => value != 0).ToArray();
        if (objects.Distinct().Count() != objects.Length)
            throw new InvalidDataException("CELL terrain continuation borrowed a reference child/root native identity.");
    }

    private void ValidateSharedGraphHistory(FalloutCellProcessesSnapshot saved,
        IReadOnlyDictionary<FalloutFormKey, FalloutCellProcessEntry> cells,
        IReadOnlyDictionary<Guid, FalloutCellProcessAttachment> attachments)
    {
        if (saved.SharedGraphs is null) throw new InvalidDataException("CELL continuation omitted its shared native graph history.");
        var identities = new HashSet<Guid>(); var priorSequence = 0L;
        var previous = new Dictionary<Guid, FalloutCellSharedGraphChange>();
        foreach (var change in saved.SharedGraphs)
        {
            if (change.Identity == Guid.Empty || !identities.Add(change.Identity) || change.Process == Guid.Empty ||
                change.NativeRoot == 0 || change.Entered <= priorSequence || change.Changed <= change.Entered ||
                change.Changed > saved.Sequence || !attachments.ContainsKey(change.Attachment) || !Enum.IsDefined(change.Phase) ||
                change.BeforeEpochs is null || change.AfterEpochs is null || change.BeforeEpochs.Count == 0 || change.AfterEpochs.Count == 0 ||
                !change.BeforeEpochs.ContainsKey(change.BeforeActive) || !change.AfterEpochs.ContainsKey(change.AfterActive) ||
                change.BeforeChildren is null || change.TargetChildren is null || change.TargetCellSources is null ||
                change.BeforeCellConsumers is null || change.PublishedChildren is null || change.PublishedCellConsumers is null ||
                change.DestroyedReferences is null || change.DestroyedCellConsumers is null ||
                change.Failure is not null && string.IsNullOrWhiteSpace(change.Failure))
                throw new InvalidDataException("Shared CELL history lost its invocation/source/native/order fields.");
            var world = _source.ReadIdentity(change.BeforeActive).Worldspace;
            if (world is null || _source.ReadIdentity(change.AfterActive).Worldspace != world ||
                change.BeforeEpochs.Concat(change.AfterEpochs).Any(pair => !cells.TryGetValue(pair.Key, out var cell) ||
                    pair.Value < 2 || pair.Value > cell.Epoch || cell.Source.Worldspace != world) ||
                change.AfterEpochs.Any(pair => change.BeforeEpochs.TryGetValue(pair.Key, out var epoch) && epoch != pair.Value))
                throw new InvalidDataException("Shared graph changed a retained CELL epoch or crossed its actual world.");
            ValidateHistoryChildren(change.BeforeChildren, change.BeforeEpochs);
            ValidateHistoryChildren(change.PublishedChildren, change.Phase == FalloutCellSharedGraphPhase.Cancelled ? change.BeforeEpochs : change.AfterEpochs);
            foreach (var child in change.TargetChildren)
            {
                _source.ValidateChildSource(new(child.Source, child.Placement, FalloutCellProcessChildPhase.Pending, [], null, null));
                if (!change.AfterEpochs.ContainsKey(child.Placement.Cell))
                    throw new InvalidDataException("Shared target child has no exact current CELL in the entered source set.");
            }
            if (change.TargetChildren.Select(child => child.Source.Reference).Distinct(FalloutFormKeyComparer.Instance).Count() != change.TargetChildren.Count ||
                change.TargetCellSources.Count != change.AfterEpochs.Count ||
                !change.TargetCellSources.Select(source => source.Cell.Cell).ToHashSet(FalloutFormKeyComparer.Instance).SetEquals(change.AfterEpochs.Keys) ||
                change.TargetCellSources.Any(source => source != _source.ReadNativeCellSource(source.Cell.Cell)))
                throw new InvalidDataException("Shared target repeated a reference or omitted/drifted a winning CELL/LAND declaration.");
            ValidateHistoryNativeCells(change.BeforeCellConsumers, change.BeforeEpochs);
            ValidateHistoryNativeOwnership(change.NativeRoot, change.BeforeChildren, change.BeforeCellConsumers);
            var terminal = change.Phase is FalloutCellSharedGraphPhase.Complete or FalloutCellSharedGraphPhase.Cancelled;
            if (terminal)
            {
                var cancelling = change.Phase == FalloutCellSharedGraphPhase.Cancelled;
                var entered = change with { Phase = cancelling ? FalloutCellSharedGraphPhase.Cancelling : FalloutCellSharedGraphPhase.Publishing };
                var expected = cancelling ? change.BeforeChildren.Select(child => new FalloutCellProcessPlacedChild(child.Source, child.Placement)) : change.TargetChildren;
                var finalEpochs = cancelling ? change.BeforeEpochs : change.AfterEpochs;
                if (!SamePlacedChildren(expected, change.PublishedChildren.Select(child => new FalloutCellProcessPlacedChild(child.Source, child.Placement))) ||
                    !UniqueSet(change.DestroyedReferences, SharedRetiringReferences(entered)) ||
                    !UniqueSet(change.DestroyedCellConsumers, SharedRetiringCells(entered)))
                    throw new InvalidDataException("Shared graph terminal receipt omitted actual destroyed or published source children.");
                ValidateHistoryNativeCells(change.PublishedCellConsumers, finalEpochs);
                ValidateHistoryNativeOwnership(change.NativeRoot, change.PublishedChildren, change.PublishedCellConsumers);
                var before = change.BeforeChildren.ToDictionary(child => child.Source.Reference, FalloutFormKeyComparer.Instance);
                foreach (var child in change.PublishedChildren)
                    if (before.TryGetValue(child.Source.Reference, out var retained) && !SameNativeChild(retained, child, comparePlacement: cancelling))
                        throw new InvalidDataException("Shared graph replaced a retained living reference without its actual native lifetime.");
                foreach (var cell in change.PublishedCellConsumers)
                    if (change.BeforeCellConsumers.SingleOrDefault(value => value.Source.Cell.Cell == cell.Source.Cell.Cell) is { } retained &&
                        !SameNativeCell(retained, cell))
                        throw new InvalidDataException("Shared graph replaced a retained living LAND consumer.");
                foreach (var cell in SharedRetiringCells(entered))
                    RequireGraphTransition(saved, change, cell, FalloutCellProcessOperation.CompleteDetach);
                if (!cancelling)
                    foreach (var cell in change.AfterEpochs.Keys.Except(change.BeforeEpochs.Keys, FalloutFormKeyComparer.Instance))
                        RequireGraphTransition(saved, change, cell, FalloutCellProcessOperation.CompleteAttach);
            }
            else if (change.Phase == FalloutCellSharedGraphPhase.RootRetired)
            {
                if (change.PublishedChildren.Count != 0 || change.PublishedCellConsumers.Count != 0 ||
                    !UniqueSet(change.DestroyedReferences, change.BeforeChildren.Select(child => child.Source.Reference)
                        .Union(change.TargetChildren.Select(child => child.Source.Reference), FalloutFormKeyComparer.Instance)) ||
                    !UniqueSet(change.DestroyedCellConsumers, change.BeforeEpochs.Keys.Union(change.AfterEpochs.Keys, FalloutFormKeyComparer.Instance)))
                    throw new InvalidDataException("Shared root retirement falsely asserted successful replacement or lost still-owned consumers.");
            }
            else if (change.PublishedChildren.Count != 0 || change.PublishedCellConsumers.Count != 0)
                throw new InvalidDataException("Entered shared graph fabricated a terminal native publication.");
            foreach (var cell in change.AfterEpochs.Keys.Except(change.BeforeEpochs.Keys, FalloutFormKeyComparer.Instance))
                RequireGraphTransition(saved, change, cell, FalloutCellProcessOperation.BeginAttach);
            if (previous.TryGetValue(change.Attachment, out var prior) && prior.Process == change.Process)
            {
                var previousEpochs = prior.Phase == FalloutCellSharedGraphPhase.Cancelled ? prior.BeforeEpochs : prior.AfterEpochs;
                if (prior.Phase is not (FalloutCellSharedGraphPhase.Complete or FalloutCellSharedGraphPhase.Cancelled) ||
                    prior.NativeRoot != change.NativeRoot || !SameEpochs(previousEpochs, change.BeforeEpochs) ||
                    !SameNativeChildren(prior.PublishedChildren, change.BeforeChildren) ||
                    !SameNativeCells(prior.PublishedCellConsumers, change.BeforeCellConsumers))
                    throw new InvalidDataException("Shared graph invocation skipped its previous exact native/source result.");
            }
            // Independent native roots can enter and finish in interleaved
            // source order. The retained list is ordered by entry, while each
            // invocation keeps its own actual changed/transition interval.
            previous[change.Attachment] = change; priorSequence = change.Entered;
        }
        foreach (var change in previous.Values.Where(value => value.Phase is FalloutCellSharedGraphPhase.Complete or FalloutCellSharedGraphPhase.Cancelled))
        {
            var current = attachments[change.Attachment];
            var epochs = change.Phase == FalloutCellSharedGraphPhase.Cancelled ? change.BeforeEpochs : change.AfterEpochs;
            if (!current.CellEpochs.Keys.ToHashSet(FalloutFormKeyComparer.Instance).SetEquals(epochs.Keys) ||
                current.CellEpochs.Any(pair => pair.Value < epochs[pair.Key]) ||
                !SamePlacedChildren(current.Children.Select(child => new FalloutCellProcessPlacedChild(child.Source, child.Placement)),
                    change.PublishedChildren.Select(child => new FalloutCellProcessPlacedChild(child.Source, child.Placement))))
                throw new InvalidDataException("Current CELL graph differs from its latest completed source graph receipt.");
            if (!current.Retired && current.Process == change.Process && current.NativeRoot != 0 &&
                (current.NativeRoot != change.NativeRoot || !SameNativeChildren(current.Children, change.PublishedChildren) ||
                    !SameNativeCells(current.CellConsumers, change.PublishedCellConsumers)))
                throw new InvalidDataException("Current shared CELL publication borrowed a different living native graph.");
        }
    }
    private void ValidateHistoryChildren(IReadOnlyList<FalloutCellProcessChild> children, IReadOnlyDictionary<FalloutFormKey, long> epochs)
    {
        if (children.Select(child => child.Source.Reference).Distinct(FalloutFormKeyComparer.Instance).Count() != children.Count)
            throw new InvalidDataException("Shared graph history repeated a source reference.");
        foreach (var child in children)
        {
            _source.ValidateChildSource(child);
            if (!epochs.ContainsKey(child.Placement.Cell) || child.Failure is not null || string.IsNullOrWhiteSpace(child.Owner) ||
                child.Phase is not (FalloutCellProcessChildPhase.Published or FalloutCellProcessChildPhase.SourceDisabled or FalloutCellProcessChildPhase.SourceNoDraw) ||
                child.NativeObjects is null || child.NativeObjects.Any(value => value == 0) || child.NativeObjects.Distinct().Count() != child.NativeObjects.Count ||
                (child.Phase == FalloutCellProcessChildPhase.Published) != (child.NativeObjects.Count != 0))
                throw new InvalidDataException("Shared graph history falsely marked an unfinished source/native child as complete.");
        }
    }
    private void ValidateHistoryNativeCells(IReadOnlyList<FalloutCellNativeConsumers> consumers, IReadOnlyDictionary<FalloutFormKey, long> epochs)
    {
        if (consumers.Count != epochs.Count || !consumers.Select(value => value.Source.Cell.Cell).ToHashSet(FalloutFormKeyComparer.Instance).SetEquals(epochs.Keys))
            throw new InvalidDataException("Shared graph history omitted a CELL/LAND scope.");
        foreach (var value in consumers)
            if (value.Source != _source.ReadNativeCellSource(value.Source.Cell.Cell) || string.IsNullOrWhiteSpace(value.Owner) ||
                value.NativeObjects is null || value.NativeObjects.Any(identity => identity == 0) || value.NativeObjects.Distinct().Count() != value.NativeObjects.Count ||
                value.Source.Landscape is null && (value.Phase != FalloutCellProcessChildPhase.SourceNoDraw || value.NativeObjects.Count != 0) ||
                value.Source.Landscape is not null && (value.Phase != FalloutCellProcessChildPhase.Published || value.NativeObjects.Count == 0))
                throw new InvalidDataException("Shared graph history changed the actual winning LAND/native consumer.");
    }
    private static void RequireGraphTransition(FalloutCellProcessesSnapshot saved, FalloutCellSharedGraphChange change,
        FalloutFormKey cell, FalloutCellProcessOperation operation)
    {
        var epoch = change.AfterEpochs.TryGetValue(cell, out var after) ? after : change.BeforeEpochs[cell];
        if (saved.Transitions.Count(row => row.Attachment == change.Attachment && row.Cell == cell && row.CellEpoch == epoch &&
                row.Operation == operation && row.Sequence > change.Entered && row.Sequence < change.Changed) != 1)
            throw new InvalidDataException("Shared graph omitted its actual CELL phase transition within the entered invocation.");
    }
    private static void ValidateHistoryNativeOwnership(ulong root, IEnumerable<FalloutCellProcessChild> children,
        IEnumerable<FalloutCellNativeConsumers> cells)
    {
        var objects = children.SelectMany(child => child.NativeObjects).Concat(cells.SelectMany(cell => cell.NativeObjects))
            .Prepend(root).ToArray();
        if (objects.Any(identity => identity == 0) || objects.Distinct().Count() != objects.Length)
            throw new InvalidDataException("Shared graph history borrowed a root, reference or CELL terrain native identity.");
    }
    private static bool UniqueSet(IEnumerable<FalloutFormKey> values, IEnumerable<FalloutFormKey> expected)
    { var rows = values.ToArray(); return rows.Distinct(FalloutFormKeyComparer.Instance).Count() == rows.Length && rows.ToHashSet(FalloutFormKeyComparer.Instance).SetEquals(expected); }
    private static bool SameEpochs(IReadOnlyDictionary<FalloutFormKey, long> a, IReadOnlyDictionary<FalloutFormKey, long> b) =>
        a.Count == b.Count && a.All(pair => b.TryGetValue(pair.Key, out var value) && value == pair.Value);
    private static bool SameNativeChild(FalloutCellProcessChild a, FalloutCellProcessChild b, bool comparePlacement) =>
        a.Source == b.Source && a.Phase == b.Phase && a.Owner == b.Owner && a.Failure == b.Failure &&
        a.NativeObjects.SequenceEqual(b.NativeObjects) && (!comparePlacement || SamePlacedChildren(
            [new(a.Source, a.Placement)], [new(b.Source, b.Placement)]));
    private static bool SameNativeChildren(IReadOnlyList<FalloutCellProcessChild> a, IReadOnlyList<FalloutCellProcessChild> b) =>
        a.Count == b.Count && a.Zip(b).All(pair => SameNativeChild(pair.First, pair.Second, comparePlacement: true));
    private static bool SameNativeCell(FalloutCellNativeConsumers a, FalloutCellNativeConsumers b) =>
        a.Source == b.Source && a.Phase == b.Phase && a.Owner == b.Owner && a.NativeObjects.SequenceEqual(b.NativeObjects);
    private static bool SameNativeCells(IReadOnlyList<FalloutCellNativeConsumers> a, IReadOnlyList<FalloutCellNativeConsumers> b) =>
        a.Count == b.Count && a.Zip(b).All(pair => SameNativeCell(pair.First, pair.Second));
    private static FalloutCellSharedGraphChange CopySharedGraph(FalloutCellSharedGraphChange change) => change with
    {
        BeforeEpochs = CopyEpochs(change.BeforeEpochs),
        AfterEpochs = CopyEpochs(change.AfterEpochs),
        BeforeChildren = CopyChildren(change.BeforeChildren),
        TargetChildren = change.TargetChildren.Select(CopyPlacedChild).ToArray(),
        TargetCellSources = change.TargetCellSources.ToArray(),
        BeforeCellConsumers = CopyNativeCells(change.BeforeCellConsumers),
        PublishedChildren = CopyChildren(change.PublishedChildren),
        PublishedCellConsumers = CopyNativeCells(change.PublishedCellConsumers),
        DestroyedReferences = change.DestroyedReferences.ToArray(),
        DestroyedCellConsumers = change.DestroyedCellConsumers.ToArray(),
    };
}
