using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static class OwnedSandboxEquipmentProbe
{
    internal static void Run(string dataRoot)
    {
        RuntimeLiveContentSource.Configure(dataRoot, RuntimeLiveContentSource.FalloutNewVegasGame,
            activePlugins: RuntimeLiveContentSource.ResolveVanillaPlugins(dataRoot), includeImplicitPlugins: false);
        using var content = RuntimeLiveContentSource.Current!;
        using var records = FalloutPluginStack.Load(content.PluginSources);
        FalloutFormKey Key(uint id) => new("FalloutNV.esm", id);
        var idles = new uint[] { 0x83518, 0x22c0f }.Select(id => records.GetEffective(Key(id))).ToArray();
        var lists = idles.SelectMany(FalloutCondition.Read).Where(condition => condition.Function == 182)
            .Select(condition => records.GetEffective(condition.FormArgument1)).DistinctBy(record => record.FormKey).ToArray();
        if (lists.Length != 2 || lists.Any(record => record.Signature != "FLST"))
            throw new InvalidDataException("Owned Sandbox IDLE equipment predicates no longer reference both expected form lists.");
        var sources = idles.Concat(lists).Select(record => (Record: record, Hash: SHA256.HashData(record.ReadData()))).ToArray();
        foreach (var list in lists)
        {
            var entries = new List<FalloutFormKey>();
            foreach (var field in list.ReadSubrecords().Where(field => field.Signature == "LNAM"))
            {
                if (field.Data.Length != 4) throw new InvalidDataException("Owned equipment list has a malformed entry.");
                var raw = BinaryPrimitives.ReadUInt32LittleEndian(field.Data.Span);
                if (raw == 0) break;
                entries.Add(list.Plugin.AdjustFormId(raw));
            }
            var item = entries.First(key => records.GetEffective(key).Signature is "ARMO" or "WEAP");
            var player = new FalloutPlayerInventory(123);
            using var world = new FalloutReferenceWorld(records);
            var commands = new FalloutInventoryCommands(records, world, player, () => 1);
            var actor = records.RuntimeFormKey(0x14);
            player.Add(records, item, 1, 1, true);
            if (commands.IsEquipped(actor, list.FormKey)) throw new InvalidDataException("Carrying an owned list member counted as wearing it.");
            player.Equip(records, item);
            var before = JsonSerializer.Serialize(player.Capture());
            if (!commands.IsEquipped(actor, list.FormKey) || !commands.IsEquipped(actor, item) ||
                before != JsonSerializer.Serialize(player.Capture()))
                throw new InvalidDataException("Owned list query missed live equipment or mutated its state.");
            var saved = JsonSerializer.Deserialize<FalloutOpeningInventoryGrant>(before)!;
            var cold = new FalloutPlayerInventory();
            cold.Restore(saved.Inventory, saved.EquippedRuntimeFormIds.ToArray(), saved.InventoryRandomState);
            if (!new FalloutInventoryCommands(records, world, cold, () => 1).IsEquipped(actor, list.FormKey))
                throw new InvalidDataException("Owned list membership lost cold equipment state.");
            player.Unequip(records, item, force: true);
            if (commands.IsEquipped(actor, list.FormKey)) throw new InvalidDataException("Owned list query retained stale worn membership.");
            Console.WriteLine($"OPENNV_OWNED_SANDBOX_EQUIPMENT_LIST_PASS source={list.FormKey} selected={item} " +
                "carriedIsNotWorn=true liveEquipUnequip=true cold=true readonlyQuery=true ordinaryNpcPlayback=unverified");
        }
        if (sources.Any(value => !SHA256.HashData(value.Record.ReadData()).SequenceEqual(value.Hash)))
            throw new InvalidDataException("Owned Sandbox equipment declarations changed during the audit.");
        Console.WriteLine("OPENNV_OWNED_SANDBOX_EQUIPMENT_PASS sourceIdles=2 sourceLists=2 selectedRecordHashesUnchanged=true parity=unverified");
    }
}
