using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static class WeaponInstanceMagazineContracts
{
    private static readonly Guid First = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid Second = Guid.Parse("10000000-0000-0000-0000-000000000002");
    private static FalloutFormKey Key(uint id) => new("Test.esm", id);

    internal static void Run(FalloutPluginStack records, FalloutWeaponPresentation source, FalloutWeaponShot sourceShot)
    {
        var weapon = source with { ClipSize = 3, AmmoUse = 1 };
        var shot = sourceShot with { RecoveredItem = null, RecoveryPercent = 0 };
        SeparateMagazines(records, weapon, shot);
        DropPickupAndTrade(records, weapon);
        BatchTransfer(records, weapon, reverse: false);
        BatchTransfer(records, weapon, reverse: true);
        Console.WriteLine("OPENNV_WEAPON_INSTANCE_MAGAZINE_CONTRACT_PASS distinctInstances=true selectedShot=true " +
            "dropPickupCold=true ownership=true tradeExplicitTotals=true takeAllOrderIndependent=true rollback=true synthetic=true");
    }

    private static FalloutPlayerInventory Inventory(FalloutPluginStack records)
    {
        var inventory = new FalloutPlayerInventory(123);
        inventory.Add(records, Key(1), 1, 1, true,
            extra: new(1, .8f, Key(0x802), Key(0x803), 2, Hotkey: 1, Instance: First));
        inventory.Add(records, Key(1), 1, 1, true,
            extra: new(1, .4f, Key(0x801), Instance: Second));
        inventory.Add(records, Key(2), 2, 1, true, extra: new(2, Owner: Key(0x801)));
        inventory.Add(records, Key(2), 15, 1, true, extra: new(15, Owner: Key(0x802), Global: Key(0x803), FactionRank: 2));
        inventory.Equip(records, Key(1));
        return inventory;
    }

    private static FalloutItemVariant Instance(FalloutPlayerInventory inventory, Guid identity) =>
        inventory.Instances(Key(1)).Single(value => value.Extra.Instance == identity).Extra;

    private static void Select(FalloutPluginStack records, FalloutPlayerInventory inventory, Guid identity)
    {
        var selected = inventory.Instances(Key(1)).Single(value => value.Extra.Instance == identity);
        inventory.SelectInstance(Key(1), selected.Variant, inventory.Revision);
        inventory.Equip(records, Key(1));
    }

    private static void SeparateMagazines(FalloutPluginStack records, FalloutWeaponPresentation weapon, FalloutWeaponShot shot)
    {
        var inventory = Inventory(records);
        var handling = new FalloutWeaponHandling(inventory);
        handling.CompleteReload(weapon);
        Require(handling.Loaded(Key(1)) == 3 && Instance(inventory, Second).Magazine is null,
            "Reloading one weapon populated another same-base instance.");
        Select(records, inventory, Second);
        Require(handling.Loaded(Key(1)) == 0, "Switching to an empty same-base instance borrowed another clip.");
        handling.CompleteReload(weapon);
        Require(Instance(inventory, First).Magazine?.Loaded == 3 && Instance(inventory, Second).Magazine?.Loaded == 3 &&
            inventory.Item(Key(2))!.Count == 17, "Two reloads shared a clip or manufactured ammunition.");
        var firstBefore = Instance(inventory, First);
        Require(handling.ConsumeShot(weapon, shot, records), "Selected loaded instance refused its shot.");
        Require(Instance(inventory, First) == firstBefore && Instance(inventory, Second) is { Magazine.Loaded: 2, Instance: var identity } &&
            identity == Second && inventory.Item(Key(2))!.Count == 16,
            "One accepted shot changed another instance or removed ammunition more than once.");
        var items = inventory.Capture();
        var coldInventory = new FalloutPlayerInventory();
        coldInventory.Restore(items.Inventory, items.EquippedRuntimeFormIds.ToArray(), items.InventoryRandomState);
        var cold = new FalloutWeaponHandling(coldInventory);
        cold.Restore(handling.Capture(), _ => weapon);
        Require(cold.Loaded(Key(1)) == 2 && cold.Capture().Magazines.Count == 2,
            "Cold handling lost the selected clip or conflated two same-base instances.");
        Select(records, coldInventory, First);
        Require(cold.Loaded(Key(1)) == 3, "Cold selection borrowed the other instance's remaining rounds.");
        coldInventory.Remove(Key(2), 12, true);
        Require(coldInventory.LoadedInventoryRounds(Key(2)) <= 4 && coldInventory.Item(Key(2))!.Count == 4,
            "Explicit ammunition removal retained more loaded rounds than exist.");
    }

    private static void DropPickupAndTrade(FalloutPluginStack records, FalloutWeaponPresentation weapon)
    {
        var inventory = Inventory(records);
        var handling = new FalloutWeaponHandling(inventory);
        handling.CompleteReload(weapon);
        var original = Instance(inventory, First);
        var ammunition = AmmoOwnership(inventory.Item(Key(2))!.Variants!);
        using var world = new FalloutReferenceWorld(records);
        var dropped = world.PrepareDroppedItem(inventory, Key(1), 0, 1, new(Key(0x800), [1, 2, 3], [0, 0, 0]));
        using (var publication = world.PrepareDroppedPublication(dropped))
            inventory.DropInstance(records, Key(1), 0, 1, inventory.Revision, publication);
        Require(inventory.Item(Key(1))!.Count == 1 && inventory.Item(Key(2))!.Count == 14 &&
            dropped.Item == original with { Hotkey = null } && dropped.Ammunition is { Count: 2 },
            "Drop deleted or duplicated the weapon, its clip, or exact ammunition slices.");
        Require(AmmoOwnership(inventory.Item(Key(2))!.Variants!.Concat(dropped.Ammunition!)) == ammunition,
            "Drop changed ammunition ownership, global or faction-rank metadata.");
        var serialized = JsonSerializer.Serialize(world.CaptureDroppedItems());
        using var cold = new FalloutReferenceWorld(records);
        cold.RestoreDroppedItems(JsonSerializer.Deserialize<FalloutDroppedReferenceSnapshot[]>(serialized)!);
        var beforeRejectedTake = JsonSerializer.Serialize(inventory.Capture());
        using (var refused = new FalloutInventoryChangeLease(() => throw new InvalidOperationException("Synthetic presentation refusal."), () => { }))
            Reject(() => cold.TakeDroppedItem(dropped.Identity, inventory, 1, null, refused));
        Require(cold.DroppedItems.Count == 1 && JsonSerializer.Serialize(inventory.Capture()) == beforeRejectedTake,
            "Failed pickup partially transferred loaded contents or retired its world identity.");
        var retirements = 0;
        using (var presentation = new FalloutInventoryChangeLease(() => { }, () => { }, () => ++retirements))
            cold.TakeDroppedItem(dropped.Identity, inventory, 1, null, presentation);
        Require(retirements == 1 && cold.DroppedItems.Count == 0 && inventory.Item(Key(2))!.Count == 17 &&
            Instance(inventory, First) == original with { Hotkey = null } &&
            AmmoOwnership(inventory.Item(Key(2))!.Variants!) == ammunition,
            "Cold pickup duplicated rounds or lost instance/ownership metadata.");
        using (var duplicate = new FalloutInventoryChangeLease(() => { }, () => { }))
            Reject(() => cold.TakeDroppedItem(dropped.Identity, inventory, 1, null, duplicate));
        Require(inventory.Item(Key(2))!.Count == 17, "Repeated pickup duplicated the loaded ammunition.");
        Select(records, inventory, First);
        var merchant = new FalloutPlayerInventory(456);
        var weaponBefore = Instance(inventory, First);
        inventory.Exchange(merchant, [new(Key(2), 5, true), new(Key(1), 1, true)]);
        Require(merchant.Item(Key(2))!.Count == 5 && inventory.Item(Key(2))!.Count == 12 &&
            Instance(merchant, First) == weaponBefore && inventory.Item(Key(1))!.Count == 1 &&
            AmmoOwnership(inventory.Item(Key(2))!.Variants!.Concat(merchant.Item(Key(2))!.Variants!)) == ammunition,
            "Trade counted a clip twice, changed explicit ammunition totals, or lost ownership metadata.");
    }

    private static void BatchTransfer(FalloutPluginStack records, FalloutWeaponPresentation weapon, bool reverse)
    {
        var source = Inventory(records);
        new FalloutWeaponHandling(source).CompleteReload(weapon);
        var ammoBefore = AmmoOwnership(source.Item(Key(2))!.Variants!);
        var target = new FalloutPlayerInventory(789);
        var transfers = source.Items.Select(item => new FalloutInventoryTransfer(item.FormKey, item.Count)).ToArray();
        if (reverse) Array.Reverse(transfers);
        source.TransferItemsTo(target, transfers);
        Require(source.Items.Count == 0 && target.Item(Key(1))!.Count == 2 && target.Item(Key(2))!.Count == 17 &&
            Instance(target, First).Magazine?.Loaded == 3 && Instance(target, Second).Magazine is null &&
            AmmoOwnership(target.Item(Key(2))!.Variants!) == ammoBefore,
            "Take All depends on request order or duplicates loaded ammunition/ownership.");
    }

    private static string AmmoOwnership(IEnumerable<FalloutItemVariant> extras) => string.Join(";", extras
        .GroupBy(extra => JsonSerializer.Serialize(extra with { Count = 1, Hotkey = null }))
        .Select(group => group.Key + "=" + group.Sum(extra => extra.Count)).Order(StringComparer.Ordinal));

    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (InvalidOperationException) { return; }
        throw new InvalidOperationException("Rejected world pickup unexpectedly committed.");
    }
}
