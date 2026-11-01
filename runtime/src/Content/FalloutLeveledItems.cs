using System.Buffers.Binary;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutItemMagazine(FalloutFormKey Ammunition, int Loaded);
internal sealed record FalloutItemVariant(int Count, float? Condition = null, FalloutFormKey? Owner = null,
    FalloutFormKey? Global = null, int? FactionRank = null, byte WeaponMods = 0, byte? Hotkey = null,
    Guid? Instance = null, FalloutItemMagazine? Magazine = null)
{
    // A shortcut assigns an inventory entry; it does not change the physical
    // item's condition, ownership or installed weapon modifications.
    internal bool SameItem(FalloutItemVariant other) =>
        this with { Count = 1, Hotkey = null, Instance = null } == other with { Count = 1, Hotkey = null, Instance = null };
}
internal sealed record FalloutItemAddition(FalloutFormKey Form, FalloutItemVariant Variant)
{
    internal bool SourceExtraPresent { get; init; }
}

internal static class FalloutLeveledItems
{
    private sealed record Entry(ushort Level, FalloutFormKey Form, ushort Count, FalloutItemVariant Extra, bool ExtraPresent);

    internal static IReadOnlyList<FalloutItemAddition> Resolve(FalloutPluginStack records, FalloutFormKey form, int count,
        int level, Func<uint, uint> random, Func<FalloutFormKey, float>? global = null, FalloutItemVariant? extra = null,
        bool nativeContainerOrder = false, IFalloutLeveledItemChanges? changes = null)
    {
        if (count <= 0 || level < 1) throw new ArgumentOutOfRangeException(nameof(count));
        var result = new List<FalloutItemAddition>();
        var active = new HashSet<FalloutFormKey>();
        Expand(form, count, extra ?? new(1), extra is not null, result);
        return result;

        void MergeFirst(List<FalloutItemAddition> output, FalloutItemAddition addition)
        {
            var extraData = addition.Variant;
            if (extraData.WeaponMods != 0 || extraData.Hotkey is not null || extraData.Instance is not null || extraData.Magazine is not null ||
                addition.SourceExtraPresent && extraData.Condition is null || !addition.SourceExtraPresent &&
                (extraData.Condition is not null || extraData.Owner is not null || extraData.Global is not null || extraData.FactionRank is not null))
                throw new NotSupportedException("Initial native container order requires its source COED identity.");
            // COED absence and its owner, owner-dependent argument and condition
            // define this identity. Unrelated live item variants do not.
            var index = output.FindIndex(value => value.Form == addition.Form && value.SourceExtraPresent == addition.SourceExtraPresent &&
                value.Variant.Owner == extraData.Owner &&
                value.Variant.Global == extraData.Global && value.Variant.FactionRank == extraData.FactionRank &&
                value.Variant.Condition == extraData.Condition);
            if (index < 0) output.Insert(0, addition);
            else output[index] = output[index] with
            {
                Variant = output[index].Variant with
                { Count = checked(output[index].Variant.Count + addition.Variant.Count) }
            };
        }

        void Expand(FalloutFormKey key, int amount, FalloutItemVariant inherited, bool inheritedExtraPresent,
            List<FalloutItemAddition> output)
        {
            var record = records.GetEffective(key);
            if (record.Signature != "LVLI")
            {
                if (record.Signature is not ("ALCH" or "AMMO" or "ARMO" or "BOOK" or "CCRD" or "CHIP" or "CMNY" or "IMOD" or "INGR" or "KEYM" or "LIGH" or "MISC" or "NOTE" or "WEAP"))
                    throw new NotSupportedException($"Inventory addition {key} is {record.Signature}.");
                var addition = new FalloutItemAddition(key, inherited with { Count = amount })
                { SourceExtraPresent = nativeContainerOrder && inheritedExtraPresent };
                if (nativeContainerOrder) MergeFirst(output, addition);
                else output.Add(addition);
                return;
            }
            if (!active.Add(key)) throw new InvalidDataException("Leveled item graph contains a cycle.");
            try
            {
                var fields = record.ReadSubrecords().ToArray();
                var chanceField = fields.Single(field => field.Signature == "LVLD").Data;
                var flagsField = fields.Single(field => field.Signature == "LVLF").Data;
                if (chanceField.Length != 1 || flagsField.Length != 1) throw new InvalidDataException("Leveled-list flags have an invalid extent.");
                var flags = flagsField.Span[0];
                if ((flags & ~7) != 0) throw new NotSupportedException("Leveled-list behavior flags are unbound.");
                float chance = chanceField.Span[0];
                if (fields.SingleOrDefault(field => field.Signature == "LVLG").Data is { IsEmpty: false } chanceGlobal)
                {
                    if (chanceGlobal.Length != 4) throw new InvalidDataException("Leveled-list global extent is invalid.");
                    var owner = record.Plugin.AdjustOptionalFormId(BinaryPrimitives.ReadUInt32LittleEndian(chanceGlobal.Span));
                    if (owner is { } chanceOwner) chance = (global ?? throw new NotSupportedException("Leveled-list chance has no global owner."))(chanceOwner);
                }
                if (!float.IsFinite(chance) || chance < 0 || chance > 100) throw new InvalidDataException("Leveled-list chance is outside 0..100.");
                var entries = new List<Entry>();
                foreach (var field in fields)
                {
                    var bytes = field.Data.Span;
                    if (field.Signature == "LVLO")
                    {
                        if (bytes.Length != 12) throw new NotSupportedException("Leveled-list entry layout is unbound.");
                        var quantity = BinaryPrimitives.ReadUInt16LittleEndian(bytes[8..]);
                        if (quantity == 0) throw new InvalidDataException("Leveled-list quantity is zero.");
                        entries.Add(new(BinaryPrimitives.ReadUInt16LittleEndian(bytes), record.Plugin.AdjustFormId(BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..])),
                            quantity, inherited, inheritedExtraPresent));
                    }
                    else if (field.Signature == "COED")
                    {
                        if (bytes.Length != 12 || entries.Count == 0) throw new InvalidDataException("Leveled-list extra data has no entry or invalid extent.");
                        var condition = BinaryPrimitives.ReadSingleLittleEndian(bytes[8..]);
                        if (!float.IsFinite(condition) || condition < 0 || condition > 1) throw new InvalidDataException("Item condition is outside 0..1.");
                        var owner = record.Plugin.AdjustOptionalFormId(BinaryPrimitives.ReadUInt32LittleEndian(bytes));
                        var argument = BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..]);
                        var ownerType = owner is { } ownerForm ? records.GetEffective(ownerForm).Signature : null;
                        if (ownerType is not (null or "FACT" or "NPC_")) throw new NotSupportedException("Item extra owner is not a faction or NPC.");
                        if (nativeContainerOrder && owner is null && argument != 0)
                            throw new NotSupportedException("Native container order has an unrepresented ownerless COED argument.");
                        entries[^1] = entries[^1] with
                        {
                            ExtraPresent = true,
                            Extra = new(1, condition, owner,
                            ownerType == "NPC_" ? record.Plugin.AdjustOptionalFormId(argument) : null,
                            ownerType == "FACT" ? unchecked((int)argument) : null)
                        };
                    }
                }
                foreach (var added in changes?.Additions(key) ?? [])
                {
                    var entry = new Entry(added.Level, added.Form, added.Count, new(1, added.Condition), true);
                    var before = entries.FindIndex(candidate => candidate.Level > added.Level);
                    if (before < 0) entries.Add(entry);
                    else entries.Insert(before, entry);
                }
                var eligible = entries.Where(entry => entry.Level <= level).ToArray();
                if (eligible.Length == 0) return;
                if ((flags & 1) == 0)
                {
                    var highest = eligible.Max(entry => entry.Level);
                    eligible = eligible.Where(entry => entry.Level == highest).ToArray();
                }
                var rolls = (flags & 2) != 0 ? amount : 1;
                var multiplier = (flags & 2) != 0 ? 1 : amount;
                var local = nativeContainerOrder ? new List<FalloutItemAddition>() : output;
                for (var roll = 0; roll < rolls; roll++)
                {
                    if (chance > 0 && random(100) < chance) continue;
                    var selected = (flags & 4) != 0 ? eligible : [eligible[checked((int)random((uint)eligible.Length))]];
                    foreach (var entry in selected) Expand(entry.Form, checked(entry.Count * multiplier), entry.Extra, entry.ExtraPresent, local);
                }
                // Each native resolver level builds its own temporary
                // container, then prepends/merges that traversal into its
                // caller. Nested lists therefore cannot use one flat reverse.
                if (nativeContainerOrder)
                    foreach (var addition in local) MergeFirst(output, addition);
            }
            finally { active.Remove(key); }
        }
    }
}
