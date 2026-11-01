namespace OpenNV.Runtime.World.Actors;

internal sealed partial class RuntimeNativeActorCombat
{
    private long? _equipmentInventoryRevision;

    private void RefreshProcessEquipment(bool initializing = false)
    {
        if (_context is null) return;
        // Inventory belongs to every actual actor process, including creatures
        // whose source contents have not been queried by combat or looting.
        var inventory = _state.Inventory ?? _world.Inventory(_state.Reference, _context.Level(), _context.Globals);
        if (Dead || _equipmentInventoryRevision == inventory.Contents.Revision) return;
        _ = _world.EquippedArmor(_state.Reference, _context.Level(), _context.Globals);
        // A retained process decision survives unrelated contents changes and
        // cold restore. Queries do not turn a pending refresh into unknown state.
        if (inventory.WeaponSelectionResolved && (initializing || !inventory.WeaponSelectionRefreshPending))
        {
            if (!inventory.WeaponSelectionRefreshPending) _equipmentInventoryRevision = inventory.Contents.Revision;
            return;
        }
        // A restored engagement prepares its weapon through its existing
        // continuation owner, before an ordinary inventory refresh can replace it.
        if (inventory.WeaponSelectionResolved && _state.Engagement is not null && !_engagementPrepared) return;
        // Preserve an existing presented choice. Retirement and replacement of
        // its attachment remain with the combat/package equipment owner.
        if (_enemyWeapon is { } enemy)
            inventory.ResolveWeaponSelection(_records, enemy.Form);
        else if (_packageWeapon is { } package)
            inventory.ResolveWeaponSelection(_records, package.Weapon.Form);
        else
            _ = SelectCombatWeapon();
        _equipmentInventoryRevision = inventory.Contents.Revision;
    }
}
