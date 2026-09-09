namespace OpenNV.Runtime.Campaigns.Classic.Native;

internal sealed partial class ClassicInventory
{
    internal void Equip(string? id, string slot)
    {
        if (player.Moving) throw new InvalidOperationException("Stop walking before changing equipment.");
        if (slot is not ("left" or "right" or "armor")) throw new ArgumentException("Unknown equipment slot.");
        if (id is not null)
        {
            var entry = Carried.Single(row => row.Id == id); CheckItem(entry);
            if (slot == "armor" && entry.Definition.Armor is null) throw new InvalidOperationException("That item is not armor.");
            if (slot != "armor" && entry.Definition.Armor is not null) throw new InvalidOperationException("Put armor in the armor slot.");
            if (LeftHand == id) LeftHand = null;
            if (RightHand == id) RightHand = null;
            if (Armor == id) Armor = null;
        }
        switch (slot) { case "left": LeftHand = id; break; case "right": RightHand = id; break; case "armor": Armor = id; break; }
        Changed?.Invoke(); EquipmentChanged?.Invoke();
    }
    internal void SwitchHand() { ActiveHand = 1 - ActiveHand; Changed?.Invoke(); EquipmentChanged?.Invoke(); }

    internal int Unload(string weaponId)
    {
        var entry = Carried.Single(row => row.Id == weaponId); CheckItem(entry);
        var weapon = entry.Definition.Weapon ?? throw new InvalidOperationException("That item is not a weapon.");
        if (weapon.Capacity <= 0) throw new InvalidOperationException("That weapon does not use ammunition.");
        if (entry.Amount != 1) throw new InvalidOperationException("Separate one weapon from the stack before unloading.");
        var values = entry.Object.InstanceValues.ToArray();
        if (values.Length != 2 || values[0] < 0 || values[0] > weapon.Capacity) throw new InvalidDataException("Weapon magazine is invalid.");
        if (values[0] == 0) return 0;
        var definition = Definition(values[1]);
        if (definition.Ammo?.Caliber != weapon.Caliber) throw new InvalidDataException("Weapon ammunition is incompatible.");
        var before = State; var rounds = values[0];
        try
        {
            var stacks = Touch(entry.Origin); var gun = stacks.Single(row => row.Id == entry.Id);
            stacks[stacks.IndexOf(gun)] = gun with { InstanceValues = [0, values[1]] };
            var ammunition = stacks.FirstOrDefault(row => row.AmmoPid == values[1] && row.Location == ClassicItemLocation.Player);
            if (ammunition is null) stacks.Add(new(NewId(entry.Origin), entry.Origin, ClassicItemLocation.Player, rounds, [0], values[1]));
            else stacks[stacks.IndexOf(ammunition)] = ammunition with { Amount = checked(ammunition.Amount + rounds) };
            Set(entry.Origin, stacks); ValidateOwnership();
            if (Weight > Capacity) throw new InvalidOperationException("You cannot carry the unloaded ammunition.");
        }
        catch { RestoreUnchecked(before); throw; }
        Changed?.Invoke(); return rounds;
    }

    internal int Reload(string weaponId, string? ammunitionId = null)
    {
        var entry = Carried.Single(row => row.Id == weaponId); CheckItem(entry);
        var weapon = entry.Definition.Weapon ?? throw new InvalidOperationException("That item is not a weapon.");
        if (weapon.Capacity <= 0) throw new InvalidOperationException("That weapon does not use ammunition.");
        if (entry.Amount != 1) throw new InvalidOperationException("Separate one weapon from the stack before reloading.");
        var values = entry.Object.InstanceValues.ToArray();
        if (values.Length != 2 || values[0] < 0 || values[0] > weapon.Capacity) throw new InvalidDataException("Weapon magazine is invalid.");
        if (values[0] == weapon.Capacity) return 0;
        var candidates = Carried.Where(row => row.Definition.Ammo?.Caliber == weapon.Caliber &&
            (ammunitionId is null || row.Id == ammunitionId) && (values[0] == 0 || row.Object.Pid == values[1])).ToArray();
        if (candidates.Length == 0) throw new InvalidOperationException("No compatible ammunition is available; loaded ammunition types cannot be mixed.");
        var before = State; var loaded = 0;
        try
        {
            var ammunitionPid = candidates[0].Object.Pid;
            foreach (var ammunition in candidates.Where(row => row.Object.Pid == ammunitionPid))
            {
                if (values[0] == weapon.Capacity) break;
                CheckItem(ammunition);
                var rounds = Math.Min(ammunition.Amount, weapon.Capacity - values[0]);
                var ammoStacks = Touch(ammunition.Origin); var source = ammoStacks.Single(row => row.Id == ammunition.Id);
                if (source.Amount == rounds) ammoStacks.Remove(source);
                else ammoStacks[ammoStacks.IndexOf(source)] = source with { Amount = source.Amount - rounds };
                Set(ammunition.Origin, ammoStacks); values[0] += rounds; values[1] = ammunitionPid; loaded += rounds;
            }
            var weaponStacks = Touch(entry.Origin); var target = weaponStacks.Single(row => row.Id == entry.Id);
            weaponStacks[weaponStacks.IndexOf(target)] = target with { InstanceValues = values };
            Set(entry.Origin, weaponStacks); ValidateOwnership(); ClearAbsentEquipment();
        }
        catch { RestoreUnchecked(before); throw; }
        Changed?.Invoke();
        if (LeftHand != before.LeftHand || RightHand != before.RightHand) EquipmentChanged?.Invoke();
        return loaded;
    }
}
