using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed record FalloutCurrentCellProcessListMember(FalloutCellProcessReference Source, long Membership);
internal sealed record FalloutCurrentCellProcessList(FalloutCellProcessIdentity Source, string GraphSha256,
    long Revision, IReadOnlyList<FalloutCurrentCellProcessListMember> Members);
internal sealed record FalloutUnownedCellProcessList(FalloutCellProcessIdentity Source, string Owner);
internal sealed record FalloutCurrentCellProcessListsSnapshot(string Schema, string Stack, string Contract, Guid CapturedProcess,
    long Revision, long Membership, IReadOnlyList<FalloutCurrentCellProcessList> Cells,
    IReadOnlyList<FalloutUnownedCellProcessList> Unowned, FalloutActorProcessRuntimeHandoff? ColdHandoff);

internal sealed partial class FalloutReferenceWorld
{
    private const string CurrentProcessListsSchema = "opennv-current-cell-process-reference-lists/v1";
    private readonly Guid _sourceCurrentListProcess = Guid.NewGuid();
    private FalloutActorProcessRuntimeHandoff? _sourceCurrentListCold;

    private FalloutCellProcessReferenceList ReadActualCurrentCellReferences(FalloutFormKey cell)
    {
        var source = CellProcesses.ReadSourceQueueReferences(cell);
        if (_unownedCellReferenceLists.TryGetValue(cell, out var unowned)) throw new NotSupportedException(unowned);
        // Winning source order is the initial source list. A reference moved
        // in by an uninspected original ParentCELL/list insertion cannot be
        // silently appended in dictionary or FormID order.
        if (_instances.Values.Any(instance => instance.Placement is { } placement &&
            placement.Cell == cell && instance.Cell != cell))
            throw new NotSupportedException("actual-cross-CELL-reference-list-insertion-and-order-producer-unbound");
        if (_currentPlayerProcessCell is { } playerCell && playerCell() == cell &&
            !source.References.Any(reference => reference.Reference == _enginePlayer))
            throw new NotSupportedException("actual-Player-current-CELL-reference-list-link-producer-unbound");
        if (!_currentCellReferenceLists.TryGetValue(cell, out var list))
        {
            var membership = new Dictionary<FalloutFormKey, long>(FalloutFormKeyComparer.Instance);
            foreach (var reference in source.References)
                membership.Add(reference.Reference, _currentCellReferenceMembership = checked(_currentCellReferenceMembership + 1));
            list = (source.GraphSha256, _currentCellReferenceRevision = checked(_currentCellReferenceRevision + 1), membership);
            _currentCellReferenceLists.Add(cell, list);
        }
        if (list.Graph != source.GraphSha256 || !list.Membership.Keys.ToHashSet(FalloutFormKeyComparer.Instance)
            .SetEquals(source.References.Select(reference => reference.Reference)))
            throw new InvalidDataException("Actual initial CELL reference-list source changed during its lifetime.");
        var current = new List<FalloutCellProcessListReference>(source.References.Count);
        foreach (var reference in source.References)
        {
            var instance = Get(reference.Reference);
            if (instance.Deleted || Placement(reference.Reference).Cell != cell)
                throw new NotSupportedException("actual-current-CELL-reference-list-removal-producer-unbound");
            var actor = reference.Signature is "ACHR" or "ACRE" ? ReadCombatActorIdentity(reference.Reference) : null;
            var process = actor is null ? null : ActorProcesses.Read(reference.Reference);
            current.Add(new(reference, cell, list.Membership[reference.Reference], ReadActualCurrentReferenceFlags(reference.Reference),
                actor, process?.Epoch, process?.Level, actor is null ? null :
                    new(process?.Level is not null, "actual-source-Actor-current-process-pointer/" + process!.Epoch)));
        }
        return new(source.Source, list.Revision, current, "actual-winning-CELL-source-reference-order-and-current-canonical-factories");
    }
    private void RetainSourceCellReferenceTransfer(FalloutReferenceInstance instance, FalloutReferencePlacement previous,
        FalloutReferencePlacement current)
    {
        if (!SourceProcessQueuesConfigured || previous.Cell == current.Cell) return;
        const string missing = "actual-cross-CELL-ParentCELL-and-ordered-reference-list-transfer-consumer-unbound";
        _unownedCellReferenceLists.TryAdd(previous.Cell, missing + ":" + instance.Reference);
        _unownedCellReferenceLists.TryAdd(current.Cell, missing + ":" + instance.Reference);
    }
    private FalloutCurrentCellProcessListsSnapshot CaptureSourceCurrentCellLists()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var rows = _currentCellReferenceLists.Select(item =>
        {
            var source = CellProcesses.ReadSourceQueueReferences(item.Key);
            return new FalloutCurrentCellProcessList(source.Source, source.GraphSha256, item.Value.Revision,
                source.References.Select(reference => new FalloutCurrentCellProcessListMember(reference, item.Value.Membership[reference.Reference])).ToArray());
        }).OrderBy(item => item.Revision).ToArray();
        return new(CurrentProcessListsSchema, _processQueueStack!, _processQueueDeclaration!.Contract, _sourceCurrentListProcess,
            _currentCellReferenceRevision, _currentCellReferenceMembership, rows,
            _unownedCellReferenceLists.Select(item => new FalloutUnownedCellProcessList(CellProcesses.ReadSourceQueueCell(item.Key), item.Value)).ToArray(),
            _sourceCurrentListCold);
    }
    private void RestoreSourceCurrentCellLists(FalloutCurrentCellProcessListsSnapshot snapshot)
    {
        ValidateSourceCurrentCellLists(snapshot);
        if (snapshot.CapturedProcess == _sourceCurrentListProcess)
            throw new InvalidDataException("Current CELL list cold restoration reused its actual process owner.");
        // Configure supplies its source identity before these current fields
        // become the published world owner; use the selected shared contract.
        foreach (var list in snapshot.Cells)
        {
            var source = CellProcesses.ReadSourceQueueReferences(list.Source.Cell);
            if (source.Source != list.Source || source.GraphSha256 != list.GraphSha256 ||
                !source.References.SequenceEqual(list.Members.Select(member => member.Source)))
                throw new InvalidDataException("Saved current CELL list changed its complete winning reference order/master graph.");
            _currentCellReferenceLists.Add(list.Source.Cell, (list.GraphSha256, list.Revision,
                list.Members.ToDictionary(member => member.Source.Reference, member => member.Membership, FalloutFormKeyComparer.Instance)));
        }
        foreach (var failure in snapshot.Unowned)
        {
            if (CellProcesses.ReadSourceQueueCell(failure.Source.Cell) != failure.Source)
                throw new InvalidDataException("Saved current CELL list failure changed its actual source owner.");
            _unownedCellReferenceLists.Add(failure.Source.Cell, failure.Owner);
        }
        _currentCellReferenceRevision = checked(snapshot.Revision + 1); _currentCellReferenceMembership = snapshot.Membership;
        _sourceCurrentListCold = new(snapshot.CapturedProcess, _sourceCurrentListProcess, _currentCellReferenceRevision);
    }
    internal static void ValidateSourceCurrentCellLists(FalloutCurrentCellProcessListsSnapshot snapshot)
    {
        if (snapshot is null || snapshot.Schema != CurrentProcessListsSchema || string.IsNullOrWhiteSpace(snapshot.Stack) ||
            snapshot.Contract is not { Length: 64 } || !snapshot.Contract.All(Uri.IsHexDigit) || snapshot.CapturedProcess == Guid.Empty ||
            snapshot.Revision < 0 || snapshot.Membership < 0 || snapshot.Cells is null || snapshot.Unowned is null ||
            snapshot.Cells.Any(value => value is null || value.Source is null || value.Members is null || value.Members.Any(member => member is null || member.Source is null)) ||
            snapshot.Unowned.Any(value => value is null || value.Source is null || string.IsNullOrWhiteSpace(value.Owner)) ||
            snapshot.Cells.Select(value => value.Source.Cell).Distinct(FalloutFormKeyComparer.Instance).Count() != snapshot.Cells.Count ||
            snapshot.Cells.Select(value => value.Revision).Distinct().Count() != snapshot.Cells.Count ||
            snapshot.Unowned.Select(value => value.Source.Cell).Distinct(FalloutFormKeyComparer.Instance).Count() != snapshot.Unowned.Count ||
            snapshot.Cells.SelectMany(value => value.Members).Select(member => member.Membership).Distinct().Count() !=
                snapshot.Cells.Sum(value => value.Members.Count) ||
            snapshot.ColdHandoff is { } cold && (cold.PreviousProcess == Guid.Empty || cold.CurrentProcess != snapshot.CapturedProcess ||
                cold.PreviousProcess == cold.CurrentProcess || cold.Sequence < 1 || cold.Sequence > snapshot.Revision))
            throw new InvalidDataException("Saved current CELL list source/membership/order/new-process owner is incomplete.");
        foreach (var list in snapshot.Cells)
        {
            FalloutCellExtraProcessState.RequireCell(list.Source, list.Source.Cell);
            if (list.GraphSha256 is not { Length: 64 } || !list.GraphSha256.All(Uri.IsHexDigit) || list.Revision < 1 || list.Revision > snapshot.Revision ||
                list.Members.Select(member => member.Source.Reference).Distinct(FalloutFormKeyComparer.Instance).Count() != list.Members.Count ||
                list.Members.Any(member => member.Membership < 1 || member.Membership > snapshot.Membership || member.Source.SourceCell != list.Source.Cell))
                throw new InvalidDataException("Saved CELL reference-list membership/source order is invalid.");
        }
        foreach (var failure in snapshot.Unowned) FalloutCellExtraProcessState.RequireCell(failure.Source, failure.Source.Cell);
    }
}
