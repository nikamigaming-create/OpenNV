using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime;

public partial class RuntimeCoordinator
{
    private RuntimeNativeDynamicMines? _nativeDynamicMines;
    private void AddNativeDynamicMines()
    {
        _nativeDynamicMines?.RetireAll();
        _nativeDynamicMines?.Free();
        _nativeDynamicMines = new(_nativePluginStack!, _nativeReferences!, _nativeInventory,
            RuntimeLiveContentSource.Current!, _configuration.World.GameUnitsToMeters,
            _configuration.Player.CollisionMask | _configuration.Player.CollisionLayer, NativeCombatContext,
            NativeDynamicMineResident,
            () => _nativeActiveCell?.Cell.FormKey ?? throw new InvalidOperationException("Mine release has no source cell."),
            () => _nativeOpeningStageDriver!.PlayerSkillValue("explosives"),
            () => _nativeOpeningStageDriver!.PlayerMineTriggerChance,
            error => _nativeReferenceDivergences["dynamic-projectile"] = error)
        { Name = "NativeDynamicProjectiles" };
        AddChild(_nativeDynamicMines);
        _nativePlayer!.PrepareMineDeployment = _nativeDynamicMines.Prepare;
        _nativePlayer.DeployMine = _nativeDynamicMines.Deploy;
    }

    private bool TryActivateNativeDynamicMine(Node collider)
    {
        if (RuntimeNativePlacedMine.Find(collider) is not { Identity.RuntimeId: not null } mine) return false;
        mine.Activate(_nativeInventory, _nativeOpeningStageDriver!.RewardExperience);
        SaveNativeInteraction();
        return true;
    }

    private bool NativeDynamicMineResident(FalloutReferencePlacement placement)
    {
        if (_nativeDoorLoading || _nativeActiveCell is null || _nativeCurrentCellRoot is null) return false;
        var cell = _nativeActiveCell.Cell;
        if (cell.Worldspace is null) return placement.Cell == cell.FormKey;
        if (!_nativeDroppedCells.TryGetValue(placement.Cell, out var source))
            _nativeDroppedCells.Add(placement.Cell, source = FalloutCellSceneReader.ReadDefinition(_nativePluginStack!, placement.Cell));
        return source.Worldspace == cell.Worldspace && _nativeWalkableGrid.Contains(
            ((int)MathF.Floor(placement.Position[0] / 4096), (int)MathF.Floor(placement.Position[1] / 4096)));
    }
}
