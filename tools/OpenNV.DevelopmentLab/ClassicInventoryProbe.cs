using System.Text.Json;
using OpenNV.Runtime.Campaigns.Classic;
using OpenNV.Runtime.Campaigns.Classic.Native;
using OpenNV.Runtime.Campaigns.Fallout2.Native;
using OpenNV.Runtime.Content;

internal static class ClassicInventoryProbe
{
    internal static int Run(string root, string campaign)
    {
        IFalloutClassicOwnedSource source = campaign == "fallout-1" ? Fallout1OwnedContentSource.LoadInstall(root) : Fo2NativeOwnedSource.LoadInstall(root);
        var premade = ClassicPremadeReader.Load(campaign, path => source.Read(path, out _))[0];
        var choice = new ClassicCharacterDraft(ClassicCharacterDraft.CurrentSchema, campaign, source.ProfileId,
            premade.Id, premade.GcdSha256, premade.BiographySha256, premade.PortraitSha256, premade.Character, null);
        var player = new ClassicPlayerSession(source, campaign == "fallout-1" ? "maps/v13ent.map" : "maps/artemple.map", choice);
        void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        var candidates = player.Level.Objects.TopLevelObjects.Where(row => row.Prototype.ObjectType == 0 && row.Prototype.Subtype == 1 && row.Inventory.Count > 0).ToArray();
        var clock = new ClassicPlayerAnimation(path => source.Read(path, out _), choice.Character.Female, campaign);
        var tested = 0;
        foreach (var host in candidates)
        {
            var paths = ClassicHexGrid.Neighbors(host.Tile).Where(player.Walkable.Contains).Select(tile => (Tile: tile, Path: ClassicHexGrid.Path(player.Tile, tile, player.Walkable)))
                .Where(row => row.Path.Length > 0).OrderBy(row => row.Path.Length).ToArray();
            if (paths.Length == 0) continue;
            player.RequestMove(paths[0].Tile);
            for (var frame = 0; player.Moving && frame < 50000; frame++) clock.Tick(player, 0.02);
            Require(!player.Moving, "Owned contact path did not finish.");
            Console.WriteLine(JsonSerializer.Serialize(new { host.Serial, host.InstanceFlags, host.InstanceValues, definition = player.Inventory.Definition(host) }));
            player.Inventory.CheckAccess(host.Serial);
            var entries = player.Inventory.Contents(host.Serial).ToArray();
            foreach (var item in entries) player.Inventory.Take(host.Serial, item.Object.Serial);
            Require(player.Inventory.Contents(host.Serial).Count == 0 && player.Inventory.Carried.Count == entries.Length, "Transfer duplicated or lost a source stack.");
            try { player.Inventory.Take(host.Serial, entries[0].Object.Serial); throw new Exception("Duplicate transfer succeeded."); }
            catch (InvalidOperationException) { }
            var path = Path.Combine(Path.GetTempPath(), "opennv-classic-inventory-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                player.Save(path); var restored = ClassicPlayerSession.Restore(source, path);
                Require(JsonSerializer.Serialize(restored.Inventory.Snapshot) == JsonSerializer.Serialize(player.Inventory.Snapshot), "Cold restore changed item origins.");
                Require(restored.Inventory.Carried.Select(row => (row.Object.Pid, row.Object.Quantity, string.Join(',', row.Object.InstanceValues)))
                    .SequenceEqual(entries.Select(row => (row.Object.Pid, row.Object.Quantity, string.Join(',', row.Object.InstanceValues)))), "Item payload or ammo state changed.");
                foreach (var item in restored.Inventory.Carried.ToArray()) restored.Inventory.Return(host.Serial, item.Origin);
                Require(restored.Inventory.Carried.Count == 0 && restored.Inventory.Contents(host.Serial).Count == entries.Length, "Returning stacks did not conserve items.");
            }
            finally { File.Delete(path); }
            Console.WriteLine(JsonSerializer.Serialize(new { campaign, host.Serial, host.Tile, items = entries.Select(row => new { row.Definition.Name, row.Object.Quantity, row.Definition.Icon }), coldRestore = true, conservation = true }));
            tested++;
        }
        Console.WriteLine(tested == 0 ? $"SKIP {campaign}: no reachable source container at the starting map; looting is not verified." :
            $"PASS {campaign}: {tested} source container transfer/return/cold-restore cases; {player.Inventory.Carried.Count} carried stacks. No invented starter inventory.");
        return 0;
    }
}
