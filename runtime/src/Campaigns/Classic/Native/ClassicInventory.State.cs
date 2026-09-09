namespace OpenNV.Runtime.Campaigns.Classic.Native;

internal sealed partial class ClassicInventory
{
    internal ClassicInventoryState State => new(_changes.Values.OrderBy(row => row.Origin.Map).ThenBy(row => row.Origin.Serial)
        .Select(row => row with { Stacks = row.Stacks.Select(stack => stack with { InstanceValues = (int[])stack.InstanceValues.Clone() }).ToArray() }).ToArray(),
        LeftHand, RightHand, Armor, ActiveHand, _nextId);

    private void Transfer(ClassicInventoryEntry entry, ClassicItemLocation destination, int amount)
    {
        CheckItem(entry);
        if (amount <= 0 || amount > entry.Amount) throw new InvalidOperationException("The requested item quantity is unavailable.");
        if (entry.Location == destination) return;
        if (entry.Definition.Subtype == 1 && (entry.Amount != 1 || amount != 1))
            throw new NotSupportedException("Stacked containers require distinct contained-object identities.");
        if (destination.Kind == "container")
        {
            var parent = FindHost(destination.Map!, destination.Host!.Value);
            if (parent.Prototype.ObjectType == 0)
            {
                var hostDefinition = Definition(parent);
                if (hostDefinition.Subtype != 1) throw new InvalidOperationException("The destination is not a container.");
                if (entry.Origin.Map == destination.Map && entry.Origin.Serial == destination.Host) throw new InvalidOperationException("A container cannot contain itself.");
                var occupied = At(destination).Sum(row => checked(row.Definition.Size * row.Object.Quantity));
                var count = entry.Definition.Ammo is { } ammo ? (amount + ammo.PackSize - 1) / ammo.PackSize : amount;
                if (checked(occupied + count * entry.Definition.Size) > hostDefinition.ContainerSize) throw new InvalidOperationException("That container is full.");
            }
        }
        var before = State;
        try
        {
            var stacks = Touch(entry.Origin); var current = stacks.Single(row => row.Id == entry.Id);
            if (current.Amount == amount) stacks.Remove(current);
            else stacks[stacks.IndexOf(current)] = current with { Amount = current.Amount - amount };
            var moved = current with { Id = current.Amount == amount ? current.Id : NewId(entry.Origin), Location = destination, Amount = amount };
            var existing = stacks.FirstOrDefault(row => row.Location == destination && row.AmmoPid == moved.AmmoPid && row.InstanceValues.SequenceEqual(moved.InstanceValues));
            if (existing is null) stacks.Add(moved);
            else stacks[stacks.IndexOf(existing)] = existing with { Amount = checked(existing.Amount + moved.Amount) };
            Set(entry.Origin, stacks); ValidateOwnership();
            if (Weight > Capacity) throw new InvalidOperationException("You cannot carry that much.");
            ClearAbsentEquipment();
        }
        catch { RestoreUnchecked(before); throw; }
        Changed?.Invoke();
        if (LeftHand != before.LeftHand || RightHand != before.RightHand || Armor != before.Armor) EquipmentChanged?.Invoke();
    }

