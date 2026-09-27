using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

// Explicit diagnostic inventory on a new checkpoint copy. World placements,
// quests, actors, player transform and vitals are retained from that checkpoint.
internal static class WeaponLoadout
{
    internal static int Run(string source, string checkpoint, string destination, string[] weapons)
    {
        if (File.Exists(destination)) throw new IOException("Diagnostic output must be a new file.");
        RuntimeLiveContentSource.Configure(source, RuntimeLiveContentSource.FalloutNewVegasGame);
        using var content = RuntimeLiveContentSource.Current!;
        using var records = FalloutPluginStack.Load(content.PluginSources);
        var input = File.ReadAllBytes(checkpoint);
        var saved = JsonSerializer.Deserialize<FalloutNativeCampaignState>(input) ?? throw new InvalidDataException("Checkpoint is empty.");
        if (saved.SaveCompatibilityId != content.SaveCompatibilityId) throw new InvalidDataException("Checkpoint source stack differs.");
        var items = FalloutCampaignInventoryResolver.Resolve(records, saved.Inventory.Select(item =>
            new FalloutCampaignInventoryRequest(item.RuntimeFormId, item.EditorId, item.RecordType, item.Count)).ToArray(), null);
        var previous = saved.Inventory.ToDictionary(item => item.RuntimeFormId);
        items = items with { Items = items.Items.Select(item => item with { Variants = previous[item.RuntimeFormId].Variants }).ToArray() };
        var inventory = new FalloutPlayerInventory();
        inventory.Restore(items, saved.EquippedRuntimeFormIds.ToArray(), saved.InventoryRandomState);
        var added = new List<object>();
        foreach (var id in weapons)
        {
            var form = records.RuntimeFormKey(uint.Parse(id, NumberStyles.HexNumber, CultureInfo.InvariantCulture));
            var weapon = FalloutWeaponPresentation.Read(records, form);
            var count = weapon.IsThrownWeapon ? 12 : 1;
            inventory.Add(records, form, count, saved.Vitals!.Level, true);
            foreach (var ammo in weapon.Ammunition.Take(1)) inventory.Add(records, ammo, 200, saved.Vitals.Level, true);
            added.Add(new { weapon = form.ToString(), count, ammunition = weapon.Ammunition.Take(1).Select(value => value.ToString()).ToArray() });
        }
        var output = saved with
        {
            Inventory = inventory.Items.Select(item => new FalloutNativeSavedItem(item.RuntimeFormId, item.EditorId, item.RecordType, item.Count, item.Variants)).ToArray(),
            InventoryRandomState = inventory.Capture().InventoryRandomState,
        };
        using (var file = new FileStream(destination, FileMode.CreateNew)) JsonSerializer.Serialize(file, output, new JsonSerializerOptions { WriteIndented = true });
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            diagnostic = "inventory-only loadout; not an earned campaign inventory",
            checkpointSha256 = Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant(), destination, added
        }));
        return 0;
    }
}
