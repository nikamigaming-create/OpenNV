using System.Buffers.Binary;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal static class FalloutInventoryItemCounts
{
    internal static double Count(FalloutPluginStack records, FalloutPlayerInventory inventory, FalloutFormKey argument)
    {
        var form = records.GetEffective(argument);
        if (form.Signature != "FLST") return Direct(form);
        double total = 0;
        foreach (var field in form.ReadSubrecords().Where(field => field.Signature == "LNAM"))
        {
            if (field.Data.Length != sizeof(uint)) throw new InvalidDataException("Item-count form-list entry has an invalid extent.");
            var entry = form.Plugin.AdjustFormId(BinaryPrimitives.ReadUInt32LittleEndian(field.Data.Span));
            // The source query tests each direct entry for inventory-object
            // participation; it does not recursively query nested form lists.
            total += Direct(records.GetEffective(entry));
        }
        return total;

        double Direct(FalloutPluginRecord item) => item.Signature is
            "ALCH" or "AMMO" or "ARMO" or "BOOK" or "CCRD" or "CHIP" or "CMNY" or "IMOD" or "KEYM" or "LIGH" or "MISC" or "WEAP"
                ? inventory.Item(item.FormKey)?.Count ?? 0 : 0;
    }
}
