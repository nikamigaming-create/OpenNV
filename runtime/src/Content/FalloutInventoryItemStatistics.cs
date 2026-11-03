using System.Buffers.Binary;
using System.Globalization;

namespace OpenNV.Runtime.Content;

internal static class FalloutInventoryItemStatistics
{
    internal static float RoundIncrement(float value, float increment)
    {
        if (!float.IsFinite(increment) || increment <= 0)
            throw new InvalidDataException("Inventory rounding increment is invalid.");
        var quotient = value / increment;
        if (!float.IsFinite(quotient) || quotient < 0 || (double)quotient >= int.MaxValue)
            throw new NotSupportedException("Inventory statistic exceeds source rounding storage.");
        var integral = (int)quotient;
        return (float)((double)checked(integral + ((double)quotient - integral >= .5 ? 1 : 0)) * increment);
    }

    internal static float ConditionValue(float value, float condition, float basis, float multiplier, float exponent)
    {
        if (!float.IsFinite(value) || value < 0 || !float.IsFinite(condition) || condition is < 0 or > 1 ||
            !float.IsFinite(basis) || !float.IsFinite(multiplier) || !float.IsFinite(exponent))
            throw new InvalidDataException("Inventory condition value inputs are invalid.");
        var conditionTen = (float)((condition * 100f) / 10d);
        var result = value > 0
            ? (float)((double)value * basis + ((1d - basis) * value * multiplier) * (float)Math.Pow(conditionTen, exponent))
            : value;
        if (!float.IsFinite(result) || result < 0) throw new InvalidDataException("Inventory condition value is invalid.");
        return result >= 1 ? RoundIncrement(result, 1) : result;
    }

    internal static string Value(FalloutPluginStack records, FalloutCampaignItem item, FalloutItemVariant extra)
    {
        if (item.RecordType is "ALCH" or "INGR")
            throw new NotSupportedException("Magic-item value requires its effective source effect-cost owner.");
        var sourceValue = item.Value ?? throw new NotSupportedException("Item has no source value owner.");
        var condition = item.RecordType is "WEAP" or "ARMO" ? extra.Condition ?? 1 : 1;
        var value = ConditionValue(sourceValue, condition,
            records.NumericSettings.Float("fItemConditionValueBase"),
            records.NumericSettings.Float("fItemConditionValueMult"),
            records.NumericSettings.Float("fItemConditionValueExp"));
        if (extra.WeaponMods != 0)
        {
            if (item.RecordType != "WEAP" || (extra.WeaponMods & ~7) != 0)
                throw new InvalidDataException("Inventory value has an invalid weapon modification mask.");
            var installed = FalloutWeaponModifications.Read(records, item.FormKey)
                .Where(mod => (extra.WeaponMods & (1 << mod.Slot)) != 0).OrderBy(mod => mod.Slot).ToArray();
            if (installed.Aggregate(0, (mask, mod) => mask | (1 << mod.Slot)) != extra.WeaponMods)
                throw new InvalidDataException("Inventory value modification has no winning source slot.");
            foreach (var mod in installed)
            {
                var data = records.GetEffective(mod.Item).ReadSubrecords().Single(field => field.Signature == "DATA").Data.Span;
                if (data.Length != 8) throw new NotSupportedException("IMOD value DATA extent is unbound.");
                var cost = BinaryPrimitives.ReadInt32LittleEndian(data);
                if (cost < 0) throw new InvalidDataException("Installed IMOD value is negative.");
                value = (float)((double)value + cost);
            }
        }
        value = RoundIncrement(value, .1f);
        if (extra.Count <= 0) throw new InvalidDataException("Selected value entry has no quantity.");
        var total = (double)value * extra.Count;
        return value <= 0 ? "--" : total.ToString(value >= 1 ? "F0" : "F1", CultureInfo.InvariantCulture);
    }

    internal static FalloutFormKey? DefaultAmmunition(FalloutPluginStack records, FalloutFormKey weapon, IFalloutFormLists? lists)
    {
        var source = records.GetEffective(weapon);
        if (source.Signature != "WEAP") throw new InvalidDataException("Inventory ammunition requires WEAP.");
        var field = source.ReadSubrecords().SingleOrDefault(field => field.Signature == "NAM0").Data;
        if (field.IsEmpty) return null;
        if (field.Length != 4) throw new InvalidDataException("WEAP ammunition reference has invalid extent.");
        var form = source.Plugin.AdjustOptionalFormId(BinaryPrimitives.ReadUInt32LittleEndian(field.Span));
        if (form is null) return null;
        if (records.GetEffective(form.Value).Signature == "FLST")
        {
            var original = FalloutFormList.Read(records, form.Value).Members;
            // Added members precede originals. The native default skips them
            // when any original member remains; an originally empty list uses
            // its actual first current member instead.
            var members = original.Count != 0 ? original : lists?.Entries(form.Value) ?? original;
            if (members.Count == 0 || members[0] is not { } member)
                throw new NotSupportedException("Default ammunition list has no actual first AMMO.");
            form = member;
        }
        if (records.GetEffective(form.Value).Signature != "AMMO")
            throw new NotSupportedException("Default ammunition does not resolve to AMMO.");
        return form;
    }

    internal static string AmmoName(FalloutPluginStack records, FalloutFormKey ammunition)
    {
        var source = records.GetEffective(ammunition);
        if (source.Signature != "AMMO") throw new InvalidDataException("Inventory ammo label requires AMMO.");
        var names = source.ReadSubrecords().Where(field => field.Signature == "ONAM").ToArray();
        if (names.Length > 1) throw new InvalidDataException("AMMO short name repeats.");
        var name = names.Length == 0 || names[0].Data.IsEmpty ? "" : FalloutDialogueTopic.Text(names[0].Data.Span);
        return name.Length == 0 ? FalloutRecordDisplayName.Read(source) : name;
    }
}
