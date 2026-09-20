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
        return actor.ActorValues.GetValueOrDefault(FalloutActorValue.UserSlot(name), new(0)).Current;
    }

    private static int? LimbActorValue(string name) => name.ToLowerInvariant() switch
    {
        "perceptioncondition" => 25,
        "endurancecondition" => 26,
        "leftattackcondition" => 27,
        "rightattackcondition" => 28,
        "leftmobilitycondition" => 29,
        "rightmobilitycondition" => 30,
        "braincondition" => 31,
        _ => null
    };

    internal void ResetHealth(FalloutFormKey reference)
    {
        var health = Health(reference);
        if (IsDead(reference)) throw new NotSupportedException("ResetHealth cannot substitute for resurrection.");
        Actor(reference).ActorValues["health"] = health with { Damage = 0 };
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
        var slot = name.Equals("aggression", StringComparison.OrdinalIgnoreCase) ? "aggression" : FalloutActorValue.UserSlot(name);
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
