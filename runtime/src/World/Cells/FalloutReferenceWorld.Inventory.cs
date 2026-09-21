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

internal readonly record struct FalloutArmorCandidate(FalloutFormKey Item, uint Slots, float Threshold, float Resistance);

internal static class FalloutActorArmorSelection
{
    // Rank the actual retained items, then admit disjoint biped slots. Exact
    // retail tie ordering is unverified; equal ratings retain inventory order.
    internal static IReadOnlyList<FalloutFormKey> Select(IReadOnlyList<FalloutArmorCandidate> candidates)
    {
        if (candidates.Select(value => value.Item).Distinct().Count() != candidates.Count ||
            candidates.Any(value => !float.IsFinite(value.Threshold) || !float.IsFinite(value.Resistance) ||
                value.Threshold < 0 || value.Resistance < 0))
            throw new InvalidDataException("Actor armor candidates are invalid or duplicated.");
        uint occupied = 0;
        var selected = new List<FalloutFormKey>();
        foreach (var item in candidates.OrderByDescending(value => value.Threshold).ThenByDescending(value => value.Resistance))
        {
            if ((occupied & item.Slots) != 0) continue;
            occupied |= item.Slots;
            selected.Add(item.Item);
        }
        return selected;
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
        var npc = records.GetEffective(actor.Base);
        if (npc.Signature is "NPC_" or "CREA") InitializeActorTemplates(reference, level, globals);
        if (actor.Inventory is { } existing) return existing;
        var owner = npc.Signature is "NPC_" or "CREA" ? FalloutActorTemplateOwner.Resolve(records, npc, 256, actor.Templates) : npc;
        var fields = owner.ReadSubrecords().ToArray();
        var inventory = new FalloutReferenceInventory();
        var inventoryLevel = actor.Templates?.Level ??
            (ReferenceEncounterZone(actor) is { } zone ? EncounterLevel(zone, level) : level);
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
            inventory.Contents.Add(records, item, count, inventoryLevel, true, globals, extra);
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
            if (records.GetEffective(Get(reference).Base).Signature == "CREA")
            {
                // Creatures can carry armor as loot, but do not wear NPC biped equipment.
                inventory.InitialArmorResolved = true;
                return [];
            }
            var armor = inventory.Contents.Items.Where(item => item.RecordType == "ARMO" &&
                !inventory.Unequipped.Contains(item.FormKey)).ToArray();
            var candidates = new List<FalloutArmorCandidate>();
            var ratingBase = armor.Length == 0 ? 0 : FalloutGameSettingFloats.Read(records, "fArmorRatingBase");
            var ratingMax = armor.Length == 0 ? 0 : FalloutGameSettingFloats.Read(records, "fArmorRatingMax");
            foreach (var item in armor)
            {
                var record = records.GetEffective(item.FormKey);
                var data = record.ReadSubrecords().Single(field => field.Signature == "BMDT").Data;
                if (data.Length != 8) throw new InvalidDataException("Actor armor slots have an invalid extent.");
                var slots = BinaryPrimitives.ReadUInt32LittleEndian(data.Span);
                var condition = FalloutWeaponCondition.SelectedCondition(item);
                if (condition <= 0) continue;
                var defense = FalloutArmorDefense.Read(record);
                var factor = ratingBase + condition * (ratingMax - ratingBase);
                candidates.Add(new(item.FormKey, slots, defense.Threshold * factor, defense.Resistance * factor));
            }
            // Keep all competing items for loot; only the worn slot set changes.
            foreach (var item in FalloutActorArmorSelection.Select(candidates)) inventory.Contents.Equip(records, item);
            inventory.InitialArmorResolved = true;
        }
        return inventory.Contents.Equipped.Select(records.RuntimeFormKey)
            .Where(key => records.GetEffective(key).Signature == "ARMO").ToArray();
    }
}
