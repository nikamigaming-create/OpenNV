using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal static class FalloutInventoryAccess
{
    // Container list admission reads the actual TESFullName component. An
    // editor identity is not a player-visible name; spaces are real characters.
    internal static bool TryDisplayName(FalloutPluginRecord item, out string name)
    {
        name = FalloutRecordDisplayName.Read(item);
        return name.Length != 0;
    }

    internal static bool CanTransfer(FalloutPluginStack records, FalloutPluginRecord item, bool fromPlayer)
    {
        if (fromPlayer && records.QuestObjects.IsQuestObject(item.FormKey)) return false;
        var (field, offset, mask) = item.Signature switch
        {
            "ARMO" => ("BMDT", 4, 0x40),
            "AMMO" => ("DATA", 4, 0x02),
            "WEAP" => ("DNAM", 12, fromPlayer ? 0x88 : 0x80),
            _ => ("", 0, 0),
        };
        if (mask == 0) return true;
        var data = item.ReadSubrecords().Single(value => value.Signature == field).Data;
        if (data.Length <= offset) throw new InvalidDataException("Inventory item flags are truncated.");
        return (data.Span[offset] & mask) == 0;
    }
}
