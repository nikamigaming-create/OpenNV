using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

// GetEquipped accepts a direct item or the direct entries of one outer FLST.
// Nested lists are not recursively queried. Native list traversal ends at a null
// entry and returns on the first worn match; later entries are not evaluated.
internal static class FalloutEquippedItems
{
    internal static bool Any(FalloutPluginStack records, FalloutFormKey argument,
        Func<FalloutFormKey, bool> equipped, IFalloutFormLists? current = null)
    {
        if (FalloutEngineIdentity.IsPlayerReference(records, argument)) return false;
        var source = records.GetEffective(argument);
        if (source.Signature != "FLST") return Direct(source);
        foreach (var member in FalloutFormList.Entries(records, argument, current))
        {
            if (member is not { } form) break;
            if (FalloutEngineIdentity.IsPlayerReference(records, form)) continue;
            if (Direct(records.GetEffective(form))) return true;
        }
        return false;

        bool Direct(FalloutPluginRecord item) => item.Signature is
            "ALCH" or "AMMO" or "ARMO" or "BOOK" or "CCRD" or "CHIP" or "CMNY" or "IMOD" or "KEYM" or "LIGH" or "MISC" or "WEAP"
                && equipped(item.FormKey);
    }
}
