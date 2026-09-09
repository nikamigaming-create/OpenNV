using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal static class FalloutInventoryAccess
{
    internal static bool CanTransfer(FalloutPluginRecord item, bool fromPlayer)
    {
        if (fromPlayer && (item.Flags & 0x400) != 0) return false;
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
