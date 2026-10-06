using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.World.Actors;

internal sealed partial class RuntimeNativeActorCombat
{
    private FalloutInventoryChangeLease? PrepareNativeInventoryRemoval(FalloutOpeningInventoryGrant before,
        FalloutOpeningInventoryGrant after)
    {
        if (!Dead)
        {
            if (before.Inventory.Items.Any(item => item.RecordType == "WEAP" &&
                before.EquippedRuntimeFormIds.Contains(item.RuntimeFormId) &&
                ((after.Inventory.Items.SingleOrDefault(value => value.FormKey == item.FormKey)?.Count ?? 0) < item.Count ||
                    !after.EquippedRuntimeFormIds.Contains(item.RuntimeFormId))))
                PrepareInventoryChange();
            return null;
        }
        if (!IsInsideTree() || IsQueuedForDeletion() || _state.PrepareNativeInventoryRemoval != PrepareNativeInventoryRemoval ||
            _state.CaptureCorpseEquipment != CaptureCorpseEquipment || _state.StoppedRetirement is not null ||
            _ragdoll?.Settled != true || _world.HitEvents.SnapshotPending(_state.Reference).Count != 0 ||
            !CorpseEquipmentCaptureReady(false) || !StoppedAiPoseCaptureReady)
            throw new NotSupportedException("Corpse transfer requires its settled current native equipment, audio, hit and physical owners.");
        // This retains the ordinary independent procedure/head/audio gates.
        // A stopped source fault is captured, not cleared by looting.
        var captured = _state.Capture();
        if (captured.CorpseEquipment is not { } equipment) return null;
        var change = FalloutCorpseEquipmentInventoryChange.Prepare(_records, equipment, before, after);
        var enemy = _enemyObject; var package = _packageWeapon;
        var weapon = _enemyWeapon; var handling = _enemyWeaponHandling;
        var muzzle = _embeddedMuzzle; var flash = _enemyMuzzle;
        var oldEquipment = _state.CorpseEquipment;
        var engagement = _state.Engagement;
        var activity = Activity.Capture();
        var packagePrepared = _packageWeaponPrepared; var packageType = _packageWeaponType;
        var removed = new[] { enemy, package }.Where(attachment => attachment is not null &&
            change.RemovedWeapons.Contains(attachment.Weapon.Form)).Select(attachment => attachment!).ToArray();
        var attachments = NativeActorWeaponAttachment.PrepareRetirement(removed);
        var materialBefore = _skeleton.MaterialChannels.PrepareReplacement(new HashSet<Node3D>(), new());
        var materialAfter = materialBefore.PrepareReplacement(removed.Select(attachment => attachment.Root).ToHashSet(), new());
        FalloutWeaponHandling? reconciled = handling;
        if (change.RetiresHandling) reconciled = null;
        else if (change.After.WeaponHandling is { } retained && !FalloutActorCorpseEquipment.SameHandling(equipment.WeaponHandling, retained))
        {
            reconciled = new(_state.Inventory!.Contents, nativeNpc: true);
            reconciled.Restore(retained, key => FalloutWeaponPresentation.Read(_records, key, false));
        }
        var muzzleParent = muzzle?.GetParent(); var muzzleIndex = muzzle?.GetIndex() ?? 0;
        return new(() =>
        {
            if (_state.PrepareNativeInventoryRemoval != PrepareNativeInventoryRemoval || !IsInsideTree() ||
                !FalloutActorCorpseEquipment.SameHandling(handling?.Capture(),
                    equipment.WeaponHandling is { } old ? FalloutWeaponHandling.Reconcile(old, after.Inventory.Items) : null) ||
                !FalloutPlayerInventory.SameSnapshot(_state.Inventory!.Contents.Capture(), after) ||
                _state.Engagement != engagement || !ReferenceEquals(_enemyObject, enemy) || !ReferenceEquals(_packageWeapon, package))
                throw new InvalidOperationException("Corpse equipment binding changed before the authoritative transfer committed.");
            attachments.Commit();
            if (enemy is not null && change.RemovedWeapons.Contains(enemy.Weapon.Form)) _enemyObject = null;
            if (package is not null && change.RemovedWeapons.Contains(package.Weapon.Form))
            {
                _packageWeapon = null; _packageWeaponPrepared = false; _packageWeaponType = 0;
            }
            _enemyWeaponHandling = reconciled;
            if (change.RetiresHandling)
            {
                _enemyWeapon = null; _enemyMuzzle = null; _embeddedMuzzle = null;
                if (muzzle is not null) muzzleParent!.RemoveChild(muzzle);
            }
            if (removed.Length != 0) _skeleton.MaterialChannels.ReplaceWith(materialAfter);
            Activity.Restore(change.After.Activity);
            _state.CorpseEquipment = change.After.Copy();
            if (_state.Engagement is { } retainedEngagement)
                _state.Engagement = retainedEngagement with { WeaponHandling = change.After.WeaponHandling };
        }, () =>
        {
            attachments.Dispose();
            if (muzzle is not null && muzzle.GetParent() is null)
            {
                muzzleParent!.AddChild(muzzle); muzzleParent.MoveChild(muzzle, muzzleIndex);
            }
            _enemyObject = enemy; _packageWeapon = package; _enemyWeapon = weapon; _enemyWeaponHandling = handling;
            _embeddedMuzzle = muzzle; _enemyMuzzle = flash;
            _packageWeaponPrepared = packagePrepared; _packageWeaponType = packageType;
            if (removed.Length != 0) _skeleton.MaterialChannels.ReplaceWith(materialBefore);
            Activity.Restore(activity);
            _state.CorpseEquipment = oldEquipment; _state.Engagement = engagement;
        }, () =>
        {
            attachments.Complete();
            if (change.RetiresHandling)
            {
                if (muzzle is not null && GodotObject.IsInstanceValid(muzzle)) muzzle.Free();
                _combatClips.Clear(); _combatAim = null; _combatGrip = null; _combatIdle = null;
                _enemyDamage = null; _enemyShot = null; _enemyWeaponSpread = null; _enemyRayExclusions = null;
            }
            if (_packageWeapon is null) { _packageAim = null; _packageGrip = null; }
        });
    }
}
