using System.Buffers.Binary;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal sealed record FalloutReferenceInventorySnapshot(FalloutOpeningInventoryGrant Contents,
    IReadOnlyList<FalloutFormKey> Unequipped, bool InitialArmorResolved = false);

internal sealed class FalloutReferenceInventory
{
    internal FalloutPlayerInventory Contents { get; } = new();
    // Script changes are independent of the AI's initial equipment selection.
    // An unresolved initial weapon choice is not an authoritative empty list.
    internal HashSet<FalloutFormKey> Unequipped { get; } = [];
    internal bool InitialArmorResolved { get; set; }
    internal FalloutReferenceInventorySnapshot Capture() => new(Contents.Capture(), Unequipped.OrderBy(key => key.ToString(), StringComparer.Ordinal).ToArray(), InitialArmorResolved);
    internal void Restore(FalloutReferenceInventorySnapshot state, FalloutPluginStack records)
    {
        if (state.Unequipped is null || state.Unequipped.Distinct().Count() != state.Unequipped.Count ||
            state.Unequipped.Any(key => records.GetEffective(key).Signature is not ("ARMO" or "WEAP")))
            throw new InvalidDataException("Saved actor unequipped items are invalid.");
        Contents.Restore(state.Contents.Inventory, state.Contents.EquippedRuntimeFormIds.ToArray(), state.Contents.InventoryRandomState);
        Unequipped.UnionWith(state.Unequipped);
        InitialArmorResolved = state.InitialArmorResolved;
    }
}

internal sealed partial class FalloutReferenceWorld
{
    private FalloutReferenceInstance InventoryOwner(FalloutFormKey reference)
    {
        var instance = Get(reference);
        if (records.GetEffective(instance.Base).Signature is not ("NPC_" or "CREA" or "CONT"))
            throw new InvalidDataException($"Reference {reference} has no container inventory.");
        return instance;
    }

    internal FalloutReferenceInventory Inventory(FalloutFormKey reference, int level, FalloutGlobalState? globals = null)
    {
        var actor = InventoryOwner(reference);
        if (actor.Inventory is { } existing) return existing;
        var npc = records.GetEffective(actor.Base);
        var owner = npc.Signature is "NPC_" or "CREA" ? FalloutAiPackages.TemplateOwner(records, npc, 256) : npc;
        var fields = owner.ReadSubrecords().ToArray();
        var inventory = new FalloutReferenceInventory();
        for (var index = 0; index < fields.Length; index++)
        {
            if (fields[index].Signature != "CNTO") continue;
            var bytes = fields[index].Data.Span;
            if (bytes.Length != 8) throw new InvalidDataException("Actor inventory entry extent is invalid.");
            var item = owner.Plugin.AdjustFormId(BinaryPrimitives.ReadUInt32LittleEndian(bytes));
            var count = BinaryPrimitives.ReadInt32LittleEndian(bytes[4..]);
            if (count <= 0) throw new NotSupportedException("Actor inventory count needs its leveled-count owner.");
            FalloutItemVariant? extra = null;
            if (index + 1 < fields.Length && fields[index + 1].Signature == "COED")
            {
                var data = fields[++index].Data.Span;
                if (data.Length != 12) throw new InvalidDataException("Actor item extra-data extent is invalid.");
                var itemOwner = owner.Plugin.AdjustOptionalFormId(BinaryPrimitives.ReadUInt32LittleEndian(data));
                var argument = BinaryPrimitives.ReadUInt32LittleEndian(data[4..]);
                var condition = BinaryPrimitives.ReadSingleLittleEndian(data[8..]);
                var type = itemOwner is { } key ? records.GetEffective(key).Signature : null;
                if (type is not (null or "NPC_" or "FACT")) throw new NotSupportedException("Actor item owner type is unbound.");
                extra = new(count, condition, itemOwner, type == "NPC_" ? owner.Plugin.AdjustOptionalFormId(argument) : null,
                    type == "FACT" ? unchecked((int)argument) : null);
            }
            inventory.Contents.Add(records, item, count, level, true, globals, extra);
        }
        actor.Inventory = inventory;
        return inventory;
    }

    internal void UnequipItem(FalloutFormKey reference, FalloutFormKey item, int level, FalloutGlobalState? globals = null)
    {
        if (records.GetEffective(item).Signature is not ("ARMO" or "WEAP")) throw new InvalidDataException("UnequipItem needs armor or a weapon.");
        var inventory = Inventory(reference, level, globals);
        if (inventory.Contents.Item(item) is null) return;
        inventory.Contents.Unequip(records, item);
        inventory.Unequipped.Add(item);
    }

    internal IReadOnlyList<FalloutFormKey> EquippedArmor(FalloutFormKey reference, int level, FalloutGlobalState? globals = null)
    {
        var inventory = Inventory(reference, level, globals);
        if (!inventory.InitialArmorResolved)
        {
            var armor = inventory.Contents.Items.Where(item => item.RecordType == "ARMO" &&
                !inventory.Unequipped.Contains(item.FormKey)).ToArray();
            uint occupied = 0;
            foreach (var item in armor)
            {
                var data = records.GetEffective(item.FormKey).ReadSubrecords().Single(field => field.Signature == "BMDT").Data;
                if (data.Length != 8) throw new InvalidDataException("Actor armor slots have an invalid extent.");
                var slots = BinaryPrimitives.ReadUInt32LittleEndian(data.Span);
                if ((occupied & slots) != 0)
                    throw new NotSupportedException($"Actor {reference} requires competing-armor selection for {item.FormKey}.");
                occupied |= slots;
            }
            // LVLI is expanded once by the retained inventory owner. Equip the
            // non-competing outfit only after validating the entire selection.
            foreach (var item in armor) inventory.Contents.Equip(records, item.FormKey);
            inventory.InitialArmorResolved = true;
        }
        return inventory.Contents.Equipped.Select(records.RuntimeFormKey)
            .Where(key => records.GetEffective(key).Signature == "ARMO").ToArray();
    }
}
