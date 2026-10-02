using System.Buffers.Binary;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal sealed record FalloutReferenceLock(int Level, FalloutFormKey? Key, bool Leveled);

internal sealed partial class FalloutReferenceWorld
{
    internal static bool IsInventoryItem(string type) => type is "ALCH" or "AMMO" or "ARMO" or "BOOK" or "CCRD" or "CHIP" or "CMNY" or "IMOD" or "KEYM" or "LIGH" or "MISC" or "NOTE" or "WEAP";

    internal FalloutReferenceLock? Lock(FalloutFormKey reference)
    {
        var (state, declaration) = EffectiveLock(reference);
        return state?.Locked == true ? new(GetLockLevel(reference), declaration?.Key,
            ((declaration?.Flags ?? 0) & 4) != 0) : null;
    }

    internal bool UnlockWithKey(FalloutFormKey reference, FalloutPlayerInventory player)
    {
        var (state, declaration) = EffectiveLock(reference);
        if (state?.Locked != true) return true;
        if (declaration?.Key is not { } key || player.Item(key) is null) return false;
        UnlockReference(reference);
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
        var ownership = Ownership(reference);
        player.Add(records, instance.Base, count, level, false, globals,
            new(count, condition, ownership.Owner, ownership.Global, ownership.FactionRank));
        instance.Taken = true;
    }
}
