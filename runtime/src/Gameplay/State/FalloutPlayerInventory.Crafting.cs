using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed partial class FalloutPlayerInventory
{
    internal int MaximumCraftCount(FalloutRecipe recipe) => Requirements(recipe).Aggregate(100, (maximum, requirement) =>
        Math.Min(maximum, (Item(requirement.Key)?.Count ?? 0) / requirement.Value));

    internal bool CanCraft(FalloutRecipe recipe) => Requirements(recipe).All(requirement =>
        Item(requirement.Key)?.Count >= requirement.Value);

    internal void Craft(FalloutPluginStack records, FalloutRecipe recipe, int level, FalloutGlobalState? globals,
        int quantity = 1, long? expectedRevision = null)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(recipe);
        if (level < 1) throw new ArgumentOutOfRangeException(nameof(level));
        if (quantity is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(quantity));
        if (expectedRevision is { } revision) RequireRevision(revision);
        var ingredients = Requirements(recipe).Select(requirement =>
            new KeyValuePair<FalloutFormKey, int>(requirement.Key, checked(requirement.Value * quantity))).ToArray();
        var outputs = recipe.Outputs.Select(output => output with { Count = checked(output.Count * quantity) }).ToArray();
        if (ingredients.Any(requirement => (Item(requirement.Key)?.Count ?? 0) < requirement.Value))
            throw new InvalidOperationException($"Recipe {recipe.Record.FormKey} ingredients are no longer available.");
        var events = ingredients.Select(requirement => new FalloutHudEvent(
                FalloutHudEventKind.ItemRemoved, requirement.Key, requirement.Value))
            .Concat(outputs.GroupBy(output => output.Form).Select(group => new FalloutHudEvent(
                FalloutHudEventKind.ItemAdded, group.Key, group.Sum(output => output.Count))))
            .ToArray();
        Notifications.RequirePublish(events);
        ApplyRemovalTransaction(null, (staged, _) =>
        {
            foreach (var (form, count) in ingredients)
                staged.RemoveCore(form, count, silent: true);
            foreach (var output in outputs)
                staged.Add(records, output.Form, output.Count, level, silent: true, globals,
                    validateAddition: selected => FalloutReferenceWorld.RequirePlayerItemAcquisition(
                        records, records.GetEffective(selected), records.RuntimeFormKey(0x14)));
        });

        Notifications.Publish(events);
    }

    private static IReadOnlyList<KeyValuePair<FalloutFormKey, int>> Requirements(FalloutRecipe recipe) =>
        recipe.Ingredients.GroupBy(item => item.Form)
            .Select(group => new KeyValuePair<FalloutFormKey, int>(group.Key,
                group.Aggregate(0, (count, item) => checked(count + item.Count))))
            .ToArray();
}
