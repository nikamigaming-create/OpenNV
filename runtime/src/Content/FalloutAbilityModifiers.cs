using System.Buffers.Binary;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutAbilityModifier(FalloutFormKey Spell, FalloutFormKey Effect, int ActorValue,
    float Amount, IReadOnlyList<FalloutCondition> Conditions);
internal sealed record FalloutAbilityScript(FalloutFormKey Spell, FalloutFormKey Effect, FalloutFormKey Script,
    IReadOnlyList<FalloutCondition> Conditions);
internal sealed record FalloutPerkEntry(byte Entry, byte Function, float Value, IReadOnlyList<FalloutCondition> Conditions,
    IReadOnlyDictionary<byte, IReadOnlyList<FalloutCondition>>? ConditionGroups = null)
{
    internal void RequireActorConditionScope()
    {
        if (ConditionGroups?.Any(group => group.Key != 0 && group.Value.Count != 0) == true)
            throw new NotSupportedException($"Perk entry {Entry} requires weapon/target condition evaluation.");
    }
}

// Constant source abilities are evaluated against live state. Conditional
// modifiers are not baked into SPECIAL or saved a second time as base values.
internal sealed class FalloutAbilityModifiers(FalloutPluginStack records)
{
    private readonly Dictionary<FalloutFormKey, IReadOnlyList<FalloutAbilityModifier>> _spells = [];
    private readonly Dictionary<FalloutFormKey, IReadOnlyList<FalloutAbilityScript>> _scripts = [];
    private readonly Dictionary<FalloutFormKey, (FalloutFormKey[] Spells, FalloutPerkEntry[] Entries)> _perks = [];

    internal IReadOnlyList<FalloutAbilityModifier> Spell(FalloutFormKey form)
    {
        var result = ConstantModifiers(form);
        if (_scripts[form].Count != 0) throw new NotSupportedException($"Ability {form} requires its script lifecycle owner.");
        return result;
    }

    internal IReadOnlyList<FalloutAbilityScript> Scripts(FalloutFormKey form) { _ = ConstantModifiers(form); return _scripts[form]; }

    internal bool TryGetConstantModifiers(FalloutFormKey form, out IReadOnlyList<FalloutAbilityModifier> modifiers)
    {
        modifiers = [];
        if (_spells.TryGetValue(form, out var cached)) { modifiers = cached; return true; }
        if (!records.TryGetEffective(form, out var source) || source.Signature is not ("SPEL" or "ENCH"))
            return false;
        var fields = source.ReadSubrecords().ToArray();
        var declarationFields = fields.Where(field => field.Signature == (source.Signature == "SPEL" ? "SPIT" : "ENIT")).ToArray();
        if (declarationFields.Length != 1 || declarationFields[0].Data.Length != 16) return false;
        var declaration = declarationFields[0].Data.Span;
        if (BinaryPrimitives.ReadUInt32LittleEndian(declaration) != (source.Signature == "SPEL" ? 4u : 3u))
            return false;
        var result = new List<FalloutAbilityModifier>();
        var scripts = new List<FalloutAbilityScript>();
        for (var index = 0; index < fields.Length; index++)
        {
            if (fields[index].Signature != "EFID") continue;
            var effectId = fields[index].Data.Span;
            if (effectId.Length != 4) return false;
            var effectForm = source.Plugin.AdjustOptionalFormId(BinaryPrimitives.ReadUInt32LittleEndian(effectId));
            if (effectForm is null || !records.TryGetEffective(effectForm.Value, out var effect)) continue;
            var end = index + 1;
            while (end < fields.Length && fields[end].Signature != "EFID") end++;
            var group = fields[(index + 1)..end];
            var efitFields = group.Where(field => field.Signature == "EFIT").ToArray();
            if (efitFields.Length != 1 || efitFields[0].Data.Length != 20 || effect.Signature != "MGEF") continue;
            var data = efitFields[0].Data.Span;
            var defFields = effect.ReadSubrecords().Where(field => field.Signature == "DATA").ToArray();
            if (defFields.Length != 1 || defFields[0].Data.Length != 72) continue;
            var definition = defFields[0].Data.Span;
            var flags = BinaryPrimitives.ReadUInt32LittleEndian(definition);
            var archetype = BinaryPrimitives.ReadUInt32LittleEndian(definition[64..]);
            if (archetype == 1 && group.All(field => field.Signature is "EFIT" or "CTDA") &&
                BinaryPrimitives.ReadUInt32LittleEndian(data[4..]) == 0 && BinaryPrimitives.ReadUInt32LittleEndian(data[8..]) == 0 &&
                BinaryPrimitives.ReadUInt32LittleEndian(data[12..]) == 0)
            {
                var scriptId = effect.Plugin.AdjustOptionalFormId(BinaryPrimitives.ReadUInt32LittleEndian(definition[8..]));
                if (scriptId is not null && records.TryGetEffective(scriptId.Value, out var scpt) && scpt.Signature == "SCPT")
                {
                    scripts.Add(new(form, effect.FormKey, scriptId.Value, group.Where(field => field.Signature == "CTDA")
                        .Select(field => FalloutCondition.Read(source, field.Data.Span)).ToArray()));
                }
                index = end - 1;
                continue;
            }
            if (archetype == 0 && (flags & 2) != 0 && group.All(field => field.Signature is "EFIT" or "CTDA") &&
                BinaryPrimitives.ReadUInt32LittleEndian(data[4..]) == 0 && BinaryPrimitives.ReadUInt32LittleEndian(data[8..]) == 0 &&
                BinaryPrimitives.ReadUInt32LittleEndian(data[12..]) == 0)
            {
                var value = BinaryPrimitives.ReadInt32LittleEndian((flags & 0x180000) != 0 ? data[16..] : definition[68..]);
                var magnitude = BinaryPrimitives.ReadUInt32LittleEndian(data);
                result.Add(new(form, effect.FormKey, value, (flags & 4) == 0 ? magnitude : -(float)magnitude,
                    group.Where(field => field.Signature == "CTDA").Select(field => FalloutCondition.Read(source, field.Data.Span)).ToArray()));
            }
            index = end - 1;
        }
        _scripts.Add(form, scripts);
        _spells.Add(form, result);
        modifiers = result;
        return true;
    }

