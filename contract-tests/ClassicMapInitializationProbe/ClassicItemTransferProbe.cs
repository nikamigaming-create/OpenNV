using System.Text.Json;
using OpenNV.Runtime.Campaigns.Classic;

internal static class ClassicItemTransferProbe
{
    internal static void Run(ClassicIntExpressionContext context)
    {
        ClassicIntItemInstance Item(int rounds = 0) => new(29, -1, 0, 4, [0, 0, 0, 0, rounds, 0, 0, 0], 24);
        var objects = new Dictionary<int, ClassicIntWorldObject>
        { [100] = new(100, 0x01000000, 20, 0, true), [1] = new(1, 29, -1, 0, false), [2] = new(2, 29, 30, 1, true) };
        foreach (var (oldRounds, newRounds, expectedCount, expectedRounds) in new[]
        { (24, 24, 5, 24), (5, 7, 4, 12), (17, 7, 4, 24), (20, 9, 5, 5), (0, 0, 4, 0) })
        {
            var instances = new Dictionary<int, ClassicIntItemInstance> { [1] = Item(oldRounds), [2] = Item(newRounds) };
            var result = ClassicIntItemTransfer.Add(100, 2, 3, [new(100, 1, 2)], objects, instances, single: false);
            if (!result.Inventory.SequenceEqual([new ClassicIntInventoryEntry(100, 2, expectedCount)]) ||
                result.Items[2].Data[4] != expectedRounds || result.Objects.ContainsKey(1) || result.Items.ContainsKey(1) ||
                result.Objects[2].Tile != -1 || result.Objects[2].Elevation != 1 || result.Objects[2].Visible ||
                instances[2].Data[4] != newRounds || objects[2].Tile != 30)
                throw new InvalidOperationException("Item merge lost ammunition, representative identity, placement or transaction isolation.");
        }
        var basic = new ClassicIntItemInstance(29, -1, 0, 2, new int[8]);
        foreach (var changed in new[]
        {
            basic with { ScriptId = 3 }, basic with { Flags = 0x2000 }, basic with { Flags = 0x01000000 },
            basic with { Data = [0, 1, 123, 0, 0, 0, 0, 0] }, basic with { Data = [0, 0, 0, 0, 9, 0, 0, 0] },
        })
        {
            var result = ClassicIntItemTransfer.Add(100, 2, 1, [new(100, 1, 2)], objects,
                new Dictionary<int, ClassicIntItemInstance> { [1] = basic, [2] = changed });
            if (result.Inventory.Length != 2 || !result.Objects.ContainsKey(1))
                throw new InvalidOperationException("Incompatible item instances were combined.");
        }
        var container = basic with { Subtype = 1 };
        var separate = ClassicIntItemTransfer.Add(100, 2, 1, [new(100, 1, 2)], objects,
            new Dictionary<int, ClassicIntItemInstance> { [1] = container, [2] = container });
        if (separate.Inventory.Length != 2) throw new InvalidOperationException("Containers were stacked.");
        var parentObjects = new Dictionary<int, ClassicIntWorldObject>(objects) { [100] = new(100, 50, 20, 0, true) };
        var parentInstances = new Dictionary<int, ClassicIntItemInstance>
        { [100] = new(50, -1, 0, 1, new int[8]), [2] = basic };
        var nested = ClassicIntItemTransfer.Add(100, 2, 1, [], parentObjects, parentInstances);
        if (nested.Items[100].Data[0] != 1 || nested.Items[100].Data[1] != 10 ||
            nested.Items[100].Data[2] == 0 || parentInstances[100].Data[0] != 0)
            throw new InvalidOperationException("Nested inventory header was not updated transactionally.");
        var repeated = ClassicIntItemTransfer.Add(100, 2, 3, nested.Inventory, nested.Objects, nested.Items, single: false);
        if (repeated.Inventory.Single().Quantity != 1)
            throw new InvalidOperationException("Same-representative insertion multiplied an already-owned item.");
        foreach (var (owner, amount) in new[] { (0, 1), (100, 0), (100, -1) })
            if (ClassicIntItemTransfer.Add(owner, 2, amount, [], objects,
                new Dictionary<int, ClassicIntItemInstance> { [2] = basic }, single: false).Inventory.Length != 0)
                throw new InvalidOperationException("Null or rejected-quantity insertion changed inventory.");

        var world = ClassicIntWorldObjectState.Empty with
        {
            Objects = objects, Inventory = [new(100, 1, 2)],
            ItemInstances = new Dictionary<int, ClassicIntItemInstance> { [1] = Item(20), [2] = Item(9) },
        };
        var cold = ClassicIntWorldObjectState.Restore(JsonSerializer.SerializeToElement(world.Save()));
        var ordered = world with { Inventory = [new(100, 2, 1), new(100, 1, 2)] };
        if (!ClassicIntWorldObjectState.Restore(JsonSerializer.SerializeToElement(ordered.Save())).Inventory.SequenceEqual(ordered.Inventory))
            throw new InvalidOperationException("Restore reordered first-match inventory candidates.");
        var state = new ClassicIntProcedureState(new Dictionary<int, int>(), new Dictionary<int, int>(),
            new Dictionary<int, int>(), new Dictionary<int, int>(), new Dictionary<int, int>(), [], null);
        ClassicIntProgram Program(ushort terminal)
        {
            ClassicIntInstruction[] code = [new(0, 0xc001, 100), new(6, 0xc001, 2), new(12, 0x80d8, null), new(14, terminal, null)];
            var procedure = new ClassicIntProcedure("transfer", 0, null, code);
            return new("item-transfer", [procedure], new Dictionary<string, ClassicIntProcedure> { [procedure.Name] = procedure },
                code.ToDictionary(row => row.Offset), new Dictionary<int, string>(), new Dictionary<int, string>());
        }
        var executed = ClassicIntProcedureVm.Execute(Program(0x801c), "transfer", state, context, cold, null, 4);
        if (executed.WorldObjects.Inventory.Single().Quantity != 3 || executed.WorldObjects.ItemInstances[2].Data[4] != 5)
            throw new InvalidOperationException("Cold VM transfer did not retain partial ammunition state.");
        try
        {
            _ = ClassicIntProcedureVm.Execute(Program(0xffff), "transfer", state, context, cold, null, 4);
            throw new Exception("Unsupported instruction after transfer was accepted.");
        }
        catch (InvalidOperationException) { }
        if (cold.ItemInstances[2].Data[4] != 9 || !cold.Objects.ContainsKey(1) || cold.Inventory.Single().Quantity != 2)
            throw new InvalidOperationException("Failed script committed an item transfer.");
        Console.WriteLine("OPENNV_CLASSIC_ITEM_TRANSFER_PASS instanceMatching=true partialAmmo=true incomingRepresentative=true placement=true cold=true rollback=true");
    }
}
