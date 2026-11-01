using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal partial class RuntimeNativeReferencePresentation
{
    private readonly Dictionary<FalloutFormKey, bool> _imposterAdmission = [];
    private readonly Dictionary<FalloutFormKey, ulong> _imposterNodeIdentities = [];
    private FalloutRegionImposterVisibility? _imposterVisibility;
    private FalloutPlayerRegionMembership? _playerRegions;
    private bool _imposterPublicationPending;

    internal object ImposterState => new
    {
        cell = _playerRegions?.Cell.ToString(),
        regions = _playerRegions?.Current.Select(key => key.ToString()).ToArray(),
        selected = _imposterVisibility?.Selected.Count ?? 0,
        droppedSourceRegions = _playerRegions?.DroppedSourceRegions?.Select(key => key?.ToString()).ToArray(),
        pending = _imposterPublicationPending,
        error = _imposterVisibility?.Error,
        owner = "source-player-CELL-entry/type8-reference-attachment-and-render-cull"
    };

    internal void EnterImposterCell(FalloutPluginStack records, FalloutCellDefinition cell, IReadOnlyList<float>? position)
    {
        if (_playerRegions?.Cell == cell.FormKey) return;
        var membership = _world.PlayerRegions(cell, position);
        if (_imposterVisibility is null)
        {
            _imposterVisibility = new(records);
            if (!_world.RegionPublicationPending) _imposterVisibility.RestorePublished(membership.Current);
        }
        _playerRegions = membership;
        _imposterPublicationPending = _world.RegionPublicationPending;
    }

    internal void PublishImposterRegions()
    {
        if (!_imposterPublicationPending || _playerRegions is null || _imposterVisibility is null) return;
        _imposterPublicationPending = false;
        var published = _imposterVisibility.Publish(_playerRegions.Current, reference =>
        {
            if (!_referencesByKey.ContainsKey(reference) || _world.Placement(reference).Cell != _playerRegions.Cell)
                throw new NotSupportedException("Selected regional reference has no current-CELL attachment owner.");
            if (_world.IsEnabled(reference) && Resolve(reference) is null)
                throw new NotSupportedException($"Selected enabled regional reference {reference} has no actual source 3D.");
            _world.AdoptRegionImposter(reference);
        }, (reference, admitted) =>
        {
            _imposterAdmission[reference] = admitted;
            if (_nodes.TryGetValue(reference, out var node)) ApplyImposterVisibility(reference, node);
        });
        if (published) _world.CompleteRegionPublication();
    }

    private void ApplyImposterVisibility(FalloutFormKey reference, Node3D node)
    {
        var declared = _referencesByKey.GetValueOrDefault(reference)?.SpecialRendering?.Imposter == true;
        if (!declared && !_world.IsRegionImposter(reference)) return;
        var identity = node.GetInstanceId();
        if (_imposterNodeIdentities.TryGetValue(reference, out var previous) && previous != identity)
            _imposterAdmission.Remove(reference);
        _imposterNodeIdentities[reference] = identity;
        if (!_imposterAdmission.TryGetValue(reference, out var admitted))
        {
            admitted = _playerRegions is not null && _imposterVisibility is not null &&
                _imposterVisibility.AdmittedAtLoad(reference, _playerRegions.Current, _playerRegions.CellRegions);
            _imposterAdmission[reference] = admitted;
        }
        // Enable owns process/collision/lifetime separately from the source cull.
        node.Visible = _world.IsEnabled(reference) && admitted;
    }
}
