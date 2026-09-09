using System.Buffers.Binary;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal sealed record FalloutReferenceLock(int Level, FalloutFormKey? Key, bool Leveled);

internal sealed partial class FalloutReferenceWorld
{
    internal static bool IsInventoryItem(string type) => type is "ALCH" or "AMMO" or "ARMO" or "BOOK" or "CCRD" or "CHIP" or "CMNY" or "IMOD" or "KEYM" or "MISC" or "NOTE" or "WEAP";

    internal FalloutReferenceLock? Lock(FalloutFormKey reference)
    {
        if (Get(reference).Unlocked) return null;
        var source = records.GetEffective(reference);
        var field = source.ReadSubrecords().SingleOrDefault(value => value.Signature == "XLOC").Data;
        if (field.IsEmpty) return null;
        if (field.Length is not (12 or 20)) throw new InvalidDataException("Reference lock has an invalid extent.");
        var level = field.Span[0];
        var key = source.Plugin.AdjustOptionalFormId(BinaryPrimitives.ReadUInt32LittleEndian(field.Span[4..]));
        return new(level, key, (field.Span[8] & 4) != 0);
    }

    internal bool UnlockWithKey(FalloutFormKey reference, FalloutPlayerInventory player)
    {
        var locked = Lock(reference);
        if (locked is null) return true;
        if (locked.Key is not { } key || player.Item(key) is null) return false;
        Get(reference).Unlocked = true;
        return true;
    }

    internal void Take(FalloutFormKey reference, FalloutPlayerInventory player, int level, FalloutGlobalState? globals)
    {
        var instance = Get(reference);
        if (!CanActivate(reference)) throw new InvalidOperationException("Item is no longer available.");
        if (!IsInventoryItem(records.GetEffective(instance.Base).Signature)) throw new InvalidDataException("Reference is not an inventory item.");
        var source = records.GetEffective(reference);
        var fields = source.ReadSubrecords().ToArray();
        var countField = fields.SingleOrDefault(field => field.Signature == "XCNT").Data;
        if (!countField.IsEmpty && countField.Length != 4) throw new InvalidDataException("Item count has an invalid extent.");
        var count = countField.IsEmpty ? 1 : BinaryPrimitives.ReadInt32LittleEndian(countField.Span);
        var conditionField = fields.SingleOrDefault(field => field.Signature == "XHLP").Data;
        if (!conditionField.IsEmpty && conditionField.Length != 4) throw new InvalidDataException("Item health has an invalid extent.");
        float? condition = conditionField.IsEmpty ? null : BinaryPrimitives.ReadSingleLittleEndian(conditionField.Span) / 100;
        var ownerField = fields.SingleOrDefault(field => field.Signature == "XOWN").Data;
        if (!ownerField.IsEmpty && ownerField.Length != 4) throw new InvalidDataException("Item owner has an invalid extent.");
        var owner = ownerField.IsEmpty ? null : source.Plugin.AdjustOptionalFormId(BinaryPrimitives.ReadUInt32LittleEndian(ownerField.Span));
        var rankField = fields.SingleOrDefault(field => field.Signature == "XRNK").Data;
        if (!rankField.IsEmpty && rankField.Length != 4) throw new InvalidDataException("Item ownership rank has an invalid extent.");
        int? rank = rankField.IsEmpty ? null : BinaryPrimitives.ReadInt32LittleEndian(rankField.Span);
        player.Add(records, instance.Base, count, level, false, globals, new(count, condition, owner, FactionRank: rank));
        instance.Taken = true;
    }
}
