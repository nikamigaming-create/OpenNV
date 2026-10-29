using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static class OwnedCaravanPlacedPickupProbe
{
    internal static void Run(string game)
    {
        RuntimeLiveContentSource.Configure(game, RuntimeLiveContentSource.FalloutNewVegasGame,
            activePlugins: RuntimeLiveContentSource.ResolveVanillaPlugins(game), includeImplicitPlugins: false);
        using var content = RuntimeLiveContentSource.Current!;
        using var records = FalloutPluginStack.Load(content.PluginSources);
        var cards = records.EffectiveRecords("CCRD").Select(card => card.FormKey).ToHashSet();
        var found = 0; var unavailable = 0; var passed = 0; var unread = 0; var failures = new List<string>();
        foreach (var reference in records.EffectiveRecords("REFR"))
        {
            FalloutFormKey item;
            try { item = FalloutDialogueTopic.RequiredForm(reference, "NAME"); }
            catch (InvalidDataException) { ++unread; continue; }
            if (!cards.Contains(item)) continue;
            ++found;
            try
            {
                var cell = FalloutCellSceneReader.ParentCell(reference) ?? throw new InvalidDataException("Reference has no parent cell.");
                using var world = new FalloutReferenceWorld(records);
                world.LoadCell(FalloutCellSceneReader.Read(records, cell));
                if (!world.CanActivate(reference.FormKey)) { ++unavailable; continue; }
                var inventory = new FalloutPlayerInventory();
                world.Take(reference.FormKey, inventory, 1, null);
                if (!world.Get(reference.FormKey).Taken || inventory.Item(item) is not null ||
                    !world.PlayerCaravanCards.First.Contains(item))
                    throw new InvalidDataException("Pickup did not publish the source event outcome.");
                ++passed;
            }
            catch (Exception error) when (error is InvalidDataException or InvalidOperationException or NotSupportedException)
            {
                failures.Add($"{reference.FormKey}: {error.Message}");
            }
        }
        Console.WriteLine($"OPENNV_OWNED_CARAVAN_PLACED_PICKUP found={found} unavailable={unavailable} passed={passed} unreadReferenceBases={unread} failures={failures.Count} retail=unverified");
        foreach (var failure in failures) Console.WriteLine(failure);
        if (found == 0 || passed == 0 || failures.Count != 0 || unread != 0)
            throw new InvalidDataException("Owned placed-card pickup audit did not pass its discovered applicable references.");
    }
}