    private void ClearAbsentEquipment()
    {
        var ids = Carried.Select(row => row.Id).ToHashSet();
        if (LeftHand is { } left && !ids.Contains(left)) LeftHand = null;
        if (RightHand is { } right && !ids.Contains(right)) RightHand = null;
        if (Armor is { } armor && !ids.Contains(armor)) Armor = null;
    }
    private void ValidateOwnership()
    {
        var ids = new HashSet<string>();
        var ammunitionBalance = new Dictionary<int, long>();
        void Ammunition(int pid, long amount)
        {
            if (amount == 0) return;
            if (Definition(pid).Ammo is null) throw new InvalidDataException("Loaded rounds have no ammunition prototype.");
            ammunitionBalance[pid] = checked(ammunitionBalance.GetValueOrDefault(pid) + amount);
        }
        foreach (var change in _changes.Values)
        {
            var initial = Initial(change.Origin);
            var originalDefinition = Definition(change.Origin.Pid);
            var total = change.Stacks.Where(stack => stack.AmmoPid is null).Sum(stack => (long)stack.Amount);
            if (total > initial.Amount || originalDefinition.Ammo is null && total != initial.Amount)
                throw new InvalidDataException("Saved item quantities do not conserve their original source stack.");
            if (originalDefinition.Ammo is not null) Ammunition(change.Origin.Pid, -initial.Amount);
            if (originalDefinition.Weapon is not null) Ammunition(initial.InstanceValues[1], -(long)initial.InstanceValues[0] * initial.Amount);
            foreach (var stack in change.Stacks)
            {
                if (stack.Origin != change.Origin || stack.Amount <= 0 || !ids.Add(stack.Id) || string.IsNullOrWhiteSpace(stack.Id))
                    throw new InvalidDataException("Saved item stack identity/quantity is invalid.");
                var original = Original(stack.Origin).Object;
                var definition = Definition(stack.AmmoPid ?? original.Pid);
                if (stack.AmmoPid is not null && (originalDefinition.Weapon is not { } sourceWeapon || definition.Ammo?.Caliber != sourceWeapon.Caliber))
                    throw new InvalidDataException("Unloaded ammunition has no compatible source weapon origin.");
                if (stack.InstanceValues.Length != (stack.AmmoPid is null ? original.InstanceValues.Count : 1)) throw new InvalidDataException("Item instance payload size changed.");
                if (definition.Weapon is { } weapon && (stack.InstanceValues.Length != 2 || stack.InstanceValues[0] < 0 || stack.InstanceValues[0] > weapon.Capacity))
                    throw new InvalidDataException("Weapon magazine state is invalid.");
                if (definition.Weapon is { } loadedWeapon)
                {
                    if (stack.InstanceValues[0] > 0 && Definition(stack.InstanceValues[1]).Ammo?.Caliber != loadedWeapon.Caliber)
                        throw new InvalidDataException("Loaded ammunition has the wrong caliber.");
                    Ammunition(stack.InstanceValues[1], (long)stack.InstanceValues[0] * stack.Amount);
                }
                else if (stack.AmmoPid is null && !stack.InstanceValues.SequenceEqual(initial.InstanceValues)) throw new InvalidDataException("Unsupported item payload change.");
                if (definition.Ammo is not null) Ammunition(definition.Pid, stack.Amount);
                var prefix = Key(stack.Origin) + ":";
                if (!stack.Id.StartsWith(prefix, StringComparison.Ordinal) || stack.Id[prefix.Length..] != "source" &&
                    (!long.TryParse(stack.Id[prefix.Length..], out var suffix) || suffix < 1 || suffix > _nextId))
                    throw new InvalidDataException("Saved stack identifier is outside its allocation sequence.");
                if (stack.Location.Kind == "player")
                { if (stack.Location != ClassicItemLocation.Player) throw new InvalidDataException("Player item has invalid location fields."); }
                else if (stack.Location.Kind == "ground")
                {
                    if (stack.Location.Map is null || stack.Location.Host is not null || stack.Location.Tile is not (>= 0 and < 40000) ||
                        stack.Location.Elevation is null || !player.Catalog.Load(stack.Location.Map).Map.Elevations.ContainsKey(stack.Location.Elevation.Value))
                        throw new InvalidDataException("Ground item location is invalid.");
                }
                else if (stack.Location.Kind == "container")
                {
                    if (stack.Location.Map is null || stack.Location.Host is null || stack.Location.Tile is not null || stack.Location.Elevation is not null)
                        throw new InvalidDataException("Container item location is invalid.");
                    var host = FindHost(stack.Location.Map, stack.Location.Host.Value);
                    if (host.Prototype.ObjectType != 1 && (host.Prototype.ObjectType != 0 || Definition(host).Subtype != 1))
                        throw new InvalidDataException("Saved item owner cannot contain items.");
                }
                else throw new InvalidDataException("Unknown item location.");
            }
        }
        if (ammunitionBalance.Values.Any(balance => balance != 0)) throw new InvalidDataException("Saved ammunition and magazines do not conserve rounds.");
        // Detect cycles even when neither container is currently carried.
        foreach (var entry in _changes.Values.SelectMany(row => row.Stacks).Select(Entry).Where(row => row.Definition.Subtype == 1))
            _ = WeightOf([entry], []);
        _ = Weight;
    }
    internal void Restore(ClassicItemOrigin[] origins)
    {
        if (_changes.Count != 0) throw new InvalidOperationException("Inventory restore requires an empty owner.");
        foreach (var origin in origins)
        {
            var initial = Initial(origin) with { Location = ClassicItemLocation.Player };
            if (!_changes.TryAdd(Key(origin), new(origin, [initial]))) throw new InvalidDataException("Saved source stack is duplicated.");
        }
        ValidateOwnership();
        if (Weight > Capacity) throw new InvalidDataException("Saved carried weight exceeds capacity.");
    }
    private void RestoreUnchecked(ClassicInventoryState state)
    {
        _changes.Clear();
        foreach (var change in state.Changes)
            _changes.Add(Key(change.Origin), change with { Stacks = change.Stacks.Select(stack => stack with { InstanceValues = (int[])stack.InstanceValues.Clone() }).ToArray() });
        LeftHand = state.LeftHand; RightHand = state.RightHand; Armor = state.Armor; ActiveHand = state.ActiveHand; _nextId = state.NextId;
    }
    internal void Restore(ClassicInventoryState state)
    {
        if (_changes.Count != 0) throw new InvalidOperationException("Inventory restore requires an empty owner.");
        RestoreUnchecked(state); ValidateOwnership();
        if (_nextId < 0 || ActiveHand is < 0 or > 1 || Weight > Capacity) throw new InvalidDataException("Saved inventory/equipment is invalid.");
        var equipment = new[] { LeftHand, RightHand, Armor }.Where(id => id is not null).ToArray();
        foreach (var id in equipment)
            if (!Carried.Any(row => row.Id == id)) throw new InvalidDataException("Equipped item is not carried.");
        if (equipment.Distinct().Count() != equipment.Length) throw new InvalidDataException("One item cannot occupy multiple equipment slots.");
        if (Worn is { Definition.Armor: null }) throw new InvalidDataException("Equipped armor has no armor prototype.");
    }
}
