using System.Buffers.Binary;
using System.Text.Json;
using OpenNV.Runtime.Campaigns.Classic;
using OpenNV.Runtime.Campaigns.Classic.Native;
using OpenNV.Runtime.Campaigns.Fallout2.Native;
using OpenNV.Runtime.Content;

internal static class ClassicItemSystemsProbe
{
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Reject(Action action, string message)
    {
        try { action(); }
        catch (Exception error) when (error is InvalidOperationException or InvalidDataException or NotSupportedException) { return; }
        throw new InvalidOperationException(message);
    }
    private static void PrototypeContracts()
    {
        int[] lengths = [129, 65, 125, 122, 81, 69, 61];
        for (var subtype = 0; subtype < lengths.Length; subtype++)
        {
            var bytes = new byte[lengths[subtype]];
            void Word(int at, int value) => BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(at), value);
            Word(0, 1); Word(4, 100); Word(28, -1); Word(32, subtype); Word(40, 2); Word(44, 3); Word(52, -1);
            for (var offset = 57; offset + 4 <= bytes.Length; offset += 4) Word(offset, offset);
            var item = ClassicItemDefinitions.Decode(bytes, 1, new Dictionary<int, string> { [100] = "Synthetic item", [101] = "Description" }, []);
            Require(item.Subtype == subtype && item.Weight == 3 && item.Size == 2 && item.Name == "Synthetic item", "Typed PRO common header drifted.");
            if (item.Armor is { } armor) Require(armor.ArmorClass == 57 && armor.Resistance[6] == 85 && armor.Threshold[0] == 89 && armor.FemaleArt == 125, "Armor field order drifted.");
            if (item.Weapon is { } weapon) Require(weapon.Caliber == 109 && weapon.Capacity == 117, "Weapon caliber/magazine field order drifted.");
            if (item.Ammo is { } ammo) Require(ammo.PackSize == 61 && ammo.Divisor == 77, "Ammunition field order drifted.");
            if (item.Drug is { } drug) Require(drug.Stats[2] == 65 && drug.FirstDelay == 81 && drug.AddictionDelay == 121, "Drug field order drifted.");
            Reject(() => ClassicItemDefinitions.Decode(bytes[..^1], 1, new Dictionary<int, string>(), []), "Truncated PRO accepted.");
            Word(0, 2); Reject(() => ClassicItemDefinitions.Decode(bytes, 1, new Dictionary<int, string>(), []), "Wrong prototype identity accepted.");
        }
        Console.WriteLine("PASS synthetic: seven typed item layouts, truncated payloads and source identity rejection.");
    }

    internal static int Run(string root, string campaign)
    {
        PrototypeContracts();
        using IFalloutClassicOwnedSource source = campaign == "fallout-1" ? Fallout1OwnedContentSource.LoadInstall(root) : Fo2NativeOwnedSource.LoadInstall(root);
        using var catalog = new ClassicMapCatalog(source, campaign);
        var definitions = new ClassicItemDefinitions(catalog);
        var names = Fallout1NativeLists.Read(catalog.Read("proto/items/items.lst", out _));
        var kinds = new Dictionary<int, int>();
        for (var pid = 1; pid <= names.Count; pid++)
        {
            var definition = definitions.Read(pid); kinds[definition.Subtype] = kinds.GetValueOrDefault(definition.Subtype) + 1;
            if (pid is 1 or 4 or 7 or 8 or 29 or 30 or 41) Console.WriteLine(JsonSerializer.Serialize(new
            {
                campaign,
                pid,
                definition.Name,
                definition.Weight,
                definition.Size,
                definition.Weapon,
                definition.Ammo,
                definition.Misc
            }));
        }
        Console.WriteLine($"PASS {campaign}: {names.Count} owned item prototypes decoded: {JsonSerializer.Serialize(kinds)}");
        var premade = ClassicPremadeReader.Load(campaign, path => source.Read(path, out _))[0];
        var choice = new ClassicCharacterDraft(ClassicCharacterDraft.CurrentSchema, campaign, source.ProfileId,
            premade.Id, premade.GcdSha256, premade.BiographySha256, premade.PortraitSha256, premade.Character, null);
        // This is an isolated source-owner fixture, not campaign travel or a player save.
        var mapPath = campaign == "fallout-1" ? "maps/v13ent.map" : "maps/arcaves.map";
        var level = catalog.Load(mapPath);
        var host = level.Objects.TopLevelObjects.First(row => row.Prototype.ObjectType == 0 && row.Prototype.Subtype == 1 && row.Inventory.Count > 0 && row.ScriptId == uint.MaxValue);
        var navigation = new ClassicMapNavigation(level.Map, level.Objects, host.Elevation);
        var contact = ClassicHexGrid.Neighbors(host.Tile).First(navigation.Walkable.Contains);
        var player = new ClassicPlayerSession(catalog, mapPath, choice, host.Elevation, contact);
        player.Inventory.CheckAccess(host.Serial);
        var entries = player.Inventory.Contents(host.Serial).ToArray();
        foreach (var item in entries)
        {
            var quantity = item.Amount > 1 ? Math.Max(1, item.Amount / 2) : 1;
            player.Inventory.Take(host.Serial, item.Id, quantity);
            var carried = player.Inventory.Carried.Single(row => row.Origin == item.Origin);
            Require(carried.Amount == quantity, "Partial transfer quantity changed.");
            player.Inventory.Drop(carried.Id, Math.Max(1, quantity / 2));
            var ground = player.Inventory.GroundItems.Single(row => row.Origin == item.Origin);
            Require(ground.Location.Tile == player.Tile && ground.Location.Elevation == player.Elevation, "Dropped item left its authoritative hex.");
            player = ColdRestore(source, player);
            player.Inventory.TakeGround(ground.Id);
            carried = player.Inventory.Carried.Single(row => row.Origin == item.Origin);
            Require(carried.Amount == quantity, "Dropped/picked stack did not rejoin.");
            player.Inventory.Deposit(host.Serial, carried.Id);
            Require(player.Inventory.Contents(host.Serial).Single(row => row.Origin == item.Origin).Amount == item.Amount, "Return did not conserve the source stack.");
        }
        Require(player.Inventory.Carried.Count == 0, "Transfer cycle retained carried items.");
        Console.WriteLine($"PASS {campaign}: source container {mapPath}:{host.Serial}; partial take, drop at player hex, cold restore, pickup, deposit and stack conservation.");
        EquipmentCases(catalog, source, choice, definitions);
        return 0;
    }

    private static ClassicPlayerSession ColdRestore(IFalloutClassicOwnedSource source, ClassicPlayerSession player)
    {
        var path = Path.Combine(Path.GetTempPath(), "opennv-classic-item-systems-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var before = JsonSerializer.Serialize(player.Inventory.State);
            player.Save(path); var restored = ClassicPlayerSession.Restore(source, path);
            Require(JsonSerializer.Serialize(restored.Inventory.State) == before, "Cold restore changed quantities, locations, magazines or equipment.");
            return restored;
        }
        finally { File.Delete(path); }
    }

    private static void EquipmentCases(ClassicMapCatalog catalog, IFalloutClassicOwnedSource source, ClassicCharacterDraft choice, ClassicItemDefinitions definitions)
    {
        var rows = new List<(ClassicItemOrigin Origin, Fallout1NativeMapObject Object, ClassicItemDefinition Definition)>();
        foreach (var map in catalog.Maps)
        {
            var level = catalog.Load(map);
            void Visit(IEnumerable<Fallout1NativeMapObject> objects)
            {
                foreach (var item in objects)
                {
                    if (item.Prototype.ObjectType == 0 && item.Prototype.LogicalPath is not null && item.ScriptId == uint.MaxValue)
                    {
                        var definition = definitions.Read(item.Pid);
                        if (definition.Script == -1) rows.Add((new(level.Path, level.Sha256, item.Serial, item.Pid), item, definition));
                    }
                    Visit(item.Inventory);
                }
            }
            Visit(level.Objects.TopLevelObjects);
        }
        var weapon = rows.OrderBy(row => row.Object.Pid == 8 ? 0 : 1).First(row => row.Definition.Weapon is { Capacity: > 0 } gun && row.Object.Quantity == 1 &&
            rows.Any(ammo => ammo.Definition.Ammo?.Caliber == gun.Caliber && ammo.Object.InstanceValues[0] > 1));
        var ammo = rows.OrderBy(row => row.Object.Quantity).First(row => row.Definition.Ammo?.Caliber == weapon.Definition.Weapon!.Caliber && row.Object.InstanceValues[0] > 1);
        var wrongAmmo = rows.OrderBy(row => row.Object.Quantity).First(row => row.Definition.Ammo is { } am && am.Caliber != weapon.Definition.Weapon!.Caliber && row.Object.InstanceValues[0] > 0);
        var alternateAmmo = rows.OrderBy(row => row.Object.Quantity).First(row => row.Definition.Ammo?.Caliber == weapon.Definition.Weapon!.Caliber && row.Object.Pid != ammo.Object.Pid && row.Object.InstanceValues[0] > 0);
        var armor = rows.First(row => row.Definition.Armor is not null && row.Object.Quantity == 1);
        var player = new ClassicPlayerSession(catalog, catalog.StartingMap, choice);
        player.Inventory.Restore([weapon.Origin, ammo.Origin, wrongAmmo.Origin, alternateAmmo.Origin, armor.Origin]);
        var inventory = player.Inventory;
        var gunEntry = inventory.Carried.Single(row => row.Origin == weapon.Origin);
        var ammoEntry = inventory.Carried.Single(row => row.Origin == ammo.Origin);
        var wrongEntry = inventory.Carried.Single(row => row.Origin == wrongAmmo.Origin);
        var alternateEntry = inventory.Carried.Single(row => row.Origin == alternateAmmo.Origin);
        var armorEntry = inventory.Carried.Single(row => row.Origin == armor.Origin);
        var unloaded = inventory.Unload(gunEntry.Id);
        Require(unloaded == gunEntry.Object.InstanceValues[0], "Unloading lost original magazine rounds.");
        Reject(() => inventory.Reload(gunEntry.Id, wrongEntry.Id), "Wrong caliber loaded.");
        inventory.Equip(gunEntry.Id, "right"); inventory.Equip(armorEntry.Id, "armor");
        Require(player.ArmorClass == player.Stats.ArmorClass + armor.Definition.Armor!.ArmorClass, "Equipped armor did not affect AC.");
        var before = ammoEntry.Amount;
        inventory.Drop(ammoEntry.Id, before - 1);
        var loaded = inventory.Reload(gunEntry.Id, ammoEntry.Id);
        Require(loaded == 1, "A one-round partial stack was not consumed exactly.");
        Reject(() => inventory.Reload(gunEntry.Id, alternateEntry.Id), "Different ammunition types mixed in one magazine.");
        inventory.TakeGround(inventory.GroundItems.Single(row => row.Origin == ammo.Origin).Id);
        loaded += inventory.Reload(gunEntry.Id, inventory.Carried.Single(row => row.Origin == ammo.Origin).Id);
        Require(loaded == Math.Min(before, weapon.Definition.Weapon!.Capacity), "Magazine loaded the wrong round count.");
        Require(inventory.Carried.Where(row => row.Origin == ammo.Origin).Sum(row => row.Amount) + inventory.Held!.Object.InstanceValues[0] == before,
            "Reload created or destroyed rounds.");
        inventory.SwitchHand(); Require(inventory.Held is null, "Hand switching retained the wrong item.");
        inventory.SwitchHand(); Require(inventory.Held?.Id == gunEntry.Id, "Hand switching lost equipment.");
        player = ColdRestore(source, player); inventory = player.Inventory;
        inventory.Drop(gunEntry.Id); Require(inventory.Held is null, "Dropping equipped weapon left a ghost hand item.");
        player = ColdRestore(source, player); inventory = player.Inventory;
        inventory.TakeGround(gunEntry.Id);
        Require(inventory.Carried.Single(row => row.Id == gunEntry.Id).Object.InstanceValues[0] == loaded, "Dropping a gun discarded its magazine.");
        var good = inventory.State;
        var counterfeit = good with
        {
            Changes = good.Changes.Select(change => change.Origin == ammo.Origin
            ? change with { Stacks = change.Stacks.Select(stack => stack with { Amount = stack.Amount + 1 }).ToArray() } : change).ToArray()
        };
        if (good.Changes.Single(row => row.Origin == ammo.Origin).Stacks.Length > 0)
            Reject(() => new ClassicPlayerSession(catalog, catalog.StartingMap, choice).Inventory.Restore(counterfeit), "Manufactured ammunition restored.");
        var duplicate = good with
        {
            Changes = good.Changes.Select(change => change.Origin == weapon.Origin
            ? change with { Stacks = [.. change.Stacks, change.Stacks[0]] } : change).ToArray()
        };
        Reject(() => new ClassicPlayerSession(catalog, catalog.StartingMap, choice).Inventory.Restore(duplicate), "Duplicated stack restored.");
        var manufactured = good with
        {
            Changes = good.Changes.Select(change => change.Origin == weapon.Origin
            ? change with { Stacks = change.Stacks.Select(stack => stack.AmmoPid is null ? stack : stack with { Amount = stack.Amount + 1 }).ToArray() } : change).ToArray()
        };
        Require(good.Changes.Single(change => change.Origin == weapon.Origin).Stacks.Any(stack => stack.AmmoPid is not null), "Source fixture did not exercise magazine unloading.");
        Reject(() => new ClassicPlayerSession(catalog, catalog.StartingMap, choice).Inventory.Restore(manufactured), "Unloaded ammunition was manufactured on restore.");
        Console.WriteLine($"PASS {choice.Campaign}: source fixture weapon={weapon.Origin.Map}:{weapon.Origin.Serial}/{weapon.Object.Pid}, ammo={ammo.Object.Pid}, armor={armor.Object.Pid}; equip, AC, hand swap, unload/reload, one-round stacks, caliber/type rejection, dropped magazine, cold saves and duplicate rejection. No campaign traversal claim.");

        var bags = rows.Where(row => row.Definition.Subtype == 1 && (row.Definition.ContainerFlags & 1) == 0 && row.Object.Inventory.Count == 0 && row.Object.Quantity == 1 && row.Object.InstanceFlags == 0)
            .OrderBy(row => row.Definition.Weight).Take(2).ToArray();
        Require(bags.Length == 2, "Source corpus has no pair of empty portable containers for the nested inventory fixture.");
        player = new ClassicPlayerSession(catalog, catalog.StartingMap, choice); inventory = player.Inventory;
        inventory.Restore([bags[0].Origin, bags[1].Origin, ammo.Origin]);
        var outer = inventory.Carried.Single(row => row.Origin == bags[0].Origin);
        var inner = inventory.Carried.Single(row => row.Origin == bags[1].Origin);
        var rounds = inventory.Carried.Single(row => row.Origin == ammo.Origin);
        Reject(() => inventory.DepositIntoContainer(outer.Id, outer.Id, 1), "Container accepted itself.");
        inventory.DepositIntoContainer(inner.Id, rounds.Id, 1);
        inventory.DepositIntoContainer(outer.Id, inner.Id, 1);
        var nestedState = JsonSerializer.Serialize(inventory.State);
        Reject(() => inventory.DepositIntoContainer(inner.Id, outer.Id, 1), "Container ownership cycle accepted.");
        Require(JsonSerializer.Serialize(inventory.State) == nestedState, "Failed nested transfer was not rolled back.");
        var nestedRound = inventory.ContainerContents(inner.Id).Single();
        inventory.Drop(outer.Id); player = ColdRestore(source, player); inventory = player.Inventory;
        inventory.TakeFromContainer(inner.Id, nestedRound.Id, 1);
        inventory.TakeGround(outer.Id);
        Require(inventory.Carried.Where(row => row.Origin == ammo.Origin).Sum(row => row.Amount) == rounds.Amount, "Nested extraction lost ammunition.");
        Console.WriteLine($"PASS {choice.Campaign}: portable containers {bags[0].Origin.Map}:{bags[0].Origin.Serial} and {bags[1].Origin.Map}:{bags[1].Origin.Serial}; arbitrary deposit, nesting, self/cycle rejection with rollback, dropped-container access and cold restoration.");
        var bulk = rows.First(row => row.Definition.Size > 0 && row.Object.Inventory.Count == 0 && row.Definition.Ammo is null && row.Definition.Weapon is null &&
            (long)row.Definition.Size * row.Object.Quantity > bags[0].Definition.ContainerSize &&
            (long)row.Definition.Weight * row.Object.Quantity + bags[0].Definition.Weight <= inventory.Capacity);
        player = new ClassicPlayerSession(catalog, catalog.StartingMap, choice); inventory = player.Inventory;
        inventory.Restore([bags[0].Origin, bulk.Origin]);
        outer = inventory.Carried.Single(row => row.Origin == bags[0].Origin);
        var oversize = inventory.Carried.Single(row => row.Origin == bulk.Origin);
        var capacityState = JsonSerializer.Serialize(inventory.State);
        Reject(() => inventory.DepositIntoContainer(outer.Id, oversize.Id, oversize.Amount), "Container accepted more than its source size capacity.");
        Require(JsonSerializer.Serialize(inventory.State) == capacityState, "Full-container failure changed item ownership.");
        Console.WriteLine($"PASS {choice.Campaign}: source container volume limit rejects oversized deposit without mutation.");
    }
}
