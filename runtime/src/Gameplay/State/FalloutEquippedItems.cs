using System.Buffers.Binary;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

// GetEquipped accepts a direct item or the direct entries of one outer FLST.
// Nested lists are not recursively queried. Native list traversal ends at a null
// entry and returns on the first worn match; later entries are not evaluated.
internal static class FalloutEquippedItems
{
    internal static bool Any(FalloutPluginStack records, FalloutFormKey argument,
        Func<FalloutFormKey, bool> equipped)
    {
        var source = records.GetEffective(argument);
        if (source.Signature != "FLST") return Direct(source);
        foreach (var field in source.ReadSubrecords().Where(field => field.Signature == "LNAM"))
        {
            if (field.Data.Length != sizeof(uint)) throw new InvalidDataException("Equipment form-list entry has an invalid extent.");
            var raw = BinaryPrimitives.ReadUInt32LittleEndian(field.Data.Span);
            if (raw == 0) break;
            if (Direct(records.GetEffective(source.Plugin.AdjustFormId(raw)))) return true;
        }
        return false;

        bool Direct(FalloutPluginRecord item) => item.Signature is
            "ALCH" or "AMMO" or "ARMO" or "BOOK" or "CCRD" or "CHIP" or "CMNY" or "IMOD" or "KEYM" or "LIGH" or "MISC" or "WEAP"
                && equipped(item.FormKey);
    }
}
