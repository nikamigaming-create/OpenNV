using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

internal static class WeaponHandlingContracts
{
    internal static void Run()
    {
        var weaponKey = new FalloutFormKey("Synthetic.esm", 1);
        var ammoKey = new FalloutFormKey("Synthetic.esm", 2);
        var weapon = new FalloutWeaponPresentation(weaponKey, new("weapon", weaponKey, null, null, 0, 0, []), "1hp", 1, 255)
        { ClipSize = 12, AmmoUse = 1, Ammunition = [ammoKey] };
        var inventory = new FalloutPlayerInventory();
        inventory.Restore(new([new(weaponKey, 1, "weapon", "WEAP", 1, 0, 1), new(ammoKey, 2, "ammo", "AMMO", 17, 0, 0)], null), [1]);
        var state = new FalloutWeaponHandling(inventory);
        Require(state.Loaded(weaponKey) == 0 && state.CanReload(weapon), "A new magazine invented loaded ammunition.");
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
        Reject(() => new FalloutWeaponHandling(inventory).Restore(new(true, [new(weaponKey, ammoKey, 13)]), _ => weapon));
        Reject(() => FalloutWeaponHandling.Validate(new(true, [new(weaponKey, ammoKey, 0), new(weaponKey, ammoKey, 0)])));
        Console.WriteLine("OPENNV_WEAPON_HANDLING_CONTRACT_PASS conservation=true cold=true scriptRemoval=true invalidState=true");
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Reject(Action action)
    {
        try { action(); } catch (InvalidDataException) { return; }
        throw new InvalidOperationException("Invalid magazine state was accepted.");
    }
}
