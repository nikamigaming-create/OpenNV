using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class RuntimeNativeCellProcessAttachment
{
    private readonly Dictionary<Guid, RuntimeNativeLandscapeConstruction> _terrainConstructions = [];
    internal void RegisterTerrainConstruction(RuntimeNativeLandscapeConstruction construction)
    {
        RequireRoot(); ArgumentNullException.ThrowIfNull(construction);
        var source = _world.CellProcesses.ReadNativeCellSource(construction.Source.ActiveCell);
        var attachment = _world.CellProcesses.ReadAttachment(Identity);
        var consumer = attachment.CellConsumers.SingleOrDefault(value => value.Source.Cell.Cell == source.Cell.Cell);
        if (attachment.Retired || attachment.RootPublished || !attachment.CellEpochs.ContainsKey(source.Cell.Cell) ||
            consumer is null || consumer.Phase != FalloutCellProcessChildPhase.Pending ||
            source.Landscape != construction.Source.Landscape || source.Cell.Worldspace != construction.Source.Worldspace ||
            source.TransportSha256 != construction.TransportSha256 || construction.Phase != RuntimeNativeLandscapeConstructionPhase.Entered ||
            _terrainConstructions.Values.Any(value => value.Source.ActiveCell == source.Cell.Cell && value.Identity != construction.Identity &&
                value.Phase != RuntimeNativeLandscapeConstructionPhase.Retired))
            throw new InvalidDataException("Native LAND construction has no unique exact pending source CELL/LAND owner.");
        if (!_terrainConstructions.TryAdd(construction.Identity, construction) && _terrainConstructions[construction.Identity] != construction)
            throw new InvalidDataException("Native LAND construction identity was reused by another actual allocation lifetime.");
    }
    private void RequireTerrainConstructionReturned(RuntimeNativeLandscapeTransport land)
    {
        if (!_terrainConstructions.TryGetValue(land.Construction.Identity, out var construction) || construction != land.Construction)
            throw new NotSupportedException("Actual CELL LAND was constructed outside its native source allocation lifetime.");
        construction.RequireReturned(); construction.BindActualParent(_root);
    }
    internal void EnterTerrainConstructionPublication(RuntimeNativeLandscapeConstruction construction)
    {
        RequireRoot();
        if (!_terrainConstructions.TryGetValue(construction.Identity, out var entered) || entered != construction)
            throw new InvalidDataException("LAND publication entry has no exact source CELL constructor invocation.");
        construction.EnterActualParentPublication(_root);
    }
    internal void RequestFailedTerrainConstructionRetirement(FalloutFormKey? cell = null)
    {
        var errors = new List<Exception>();
        foreach (var construction in _terrainConstructions.Values.Where(value => value.OriginalFailure is not null &&
            (cell is null || value.Source.ActiveCell == cell)))
        {
            // This call is joined only after actual CELL detach entry. It may
            // reclaim detached pre-return allocations and failed root children.
            if (_world.CellProcesses.ReadPhase(construction.Source.ActiveCell).Value != (byte)FalloutCellProcessPhase.Detaching)
            { errors.Add(new InvalidOperationException("Failed LAND retirement has no actual source CELL detach prefix.")); continue; }
            try { construction.RetireFailedAllocations(); } catch (Exception error) { errors.Add(error); }
        }
        if (errors.Count != 0) throw new AggregateException("Actual CELL retains failed LAND allocation retirement.", errors);
    }
    private void ObserveTerrainConstructionRetirement(FalloutFormKey cell)
    {
        var errors = new List<Exception>();
        foreach (var construction in _terrainConstructions.Values.Where(value => value.Source.ActiveCell == cell).ToArray())
        {
            try
            {
                construction.ObserveNativeDestructionAndRetireResources();
                if (construction.Phase != RuntimeNativeLandscapeConstructionPhase.Retired)
                    throw new InvalidOperationException("Actual CELL terrain allocation retirement did not return.");
                _terrainConstructions.Remove(construction.Identity);
            }
            catch (Exception error) { errors.Add(error); }
        }
        if (errors.Count != 0) throw new AggregateException("Actual CELL terrain resources retain independent lifetime failures.", errors);
    }
}
