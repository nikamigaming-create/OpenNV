using System.Text.Json;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Gameplay.Items;

namespace OpenNV.Runtime.Gameplay.Crafting;

internal sealed record CraftingStationContract(
    string ScriptFormId,
    string ScriptEditorId,
    CraftingCategory Category,
    IReadOnlyList<CraftingRecipe> Recipes)
{
    internal static CraftingStationContract Read(JsonElement source)
    {
        if (source.GetProperty("type").GetString() != "crafting-station" ||
            source.GetProperty("support").GetString() != "unconditioned-zero-skill-recipes")
            throw new InvalidOperationException("Crafting station contract is unsupported.");
        var script = source.GetProperty("script");
        var category = CraftingCategory.Read(source.GetProperty("category"));
        var recipes = source.GetProperty("recipes").EnumerateArray()
            .Select(row => CraftingRecipe.Read(row, category.FormId))
            .ToArray();
        var result = new CraftingStationContract(
            FalloutFormId.Normalize(script.GetProperty("formId").GetString()!),
            script.GetProperty("editorId").GetString()!,
            category,
            recipes);
        if (string.IsNullOrWhiteSpace(result.ScriptEditorId) || recipes.Length == 0 ||
            recipes.Select(recipe => recipe.FormId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != recipes.Length)
            throw new InvalidOperationException("Crafting station contract is incomplete.");
        return result;
    }
}

internal sealed record CraftingCategory(
    string FormId,
    string EditorId,
    string DisplayName,
    int SourceKind)
{
    internal static CraftingCategory Read(JsonElement source)
    {
        var result = new CraftingCategory(
            FalloutFormId.Normalize(source.GetProperty("formId").GetString()!),
            source.GetProperty("editorId").GetString()!,
            source.GetProperty("displayName").GetString()!,
            source.GetProperty("sourceKind").GetInt32());
        if (string.IsNullOrWhiteSpace(result.EditorId) || string.IsNullOrWhiteSpace(result.DisplayName) ||
            result.SourceKind is < byte.MinValue or > byte.MaxValue)
            throw new InvalidOperationException("Crafting category identity is invalid.");
        return result;
    }
}

internal sealed record CraftingRecipe(
    string FormId,
    string EditorId,
    string DisplayName,
    string CategoryFormId,
    string SubcategoryFormId,
    IReadOnlyList<CraftingItem> Ingredients,
    IReadOnlyList<CraftingItem> Outputs)
{
    internal static CraftingRecipe Read(JsonElement source, string expectedCategoryFormId)
    {
        if (source.GetProperty("schema").GetString() != "opennv-owned-crafting-recipe/v1" ||
            source.GetProperty("requiredSkillLevel").GetInt32() != 0)
            throw new InvalidOperationException("Crafting recipe contract is unsupported.");
        var result = new CraftingRecipe(
            FalloutFormId.Normalize(source.GetProperty("formId").GetString()!),
            source.GetProperty("editorId").GetString()!,
            source.GetProperty("displayName").GetString()!,
            FalloutFormId.Normalize(source.GetProperty("categoryFormId").GetString()!),
            FalloutFormId.Normalize(source.GetProperty("subcategoryFormId").GetString()!),
            source.GetProperty("ingredients").EnumerateArray().Select(CraftingItem.Read).ToArray(),
            source.GetProperty("outputs").EnumerateArray().Select(CraftingItem.Read).ToArray());
        if (!result.CategoryFormId.Equals(expectedCategoryFormId, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(result.EditorId) || string.IsNullOrWhiteSpace(result.DisplayName) ||
            result.Ingredients.Count == 0 || result.Outputs.Count == 0 ||
            HasDuplicateItems(result.Ingredients) || HasDuplicateItems(result.Outputs))
            throw new InvalidOperationException("Crafting recipe identity or contents are invalid.");
        return result;
    }

    private static bool HasDuplicateItems(IEnumerable<CraftingItem> items) =>
        items.Select(item => item.Definition.FormId)
            .Distinct(StringComparer.OrdinalIgnoreCase).Count() != items.Count();
}

internal sealed record CraftingItem(ItemDefinition Definition, int Count)
{
    internal static CraftingItem Read(JsonElement source)
    {
        var result = new CraftingItem(
            ItemDefinition.ReadCompiled(source),
            source.GetProperty("count").GetInt32());
        if (result.Count <= 0 || string.IsNullOrWhiteSpace(result.Definition.DisplayName))
            throw new InvalidOperationException("Crafting item identity or count is invalid.");
        return result;
    }
}