    internal IReadOnlyList<FalloutAbilityModifier> ConstantModifiers(FalloutFormKey form)
    {
        if (TryGetConstantModifiers(form, out var modifiers) && (modifiers.Count > 0 || _scripts.GetValueOrDefault(form)?.Count > 0))
            return modifiers;
        var source = records.GetEffective(form);
        if (source.Signature is not ("SPEL" or "ENCH")) throw new InvalidDataException("Constant actor effect is neither SPEL nor ENCH.");
        var fields = source.ReadSubrecords().ToArray();
        var declaration = fields.Single(field => field.Signature == (source.Signature == "SPEL" ? "SPIT" : "ENIT")).Data.Span;
        if (declaration.Length != 16 || BinaryPrimitives.ReadUInt32LittleEndian(declaration) != (source.Signature == "SPEL" ? 4 : 3))
            throw new NotSupportedException($"Actor effect {form} requires a timed/scripted effect owner.");
        if (modifiers.Count == 0 && _scripts.GetValueOrDefault(form)?.Count == 0)
            throw new InvalidDataException($"Ability {form} has no effects.");
        return modifiers;
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
                if (type.Length != 1 || type[0] != 1 || parameter.Length != 4 || data[2] == 0 ||
                    group.Any(field => field.Signature is not ("DATA" or "EPFT" or "EPFD" or "CTDA" or "PRKC")))
                    throw new NotSupportedException($"Perk {form} entry {data[0]} requires its condition/parameter owner.");
                var value = BinaryPrimitives.ReadSingleLittleEndian(parameter);
                if (!float.IsFinite(value)) throw new InvalidDataException("Perk value is not finite.");
                var conditions = new Dictionary<byte, List<FalloutCondition>>();
                byte tab = 0;
                foreach (var field in group)
                {
                    if (field.Signature == "PRKC")
                    {
                        if (field.Data.Length != 1 || field.Data.Span[0] >= data[2])
                            throw new InvalidDataException("Perk condition tab is outside its declared extent.");
                        tab = field.Data.Span[0];
                        if (!conditions.TryAdd(tab, [])) throw new InvalidDataException("Perk condition tab repeats.");
                    }
                    else if (field.Signature == "CTDA")
                    {
                        if (!conditions.TryGetValue(tab, out var list)) conditions.Add(tab, list = []);
                        list.Add(FalloutCondition.Read(source, field.Data.Span));
                    }
                }
                entries.Add(new(data[0], data[1], value, conditions.GetValueOrDefault((byte)0) ?? [],
                    conditions.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<FalloutCondition>)pair.Value.ToArray())));
            }
            else throw new NotSupportedException($"Perk {form} entry type {header[0]} is unbound.");
            index = end;
        }
        var result = (spells.ToArray(), entries.ToArray());
        _perks.Add(form, result);
        return result;
    }
}
