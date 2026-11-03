using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using System.Text.Json;

internal static class WeaponHandlingContracts
{
    internal static void Run()
    {
        var weaponKey = new FalloutFormKey("Synthetic.esm", 1);
        var ammoKey = new FalloutFormKey("Synthetic.esm", 2);
        var weapon = new FalloutWeaponPresentation(weaponKey, new("weapon", weaponKey, null, null, 0, 0, []), "1hp", 1, 255)
        { ClipSize = 12, AmmoUse = 1, ConditionHealth = 100, Ammunition = [ammoKey], HasAmmunitionSource = true };
        var inventory = new FalloutPlayerInventory();
        inventory.Restore(new([new(weaponKey, 1, "weapon", "WEAP", 1, 0, 1), new(ammoKey, 2, "ammo", "AMMO", 17, 0, 0)], null), [1]);
        var state = new FalloutWeaponHandling(inventory);
        var bare = new FalloutWeaponPresentation(new("Synthetic.esm", 0x1f4), null, "h2h", 1, 255)
        { DefaultUnarmed = true, EquipmentType = 4, WeaponAnimationType = 0 };
        Require(!state.CanUse(bare), "Unarmed fallback superseded an equipped physical weapon.");
        var empty = new FalloutPlayerInventory();
        var bareHandling = new FalloutWeaponHandling(empty);
        var emptyRevision = empty.Revision;
        Require(bareHandling.CanUse(bare) && bareHandling.CanFire(bare) && !bareHandling.CanReload(bare) &&
            !bareHandling.CanUse(bare with { DefaultUnarmed = false }),
            "Empty selection did not admit only the explicit default unarmed process owner.");
        bareHandling.ApplyMeleeConditionWear(bare, null!);
        bareHandling.SetDrawn(false);
        Require(!bareHandling.CanFire(bare) && empty.Revision == emptyRevision && empty.Items.Count == 0 &&
            bareHandling.Capture().Magazines.Count == 0, "Bare hands fabricated inventory, condition wear or magazine state.");
        empty.Restore(new([new(weaponKey, 1, "last thrown item", "WEAP", 1, 0, 1)], null), [1]);
        Require(!bareHandling.CanUse(bare), "Default hands superseded a selected inventory item.");
        empty.Remove(weaponKey, 1, true);
        Require(bareHandling.CanUse(bare) && empty.Equipped.Count == 0 && empty.Items.Count == 0,
            "Consuming the final equipped item did not admit empty-selection hands.");
        Require(state.Loaded(weaponKey) == 0 && state.CanReload(weapon), "A new magazine invented loaded ammunition.");
        var materialized = new FalloutPlayerInventory();
        materialized.Restore(new([new(weaponKey, 1, "weapon", "WEAP", 1, 0, 1), new(ammoKey, 2, "ammo", "AMMO", 17, 0, 0)], null), [1]);
        materialized.MaterializeEquippedPlayerMagazine(weapon, null);
        var process = new FalloutWeaponHandling(materialized);
        Require(process.Loaded(weaponKey) == 12 && process.CanFire(weapon) && materialized.Item(ammoKey)!.Count == 17,
            "Actual source equipment materialization did not admit the carried clip without granting ammunition.");
        var processIdentity = materialized.SelectedWeapon(weaponKey).Extra.Instance!.Value;
        materialized.SetWeaponMagazine(weaponKey, processIdentity, new(ammoKey, 0));
        var coldProcess = new FalloutWeaponHandling(materialized);
        coldProcess.Restore(process.Capture(), _ => weapon);
        Require(coldProcess.Loaded(weaponKey) == 0 && !coldProcess.CanFire(weapon),
            "A cold/query path fabricated an equipment materialization and refilled a known empty magazine.");
        Require(FalloutWeaponModifications.ModifiedClipCapacity(12, 3.9f) == 15 &&
            FalloutWeaponModifications.ModifiedClipCapacity(12, -1) == 267 &&
            FalloutWeaponModifications.ModifiedClipCapacity(255, 255) == 510,
            "Source modified clip did not retain signed truncation, low-byte delta and full integer capacity.");
        state.CompleteReload(weapon);
        Require(state.Loaded(weaponKey) == 12 && inventory.Item(ammoKey)!.Count == 17 && !state.CanReload(weapon),
            "Reload did not conserve carried rounds or reject a full magazine.");
        state.SetDrawn(false);
        var restored = new FalloutWeaponHandling(inventory);
        restored.Restore(state.Capture(), _ => weapon);
        Require(!restored.Drawn && restored.Loaded(weaponKey) == 12, "Cold handling restoration lost holster or magazine state.");
        inventory.Remove(ammoKey, 9, true);
        Require(restored.Loaded(weaponKey) == 8 && restored.Capture().Magazines.Single().Loaded == 8,
            "Script inventory removal left phantom loaded ammunition.");
        inventory.Remove(ammoKey, 8, true);
        Require(!restored.CanReload(weapon) && restored.Loaded(weaponKey) == 0, "Empty inventory allowed reload.");
        var magazine = restored.Capture().Magazines.Single();
        Reject(() => new FalloutWeaponHandling(inventory).Restore(new(true, [magazine with { Loaded = 13 }]), _ => weapon));
        Reject(() => FalloutWeaponHandling.Validate(new(true, [magazine, magazine])));
        var inserts = new FalloutPlayerInventory();
        inserts.Restore(new([new(weaponKey, 1, "weapon", "WEAP", 1, 0, 1),
            new(ammoKey, 2, "ammo", "AMMO", 3, 0, 0)], null), [1]);
        var inserting = new FalloutWeaponHandling(inserts);
        var instance = inserts.EnsureWeaponInstance(weaponKey).Extra.Instance!.Value;
        Require(inserting.InsertReloadRound(weapon, instance, ammoKey) && inserting.Loaded(weaponKey) == 1 &&
            inserts.Item(ammoKey)!.Count == 3, "One insert filled the clip or consumed carried ammunition.");
        Reject(() => inserting.InsertReloadRound(weapon, Guid.NewGuid(), ammoKey));
        Require(inserting.Loaded(weaponKey) == 1, "Rejected reload identity changed the actual magazine.");
        var partial = inserting.Capture();
        var resumed = new FalloutWeaponHandling(inserts); resumed.Restore(partial, _ => weapon);
        Require(resumed.Loaded(weaponKey) == 1 && resumed.InsertReloadRound(weapon, instance, ammoKey) &&
            resumed.InsertReloadRound(weapon, instance, ammoKey) && !resumed.InsertReloadRound(weapon, instance, ammoKey) &&
            resumed.Loaded(weaponKey) == 3 && inserts.Item(ammoKey)!.Count == 3,
            "Partial insert restoration duplicated rounds or exceeded actual carried ammunition.");
        Require(FalloutWeaponReloadSpeed.Calculate(5, 5, .1f, [], _ => 1) == 1 &&
            FalloutWeaponReloadSpeed.Calculate(10, 5, .1f, [], _ => 1) == 1.5f &&
            FalloutWeaponReloadSpeed.Calculate(0, 5, .1f, [], _ => 1) == .5f &&
            FalloutWeaponReloadSpeed.Calculate(10, 5, .1f, [new(37, 3, 1.25f, [])], _ => 1) == 1.875f,
            "Reload clock lost current Agility, source settings, or actual reload-speed perk arithmetic.");
        Recharge(weapon);
        Console.WriteLine("OPENNV_WEAPON_HANDLING_CONTRACT_PASS conservation=true cold=true scriptRemoval=true invalidState=true generatedCharge=true");
    }
    private static void Recharge(FalloutWeaponPresentation original)
    {
        var weapon = original with { ClipSize = 7, AmmoRegenerationSeconds = 1 };
        var inventory = new FalloutPlayerInventory();
        inventory.Restore(new([new(weapon.Form, 1, "recharger", "WEAP", 1, 0, 1)], null), [1]);
        var handling = new FalloutWeaponHandling(inventory);
        handling.MaterializeRecharge(weapon);
        var instance = inventory.SelectedWeapon(weapon.Form).Extra.Instance!.Value;
        Require(handling.Loaded(weapon.Form) == 7 && inventory.Item(weapon.Ammunition[0]) is null && !handling.CanReload(weapon),
            "Source recharger fabricated AMMO loot, lacked initial charge or admitted manual reload.");
        inventory.SetWeaponMagazine(weapon.Form, instance, new(weapon.Ammunition[0], 0, true));
        handling.AdvanceRecharge(weapon, .75);
        var paused = inventory.SelectedWeapon(weapon.Form).Extra.Magazine!;
        handling.AdvanceRecharge(weapon, 0);
        Require(handling.Loaded(weapon.Form) == 0 && paused.RechargeSeconds == .75 &&
            inventory.SelectedWeapon(weapon.Form).Extra.Magazine == paused,
            "Partial/paused simulation time generated a round or lost fractional credit.");
        var saved = JsonSerializer.Deserialize<FalloutOpeningInventoryGrant>(JsonSerializer.Serialize(inventory.Capture()))!;
        var cold = new FalloutPlayerInventory();
        cold.Restore(saved.Inventory, saved.EquippedRuntimeFormIds.ToArray(), saved.InventoryRandomState);
        var resumed = new FalloutWeaponHandling(cold);
        resumed.Restore(handling.Capture(), _ => weapon);
        var npcInventory = new FalloutPlayerInventory();
        npcInventory.Restore(saved.Inventory, saved.EquippedRuntimeFormIds.ToArray(), saved.InventoryRandomState);
        var npc = new FalloutWeaponHandling(npcInventory, nativeNpc: true);
        npc.Restore(handling.Capture(), _ => weapon);
        npc.AdvanceRecharge(weapon, .25);
        Require(npc.Loaded(weapon.Form) == 1 && npcInventory.Item(weapon.Ammunition[0]) is null,
            "NPC generated charge used a different clock or materialized carried ammunition.");
        resumed.AdvanceRecharge(weapon, .25);
        Require(resumed.Loaded(weapon.Form) == 1 && cold.SelectedWeapon(weapon.Form).Extra.Magazine!.RechargeSeconds == 0,
            "Cold generated charge lost its actual item identity or fractional simulation interval.");
        resumed.CompleteReload(weapon);
        Require(resumed.Loaded(weapon.Form) == 1, "Manual reload refilled generated charge.");
        resumed.AdvanceRecharge(weapon, 100);
        Require(resumed.Loaded(weapon.Form) == 7 && cold.SelectedWeapon(weapon.Form).Extra.Magazine!.RechargeSeconds == 0,
            "Recharge exceeded capacity or retained surplus full-clip time.");
        var faster = weapon with { AmmoRegenerationSeconds = .3f };
        var empty = new FalloutItemMagazine(weapon.Ammunition[0], 0, true);
        Require(FalloutWeaponRecharge.Advance(empty, faster, 3 * (double)faster.AmmoRegenerationSeconds).Loaded == 3,
            "Fractional source Regen Rate was treated as rounds per second or rounded to an integer.");
        cold.SetWeaponMagazine(weapon.Form, instance, new(weapon.Ammunition[0], 2, true, .5));
        var stowed = new FalloutPlayerInventory(); stowed.Restore(cold.Capture().Inventory, []);
        var inactive = new FalloutWeaponHandling(stowed);
        inactive.AdvanceRecharge(weapon, 10);
        Require(stowed.SelectedWeapon(weapon.Form).Extra.Magazine == cold.SelectedWeapon(weapon.Form).Extra.Magazine,
            "Stowed generated charge advanced outside its equipped process.");
        var target = new FalloutPlayerInventory();
        stowed.TransferItemsTo(target, [new(weapon.Form, 1)]);
        Require(target.SelectedWeapon(weapon.Form).Extra.Instance == instance &&
            target.SelectedWeapon(weapon.Form).Extra.Magazine is { Generated: true, Loaded: 2, RechargeSeconds: .5 } &&
            target.Item(weapon.Ammunition[0]) is null && stowed.Item(weapon.Ammunition[0]) is null,
            "Generated charge transfer created loot or lost its individual weapon ownership.");
        var stack = new FalloutPlayerInventory();
        stack.Restore(new([new(weapon.Form, 1, "recharger", "WEAP", 2, 0, 1)], null), [1]);
        FalloutWeaponRecharge.Materialize(stack, weapon);
        var first = stack.SelectedWeapon(weapon.Form).Extra.Instance!.Value;
        stack.SetWeaponMagazine(weapon.Form, first, new(weapon.Ammunition[0], 0, true, .25));
        stack.SelectInstance(weapon.Form, 1, stack.Revision);
        FalloutWeaponRecharge.Materialize(stack, weapon);
        Require(stack.SelectedWeapon(weapon.Form).Extra.Instance != first &&
            stack.SelectedWeapon(weapon.Form).Extra.Magazine is { Loaded: 7 } &&
            stack.Instances(weapon.Form).Single(value => value.Extra.Instance == first).Extra.Magazine is { Loaded: 0, RechargeSeconds: .25 },
            "Selecting another copy refilled or replaced the first weapon's individual generated charge.");
        Reject(() => FalloutWeaponRecharge.Advance(empty with { RechargeSeconds = 1 }, weapon, 0));
        Reject(() => FalloutWeaponRecharge.Advance(empty, weapon, double.NaN));
        Reject(() => new FalloutWeaponHandling(target).Restore(handling.Capture() with
            { Magazines = [handling.Capture().Magazines.Single() with { UsesInventoryAmmo = true }] }, _ => weapon));
    }
    internal static void RechargeShot(FalloutPluginStack records, FalloutWeaponPresentation weapon, FalloutWeaponShot shot)
    {
        var inventory = new FalloutPlayerInventory();
        var runtime = records.RuntimeFormId(weapon.Form);
        inventory.Restore(new([new(weapon.Form, runtime, "recharger", "WEAP", 1, 0, 1)], null), [runtime]);
        var handling = new FalloutWeaponHandling(inventory);
        handling.MaterializeRecharge(weapon);
        var instance = inventory.SelectedWeapon(weapon.Form).Extra.Instance!.Value;
        var halfInterval = weapon.AmmoRegenerationSeconds / 2.0;
        inventory.SetWeaponMagazine(weapon.Form, instance, new(shot.Ammunition!.Value, weapon.ClipSize, true));
        Require(handling.ConsumeShot(weapon, shot, records) && handling.Loaded(weapon.Form) == weapon.ClipSize - weapon.AmmoUse &&
            inventory.Items.Count == 1 && inventory.Item(shot.Ammunition.Value) is null &&
            FalloutWeaponCondition.SelectedCondition(inventory.Item(weapon.Form)!) < 1,
            "Generated shot failed to consume real charge/condition or created an AMMO loot stack.");
        handling.AdvanceRecharge(weapon, halfInterval);
        var saved = inventory.Capture();
        var restored = new FalloutPlayerInventory();
        restored.Restore(saved.Inventory, saved.EquippedRuntimeFormIds.ToArray());
        var cold = new FalloutWeaponHandling(restored); cold.Restore(handling.Capture(), _ => weapon);
        cold.AdvanceRecharge(weapon, halfInterval);
        Require(cold.Loaded(weapon.Form) == weapon.ClipSize - weapon.AmmoUse + 1 && restored.Items.Count == 1 &&
            restored.SelectedWeapon(weapon.Form).Extra.Instance == instance,
            "Generated shot recharge lost its per-item fractional clock across restoration.");
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Reject(Action action)
    {
        try { action(); } catch (InvalidDataException) { return; }
        throw new InvalidOperationException("Invalid magazine state was accepted.");
    }
}
