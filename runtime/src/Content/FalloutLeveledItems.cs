using System.Buffers.Binary;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutItemVariant(int Count, float? Condition = null, FalloutFormKey? Owner = null,
    FalloutFormKey? Global = null, int? FactionRank = null);
internal sealed record FalloutItemAddition(FalloutFormKey Form, FalloutItemVariant Variant);

internal static class FalloutLeveledItems
{
    private sealed record Entry(ushort Level, FalloutFormKey Form, ushort Count, FalloutItemVariant Extra);

    internal static IReadOnlyList<FalloutItemAddition> Resolve(FalloutPluginStack records, FalloutFormKey form, int count,
        int level, Func<uint, uint> random, Func<FalloutFormKey, float>? global = null, FalloutItemVariant? extra = null)
    {
        if (count <= 0 || level < 1) throw new ArgumentOutOfRangeException(nameof(count));
        var result = new List<FalloutItemAddition>();
        var active = new HashSet<FalloutFormKey>();
        Expand(form, count, extra ?? new(1));
        return result;

        void Expand(FalloutFormKey key, int amount, FalloutItemVariant inherited)
        {
            var record = records.GetEffective(key);
            if (record.Signature != "LVLI")
            {
                if (record.Signature is not ("ALCH" or "AMMO" or "ARMO" or "BOOK" or "CCRD" or "CHIP" or "CMNY" or "IMOD" or "KEYM" or "MISC" or "NOTE" or "WEAP"))
                    throw new NotSupportedException($"Inventory addition {key} is {record.Signature}.");
                result.Add(new(key, inherited with { Count = amount }));
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
                        entries.Add(new(BinaryPrimitives.ReadUInt16LittleEndian(bytes), record.Plugin.AdjustFormId(BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..])), quantity, inherited));
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
                        entries[^1] = entries[^1] with
                        {
                            Extra = new(1, condition, owner,
                            ownerType == "NPC_" ? record.Plugin.AdjustOptionalFormId(argument) : null,
                            ownerType == "FACT" ? unchecked((int)argument) : null)
                        };
                    }
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
                for (var roll = 0; roll < rolls; roll++)
                {
                    if (chance > 0 && random(100) < chance) continue;
                    var selected = (flags & 4) != 0 ? eligible : [eligible[checked((int)random((uint)eligible.Length))]];
                    foreach (var entry in selected) Expand(entry.Form, checked(entry.Count * multiplier), entry.Extra);
                }
            }
            finally { active.Remove(key); }
        }
    }
}
