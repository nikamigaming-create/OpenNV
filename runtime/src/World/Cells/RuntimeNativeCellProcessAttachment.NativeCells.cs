using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class RuntimeNativeCellProcessAttachment
{
    private void BindNativeCellConsumers(IReadOnlyCollection<FalloutFormKey>? selected = null, bool requireTree = true)
    {
        RequireRoot();
        var attachment = _world.CellProcesses.ReadAttachment(Identity);
        var landscapes = _root.GetChildren().OfType<RuntimeNativeLandscapeTransport>().ToArray();
        if (landscapes.Any(land => !attachment.CellEpochs.ContainsKey(land.Source.ActiveCell)))
            throw new InvalidDataException("Shared native root owns an unaccounted source CELL terrain consumer.");
        foreach (var cell in attachment.CellConsumers)
        {
            if (selected is not null && !selected.Contains(cell.Source.Cell.Cell)) continue;
            if (cell.Source.Landscape is null) continue;
            var matches = landscapes.Where(land => land.Source.ActiveCell == cell.Source.Cell.Cell).ToArray();
            if (matches.Length != 1) throw new NotSupportedException("Actual source CELL terrain factory did not return one exact native consumer.");
            var land = matches[0]; RequireNativeLand(land, cell.Source, requireTree);
            if (cell.Phase == FalloutCellProcessChildPhase.Pending)
                _world.CellProcesses.CompleteNativeCell(Identity, cell.Source.Cell.Cell, cell.Source, [land.GetInstanceId()],
                    "actual-winning-source-LAND-native-consumer-returned");
            else if (cell.Phase != FalloutCellProcessChildPhase.Published || !cell.NativeObjects.SequenceEqual([land.GetInstanceId()]))
                throw new InvalidDataException("Actual CELL terrain consumer changed without its source/native destruction receipt.");
        }
    }
    private void RequireLiveNativeCells(IEnumerable<FalloutCellNativeConsumers>? selected = null)
    {
        foreach (var cell in selected ?? _world.CellProcesses.ReadAttachment(Identity).CellConsumers)
        {
            if (cell.Source.Landscape is null)
            {
                if (cell.Phase != FalloutCellProcessChildPhase.SourceNoDraw || cell.NativeObjects.Count != 0)
                    throw new InvalidDataException("Native root invented a terrain consumer outside the actual source CELL scope.");
                continue;
            }
            if (cell.Phase != FalloutCellProcessChildPhase.Published || cell.NativeObjects.Count != 1 ||
                GodotObject.InstanceFromId(cell.NativeObjects[0]) is not RuntimeNativeLandscapeTransport land)
                throw new InvalidDataException("Current CELL publication lost its actual source LAND node.");
            RequireNativeLand(land, cell.Source);
        }
    }
    private void RequireNativeLand(RuntimeNativeLandscapeTransport land, FalloutCellNativeSource source, bool requireTree = true)
    {
        RequireTerrainConstructionReturned(land);
        if (!GodotObject.IsInstanceValid(land) || land.IsQueuedForDeletion() || requireTree && !land.IsInsideTree() || land.GetParent() != _root ||
            land.Transform != Transform3D.Identity || !GodotObject.IsInstanceValid(land.Geometry) || land.Geometry.GetParent() != land ||
            land.Geometry.Transform != Transform3D.Identity ||
            land.Source.ActiveCell != source.Cell.Cell || land.Source.Landscape != source.Landscape ||
            land.Source.Worldspace != source.Cell.Worldspace || source.TransportSha256 != FalloutCellNativeSource.TransportDigest(land.Source))
            throw new InvalidDataException("Native LAND consumer differs from its exact winning CELL/world/source transport or living root.");
    }
    private void ObserveRetiredNativeCells()
    {
        foreach (var cell in _world.CellProcesses.ReadAttachment(Identity).CellConsumers)
        {
            if (cell.Phase == FalloutCellProcessChildPhase.Retired) continue;
            if (cell.NativeObjects.Any(identity => GodotObject.IsInstanceValid(GodotObject.InstanceFromId(identity))))
                throw new InvalidOperationException("CELL LAND retirement still owns a native object; detachment/QueueFree is not destruction.");
            ObserveTerrainConstructionRetirement(cell.Source.Cell.Cell);
            _world.CellProcesses.RetireNativeCell(Identity, cell.Source.Cell.Cell, "actual-source-CELL-LAND-native-objects-destroyed");
        }
    }
}
