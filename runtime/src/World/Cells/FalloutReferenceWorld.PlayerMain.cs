using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    private readonly Dictionary<FalloutFormKey, FalloutMainPlayerSourceCell> _mainPlayerCells = new(FalloutFormKeyComparer.Instance);
    internal bool CampaignMainPlayerCellConstructed => _processRuntime?.MainPlayerCellConstructed == true;
    internal object? CampaignMainPlayerCellState => _processRuntime?.MainPlayerCellState;
    internal Guid ActualSourceProcessIdentity
    {
        get { ObjectDisposedException.ThrowIf(_disposed, this); return ProcessRuntime.CharacterControllerSourceProcess; }
    }
    internal void RequireCampaignMainPlayerInvocation(FalloutMainPlayerCellInvocation invocation, FalloutMainPlayerCellStep step)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); invocation.Require(step);
        if (!ReferenceEquals(invocation.Owner, ProcessRuntime))
            throw new InvalidDataException("Player caller changed the actual campaign Main owner.");
    }
    internal FalloutMainPlayerCellSource CampaignMainPlayerCellSource => FalloutMainPlayerCellSource.Read(CampaignMainScriptSource);
    internal IDisposable BindCampaignMainPlayerCell(IFalloutMainPlayerCellConsumers consumers, string delivery) =>
        ProcessRuntime.BindMainPlayerCell(consumers, delivery);
    internal FalloutFormKey? MainPlayerColdRootToRebind => ProcessRuntime.MainPlayerColdRootToRebind;
    internal void RebindColdMainPlayerRoot(FalloutFormKey cell, ulong nativeRoot) => ProcessRuntime.RebindColdMainPlayerRoot(cell, nativeRoot);
    private void RetireCampaignMainPlayerCell()
    {
        if (_processRuntime?.MainPlayerCellConstructed == true) _processRuntime.RetireMainPlayerCell();
        _mainPlayerCells.Clear(); _mainPlayerExteriorOwner = null;
    }
    internal FalloutMainPlayerSourceCell ReadMainPlayerSourceCell(FalloutFormKey cell)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_mainPlayerCells.TryGetValue(cell, out var cached)) return cached;
        var identity = CellProcesses.ReadSourceQueueCell(cell);
        var definition = FalloutCellSceneReader.ReadDefinition(records, cell);
        if (definition.FormKey != identity.Cell || definition.Worldspace != identity.Worldspace)
            throw new InvalidDataException("Player containment changed winning CELL/world ownership.");
        var result = new FalloutMainPlayerSourceCell(identity, definition.Flags,
            definition.Coordinates?.X, definition.Coordinates?.Y);
        _mainPlayerCells.Add(cell, result); return result;
    }
    internal FalloutMainPlayerSourceCell? ReadMainPlayerParentCell(FalloutMainPlayerCellInvocation invocation)
    {
        invocation.Require(FalloutMainPlayerCellStep.ParentCellRead);
        if (_currentPlayerProcessCell is null) throw new NotSupportedException("actual-Player-parent-CELL-producer-unbound");
        return _currentPlayerProcessCell() is { } cell ? ReadMainPlayerSourceCell(cell) : null;
    }
    internal FalloutMainPlayerCellTarget? ReadMainPlayerTargetCell(FalloutMainPlayerCellInvocation invocation,
        FalloutMainPlayerSourceCell before, FalloutMainPlayerSourcePosition position)
    {
        invocation.Require(FalloutMainPlayerCellStep.TargetCellRead);
        if (ReadMainPlayerSourceCell(before.Source.Cell) != before || (before.CellFlags & 1) != 0 ||
            before.Source.Worldspace is not { } world)
            throw new InvalidDataException("Player exterior lookup has no actual winning current CELL/world declaration.");
        RequireCampaignMainPlayerInvocation(invocation, FalloutMainPlayerCellStep.TargetCellRead);
        var (x, y) = ProcessRuntime.ConvertMainPlayerSourceGrid(invocation, position.XBits, position.YBits, FalloutSourceFistpSite.PlayerTargetCell);
        if (FalloutSourceMainFamily.IsFallout3(CampaignMainScriptSource.EngineSha256) &&
            (x is < short.MinValue or > short.MaxValue || y is < short.MinValue or > short.MaxValue)) return null;
        var cell = _mainPlayerExterior.SpatialCellAtSourceCoordinates(world, x, y);
        var source = ReadMainPlayerSourceCell(cell);
        if (source.X != x || source.Y != y || source.Source.Worldspace != world || (source.CellFlags & 1) != 0)
            throw new InvalidDataException("Player target lookup changed original converted coordinates or source ancestry.");
        return new(source.Source, CellProcesses.ReadPhase(cell).Require());
    }
    private FalloutExteriorGrid? _mainPlayerExteriorOwner;
    private FalloutExteriorGrid _mainPlayerExterior => _mainPlayerExteriorOwner ??= new(records);
    private void RequireMainPlayerColdSources(FalloutMainPlayerCellSnapshot saved)
    {
        RequirePendingColdPayloadSources(saved);
        foreach (var source in new[] { saved.LastCall?.BeforeCell, saved.LastCall?.AfterCell }.OfType<FalloutCellProcessIdentity>())
            if (ReadMainPlayerSourceCell(source.Cell).Source != source)
                throw new InvalidDataException("Cold Player child changed original winning CELL/world bytes.");
        if (saved.Pending.Pending is { Move: { } move })
        {
            _ = records.GetEffective(move.Source);
            if (move.Destination != records.RuntimeFormKey(0x14))
            {
                var target = records.GetEffective(move.Destination);
                if (target.IsDeleted || target.Signature is not ("REFR" or "ACHR" or "ACRE" or "PGRE" or "PMIS"))
                    throw new InvalidDataException("Cold pending Player destination has no exact live source reference.");
            }
        }
        if (saved.RootBinding is { } root) _ = ReadMainPlayerSourceCell(root.Cell);
        if (saved.Pending.Pending is { Door: { } door })
        {
            var record = records.GetEffective(door);
            if (record.IsDeleted || record.Signature != "REFR") throw new InvalidDataException("Cold Player door request changed its winning source.");
            var cell = FalloutCellSceneReader.ParentCell(record) ?? throw new InvalidDataException("Pending door has no source CELL.");
            var reference = FalloutCellSceneReader.Read(records, cell).References.Single(value => value.FormKey == door);
            _ = FalloutDoorDestinationResolver.Resolve(records, reference);
        }
    }
}
