using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceInstance
{
    internal bool RegionCellAttached { get; set; }
    internal bool RegionImposterRole { get; set; }
}

internal sealed partial class FalloutReferenceWorld
{
    private FalloutPlayerRegionMembership? _playerRegions;
    private bool _regionPublicationPending;
    internal bool RegionPublicationPending => _regionPublicationPending;
    internal void CompleteRegionPublication() => _regionPublicationPending = false;
    internal FalloutPlayerRegionMembership? CapturePlayerRegions() => _regionPublicationPending
        ? throw new InvalidOperationException("Accepted regional reference activation has not completed.")
        : _playerRegions is { } membership
        ? membership with
        {
            CellRegions = membership.CellRegions.ToArray(),
            Current = membership.Current.ToArray(),
            DroppedSourceRegions = membership.DroppedSourceRegions?.ToArray(),
            EntryPosition = membership.EntryPosition?.ToArray()
        }
        : null;
    internal void RestorePlayerRegions(FalloutPlayerRegionMembership? snapshot, FalloutFormKey activeCell)
    {
        var membership = snapshot is null ? null : FalloutPlayerRegionMembership.Restore(records, snapshot, activeCell);
        _playerRegions = membership;
        _regionPublicationPending = false;
    }
    internal FalloutPlayerRegionMembership PlayerRegions(FalloutCellDefinition cell, IReadOnlyList<float>? position) =>
        _playerRegions is { } membership && membership.Cell == cell.FormKey ? membership : FalloutPlayerRegionMembership.Enter(records, cell, position);

    internal bool IsRegionImposter(FalloutFormKey reference)
    {
        var instance = Get(reference);
        return instance.RegionImposterRole || PlacementSourceReference(instance).SpecialRendering?.Imposter == true;
    }

    internal void AdoptRegionImposter(FalloutFormKey reference) => Get(reference).RegionImposterRole = true;

    // Source region activation reattaches the actual reference to the player's
    // accepted CELL. World position, facing, enable, inventory and execution
    // continuation are unchanged. This is not a MoveTo/script placement reset.
    internal RegionCellEntry BeginRegionCellEntry(FalloutCellDefinition cell, IReadOnlyList<float>? position)
    {
        var changedCell = _playerRegions?.Cell != cell.FormKey;
        var membership = PlayerRegions(cell, position);
        var selectedRegions = changedCell ? membership.Current : Array.Empty<FalloutFormKey>();
        var selected = selectedRegions.Select(region => FalloutRegionImposters.Read(records, region))
            .OfType<FalloutRegionImposters>().SelectMany(group => group.References).Distinct().Select(Get).ToArray();
        if (selected.Any(instance => records.GetEffective(instance.Base).Signature is "NPC_" or "CREA"))
            throw new NotSupportedException("Regional actor CELL/process-tier continuation is unqualified.");
        var entry = new RegionCellEntry(this, selected);
        try
        {
            _playerRegions = membership;
            if (changedCell) _regionPublicationPending = true;
            foreach (var instance in selected)
            {
                var placement = Placement(instance.Reference).Copy() with { Cell = cell.FormKey };
                placement.Validate();
                instance.Placement = placement;
                instance.RegionCellAttached = true;
                instance.PlacementRevision = ++PlacementRevision;
            }
            return entry;
        }
        catch { entry.Dispose(); throw; }
    }

    internal sealed class RegionCellEntry : IDisposable
    {
        private readonly FalloutReferenceWorld _world;
        private readonly (FalloutReferenceInstance Instance, FalloutReferencePlacement? Placement, bool Attached, bool Role)[] _previous;
        private readonly FalloutPlayerRegionMembership? _previousRegions;
        private readonly bool _previousPending;
        private bool _committed;
        internal RegionCellEntry(FalloutReferenceWorld world, IReadOnlyList<FalloutReferenceInstance> references)
        {
            _world = world;
            _previousRegions = world._playerRegions;
            _previousPending = world._regionPublicationPending;
            _previous = references.Select(instance => (instance, instance.Placement?.Copy(),
                instance.RegionCellAttached, instance.RegionImposterRole)).ToArray();
        }
        internal void Commit() => _committed = true;
        public void Dispose()
        {
            if (_committed) return;
            _committed = true;
            _world._playerRegions = _previousRegions;
            _world._regionPublicationPending = _previousPending;
            foreach (var (instance, placement, attached, role) in _previous)
            {
                instance.Placement = placement;
                instance.RegionCellAttached = attached;
                instance.RegionImposterRole = role;
                instance.PlacementRevision = ++_world.PlacementRevision;
            }
        }
    }
}
