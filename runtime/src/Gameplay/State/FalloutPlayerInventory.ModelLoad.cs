using System.Buffers.Binary;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed partial class FalloutPlayerInventory
{
    // Same equipped instances as commands, combat and saves. The source
    // loader's slots zero/one additionally consult the item's BIPL alternatives.
    internal FalloutCampaignItem? ReadModelLoadWornEntry(FalloutPluginStack records, int slot)
    {
        if (slot is < 0 or > 4) throw new ArgumentOutOfRangeException(nameof(slot));
        FalloutCampaignItem? result = null;
        foreach (var id in _equipped)
        {
            var key = records.RuntimeFormKey(id); var item = Item(key) ?? throw new InvalidDataException("Current worn item has no actual inventory entry.");
            var record = records.GetEffective(key);
            if (!Matches(record, allowAlternatives: slot is 0 or 1)) continue;
            if (result is not null) throw new NotSupportedException("source-model-load-multiple-worn-entry-order-unowned");
            result = item;
        }
        return result;

        bool Matches(FalloutPluginRecord record, bool allowAlternatives)
        {
            if (record.Signature is not ("ARMO" or "ARMA")) return false;
            var rows = record.ReadSubrecords().ToArray();
            var flags = rows.Where(row => row.Signature == "BMDT").ToArray() is [var biped] && biped.Data.Length == 8 ?
                BinaryPrimitives.ReadUInt32LittleEndian(biped.Data.Span) : throw new InvalidDataException("Current source worn armor has no exact biped mask.");
            if ((flags & 1u << slot) != 0) return true;
            if (!allowAlternatives) return false;
            var alternatives = rows.Where(row => row.Signature == "BIPL").ToArray();
            if (alternatives.Length == 0) return false;
            if (alternatives is not [var field] || field.Data.Length != 4) throw new InvalidDataException("Current armor BIPL has an invalid source extent.");
            var link = record.Plugin.AdjustOptionalFormId(BinaryPrimitives.ReadUInt32LittleEndian(field.Data.Span));
            if (link is null) return false;
            var list = records.GetEffective(link.Value);
            if (list.Signature != "FLST") throw new InvalidDataException("Armor BIPL does not resolve to its actual form list.");
            foreach (var row in list.ReadSubrecords().Where(row => row.Signature == "LNAM"))
            {
                if (row.Data.Length != 4) throw new InvalidDataException("BIPL member has an invalid source extent.");
                var member = list.Plugin.AdjustOptionalFormId(BinaryPrimitives.ReadUInt32LittleEndian(row.Data.Span));
                if (member is { } form && Matches(records.GetEffective(form), allowAlternatives: false)) return true;
            }
            return false;
        }
    }
}
