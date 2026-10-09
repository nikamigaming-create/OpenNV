using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal sealed record FalloutCurrentCellProcessListMember(FalloutCellProcessReference? Source, long Membership,
    FalloutCombatActorIdentity? RuntimeActor = null);
internal sealed record FalloutCurrentCellProcessList(FalloutCellProcessIdentity Source, string GraphSha256,
    long Revision, IReadOnlyList<FalloutCurrentCellProcessListMember> Members);
internal sealed record FalloutUnownedCellProcessList(FalloutCellProcessIdentity Source, string Owner);
internal sealed record FalloutCurrentCellProcessListsSnapshot(string Schema, string Stack, string Contract, Guid CapturedProcess,
    long Revision, long Membership, IReadOnlyList<FalloutCurrentCellProcessList> Cells,
    IReadOnlyList<FalloutUnownedCellProcessList> Unowned, FalloutActorProcessRuntimeHandoff? ColdHandoff,
    FalloutSourceCellReferenceLinksSnapshot Links);

internal sealed partial class FalloutReferenceWorld
{
    private const string CurrentProcessListsSchema = "opennv-current-cell-process-reference-lists/v2";

    private FalloutCellProcessReferenceList ReadActualCurrentCellReferences(FalloutFormKey cell)
    {
        var list = SourceCellLinks.Read(cell);
        if (_unownedCellReferenceLists.TryGetValue(cell, out var missing)) throw new NotSupportedException(missing);
        FalloutCombatActorIdentity? player = null;
        if ((list.RuntimeActors?.Count ?? 0) != 0 || CurrentSourcePlayerLinkedCell is not null)
        {
            player = RequirePublishedLinkedPlayer();
            SourceCellLinks.RequireRuntimeActorParent(player, _currentPlayerProcessCell!());
        }
        var current = new List<FalloutCellProcessListReference>(list.Members.Count + (list.RuntimeActors?.Count ?? 0));
        foreach (var ordered in list.OrderedReferences)
        {
            if ((list.RuntimeActors ?? []).SingleOrDefault(member => member.Membership == ordered.Membership) is { } runtime)
            {
                if (player is null || runtime.Source != player || _currentPlayerProcessCell!() != cell)
                    Refuse("canonical-Player-current-source-ParentCELL-differs-from-linked-membership");
                var actualPlayer = player ?? throw new InvalidDataException("Canonical Player current source is absent.");
                var playerProcess = ActorProcesses.Read(actualPlayer.Reference);
                current.Add(new(null, cell, runtime.Membership, ReadSourceCanonicalPlayerReferenceFlags(), actualPlayer,
                    playerProcess.Epoch, playerProcess.Level,
                    new(playerProcess.Level is not null, "actual-source-Player-current-process-pointer/" + playerProcess.Epoch)));
                continue;
            }
            var member = list.Members.Single(member => member.Membership == ordered.Membership);
            var reference = member.Source;
            var instance = Get(reference.Reference);
            if (instance.Deleted || Placement(reference.Reference).Cell != cell)
                Refuse("actual-current-CELL-unlink-or-ParentCELL-setter-prefix-not-returned");
            var actor = reference.Signature is "ACHR" or "ACRE" ? ReadCombatActorIdentity(reference.Reference) : null;
            var process = actor is null ? null : ActorProcesses.Read(reference.Reference);
            current.Add(new(reference, cell, member.Membership, ReadActualCurrentReferenceFlags(reference.Reference),
                actor, process?.Epoch, process?.Level, actor is null ? null :
                    new(process?.Level is not null, "actual-source-Actor-current-process-pointer/" + process!.Epoch)));
        }
        return new(list.Source, list.Revision, current, "actual-source-CELL-linked-insertion-order-and-current-canonical-factories");
        void Refuse(string failure)
        {
            _unownedCellReferenceLists.TryAdd(cell, failure);
            throw new NotSupportedException(failure);
        }
    }
    private FalloutCurrentCellProcessListsSnapshot CaptureSourceCurrentCellLists()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        // An omitted list is not an empty list. Every actually loaded CELL must
        // have its current source links before this shared capture can return.
        foreach (var cell in CellProcesses.Capture().Cells.Where(value => value.Data is not null))
            _ = ReadActualCurrentCellReferences(cell.Source.Cell);
        var linked = SourceCellLinks.Capture();
        var rows = linked.Cells.Select(list => new FalloutCurrentCellProcessList(list.Source, list.GraphSha256, list.Revision,
            list.OrderedReferences.Select(ordered => list.Members.SingleOrDefault(member => member.Membership == ordered.Membership) is { } placed ?
                new FalloutCurrentCellProcessListMember(placed.Source, placed.Membership) :
                new FalloutCurrentCellProcessListMember(null, ordered.Membership,
                    (list.RuntimeActors ?? []).Single(member => member.Membership == ordered.Membership).Source)).ToArray())).ToArray();
        return new(CurrentProcessListsSchema, _processQueueStack!, _processQueueDeclaration!.Contract, linked.CapturedProcess,
            linked.Sequence, linked.Membership, rows,
            _unownedCellReferenceLists.Select(item => new FalloutUnownedCellProcessList(CellProcesses.ReadSourceQueueCell(item.Key), item.Value)).ToArray(),
            linked.ColdHandoff, linked);
    }
    private void RestoreSourceCurrentCellLists(FalloutCurrentCellProcessListsSnapshot snapshot)
    {
        ValidateSourceCurrentCellLists(snapshot);
        if (CellProcesses.Capture().Cells.Where(value => value.Data is not null).Any(value =>
            !snapshot.Links.Cells.Any(list => list.Source.Cell == value.Source.Cell)))
            throw new InvalidDataException("Cold source links omitted an actually loaded CELL; absence cannot become an empty list.");
        if (_sourceCellLinks is not null)
            throw new InvalidOperationException("Cold CELL link restoration requires its fresh world owner.");
        ConfigureSourceCellLinks(snapshot.Stack, saved: snapshot.Links);
        foreach (var refusal in snapshot.Unowned)
        {
            if (CellProcesses.ReadSourceQueueCell(refusal.Source.Cell) != refusal.Source)
                throw new InvalidDataException("Saved CELL list refusal changed its source owner.");
            _unownedCellReferenceLists.Add(refusal.Source.Cell, refusal.Owner);
        }
    }
    internal static void ValidateSourceCurrentCellLists(FalloutCurrentCellProcessListsSnapshot snapshot)
    {
        if (snapshot is null || snapshot.Schema != CurrentProcessListsSchema || string.IsNullOrWhiteSpace(snapshot.Stack) ||
            !FalloutAdvancementRuntimeReceipt.Digest(snapshot.Contract) || snapshot.Links is null || snapshot.Cells is null ||
            snapshot.Unowned is null || snapshot.Unowned.Any(value => value is null || value.Source is null || string.IsNullOrWhiteSpace(value.Owner)))
            throw new InvalidDataException("Saved CELL process lists omitted their actual ordered linked owner.");
        FalloutSourceCellReferenceLinks.Validate(snapshot.Links);
        if (snapshot.Stack != snapshot.Links.Stack || snapshot.CapturedProcess != snapshot.Links.CapturedProcess ||
            snapshot.Revision != snapshot.Links.Sequence || snapshot.Membership != snapshot.Links.Membership ||
            snapshot.ColdHandoff != snapshot.Links.ColdHandoff || snapshot.Cells.Count != snapshot.Links.Cells.Count)
            throw new InvalidDataException("Saved CELL process and Sandbox lists have different actual owners.");
        for (var index = 0; index < snapshot.Cells.Count; ++index)
        {
            var view = snapshot.Cells[index]; var linked = snapshot.Links.Cells[index];
            if (view is null || view.Source != linked.Source || view.GraphSha256 != linked.GraphSha256 ||
                view.Revision != linked.Revision || view.Members is null || view.Members.Count != linked.Members.Count + (linked.RuntimeActors?.Count ?? 0) ||
                view.Members.Any(member => member is null || (member.Source is null) == (member.RuntimeActor is null) ||
                    member.RuntimeActor is not null && !member.RuntimeActor.EnginePlayer) ||
                !view.Members.Select(member => (member.Source?.Reference ?? member.RuntimeActor!.Reference, member.Membership))
                    .SequenceEqual(linked.OrderedReferences) ||
                view.Members.Any(member => member.Source is not null ?
                    !linked.Members.Any(actual => actual.Source == member.Source && actual.Membership == member.Membership) :
                    !(linked.RuntimeActors ?? []).Any(actual => actual.Source == member.RuntimeActor && actual.Membership == member.Membership)))
                throw new InvalidDataException("Saved CELL process view changed the exact linked member order/membership.");
        }
        if (snapshot.Unowned.Select(value => value.Source.Cell).Distinct(FalloutFormKeyComparer.Instance).Count() != snapshot.Unowned.Count)
            throw new InvalidDataException("Saved CELL link refusals have duplicate source owners.");
        foreach (var refusal in snapshot.Unowned) FalloutCellExtraProcessState.RequireCell(refusal.Source, refusal.Source.Cell);
    }
}
