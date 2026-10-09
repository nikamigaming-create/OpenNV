using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class RuntimeNativeCellProcessAttachment
{
    internal Guid PrepareSharedGraph(FalloutCellScene before, FalloutCellScene after, IReadOnlyList<FalloutFormKey> cells)
    {
        RequireRoot();
        if (!_root.IsInsideTree()) throw new InvalidOperationException("Shared CELL graph preparation has no published native root.");
        RequireLiveChildNodes(); RequireLiveNativeCells();
        return _world.CellProcesses.BeginSharedGraph(Identity, _rootIdentity, before, after, cells);
    }
    internal IReadOnlySet<FalloutFormKey> SharedPendingReferences()
    {
        var change = _world.CellProcesses.ReadSharedGraph(Identity) ?? throw new InvalidOperationException("Shared preparation has no entered source graph.");
        var owner = _world.CellProcesses.ReadAttachment(Identity);
        return change.TargetChildren.Where(target => owner.Children.Single(child => child.Source == target.Source).Phase == FalloutCellProcessChildPhase.Pending)
            .Select(target => target.Source.Reference).ToHashSet(FalloutFormKeyComparer.Instance);
    }
    internal void NativeTerrainReturned(FalloutFormKey cell)
    { RequireRoot(); BindNativeCellConsumers([cell], requireTree: false); }
    internal void NativeTerrainFailed(FalloutFormKey cell, Exception error, IReadOnlyList<ulong> actualObjects) =>
        _world.CellProcesses.FailNativeCell(Identity, cell, error, actualObjects);
    internal void BeginSharedPublication(FalloutCellScene target)
    {
        RequireRoot();
        _world.CellProcesses.BeginSharedGraphPublication(Identity, _rootIdentity, target);
    }
    internal void BeginSharedCancellation()
    { RequireRoot(); _world.CellProcesses.BeginSharedGraphCancellation(Identity, _rootIdentity); }
    internal void RetireSharedConsumers(RuntimeNativeReferencePresentation presentation, RuntimeNativeReferenceEvents events)
    {
        RequireRoot(); var change = _world.CellProcesses.ReadSharedGraph(Identity) ??
            throw new InvalidOperationException("Shared native destruction has no actual entered graph.");
        if (change.Phase is not (FalloutCellSharedGraphPhase.Publishing or FalloutCellSharedGraphPhase.Cancelling))
            throw new InvalidOperationException("Shared native destruction precedes actual publication/cancellation.");
        var cancelling = change.Phase == FalloutCellSharedGraphPhase.Cancelling;
        var references = cancelling ? change.TargetChildren.Select(child => child.Source.Reference)
            .Except(change.BeforeChildren.Select(child => child.Source.Reference), FalloutFormKeyComparer.Instance) :
            change.BeforeChildren.Select(child => child.Source.Reference)
                .Except(change.TargetChildren.Select(child => child.Source.Reference), FalloutFormKeyComparer.Instance);
        var cells = cancelling ? change.AfterEpochs.Keys.Except(change.BeforeEpochs.Keys, FalloutFormKeyComparer.Instance) :
            change.BeforeEpochs.Keys.Except(change.AfterEpochs.Keys, FalloutFormKeyComparer.Instance);
        foreach (var reference in references.ToArray())
        {
            var child = Child(reference);
            if (child.Phase == FalloutCellProcessChildPhase.Retired) continue;
            if (!cancelling)
            {
                events.RequireSharedReferenceRetirement(reference, child.NativeObjects);
                presentation.ForgetDestroyedSourceReference(reference, child.NativeObjects);
                events.ForgetDestroyedSourceReference(reference);
            }
            DestroyActualConsumers(child.NativeObjects);
            _world.CellProcesses.RetireSharedChild(Identity, reference, "actual-shared-CELL-reference-native-objects-destroyed");
        }
        foreach (var cell in cells.ToArray())
        {
            var resource = _world.CellProcesses.ReadAttachment(Identity).CellConsumers.Single(value => value.Source.Cell.Cell == cell);
            if (resource.Phase == FalloutCellProcessChildPhase.Retired) continue;
            RequestFailedTerrainConstructionRetirement(cell);
            DestroyActualConsumers(resource.NativeObjects);
            ObserveTerrainConstructionRetirement(cell);
            _world.CellProcesses.RetireNativeCell(Identity, cell, "actual-shared-CELL-LAND-native-objects-destroyed");
        }
    }
    internal void PublishSharedGraph(RuntimeNativeReferenceEvents events, FalloutCellScene scene)
    {
        RequireRoot(); var change = _world.CellProcesses.ReadSharedGraph(Identity) ?? throw new InvalidOperationException("Actual shared CELL publication has no entered graph.");
        if (change.Phase != FalloutCellSharedGraphPhase.Publishing) throw new InvalidOperationException("Shared publication has no actual target entry.");
        foreach (var target in change.TargetChildren)
        {
            var nodes = events.SourceCellNativeConsumers(target.Source.Reference);
            if (nodes.Any(node => !GodotObject.IsInstanceValid(node) || node.IsQueuedForDeletion() || !node.IsInsideTree() || node.GetParent() != _root))
                throw new InvalidDataException("Shared publication borrowed another root's source reference-event consumer.");
            if (change.BeforeChildren.SingleOrDefault(child => child.Source.Reference == target.Source.Reference) is { } retained &&
                nodes.Any(node => !retained.NativeObjects.Contains(node.GetInstanceId())))
                throw new NotSupportedException("Retained reference rematerialization needs its own actual source/native construction receipt; shared root residency cannot substitute it.");
            _world.CellProcesses.JoinNativeChildConsumers(Identity, target.Source, nodes.Select(node => node.GetInstanceId()).ToArray(),
                "actual-native-source-reference-event-consumers-returned");
        }
        BindNativeCellConsumers(change.AfterEpochs.Keys.ToArray());
        RequireLiveChildNodes();
        RequireLiveNativeCells(_world.CellProcesses.ReadAttachment(Identity).CellConsumers.Where(cell => change.AfterEpochs.ContainsKey(cell.Source.Cell.Cell)));
        _world.CellProcesses.CompleteSharedGraph(Identity, _rootIdentity); _sourceEvents = events; _publishedScene = scene;
    }
    internal void CompleteSharedCancellation()
    {
        RequireRoot(); var change = _world.CellProcesses.ReadSharedGraph(Identity) ?? throw new InvalidOperationException("Shared cancellation has no source invocation.");
        RequireLiveChildNodes();
        RequireLiveNativeCells(_world.CellProcesses.ReadAttachment(Identity).CellConsumers.Where(cell => change.BeforeEpochs.ContainsKey(cell.Source.Cell.Cell)));
        _world.CellProcesses.CompleteSharedGraph(Identity, _rootIdentity);
    }
    private void DestroyActualConsumers(IReadOnlyList<ulong> identities)
    {
        var failures = new List<Exception>();
        foreach (var identity in identities)
        {
            if (!GodotObject.IsInstanceValid(GodotObject.InstanceFromId(identity))) continue;
            try
            {
                if (GodotObject.InstanceFromId(identity) is not Node node || node.GetParent() != _root)
                    throw new InvalidDataException("Outgoing CELL object lost its actual direct native root ownership.");
                node.Free();
            }
            catch (Exception error) { failures.Add(error); }
        }
        if (identities.Any(identity => GodotObject.IsInstanceValid(GodotObject.InstanceFromId(identity))))
            failures.Add(new InvalidOperationException("Shared CELL destruction retains an actual native child; no detach/completion receipt is admissible."));
        if (failures.Count != 0) throw new AggregateException("Actual shared CELL destruction retains independent native failures.", failures);
    }
}
