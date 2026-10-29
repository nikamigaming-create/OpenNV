using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal static class FalloutInventoryItemCounts
{
    internal static double Count(FalloutPluginStack records, FalloutPlayerInventory inventory, FalloutFormKey argument,
        IFalloutFormLists? current = null)
    {
        if (FalloutEngineIdentity.IsPlayerReference(records, argument)) return 0;
        var form = records.GetEffective(argument);
        if (form.Signature != "FLST") return Direct(form);
        double total = 0;
        foreach (var member in FalloutFormList.Entries(records, argument, current))
        {
            if (member is not { } entry) throw new InvalidDataException("Item-count form-list member is null.");
            if (FalloutEngineIdentity.IsPlayerReference(records, entry)) continue;
            // The source query tests each direct entry for inventory-object
            // participation; it does not recursively query nested form lists.
            total += Direct(records.GetEffective(entry));
        }
        return total;

        double Direct(FalloutPluginRecord item) => item.Signature is
            "ALCH" or "AMMO" or "ARMO" or "BOOK" or "CCRD" or "CHIP" or "CMNY" or "IMOD" or "INGR" or "KEYM" or "LIGH" or "MISC" or "WEAP"
                ? inventory.Item(item.FormKey)?.Count ?? 0 : 0;
    }
}
