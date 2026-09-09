using System.Buffers.Binary;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutAbilityModifier(FalloutFormKey Spell, FalloutFormKey Effect, int ActorValue,
    float Amount, IReadOnlyList<FalloutCondition> Conditions);
internal sealed record FalloutPerkEntry(byte Entry, byte Function, float Value, IReadOnlyList<FalloutCondition> Conditions);

// Constant source abilities are evaluated against live state. Conditional
// modifiers are not baked into SPECIAL or saved a second time as base values.
internal sealed class FalloutAbilityModifiers(FalloutPluginStack records)
{
    private readonly Dictionary<FalloutFormKey, IReadOnlyList<FalloutAbilityModifier>> _spells = [];
    private readonly Dictionary<FalloutFormKey, (FalloutFormKey[] Spells, FalloutPerkEntry[] Entries)> _perks = [];

    internal IReadOnlyList<FalloutAbilityModifier> Spell(FalloutFormKey form)
    {
        if (_spells.TryGetValue(form, out var cached)) return cached;
        var source = records.GetEffective(form);
        if (source.Signature is not ("SPEL" or "ENCH")) throw new InvalidDataException("Constant actor effect is neither SPEL nor ENCH.");
        var fields = source.ReadSubrecords().ToArray();
        var declaration = fields.Single(field => field.Signature == (source.Signature == "SPEL" ? "SPIT" : "ENIT")).Data.Span;
        if (declaration.Length != 16 || BinaryPrimitives.ReadUInt32LittleEndian(declaration) != (source.Signature == "SPEL" ? 4 : 3))
            throw new NotSupportedException($"Actor effect {form} requires a timed/scripted effect owner.");
        var result = new List<FalloutAbilityModifier>();
        for (var index = 0; index < fields.Length; index++)
        {
            if (fields[index].Signature != "EFID") continue;
            var effectId = fields[index].Data.Span;
            if (effectId.Length != 4) throw new InvalidDataException("Ability EFID extent is invalid.");
            var effect = records.GetEffective(source.Plugin.AdjustFormId(BinaryPrimitives.ReadUInt32LittleEndian(effectId)));
            var end = index + 1;
            while (end < fields.Length && fields[end].Signature != "EFID") end++;
            var group = fields[(index + 1)..end];
            var data = group.Single(field => field.Signature == "EFIT").Data.Span;
            if (data.Length != 20 || effect.Signature != "MGEF") throw new InvalidDataException("Ability effect extent/type is invalid.");
            var definition = effect.ReadSubrecords().Single(field => field.Signature == "DATA").Data.Span;
            if (definition.Length != 72) throw new NotSupportedException($"MGEF {effect.FormKey} extent is unbound.");
            var flags = BinaryPrimitives.ReadUInt32LittleEndian(definition);
            var archetype = BinaryPrimitives.ReadUInt32LittleEndian(definition[64..]);
            if (archetype != 0 || (flags & 2) == 0 || group.Any(field => field.Signature is not ("EFIT" or "CTDA")) ||
                BinaryPrimitives.ReadUInt32LittleEndian(data[4..]) != 0 || BinaryPrimitives.ReadUInt32LittleEndian(data[8..]) != 0 ||
                BinaryPrimitives.ReadUInt32LittleEndian(data[12..]) != 0)
                throw new NotSupportedException($"Ability {form}/{effect.FormKey} requires its effect lifecycle/archetype owner.");
            var value = BinaryPrimitives.ReadInt32LittleEndian((flags & 0x180000) != 0 ? data[16..] : definition[68..]);
            var magnitude = BinaryPrimitives.ReadUInt32LittleEndian(data);
            result.Add(new(form, effect.FormKey, value, (flags & 4) == 0 ? magnitude : -(float)magnitude,
                group.Where(field => field.Signature == "CTDA").Select(field => FalloutCondition.Read(source, field.Data.Span)).ToArray()));
            index = end - 1;
        }
        if (result.Count == 0) throw new InvalidDataException($"Ability {form} has no effects.");
        _spells.Add(form, result);
        return result;
    }

    internal (FalloutFormKey[] Spells, FalloutPerkEntry[] Entries) Perk(FalloutFormKey form)
    {
        if (_perks.TryGetValue(form, out var cached)) return cached;
        var source = records.GetEffective(form);
        if (source.Signature != "PERK") throw new InvalidDataException("Acquired perk is not PERK.");
        var fields = source.ReadSubrecords().ToArray();
        var spells = new List<FalloutFormKey>(); var entries = new List<FalloutPerkEntry>();
        for (var index = 0; index < fields.Length; index++)
        {
            if (fields[index].Signature != "PRKE") continue;
            var header = fields[index].Data.Span;
            if (header.Length != 3) throw new InvalidDataException("Perk entry header extent is invalid.");
            if (header[1] != 0) throw new NotSupportedException($"Perk {form} requires acquired rank state.");
            var end = Array.FindIndex(fields, index + 1, field => field.Signature == "PRKF");
            if (end < 0 || fields[(index + 1)..end].Any(field => field.Signature == "PRKE")) throw new InvalidDataException("Perk entry is unterminated.");
            var group = fields[(index + 1)..end];
            var data = group.Single(field => field.Signature == "DATA").Data.Span;
            if (header[0] == 1 && data.Length == 4 && group.Length == 1)
                spells.Add(source.Plugin.AdjustFormId(BinaryPrimitives.ReadUInt32LittleEndian(data)));
            else if (header[0] == 2 && data.Length == 3)
            {
                var type = group.Single(field => field.Signature == "EPFT").Data.Span;
                var parameter = group.Single(field => field.Signature == "EPFD").Data.Span;
                if (type.Length != 1 || type[0] != 1 || parameter.Length != 4 || data[2] != 1 ||
                    group.Any(field => field.Signature is not ("DATA" or "EPFT" or "EPFD" or "CTDA")))
                    throw new NotSupportedException($"Perk {form} entry {data[0]} requires its condition/parameter owner.");
                var value = BinaryPrimitives.ReadSingleLittleEndian(parameter);
                if (!float.IsFinite(value)) throw new InvalidDataException("Perk value is not finite.");
                entries.Add(new(data[0], data[1], value,
                    group.Where(field => field.Signature == "CTDA").Select(field => FalloutCondition.Read(source, field.Data.Span)).ToArray()));
            }
            else throw new NotSupportedException($"Perk {form} entry type {header[0]} is unbound.");
            index = end;
        }
        var result = (spells.ToArray(), entries.ToArray());
        _perks.Add(form, result);
        return result;
    }
}
