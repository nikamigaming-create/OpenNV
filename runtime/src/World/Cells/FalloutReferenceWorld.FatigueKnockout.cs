using System.Buffers.Binary;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    // A missing AV47 pool is not evidence that an unowned source effect is
    // inactive. Qualify the actual persistent effect producers before waking.
    internal void RequireFatigueKnockoutEffects(FalloutFormKey reference, int level, FalloutGlobalState globals)
    {
        var actor = Actor(reference);
        var source = FalloutActorTemplateOwner.Resolve(records, records.GetEffective(actor.Base), 8, actor.Templates);
        foreach (var field in source.ReadSubrecords().Where(field => field.Signature == "SPLO"))
        {
            if (field.Data.Length != 4) throw new InvalidDataException("Actor knockout ability identity is invalid.");
            if (source.Plugin.AdjustOptionalFormId(BinaryPrimitives.ReadUInt32LittleEndian(field.Data.Span)) is { } spell) Require(spell);
        }
        foreach (var armor in EquippedArmor(reference, level, globals))
        {
            var item = records.GetEffective(armor);
            foreach (var field in item.ReadSubrecords().Where(field => field.Signature == "EITM"))
            {
                if (field.Data.Length != 4) throw new InvalidDataException("Actor knockout apparel effect identity is invalid.");
                if (item.Plugin.AdjustOptionalFormId(BinaryPrimitives.ReadUInt32LittleEndian(field.Data.Span)) is { } effect) Require(effect);
            }
        }
        if (PerkModifiers(reference).Any(modifier => modifier.ActorValue == 47) ||
            actor.ActorValues.ContainsKey("paralysis"))
            throw new NotSupportedException("Actor knockout paralysis requires its active effect owner.");
        if (actor.Ingestibles is { Failure: not null })
            throw new NotSupportedException("Actor knockout cannot qualify failed ingestible effects.");
        foreach (var active in actor.Ingestibles?.Effects ?? [])
        {
            var effect = FalloutIngestible.Read(records, active.Item).Effects.Single(value => value.Index == active.Index);
            if (effect.Archetype != 0 || effect.ActorValue == 47)
                throw new NotSupportedException("Actor knockout ingestible requires its active paralysis/effect owner.");
        }

        void Require(FalloutFormKey effect)
        {
            if (!_perkAbilities.TryGetConstantModifiers(effect, out var modifiers) || _perkAbilities.Scripts(effect).Count != 0 ||
                modifiers.Any(modifier => modifier.ActorValue == 47))
                throw new NotSupportedException($"Actor knockout effect {effect} requires its active paralysis/effect owner.");
        }
    }
}
