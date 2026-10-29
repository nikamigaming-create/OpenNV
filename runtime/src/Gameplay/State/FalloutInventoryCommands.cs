using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Gameplay.State;

internal enum FalloutInventoryCommandKind { RemoveAll, Equip, Reset, Add, Remove, Unequip }
internal sealed record FalloutInventoryCommand(FalloutInventoryCommandKind Kind, FalloutFormKey Target,
    FalloutFormKey? Item = null, FalloutFormKey? Destination = null, bool RetainOwnership = false,
    bool NoUnequip = false, bool Silent = true, int Count = 1, bool NoEquip = false);

// Shared state owner for result/object/quest commands and both player views.
internal sealed class FalloutInventoryCommands(FalloutPluginStack records, FalloutReferenceWorld world,
    FalloutPlayerInventory player, Func<int> level, FalloutGlobalState? globals = null,
    Action<FalloutFormKey>? prepareActorChange = null, Func<FalloutFormKey, bool>? weaponDrawn = null,
    Action<FalloutFormKey, FalloutFormKey>? consumeIngestible = null)
{
    private bool IsPlayer(FalloutFormKey target) => target == records.RuntimeFormKey(0x14);
    private FalloutPlayerInventory Inventory(FalloutFormKey target) => IsPlayer(target) ? player : world.Inventory(target, level(), globals).Contents;

    internal double ItemCount(FalloutFormKey target, FalloutFormKey item) =>
        FalloutInventoryItemCounts.Count(records, Inventory(target), item, world.FormLists);

    internal FalloutFormKey? EquippedObject(FalloutFormKey target, uint slot) => IsPlayer(target)
        ? player.EquippedObject(records, slot) : world.EquippedObject(target, slot, level(), globals);

    internal bool IsWeaponOut(FalloutFormKey target) =>
        (weaponDrawn ?? throw new NotSupportedException("Weapon draw query has no active actor owner."))(target);

    internal uint WeaponAnimationType(FalloutFormKey target) => EquippedObject(target, 5) is { } weapon
        ? FalloutWeaponPresentation.ReadScriptAnimationType(records.GetEffective(weapon)) : 0;

    internal float WeaponHealthPercentage(FalloutFormKey target) => EquippedObject(target, 5) is { } weapon
        ? FalloutWeaponCondition.SelectedCondition(Inventory(target).Item(weapon) ??
            throw new InvalidDataException("Equipped weapon has no inventory instance.")) * 100 : 100;

    internal bool IsEquipped(FalloutFormKey target, FalloutFormKey item)
    {
        return IsPlayer(target)
            ? FalloutEquippedItems.Any(records, item, direct => player.IsEquipped(records, direct), world.FormLists)
            : world.IsEquipped(target, item, level(), globals);
    }

    internal void Execute(FalloutInventoryCommand command)
    {
        if (command.Kind == FalloutInventoryCommandKind.Add && command.Count == 0 && IsPlayer(command.Target) &&
            command.Item is { } zeroItem && records.GetEffective(zeroItem).Signature == "NOTE") return;
        if (command.Kind is FalloutInventoryCommandKind.Add or FalloutInventoryCommandKind.Remove && command.Count <= 0)
            throw new InvalidDataException("Inventory command count must be positive.");
        var isPlayer = IsPlayer(command.Target);
        var inventory = Inventory(command.Target);
        switch (command.Kind)
        {
            case FalloutInventoryCommandKind.Add or FalloutInventoryCommandKind.Remove:
                var changedItem = command.Item ?? throw new InvalidDataException("Inventory count command has no item.");
                if (isPlayer && command.Kind == FalloutInventoryCommandKind.Add)
                {
                    var source = records.GetEffective(changedItem);
                    if (source.Signature == "NOTE")
                    {
                        world.AcquirePlayerNote(changedItem, player.Notifications); return;
                    }
                    FalloutReferenceWorld.RequirePlayerItemAcquisition(records, source, command.Target);
                }
                var revision = inventory.Revision;
                if (command.Kind == FalloutInventoryCommandKind.Add)
                    inventory.Add(records, changedItem, command.Count, level(), command.Silent || !isPlayer, globals,
                        validateAddition: isPlayer ? selected => FalloutReferenceWorld.RequirePlayerItemAcquisition(
                            records, records.GetEffective(selected), command.Target) : null);
                else
                {
                    // Retire an actual worn weapon before deleting its last item.
                    // Armor and unrelated contents retain their existing pose owner.
                    if (!isPlayer && world.Get(command.Target).Injury?.Dead != true &&
                        inventory.Item(changedItem) is { RecordType: "WEAP" } weapon &&
                        command.Count >= weapon.Count && inventory.Equipped.Contains(weapon.RuntimeFormId))
                        prepareActorChange?.Invoke(command.Target);
                    inventory.Remove(changedItem, command.Count, command.Silent || !isPlayer);
                }
                if (!isPlayer && inventory.Revision != revision) world.InventoryChanged(command.Target);
                break;
            case FalloutInventoryCommandKind.RemoveAll:
                var destination = command.Destination is { } target ? Inventory(target) : null;
                if (command.Destination is { } noteReceiver && IsPlayer(noteReceiver))
                    foreach (var transferred in inventory.Items)
                        FalloutReferenceWorld.RequirePlayerItemAcquisition(records, records.GetEffective(transferred.FormKey), noteReceiver);
                if (!isPlayer && world.Get(command.Target).Injury?.Dead != true) prepareActorChange?.Invoke(command.Target);
                inventory.RemoveAll(records, isPlayer, destination, command.RetainOwnership,
                    command.Destination is { } receiver && IsPlayer(receiver) && !command.Silent);
                if (!isPlayer) world.InventoryChanged(command.Target);
                if (command.Destination is { } recipient && !IsPlayer(recipient)) world.InventoryChanged(recipient);
                break;
            case FalloutInventoryCommandKind.Equip:
                var item = command.Item ?? throw new InvalidDataException("EquipItem has no item.");
                if (records.GetEffective(item).Signature is "ALCH" or "INGR")
                {
                    if (inventory.Item(item) is null) return;
                    if (command.NoUnequip) throw new NotSupportedException("An ingestible cannot retain an equipped lock.");
                    (consumeIngestible ?? throw new NotSupportedException("EquipItem has no bound ingestible transaction."))(command.Target, item);
                    return;
                }
                if (records.GetEffective(item).Signature is not ("ARMO" or "WEAP"))
                    throw new NotSupportedException("EquipItem consumption requires its book/ammo/ingestible owner.");
                if (inventory.Item(item) is null) return;
                if (!command.Silent && isPlayer)
                    throw new NotSupportedException("EquipItem visible equipment notification requires its source HUD message owner.");
                if (isPlayer) inventory.Equip(records, item, command.NoUnequip);
                else
                {
                    prepareActorChange?.Invoke(command.Target);
                    world.EquipItem(command.Target, item, command.NoUnequip, level(), globals);
                }
                break;
            case FalloutInventoryCommandKind.Unequip:
                var removedEquipment = command.Item ?? throw new InvalidDataException("UnequipItem has no item.");
                if (records.GetEffective(removedEquipment).Signature is not ("ARMO" or "WEAP"))
                    throw new InvalidDataException("UnequipItem requires armor or a weapon.");
                if (command.NoEquip) throw new NotSupportedException("UnequipItem NoEquip requires a persistent equipment-admission lock owner.");
                if (!command.Silent && isPlayer)
                    throw new NotSupportedException("UnequipItem visible equipment notification requires its source HUD message owner.");
                if (!inventory.Equipped.Contains(records.RuntimeFormId(removedEquipment))) return;
                if (isPlayer) inventory.Unequip(records, removedEquipment, force: true);
                else
                {
                    prepareActorChange?.Invoke(command.Target);
                    world.UnequipItem(command.Target, removedEquipment, level(), globals);
                }
                break;
            case FalloutInventoryCommandKind.Reset:
                if (isPlayer) throw new NotSupportedException("Player ResetInventory requires its initial player-container owner.");
                prepareActorChange?.Invoke(command.Target);
                world.ResetInventory(command.Target, level(), globals);
                break;
            default: throw new InvalidDataException("Inventory command kind is unknown.");
        }
    }

    internal static uint Slot(double value) => double.IsFinite(value) && value == Math.Truncate(value) && value >= int.MinValue && value <= uint.MaxValue
        ? unchecked((uint)(long)value) : throw new InvalidDataException("Equipment slot is not an integer.");
}
