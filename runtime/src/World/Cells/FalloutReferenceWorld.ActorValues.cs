using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Actors;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    private FalloutReferenceInstance Actor(FalloutFormKey reference)
    {
        var instance = Get(reference);
        if (records.GetEffective(instance.Base).Signature is not ("NPC_" or "CREA"))
            throw new InvalidDataException($"Actor value target {reference} is not an actor.");
        return instance;
    }

    internal float ActorValue(FalloutFormKey reference, string name)
    {
        if (name.Equals("health", StringComparison.OrdinalIgnoreCase)) return Health(reference).Current;
        var actor = Actor(reference);
        if (name.Equals("aggression", StringComparison.OrdinalIgnoreCase)) return actor.ActorValues.GetValueOrDefault("aggression",
            new(FalloutActorThreat.Read(records, actor.Base, actor.Templates).Aggression)).Current;
        if (LimbActorValue(name) is { } limb) return BodyParts(reference).LimbCondition(limb, Health(reference).Base, actor.Injury?.LimbDamage);
        var slot = FalloutActorValue.SourceSlot(name);
        return actor.ActorValues.GetValueOrDefault(slot, new(SourceActorValue(actor, slot))).Current;
    }

    private float SourceActorValue(FalloutReferenceInstance actor, string slot)
    {
        var index = FalloutActorValue.SourceIndex(slot);
        if (index == 54) return 0; // Accumulated radiation starts in an empty engine value pool.
        if (index < 0) return 0; // Declared user slots have zero initial pools.
        var source = FalloutActorTemplateOwner.Resolve(records, records.GetEffective(actor.Base), 2, actor.Templates);
        var data = source.ReadSubrecords().Single(field => field.Signature == "DATA").Data.Span;
        if (source.Signature == "NPC_")
        {
            if (data.Length != 11) throw new InvalidDataException("NPC actor-value DATA extent is invalid.");
            if (index is >= 5 and <= 11) return data[index - 1];
            var skills = source.ReadSubrecords().Single(field => field.Signature == "DNAM").Data.Span;
            if (skills.Length != 28) throw new InvalidDataException("NPC actor-value skill extent is invalid.");
            return skills[index - 32];
        }
        if (source.Signature != "CREA" || data.Length != 17) throw new InvalidDataException("Creature actor-value DATA extent is invalid.");
        if (index is >= 5 and <= 11) return data[index + 5];
        if (index is 33 or 34 or 35 or 38 or 41 or 45) return data[1];
        // These noncombat skills have an empty native creature base pool;
        // they do not borrow combatSkill or an NPC's DNAM table.
        if (index is 37 or 44) return 0;
        throw new NotSupportedException($"Creature {slot} requires its source skill owner.");
    }

    private static int? LimbActorValue(string name) => FalloutBodyPartData.LimbActorValue(name);

    internal void ResetHealth(FalloutFormKey reference)
    {
        var health = Health(reference);
        if (IsDead(reference)) throw new NotSupportedException("ResetHealth cannot substitute for resurrection.");
        var actor = Actor(reference);
        actor.ActorValues["health"] = health with { Damage = 0 };
        if (actor.Injury is { } injury) actor.Injury = injury with { LimbDamage = new Dictionary<byte, float>() };
    }

    internal void RestoreActorValue(FalloutFormKey reference, string name, float value)
    {
        if (!float.IsFinite(value) || value < 0) throw new InvalidDataException("Actor restore amount is invalid.");
        var actor = Actor(reference);
        if (name.Equals("health", StringComparison.OrdinalIgnoreCase))
        {
            var health = Health(reference);
            if (IsDead(reference)) return;
            actor.ActorValues["health"] = health with { Damage = Math.Min(0, health.Damage + value) };
        }
        else if (LimbActorValue(name) is { } limb)
        {
            var parts = BodyParts(reference).Parts.Where(part => part.ActorValue == limb && part.HealthPercent > 0).ToArray();
            if (parts.Length == 0) return;
            if (parts.Length != 1) throw new NotSupportedException("Actor limb restore has ambiguous source anatomy.");
            var part = parts[0];
            var health = Health(reference);
            var damage = new Dictionary<byte, float>(actor.Injury!.LimbDamage);
            damage[part.Type] = Math.Max(0, damage.GetValueOrDefault(part.Type) - value * health.Base * part.HealthPercent / 10000);
            actor.Injury = actor.Injury with { LimbDamage = damage };
        }
        else throw new NotSupportedException($"Actor restore value {name} is unbound.");
    }

    internal bool IsUnconscious(FalloutFormKey reference) => Actor(reference).Unconscious;
    internal void SetUnconscious(FalloutFormKey reference, bool unconscious) => Actor(reference).Unconscious = unconscious;
    internal void SetRestrained(FalloutFormKey reference, bool restrained) => Actor(reference).Restrained = restrained;
    internal void SetPlayerTeammate(FalloutFormKey reference, bool teammate) => Actor(reference).PlayerTeammate = teammate;

    internal void ChangeActorValue(FalloutFormKey reference, string name, string operation, float value)
    {
        var instance = Actor(reference);
        var slot = name.Equals("aggression", StringComparison.OrdinalIgnoreCase) ? "aggression" : FalloutActorValue.SourceSlot(name);
        var previous = instance.ActorValues.GetValueOrDefault(slot, new(ActorValue(reference, slot)));
        var changed = operation switch
        {
            "setav" or "setactorvalue" => previous with { Base = value },
            "modav" or "modactorvalue" => previous with { Permanent = previous.Permanent + value },
            "forceav" or "forceactorvalue" => previous with { Permanent = previous.Permanent + value - previous.Current },
            _ => throw new NotSupportedException($"Actor value operation {operation} is unbound."),
        };
        if (!changed.IsFinite) throw new InvalidDataException("Actor value exceeds Float32 storage.");
        instance.ActorValues[slot] = changed;
    }
}
