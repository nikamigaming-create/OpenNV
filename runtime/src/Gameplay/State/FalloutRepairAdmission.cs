using System.Buffers.Binary;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal static class FalloutRepairAdmission
{
    internal static bool Allows(FalloutPluginStack records, FalloutFormKey target, FalloutFormKey donor,
        IFalloutFormLists? lists, Func<bool>? juryRigging)
    {
        if (target == donor) return true;
        var item = records.GetEffective(target);
        var repair = item.ReadSubrecords().SingleOrDefault(field => field.Signature == "REPL").Data;
        if (!repair.IsEmpty)
        {
            if (repair.Length != 4) throw new InvalidDataException("Repair list FormID extent is invalid.");
            var raw = BinaryPrimitives.ReadUInt32LittleEndian(repair.Span);
            if (raw != 0)
                foreach (var member in FalloutFormList.Entries(records, item.Plugin.AdjustFormId(raw), lists))
                    if (member == donor) return true;
        }
        if (!(juryRigging ?? throw new NotSupportedException("Repair admission has no current player perk owner."))()) return false;
        var material = records.GetEffective(donor);
        if (material.Signature != item.Signature || records.QuestObjects.IsQuestObject(donor)) return false;
        if (item.Signature == "WEAP")
        {
            var a = Weapon(item); var b = Weapon(material);
            return (b.Span[12] & 0x80) == 0 && a.Span[0] == b.Span[0] &&
                BinaryPrimitives.ReadUInt32LittleEndian(a.Span[104..]) == BinaryPrimitives.ReadUInt32LittleEndian(b.Span[104..]);
        }
        if (item.Signature == "ARMO")
        {
            var a = Armor(item); var b = Armor(material);
            return (b & 0x40) == 0 && ArmorClass(a) == ArmorClass(b) && EquipType(item) == EquipType(material);
        }
        return false;
    }

    private static ReadOnlyMemory<byte> Weapon(FalloutPluginRecord source)
    {
        var data = source.ReadSubrecords().Single(field => field.Signature == "DNAM").Data;
        if (!FalloutWeaponPresentation.IsSupportedDnamExtent(data.Length))
            throw new NotSupportedException("Jury Rigging weapon layout is unbound.");
        return data;
    }
    private static byte Armor(FalloutPluginRecord source)
    {
        var data = source.ReadSubrecords().Single(field => field.Signature == "BMDT").Data;
        if (data.Length != 8) throw new NotSupportedException("Jury Rigging armor layout is unbound.");
        return data.Span[4];
    }
    private static int ArmorClass(byte flags) => (flags & 0x80) != 0 ? 1 : (flags & 8) != 0 ? 2 : 0;
    private static uint EquipType(FalloutPluginRecord source)
    {
        var data = source.ReadSubrecords().SingleOrDefault(field => field.Signature == "ETYP").Data;
        if (data.Length != 4) throw new NotSupportedException("Jury Rigging armor equip type is unbound.");
        return BinaryPrimitives.ReadUInt32LittleEndian(data.Span);
    }
}
