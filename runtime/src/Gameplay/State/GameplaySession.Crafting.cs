using OpenNV.Runtime.Gameplay.Crafting;
using OpenNV.Runtime.World.Interactions;

namespace OpenNV.Runtime.Gameplay.State;

internal partial class GameplaySession
{
    private CraftingInteractionView? _craftingView;
    private CraftingStationInstance? _activeCraftingStation;

    internal bool IsCraftingOpen => _craftingView?.IsOpen == true;

    internal void OpenCrafting(CraftingStationInstance station)
    {
        if (_craftingView is null)
            throw new InvalidOperationException("Crafting interaction UI is not ready.");
        ClosePipBoy();
        CloseContainer();
        _activeCraftingStation = station;
        _craftingView.Open(
            station.Contract,
            CanCraft,
            Craft,
            CloseCrafting);
        RefreshHud($"Opened {station.Contract.Category.DisplayName}");
    }

    internal bool CanCraft(CraftingRecipe recipe) =>
        TryBuildCraftingInventory(recipe, out _, out _);

    internal void Craft(CraftingRecipe recipe)
    {
        if (_activeCraftingStation is null ||
            !_activeCraftingStation.Contract.Recipes.Any(candidate =>
                candidate.FormId.Equals(recipe.FormId, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Crafting recipe does not belong to the active station.");
        if (!TryBuildCraftingInventory(recipe, out var proposed, out var failure))
        {
            RefreshHud(failure);
            _craftingView?.Refresh();
            return;
        }
        var equippedRemoved = _equippedWeaponFormId is not null &&
            !proposed.ContainsKey(_equippedWeaponFormId);
        _inventory.Clear();
        foreach (var item in proposed)
            _inventory.Add(item.Key, item.Value);
        if (equippedRemoved)
            ClearEquippedWeapon();
        Save();
        RefreshHud($"Crafted {recipe.DisplayName}");
        _craftingView?.Refresh();
    }

    private bool TryBuildCraftingInventory(
        CraftingRecipe recipe,
        out Dictionary<string, InventoryEntry> proposed,
        out string failure)
    {
        proposed = new Dictionary<string, InventoryEntry>(_inventory, StringComparer.OrdinalIgnoreCase);
        foreach (var ingredient in recipe.Ingredients)
        {
            if (!proposed.TryGetValue(ingredient.Definition.FormId, out var current) ||
                current.Count < ingredient.Count)
            {
                failure = $"Missing {ingredient.Definition.DisplayName}";
                return false;
            }
            var definition = current.Definition.Merge(ingredient.Definition);
            if (current.Count == ingredient.Count)
                proposed.Remove(ingredient.Definition.FormId);
            else
                proposed[ingredient.Definition.FormId] = new InventoryEntry(
                    definition,
                    current.Count - ingredient.Count);
        }
        foreach (var output in recipe.Outputs)
        {
            if (proposed.TryGetValue(output.Definition.FormId, out var current))
            {
                proposed[output.Definition.FormId] = new InventoryEntry(
                    current.Definition.Merge(output.Definition),
                    checked(current.Count + output.Count));
            }
            else
                proposed.Add(
                    output.Definition.FormId,
                    new InventoryEntry(output.Definition, output.Count));
        }
        failure = "";
        return true;
    }

    private void CloseCrafting()
    {
        if (_craftingView?.IsOpen != true)
            return;
        _craftingView.Close();
        _activeCraftingStation = null;
        RefreshHud("Crafting closed");
    }
}
