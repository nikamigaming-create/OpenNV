using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal sealed record FalloutCorpseEquipmentInventoryChange(FalloutActorCorpseEquipment Before,
    FalloutActorCorpseEquipment After, IReadOnlySet<FalloutFormKey> RemovedWeapons, bool RetiresHandling)
{
    internal static FalloutCorpseEquipmentInventoryChange Prepare(FalloutPluginStack records,
        FalloutActorCorpseEquipment equipment, FalloutOpeningInventoryGrant before, FalloutOpeningInventoryGrant after)
    {
        var previous = Inventory(before); var next = Inventory(after);
        equipment.ValidateSource(records, previous);
        foreach (var item in before.Inventory.Items.Where(item => item.RecordType == "WEAP" &&
            before.EquippedRuntimeFormIds.Contains(item.RuntimeFormId)))
        {
            if (next.Item(item.FormKey) is not { } remaining) continue;
            if (remaining.Count < item.Count ||
                !after.EquippedRuntimeFormIds.Contains(item.RuntimeFormId))
                throw new NotSupportedException("Corpse equipped-stack removal or reselection needs its source item-instance owner.");
        }
        if (after.EquippedRuntimeFormIds.Except(before.EquippedRuntimeFormIds).Any())
            throw new NotSupportedException("Corpse transfer cannot select new equipment.");
        var removed = equipment.Attachments.Select(attachment => attachment.Source.Weapon)
            .Concat(equipment.HandlingWeapon is { } source ? [source.Weapon] : [])
            .Where(weapon => next.Item(weapon) is null).ToHashSet();
        var retiredHandling = equipment.HandlingWeapon is { } selected && removed.Contains(selected.Weapon);
        var removesDrawn = retiredHandling || equipment.HandlingWeapon is null &&
            equipment.Attachments.Any(attachment => attachment.Owner == "package" && removed.Contains(attachment.Source.Weapon));
        var activity = equipment.Activity;
        if (removesDrawn && activity.WeaponDrawn)
            activity = activity with { WeaponDrawn = false, Revision = checked(activity.Revision + 1) };
        var result = equipment with
        {
            Attachments = equipment.Attachments.Where(attachment => !removed.Contains(attachment.Source.Weapon))
                .Select(attachment => attachment.Copy()).ToArray(),
            HandlingWeapon = retiredHandling ? null : equipment.HandlingWeapon,
            WeaponHandling = retiredHandling ? null : equipment.WeaponHandling is { } handling
                ? FalloutWeaponHandling.Reconcile(handling, after.Inventory.Items) : null,
            EmbeddedMuzzleBone = retiredHandling ? null : equipment.EmbeddedMuzzleBone,
            Activity = activity
        };
        result.ValidateSource(records, next);
        return new(equipment.Copy(), result, removed, retiredHandling);
    }

    private static FalloutPlayerInventory Inventory(FalloutOpeningInventoryGrant snapshot)
    {
        var inventory = new FalloutPlayerInventory(snapshot.InventoryRandomState);
        inventory.Restore(snapshot.Inventory, snapshot.EquippedRuntimeFormIds.ToArray(), snapshot.InventoryRandomState);
        return inventory;
    }
}
