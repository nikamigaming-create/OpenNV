using System.Buffers.Binary;

namespace OpenNV.Runtime.Content;

// The Creature producer's used-item context is a source presentation choice,
// never an inventory grant or an ingestible transaction.
internal sealed record FalloutCreatureEatItem(FalloutFormKey Source, string SourceSha256, int Index,
    FalloutFormKey Item, string ItemSha256)
{
    internal static FalloutCreatureEatItem Select(FalloutPluginStack records, FalloutEatPackage package, Func<uint, uint> random)
    {
        var source = ChoiceSource(records, package);
        var items = Items(source);
        var index = source.Signature == "FLST" ? checked((int)random(checked((uint)items.Length))) : 0;
        var item = records.GetEffective(items[index]);
        RequireItem(item);
        return new(source.FormKey, source.ReadDataHash(), index, item.FormKey, item.ReadDataHash());
    }

    internal void Validate(FalloutPluginStack records, FalloutEatPackage package)
    {
        Validate();
        var source = ChoiceSource(records, package);
        var items = Items(source);
        var item = records.GetEffective(Item);
        if (Source != source.FormKey || !SourceSha256.Equals(source.ReadDataHash(), StringComparison.OrdinalIgnoreCase) ||
            Index >= items.Length || items[Index] != Item || !ItemSha256.Equals(item.ReadDataHash(), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Creature Eat used item differs from its retained winning source choice.");
        RequireItem(item);
    }

    internal void Validate()
    {
        if (!Valid(Source) || !Valid(Item) || Index < 0 || !Hash(SourceSha256) || !Hash(ItemSha256))
            throw new InvalidDataException("Creature Eat used-item identity is invalid.");
        static bool Valid(FalloutFormKey form) => !string.IsNullOrWhiteSpace(form.OwnerPlugin) && form.ObjectId is > 0 and <= FalloutFormKey.ObjectIdMask;
        static bool Hash(string value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
    }

    private static FalloutPluginRecord ChoiceSource(FalloutPluginStack records, FalloutEatPackage package)
    {
        if (!package.Creature) throw new InvalidDataException("Creature used item requires its source producer.");
        var form = package.FoodTarget is { Type: 1, Form: { } authored } ? authored :
            FalloutDefaultObjects.Read(records, FalloutDefaultObject.CreatureFood) ??
                throw new NotSupportedException("Creature Eat has no winning default used-item source.");
        return records.GetEffective(form);
    }

    private static FalloutFormKey[] Items(FalloutPluginRecord source)
    {
        if (source.Signature != "FLST") { RequireItem(source); return [source.FormKey]; }
        var fields = source.ReadSubrecords().Where(field => field.Signature == "LNAM").ToArray();
        if (fields.Length == 0) throw new NotSupportedException("Empty Creature Eat source list requires its native empty-selection owner.");
        return fields.Select(field => field.Data.Length == 4
            ? source.Plugin.AdjustFormId(BinaryPrimitives.ReadUInt32LittleEndian(field.Data.Span))
            : throw new InvalidDataException("Creature Eat source-list identity extent is invalid.")).ToArray();
    }

    private static void RequireItem(FalloutPluginRecord item)
    {
        if (item.Signature is not ("ALCH" or "INGR"))
            throw new NotSupportedException("Creature Eat used-item source is not an ingredient or alchemy form.");
    }
}
