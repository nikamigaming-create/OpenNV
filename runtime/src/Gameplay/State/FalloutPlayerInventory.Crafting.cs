using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed partial class FalloutPlayerInventory
{
    internal bool CanCraft(FalloutRecipe recipe) => Requirements(recipe).All(requirement =>
        Item(requirement.Key)?.Count >= requirement.Value);

    internal void Craft(FalloutPluginStack records, FalloutRecipe recipe, int level, FalloutGlobalState? globals)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(recipe);
        if (level < 1) throw new ArgumentOutOfRangeException(nameof(level));
        var ingredients = Requirements(recipe);
        if (ingredients.Any(requirement => (Item(requirement.Key)?.Count ?? 0) < requirement.Value))
            throw new InvalidOperationException($"Recipe {recipe.Record.FormKey} ingredients are no longer available.");
        var events = ingredients.Select(requirement => new FalloutHudEvent(
                FalloutHudEventKind.ItemRemoved, requirement.Key, requirement.Value))
            .Concat(recipe.Outputs.GroupBy(output => output.Form).Select(group => new FalloutHudEvent(
                FalloutHudEventKind.ItemAdded, group.Key, group.Sum(output => output.Count))))
            .ToArray();
        Notifications.RequirePublish(events);
        var priorItems = new Dictionary<FalloutFormKey, FalloutCampaignItem>(_items);
        var priorTraversal = _traversal;
        var priorEquipped = _equipped.ToArray();
        var priorRandom = _random.State;
        var priorRevision = Revision;
        try
        {
            foreach (var (form, count) in ingredients)
                Remove(form, count, silent: true);
            foreach (var output in recipe.Outputs)
                Add(records, output.Form, output.Count, level, silent: true, globals,
                    validateAddition: selected => FalloutReferenceWorld.RequirePlayerItemAcquisition(
                        records, records.GetEffective(selected), records.RuntimeFormKey(0x14)));
        }
        catch
        {
            _items.Clear();
            foreach (var (form, item) in priorItems) _items.Add(form, item);
            _equipped.Clear();
            _equipped.UnionWith(priorEquipped);
            _random = new(priorRandom);
            _traversal = priorTraversal;
            Revision = priorRevision;
            throw;
        }

        Notifications.Publish(events);
    }

    private static IReadOnlyList<KeyValuePair<FalloutFormKey, int>> Requirements(FalloutRecipe recipe) =>
        recipe.Ingredients.GroupBy(item => item.Form)
            .Select(group => new KeyValuePair<FalloutFormKey, int>(group.Key,
                group.Aggregate(0, (count, item) => checked(count + item.Count))))
            .ToArray();
}
