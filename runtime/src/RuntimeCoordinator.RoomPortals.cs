using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime;

public partial class RuntimeCoordinator
{
    private readonly Dictionary<ulong, RuntimeNativeRoomPortalGraph> _nativeRoomPortals = [];

    private void ConstructNativeRoomPortalGraph(Node3D root, FalloutCellScene cell,
        RuntimeNativeReferencePresentation presentation, IReadOnlyList<FalloutRoomPortalSnapshot>? saved)
    {
        var identity = root.GetInstanceId();
        if (_nativeRoomPortals.ContainsKey(identity))
            throw new InvalidOperationException("Actual CELL room graph was constructed twice.");
        var world = _nativeReferences ?? throw new InvalidOperationException("Room graph lost its campaign world.");
        var attachment = RequireNativeSourceCellAttachment(root);
        var cold = world.CellProcesses.Capture().ColdHandoff?.AwaitingNativeAttachments.Contains(attachment.Identity) == true;
        var returning = root.GetChildren().Count(node =>
            node is RuntimeNativeSourcePortalPrimitive or RuntimeNativeSourceOcclusionPlane);
        if (cold && saved is null) throw new InvalidDataException("Cold CELL attachment omitted its current room/portal owner list.");
        var previous = cold ? saved!.Where(row => row.Cell == cell.Cell.FormKey).ToArray() : [];
        if (previous.Length > 1 || returning == 0 && previous.Length != 0 ||
            cold && returning != 0 && previous.Length != 1)
            throw new InvalidDataException("Cold CELL portal graph has duplicate or missing actual source factories.");
        if (returning == 0) return;
        var graph = RuntimeNativeRoomPortalGraph.Attach(root, world,
            _nativePluginStack ?? throw new InvalidOperationException("Room graph lost its selected records."),
            cell, FalloutPortalCullingSource.Read(world.CampaignPlayerRuntimeSource.Receipt.EngineSha256),
            presentation, () => presentation.Nodes, _configuration.World.GameUnitsToMeters,
            error => RetainNativeRoomPortalFailure(root, error), previous.SingleOrDefault());
        _nativeRoomPortals.Add(identity, graph);
    }

    private void RetainNativeRoomPortalFailure(Node3D root, Exception original)
    {
        var failure = string.IsNullOrWhiteSpace(original.Message) ? original.GetType().Name : original.Message;
        var world = _nativeReferences ?? throw new InvalidOperationException("Failed portal draw lost its actual world.", original);
        var attachment = RequireNativeSourceCellAttachment(root);
        var children = world.CellProcesses.ReadAttachment(attachment.Identity).Children;
        var references = root.GetChildren().OfType<RuntimeNativeSourcePortalPrimitive>()
            .Select(node => node.Source.Reference)
            .Concat(root.GetChildren().OfType<RuntimeNativeSourceOcclusionPlane>().Select(node => node.Source.Reference))
            .ToHashSet(FalloutFormKeyComparer.Instance);
        var failures = new List<Exception>();
        foreach (var child in children.Where(child => references.Contains(child.Source.Reference)))
        {
            _nativeReferenceDivergences[child.Source.Reference.ToString()] = failure;
            try
            {
                world.CellProcesses.FailChild(attachment.Identity, child.Source, original,
                "actual-source-room-portal-camera-render-consumer-failed");
            }
            catch (Exception error) { failures.Add(error); }
        }
        // A failure during initial native publication is retained in the real
        // CELL protocol even before a player driver exists. Later draw faults
        // also reach the actual gameplay driver; this callback is not readiness.
        if (_nativeOpeningStageDriver is { } driver)
            try { driver.RetainSourceRoomPortalFailure(original); }
            catch (Exception error) { failures.Add(error); }
        if (failures.Count != 0)
            throw new AggregateException("Portal draw retained its original failure and failed source observers.",
                new[] { original }.Concat(failures));
    }

    private void BindCurrentNativeRoomPortalCapture() =>
        (_nativeOpeningStageDriver ?? throw new InvalidOperationException("Room capture has no actual player driver."))
            .BindSourceRoomPortalCapture(CaptureNativeRoomPortals);

    private IReadOnlyList<FalloutRoomPortalSnapshot> CaptureNativeRoomPortals()
    {
        var world = _nativeReferences ?? throw new InvalidOperationException("Room capture lost its actual world.");
        var result = new List<FalloutRoomPortalSnapshot>();
        foreach (var (identity, owner) in _nativeCellProcessAttachments)
        {
            var attachment = world.CellProcesses.ReadAttachment(owner.Identity);
            if (attachment.Retired) continue;
            if (!attachment.RootPublished || _nativeCellProcessRetirements.Contains(identity))
                throw new NotSupportedException("Portal capture cannot cross incomplete CELL publication/retirement.");
            if (_nativeRoomPortals.TryGetValue(identity, out var graph)) result.Add(graph.Capture());
            else if (attachment.Children.Any(child => child.Phase == FalloutCellProcessChildPhase.Failed))
                throw new NotSupportedException("Source CELL capture retains its failed native child consumer.");
        }
        if (result.Select(row => row.Cell).Distinct(FalloutFormKeyComparer.Instance).Count() != result.Count)
            throw new NotSupportedException("Two current native roots claim one source interior portal graph.");
        return result.AsReadOnly();
    }

    private void RetireNativeRoomPortalGraph(Node3D root)
    {
        if (!_nativeRoomPortals.TryGetValue(root.GetInstanceId(), out var graph)) return;
        graph.Retire();
        _nativeRoomPortals.Remove(root.GetInstanceId());
    }
}
