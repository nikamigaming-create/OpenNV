using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static class OwnedInventoryCommandProbe
{
    internal static void Run(string mod, string root, string game, string questId, short stage, string[] dependencies)
    {
        var setup = new FalloutModStackSelection([new(mod, root, dependencies)]).Resolve(game);
        using var content = setup.OpenSource();
        using var records = FalloutPluginStack.Load(content.PluginSources);
        var quest = FalloutDialogueTopic.Find(records, "QUST", questId);
        var hash = SHA256.HashData(quest.ReadData());
        var fields = quest.ReadSubrecords().ToArray();
        var begin = Array.FindIndex(fields, field => field.Signature == "INDX" && BinaryPrimitives.ReadInt16LittleEndian(field.Data.Span) == stage);
        if (begin < 0) throw new InvalidDataException("Selected stage is absent.");
        var end = begin + 1; while (end < fields.Length && fields[end].Signature is not ("INDX" or "QOBJ")) end++;
        bool InventoryCommand(string line) =>
            line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)[0].Split('.')[^1].ToLowerInvariant() is
                "removeallitems" or "additem" or "removeitem" or "equipitem" or "equipobject" or "resetinventory";
        var candidates = new List<FalloutPluginSubrecord[]>();
        for (var index = begin + 1; index < end;)
        {
            var next = index + 1; while (next < end && fields[next].Signature != "QSDT") next++;
            var candidate = fields[index..next];
            if (candidate.Where(field => field.Signature == "SCTX").Any(field =>
                FalloutDialogueTopic.CodeLines(FalloutDialogueTopic.ScriptText(field.Data.Span)).Any(InventoryCommand))) candidates.Add(candidate);
            index = next;
        }
        if (candidates.Count == 0) throw new NotSupportedException("Audit needs an inventory-bearing result entry.");
        var sourceEntry = 0;
        foreach (var entry in candidates)
        {
            using var world = new FalloutReferenceWorld(records);
            var source = FalloutDialogueTopic.ScriptText(entry.Single(field => field.Signature == "SCTX").Data.Span);
            var commands = FalloutDialogueTopic.CodeLines(source).Where(InventoryCommand).ToArray();
            var bindings = new FalloutScriptBindings(records, quest, quest, entry);
            var player = new FalloutPlayerInventory(123);
            _ = world.EquippedArmor(records.RuntimeFormKey(0x14), 1);
            player.Replace(world.Inventory(records.RuntimeFormKey(0x14), 1).Contents.Capture());
            world.BindPlayerInventory(player);
            var equipped = commands.Select(line => line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                .Where(parts => parts[0].Equals("player.EquipItem", StringComparison.OrdinalIgnoreCase)).Select(parts => bindings.Form(parts[1]).FormKey).Distinct().ToArray();
            foreach (var item in equipped)
                if (player.Item(item) is null) player.Add(records, item, 1, 1, true);
            var resetTargets = commands.Select(line => line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)[0].Split('.'))
                .Where(parts => parts.Length == 2 && parts[1].Equals("ResetInventory", StringComparison.OrdinalIgnoreCase))
                .Select(parts => bindings.Reference(parts[0])).Distinct().ToArray();
            foreach (var target in resetTargets)
            {
                var inventory = world.Inventory(target, 1).Contents;
                foreach (var item in inventory.Items) inventory.Remove(item.FormKey, item.Count, true);
            }
            var countTargets = commands.Select(line => line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                .Where(parts => parts[0].Split('.')[^1].ToLowerInvariant() is "additem" or "removeitem")
                .Select(parts => bindings.Reference(parts[0].Split('.')[0])).Distinct().ToArray();
            foreach (var target in countTargets)
            {
                _ = world.Inventory(target, 1);
                if (target != records.RuntimeFormKey(0x14) && records.GetEffective(world.Get(target).Base).Signature == "NPC_")
                    _ = world.EquippedArmor(target, 1);
            }
            var removals = commands.Select(line => line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                .Where(parts => parts[0].Split('.')[^1].Equals("RemoveItem", StringComparison.OrdinalIgnoreCase))
                .Select(parts => (Target: bindings.Reference(parts[0].Split('.')[0]), Item: bindings.Form(parts[1]).FormKey,
                    Count: int.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture))).ToArray();
            var initialRemovalCounts = removals.Select(command => world.Inventory(command.Target, 1).Contents.Item(command.Item)?.Count ?? 0).ToArray();
            using var coldWorld = new FalloutReferenceWorld(records);
            coldWorld.Restore(JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(world.Capture()))!);
            var snapshot = JsonSerializer.Deserialize<FalloutOpeningInventoryGrant>(JsonSerializer.Serialize(player.Capture()))!;
            var coldPlayer = new FalloutPlayerInventory(); coldPlayer.Restore(snapshot.Inventory, snapshot.EquippedRuntimeFormIds.ToArray(), snapshot.InventoryRandomState);
            void Execute(FalloutReferenceWorld references, FalloutPlayerInventory inventory)
            {
                references.BindPlayerInventory(inventory);
                var owner = new FalloutInventoryCommands(records, references, inventory, () => 1);
                var executor = new FalloutReferenceScripts(records, references, new(records), new((_, _) => false,
                    _ => throw new NotSupportedException("Selected inventory component reached another effect."), Inventory: owner));
                executor.ExecuteStage(quest, entry, string.Join('\n', commands));
                if (equipped.Any(item => !inventory.Equipped.Contains(records.RuntimeFormId(item))))
                    throw new InvalidDataException("Selected source equipment did not become worn.");
                if (owner.EquippedObject(records.RuntimeFormKey(0x14), 5) is not null)
                    throw new InvalidDataException("Selected source leaves an unexpected weapon equipped.");
                for (var index = 0; index < removals.Length; index++)
                {
                    var removal = removals[index];
                    var retained = references.Inventory(removal.Target, 1).Contents;
                    var expected = Math.Max(0, initialRemovalCounts[index] - removal.Count);
                    if ((retained.Item(removal.Item)?.Count ?? 0) != expected ||
                        expected == 0 && retained.Equipped.Contains(records.RuntimeFormId(removal.Item)))
                        throw new InvalidDataException("Selected source removal lost its retained count or equipment identity.");
                }
            }
            Execute(world, player); Execute(coldWorld, coldPlayer);
            if (JsonSerializer.Serialize(player.Capture()) != JsonSerializer.Serialize(coldPlayer.Capture()) ||
                JsonSerializer.Serialize(world.Capture()) != JsonSerializer.Serialize(coldWorld.Capture()) ||
                !SHA256.HashData(quest.ReadData()).AsSpan().SequenceEqual(hash))
                throw new InvalidDataException("Owned inventory component changed cold continuation or source bytes.");
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                schema = "opennv-owned-inventory-commands/v1",
                quest = quest.FormKey,
                sourceStage = stage,
                sourceEntry = sourceEntry++,
                selectedCommands = commands.Length,
                playerItems = player.Items.Count,
                playerEquipment = player.Equipped.Count,
                resetActors = resetTargets.Length,
                countTargets = countTargets.Length,
                sourceRemovalItems = initialRemovalCounts.Sum(),
                coldInventoryAndRandom = true,
                sourceReadOnly = true,
                recording = false,
                boundary = "isolated-owned-inventory-command-slice;whole-stage-ordinary-input-and-retail-parity-unverified"
            }));
        }
    }
}
