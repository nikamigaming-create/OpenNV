using OpenNV.Runtime.Content;

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
        if (ingredients.Any(requirement => Item(requirement.Key)?.Count < requirement.Value))
            throw new InvalidOperationException($"Recipe {recipe.Record.FormKey} ingredients are no longer available.");
        var priorItems = new Dictionary<FalloutFormKey, FalloutCampaignItem>(_items);
        var priorEquipped = _equipped.ToArray();
        var priorRandom = _random.State;
        var priorRevision = Revision;
        try
        {
            foreach (var (form, count) in ingredients)
                Remove(form, count, silent: true);
            foreach (var output in recipe.Outputs)
                Add(records, output.Form, output.Count, level, silent: true, globals);
        }
        catch
        {
            _items.Clear();
            foreach (var (form, item) in priorItems) _items.Add(form, item);
            _equipped.Clear();
            _equipped.UnionWith(priorEquipped);
            _random = new(priorRandom);
            Revision = priorRevision;
            throw;
        }

        var events = ingredients.Select(requirement => new FalloutHudEvent(
                FalloutHudEventKind.ItemRemoved, requirement.Key, requirement.Value))
            .Concat(recipe.Outputs.GroupBy(output => output.Form).Select(group => new FalloutHudEvent(
                FalloutHudEventKind.ItemAdded, group.Key, group.Sum(output => output.Count))))
            .ToArray();
        Notifications.Publish(events);
    }

    private static IReadOnlyList<KeyValuePair<FalloutFormKey, int>> Requirements(FalloutRecipe recipe) =>
        recipe.Ingredients.GroupBy(item => item.Form)
            .Select(group => new KeyValuePair<FalloutFormKey, int>(group.Key,
                group.Aggregate(0, (count, item) => checked(count + item.Count))))
            .ToArray();
}
