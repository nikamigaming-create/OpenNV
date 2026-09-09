using System.Buffers.Binary;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal readonly record struct FalloutActorDefense(float Threshold, float Resistance)
{
    internal float Absorb(float damage, float minimumFraction) => Math.Max(damage * minimumFraction,
        damage * (1 - Math.Clamp(Resistance, 0, 85) / 100) - Math.Max(0, Threshold));
}

internal sealed partial class FalloutReferenceWorld
{
    private readonly Dictionary<FalloutFormKey, FalloutActorDefense> _defenseSources = [];

    internal FalloutActorDefense Defense(FalloutFormKey reference, int level, FalloutGlobalState globals)
    {
        var actor = Actor(reference);
        if (EquippedArmor(reference, level, globals).Count != 0)
            throw new NotSupportedException("Equipped armor requires its damage-resistance owner.");
        if (!_defenseSources.TryGetValue(actor.Base, out var source))
        {
            var effects = FalloutActorTemplateOwner.Resolve(records, records.GetEffective(actor.Base), 8);
            var abilities = new FalloutAbilityModifiers(records);
            var threshold = 0f; var resistance = 0f;
            foreach (var field in effects.ReadSubrecords().Where(field => field.Signature == "SPLO"))
            {
                if (field.Data.Length != 4) throw new InvalidDataException("Actor ability identity extent is invalid.");
                var spell = effects.Plugin.AdjustOptionalFormId(BinaryPrimitives.ReadUInt32LittleEndian(field.Data.Span));
                if (spell is null) continue;
                foreach (var effect in abilities.Spell(spell.Value))
                {
                    if (effect.ActorValue is not (12 or 76)) continue;
                    if (effect.Conditions.Count != 0) throw new NotSupportedException("Conditional actor resistance requires its condition owner.");
                    if (effect.ActorValue == 76) threshold += effect.Amount; else resistance += effect.Amount;
                }
            }
            source = new(threshold, resistance); _defenseSources.Add(actor.Base, source);
        }
        return source;
    }
}
