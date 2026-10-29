using System.Buffers.Binary;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Content.Definitions;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    internal FalloutWeaponPresentation WeaponPresentation(FalloutFormKey weapon, bool firstPerson = true, byte weaponMods = 0) =>
        FalloutWeaponPresentation.Read(records, weapon, firstPerson, weaponMods, FormLists);

    private void FormListChanged(FalloutFormKey list)
    {
        foreach (var actor in _instances.Values)
            if (actor.Inventory is { } inventory && records.GetEffective(actor.Base).Signature == "NPC_" && Uses(inventory.Contents))
                InvalidateActorAppearance(actor.Reference);
        if (_playerInventory is { } player && Uses(player.Contents))
            InvalidateActorAppearance(records.RuntimeFormKey(FalloutEngineForms.PlayerReference));

        bool Uses(FalloutPlayerInventory inventory)
        {
            foreach (var equipped in inventory.Equipped)
            {
                var armor = records.GetEffective(records.RuntimeFormKey(equipped));
                if (armor.Signature != "ARMO") continue;
                var addon = armor.ReadSubrecords().SingleOrDefault(field => field.Signature == "BIPL").Data;
                if (addon.IsEmpty) continue;
                if (addon.Length != sizeof(uint)) throw new InvalidDataException("Armor addon-list FormID extent is invalid.");
                var raw = BinaryPrimitives.ReadUInt32LittleEndian(addon.Span);
                if (raw != 0 && armor.Plugin.AdjustFormId(raw) == list) return true;
            }
            return false;
        }
    }
}
