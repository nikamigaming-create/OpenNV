using System.Buffers.Binary;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal readonly record struct FalloutActorDefense(float Threshold, float Resistance)
{
    internal float Absorb(float damage, float minimumFraction, IReadOnlyList<FalloutAmmoEffect>? ammoEffects = null)
    {
        var threshold = Threshold;
        var resistance = Resistance;
        if (ammoEffects is not null)
            foreach (var effect in ammoEffects)
            {
                if (effect.Type == FalloutAmmoEffect.DamageResistance) resistance = effect.Apply(resistance);
                else if (effect.Type == FalloutAmmoEffect.DamageThreshold) threshold = effect.Apply(threshold);
            }
        return Math.Max(damage * minimumFraction,
            damage * (1 - Math.Clamp(resistance, 0, 85) / 100) - Math.Max(0, threshold));
    }
}

internal sealed partial class FalloutReferenceWorld
{
    private readonly FalloutActorDefenseResolver _actorDefense = new(records);
    internal FalloutActorDefense Defense(FalloutFormKey reference, int level, FalloutGlobalState globals)
    {
        var actor = Actor(reference);
        var equipped = EquippedArmor(reference, level, globals);
        var result = _actorDefense.Read(actor.Base, actor.Inventory!.Contents, equipped, actor.Templates);
        foreach (var effect in PerkModifiers(reference).Where(effect => effect.ActorValue is 12 or 76))
        {
            if (effect.Conditions.Count != 0) throw new NotSupportedException("Conditional acquired resistance requires its condition owner.");
            result = effect.ActorValue == 76 ? result with { Threshold = result.Threshold + effect.Amount } : result with { Resistance = result.Resistance + effect.Amount };
        }
        return result;
    }
}

internal sealed class FalloutActorDefenseResolver(FalloutPluginStack records)
{
    private readonly Dictionary<FalloutFormKey, FalloutActorDefense> _defenseSources = [];
    private readonly Dictionary<FalloutFormKey, FalloutArmorDefense> _armorDefense = [];
    private readonly Dictionary<FalloutFormKey, FalloutActorDefense> _armorEffects = [];
    private readonly FalloutAbilityModifiers _defenseAbilities = new(records);
    private (float Base, float Maximum)? _armorRating;

    internal FalloutActorDefense Read(FalloutFormKey actor, FalloutPlayerInventory inventory, IReadOnlyList<FalloutFormKey> equipped,
        FalloutActorTemplateSelection? selection = null)
    {
        var effects = FalloutActorTemplateOwner.Resolve(records, records.GetEffective(actor), 8, selection);
        if (!_defenseSources.TryGetValue(effects.FormKey, out var source))
        {
            var threshold = 0f; var resistance = 0f;
            foreach (var field in effects.ReadSubrecords().Where(field => field.Signature == "SPLO"))
            {
                if (field.Data.Length != 4) throw new InvalidDataException("Actor ability identity extent is invalid.");
                var spell = effects.Plugin.AdjustOptionalFormId(BinaryPrimitives.ReadUInt32LittleEndian(field.Data.Span));
                if (spell is null) continue;
                if (!_defenseAbilities.TryGetConstantModifiers(spell.Value, out var modifiers)) continue;
                foreach (var effect in modifiers)
                {
                    if (effect.ActorValue is not (12 or 76)) continue;
                    if (effect.Conditions.Count != 0) throw new NotSupportedException("Conditional actor resistance requires its condition owner.");
                    if (effect.ActorValue == 76) threshold += effect.Amount; else resistance += effect.Amount;
                }
            }
            source = new(threshold, resistance); _defenseSources.Add(effects.FormKey, source);
        }
        var armorThreshold = 0f; var armorResistance = 0f;
        foreach (var key in equipped)
        {
            if (!_armorDefense.TryGetValue(key, out var armor))
                _armorDefense.Add(key, armor = FalloutArmorDefense.Read(records.GetEffective(key)));
            var item = inventory.Item(key)!;
            var condition = FalloutWeaponCondition.SelectedCondition(item);
            var rating = _armorRating ??= (FalloutGameSettingFloats.Read(records, "fArmorRatingBase"), FalloutGameSettingFloats.Read(records, "fArmorRatingMax"));
            var factor = rating.Base + condition * (rating.Maximum - rating.Base);
            armorThreshold += armor.Threshold * factor;
            armorResistance += armor.Resistance * factor;
            if (!_armorEffects.TryGetValue(key, out var modifiers))
            {
                var armorRecord = records.GetEffective(key);
                var threshold = 0f; var resistance = 0f;
                foreach (var field in armorRecord.ReadSubrecords().Where(field => field.Signature == "EITM"))
                {
                    if (field.Data.Length != 4) throw new InvalidDataException("Armor effect identity extent is invalid.");
                    var form = armorRecord.Plugin.AdjustOptionalFormId(BinaryPrimitives.ReadUInt32LittleEndian(field.Data.Span));
                    if (form is null) continue;
                    if (!_defenseAbilities.TryGetConstantModifiers(form.Value, out var armorModifiers)) continue;
                    foreach (var effect in armorModifiers)
                    {
                        if (effect.ActorValue is not (12 or 76)) continue;
                        if (effect.Conditions.Count != 0) throw new NotSupportedException("Conditional armor resistance requires its condition owner.");
                        if (effect.ActorValue == 76) threshold += effect.Amount; else resistance += effect.Amount;
                    }
                }
                modifiers = new(threshold, resistance); _armorEffects.Add(key, modifiers);
            }
            armorThreshold += modifiers.Threshold; armorResistance += modifiers.Resistance;
        }
        return new(source.Threshold + armorThreshold, source.Resistance + armorResistance);
    }
}
