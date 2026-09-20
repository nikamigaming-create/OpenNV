using System.Buffers.Binary;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutRecipeCategory(FalloutFormKey Form, string EditorId, string Name, bool IsSubcategory)
{
    internal static FalloutRecipeCategory Read(FalloutPluginStack records, FalloutFormKey form)
    {
        var record = records.GetEffective(form);
        if (record.Signature != "RCCT") throw new InvalidDataException($"Recipe category {form} is not RCCT.");
        var fields = record.ReadSubrecords().ToArray();
        var editor = fields.Single(field => field.Signature == "EDID").Data;
        var name = fields.SingleOrDefault(field => field.Signature == "FULL").Data;
        var data = fields.Single(field => field.Signature == "DATA").Data;
        if (data.Length != 1 || data.Span[0] > 1)
            throw new InvalidDataException($"Recipe category {form} has an invalid subcategory flag.");
        var editorId = FalloutDialogueTopic.Text(editor.Span);
        var displayName = name.IsEmpty ? editorId : FalloutDialogueTopic.Text(name.Span);
        if (string.IsNullOrWhiteSpace(editorId) || string.IsNullOrWhiteSpace(displayName))
            throw new InvalidDataException($"Recipe category {form} has no name.");
        return new(form, editorId, displayName, data.Span[0] != 0);
    }
}

internal sealed record FalloutRecipeItem(FalloutFormKey Form, int Count);

internal sealed record FalloutRecipe(FalloutPluginRecord Record, string EditorId, string Name,
    int RequiredSkill, int RequiredLevel, FalloutFormKey? Category, FalloutFormKey? Subcategory,
    IReadOnlyList<FalloutCondition> Conditions, IReadOnlyList<FalloutRecipeItem> Ingredients,
    IReadOnlyList<FalloutRecipeItem> Outputs)
{
    private static readonly HashSet<string> ItemSignatures = new(StringComparer.Ordinal)
    { "ARMO", "AMMO", "MISC", "WEAP", "BOOK", "KEYM", "ALCH", "NOTE", "IMOD", "CMNY", "CCRD", "CHIP", "LIGH" };

    internal static FalloutRecipe Read(FalloutPluginStack records, FalloutPluginRecord record)
    {
        if (record.Signature != "RCPE") throw new InvalidDataException("Recipe source record is not RCPE.");
        var fields = record.ReadSubrecords().ToArray();
        var editorBytes = fields.Single(field => field.Signature == "EDID").Data;
        var nameBytes = fields.SingleOrDefault(field => field.Signature == "FULL").Data;
        var data = fields.Single(field => field.Signature == "DATA").Data.Span;
        if (data.Length != 16) throw new NotSupportedException($"Recipe {record.FormKey} DATA extent {data.Length} is unbound.");
        var skill = BinaryPrimitives.ReadInt32LittleEndian(data);
        var level = BinaryPrimitives.ReadUInt32LittleEndian(data[4..]);
        if (skill < -1 || skill > 76 || level > 100)
            throw new InvalidDataException($"Recipe {record.FormKey} has an invalid skill or level requirement.");
        var category = record.Plugin.AdjustOptionalFormId(BinaryPrimitives.ReadUInt32LittleEndian(data[8..]));
        var subcategory = record.Plugin.AdjustOptionalFormId(BinaryPrimitives.ReadUInt32LittleEndian(data[12..]));
        foreach (var key in new[] { category, subcategory }.Where(key => key is not null).Select(key => key!.Value))
            if (records.GetEffective(key).Signature != "RCCT")
                throw new InvalidDataException($"Recipe {record.FormKey} category reference {key} is not RCCT.");

        var ingredients = new List<FalloutRecipeItem>();
        var outputs = new List<FalloutRecipeItem>();
        for (var index = 0; index < fields.Length; index++)
        {
            var field = fields[index];
            if (field.Signature is not ("RCIL" or "RCOD")) continue;
            if (field.Data.Length != 4 || index + 1 >= fields.Length || fields[index + 1].Signature != "RCQY" ||
                fields[index + 1].Data.Length != 4)
                throw new InvalidDataException($"Recipe {record.FormKey} item/quantity pair is malformed.");
            var item = record.Plugin.AdjustFormId(BinaryPrimitives.ReadUInt32LittleEndian(field.Data.Span));
            var count = BinaryPrimitives.ReadUInt32LittleEndian(fields[++index].Data.Span);
            if (count is 0 or > int.MaxValue)
                throw new InvalidDataException($"Recipe {record.FormKey} item quantity is outside runtime storage.");
            var source = records.GetEffective(item);
            if (!ItemSignatures.Contains(source.Signature))
                throw new NotSupportedException($"Recipe {record.FormKey} item {item} has unsupported type {source.Signature}.");
            (field.Signature == "RCIL" ? ingredients : outputs).Add(new(item, (int)count));
        }
        if (outputs.Count == 0) throw new InvalidDataException($"Recipe {record.FormKey} has no output items.");
        var editorId = FalloutDialogueTopic.Text(editorBytes.Span);
        var name = nameBytes.IsEmpty ? editorId : FalloutDialogueTopic.Text(nameBytes.Span);
        if (string.IsNullOrWhiteSpace(editorId) || string.IsNullOrWhiteSpace(name))
            throw new InvalidDataException($"Recipe {record.FormKey} has no name.");
        return new(record, editorId, name, skill, (int)level, category, subcategory,
            FalloutCondition.Read(record), ingredients, outputs);
    }

    internal static IReadOnlyList<FalloutRecipe> ReadCategory(FalloutPluginStack records, FalloutFormKey category)
    {
        var sourceCategory = FalloutRecipeCategory.Read(records, category);
        if (sourceCategory.IsSubcategory) throw new InvalidDataException($"Recipe menu category {category} is a subcategory.");
        return records.EffectiveRecords("RCPE").Select(record => Read(records, record))
            .Where(recipe => recipe.Category == category).ToArray();
    }
}
