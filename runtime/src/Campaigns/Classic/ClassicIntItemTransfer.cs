namespace OpenNV.Runtime.Campaigns.Classic;

/// <summary>Owned item instance state; Data retains the complete stack-comparison payload.</summary>
internal sealed record ClassicIntItemInstance(int Pid, int ScriptId, uint Flags, int Subtype,
    int[] Data, int AmmoCapacity = 0)
{
    internal void Validate()
    {
        if ((uint)Pid >> 24 != 0 || Subtype is < 0 or > 6 || Data is not { Length: 8 } ||
            Data[0] < 0 || Data[1] < Data[0] || Subtype == 4 && (AmmoCapacity <= 0 || Data[4] < 0))
            throw new InvalidOperationException("Classic item instance is incomplete or invalid.");
    }
}

internal sealed record ClassicIntItemTransferResult(ClassicIntInventoryEntry[] Inventory,
    IReadOnlyDictionary<int, ClassicIntWorldObject> Objects, IReadOnlyDictionary<int, ClassicIntItemInstance> Items);

internal static class ClassicIntItemTransfer
{
    internal static ClassicIntItemTransferResult Add(int owner, int item, int quantity,
        IReadOnlyList<ClassicIntInventoryEntry> sourceInventory,
        IReadOnlyDictionary<int, ClassicIntWorldObject> sourceObjects,
        IReadOnlyDictionary<int, ClassicIntItemInstance> sourceItems, bool single = true)
    {
        var inventory = sourceInventory.ToList();
        var objects = new Dictionary<int, ClassicIntWorldObject>(sourceObjects);
        var items = sourceItems.ToDictionary(row => row.Key, row =>
        {
            row.Value.Validate();
            return row.Value with { Data = (int[])row.Value.Data.Clone() };
        });
        ClassicIntItemTransferResult Result() => new(inventory.ToArray(), objects, items);
        if (owner == 0 || item == 0) return Result();
        if (!objects.ContainsKey(owner) || !objects.ContainsKey(item))
            throw new InvalidOperationException("Inventory transfer has an unknown object handle.");
        var priorOwner = inventory.FirstOrDefault(row => row.ObjectHandle == item);
        if (single && priorOwner is not null || quantity <= 0) return Result();
        var incoming = Require(item);
        if (items.ContainsKey(owner)) _ = Require(owner);
        if (priorOwner is not null)
        {
            if (priorOwner.OwnerHandle != owner || !Matches(incoming, incoming))
                throw new NotSupportedException("Multiple-item insertion requires aliased inventory ownership for this already-owned item.");
            objects[item] = objects[item] with { Tile = -1, Visible = false };
            return Result();
        }
        for (var index = 0; index < inventory.Count; index++)
        {
            var entry = inventory[index];
            if (entry.OwnerHandle != owner) continue;
            var existing = Require(entry.ObjectHandle);
            if (!Matches(existing, incoming)) continue;
            var count = checked(entry.Quantity + quantity);
            if (incoming.Subtype == 4)
            {
                if (incoming.AmmoCapacity != existing.AmmoCapacity)
                    throw new InvalidOperationException("Matching ammunition instances disagree on their source capacity.");
                var rounds = checked(existing.Data[4] + incoming.Data[4]);
                if (rounds <= incoming.AmmoCapacity) count = checked(count - 1);
                else rounds -= incoming.AmmoCapacity;
                incoming.Data[4] = rounds;
            }
            inventory[index] = new(owner, item, count);
            objects.Remove(entry.ObjectHandle);
            items.Remove(entry.ObjectHandle);
            objects[item] = objects[item] with { Tile = -1, Visible = false };
            return Result();
        }
        // Active source stealth devices also change their owner's rendering
        // flags on insertion. Keep that actor effect unbound until it has an owner.
        if (incoming.Pid == 210 && (incoming.Flags & 0x03000000) != 0)
            throw new NotSupportedException("Active item insertion requires its owner-visibility effect.");
        inventory.Add(new(owner, item, quantity));
        if (items.TryGetValue(owner, out var parent))
        {
            parent.Data[0] = checked(parent.Data[0] + 1);
            if (parent.Data[0] > parent.Data[1])
            {
                parent.Data[1] = checked(parent.Data[1] + 10);
                // A runtime allocation identity, never a retail process pointer.
                parent.Data[2] = owner;
            }
        }
        objects[item] = objects[item] with { Tile = -1, Visible = false };
        return Result();

        ClassicIntItemInstance Require(int handle)
        {
            if (!items.TryGetValue(handle, out var instance) || !objects.TryGetValue(handle, out var world) || world.Pid != instance.Pid)
                throw new InvalidOperationException("Inventory transfer lacks source item-instance ownership.");
            instance.Validate();
            if (inventory.Count(row => row.OwnerHandle == handle) != instance.Data[0])
                throw new InvalidOperationException("Item instance and nested inventory disagree.");
            return instance;
        }
    }

    private static bool Matches(ClassicIntItemInstance left, ClassicIntItemInstance right)
    {
        if (left.Pid != right.Pid || left.ScriptId != right.ScriptId ||
            ((left.Flags | right.Flags) & 0x07002000) != 0 || left.Subtype == 1 ||
            left.Data[0] != 0 || right.Data[0] != 0) return false;
        if (left.Subtype != right.Subtype) throw new InvalidOperationException("Matching PIDs have inconsistent source subtypes.");
        for (var index = 0; index < 8; index++)
            if (!(index == 4 && (left.Subtype == 4 || left.Pid == 41)) && left.Data[index] != right.Data[index]) return false;
        return true;
    }
}
