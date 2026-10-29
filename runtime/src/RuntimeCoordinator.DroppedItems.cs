using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime;

public partial class RuntimeCoordinator
{
    private RuntimeNativeDroppedItems? _nativeDroppedItems;
    private readonly Dictionary<FalloutFormKey, FalloutCellDefinition> _nativeDroppedCells = [];

    private void AddNativeDroppedItems()
    {
        _nativeDroppedItems?.RetireAll();
        _nativeDroppedItems?.Free();
        _nativeDroppedCells.Clear();
        _nativeDroppedItems = new(_nativePluginStack!, _nativeReferences!, _nativeInventory,
            _configuration.World.GameUnitsToMeters, NativeDroppedItemResident,
            () => _nativeOpeningStageDriver!.PlayerLevel, _nativeGlobals,
            node => _nativePlayer?.ReleaseGrabOwner(node))
        { Name = "NativeDroppedReferences" };
        AddChild(_nativeDroppedItems);
    }

    private bool NativeDroppedItemResident(FalloutDroppedReferenceSnapshot item)
    {
        if (_nativeDoorLoading || _nativeActiveCell is null || _nativeCurrentCellRoot is null) return false;
        var cell = _nativeActiveCell.Cell;
        if (cell.Worldspace is null) return item.Placement.Cell == cell.FormKey;
        if (!_nativeDroppedCells.TryGetValue(item.Placement.Cell, out var source))
            _nativeDroppedCells.Add(item.Placement.Cell, source = FalloutCellSceneReader.ReadDefinition(_nativePluginStack!, item.Placement.Cell));
        return source.Worldspace == cell.Worldspace && _nativeWalkableGrid.Contains(
            ((int)MathF.Floor(item.Placement.Position[0] / 4096), (int)MathF.Floor(item.Placement.Position[1] / 4096)));
    }

    private void DropNativeInventoryItem(FalloutFormKey form, int variant, int count, long revision)
    {
        var player = _nativePlayer ?? throw new InvalidOperationException("Drop has no resident player.");
        var cell = _nativeActiveCell?.Cell ?? throw new InvalidOperationException("Drop has no active cell.");
        if (_nativeDoorLoading) throw new InvalidOperationException("Wait for travel to finish before dropping an item.");
        // The world NIF starts in front of the actual player capsule. Its
        // authored collision body owns gravity and subsequent movement.
        var height = _configuration.Player.SpawnCenterHeightMeters * player.GlobalBasis.Scale.Y;
        var radius = _configuration.Player.CapsuleRadiusMeters * Math.Max(player.GlobalBasis.Scale.X, player.GlobalBasis.Scale.Z);
        var position = player.GlobalPosition + Vector3.Up * height;
        var source = new Vector3(position.X, -position.Z, position.Y) / player.UnitsToMeters;
        (_nativeDroppedItems ?? throw new InvalidOperationException("Drop has no physical world owner."))
            .Drop(form, variant, count, revision, new(cell.FormKey, [source.X, source.Y, source.Z], [0, 0, 0]),
                new(player.GlobalPosition, -player.GlobalBasis.Z, radius, height, player.GetRid()));
    }

    private Action<bool>? BindNativePlayerGrab(Node collider) =>
        _nativeDroppedItems?.BindGrab(collider) ?? _nativeReferenceEvents?.BindPlayerGrab(collider);
}
