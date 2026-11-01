using OpenNV.Runtime.Content;
using OpenNV.Runtime.Content.Definitions;
using OpenNV.Runtime.World.Actors;
using System.Buffers.Binary;

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

    internal float ActorValue(FalloutFormKey reference, string name) => ActorValue(reference, name, FalloutActorValueRead.Current);

    internal float ActorValue(FalloutFormKey reference, string name, FalloutActorValueRead read)
    {
        if (name.Equals("health", StringComparison.OrdinalIgnoreCase)) return Select(Health(reference), read);
        var actor = Actor(reference);
        if (LimbActorValue(name) is { } limb)
        {
            if (read != FalloutActorValueRead.Current) throw new NotSupportedException("Limb base/permanent queries require their anatomy value contract.");
            return BodyParts(reference).LimbCondition(limb, Health(reference).Base, actor.Injury?.LimbDamage);
        }
        var slot = FalloutActorValue.SourceSlot(name);
        return Select(actor.ActorValues.GetValueOrDefault(slot, new(SourceActorValue(actor, slot))), read);
    }

    private static float Select(FalloutActorValue value, FalloutActorValueRead read) => read switch
    {
        FalloutActorValueRead.Base => value.Base,
        FalloutActorValueRead.Permanent => value.Base + value.Permanent,
        FalloutActorValueRead.Current => value.Current,
        _ => throw new ArgumentOutOfRangeException(nameof(read)),
    };

    private float SourceActorValue(FalloutReferenceInstance actor, string slot)
    {
        var index = FalloutActorValue.SourceIndex(slot);
        if (FalloutActorAiData.Contains((FalloutActorValueId)index))
            return FalloutActorAiData.Read(records, actor.Base, actor.Templates).Value((FalloutActorValueId)index);
        if (index == 54) return 0; // Accumulated radiation starts in an empty engine value pool.
        if (index < 0) return 0; // Declared user slots have zero initial pools.
        var source = FalloutActorTemplateOwner.Resolve(records, records.GetEffective(actor.Base), 2, actor.Templates);
        if (index == (int)FalloutActorValueId.SpeedMult)
        {
            var configuration = source.ReadSubrecords().Single(field => field.Signature == "ACBS").Data.Span;
            if (configuration.Length != 24) throw new InvalidDataException("Actor speed multiplier ACBS extent is invalid.");
            return BinaryPrimitives.ReadUInt16LittleEndian(configuration[14..]);
        }
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

    internal FalloutActorThreat ActorThreat(FalloutFormKey reference, FalloutActorThreat? source = null)
    {
        var actor = Actor(reference);
        source ??= FalloutActorThreat.Read(records, actor.Base, actor.Templates);
        return source with
        {
            Aggression = Value(FalloutActorValueId.Aggression, source.Aggression, 3),
            Confidence = Value(FalloutActorValueId.Confidence, source.Confidence, 4),
            Energy = Value(FalloutActorValueId.Energy, source.Energy, byte.MaxValue),
            Assistance = Value(FalloutActorValueId.Assistance, source.Assistance, 2),
        };

        byte Value(FalloutActorValueId id, byte original, byte maximum)
        {
            var value = actor.ActorValues.TryGetValue(FalloutActorValueNames.Name(id), out var pool) ? pool.Current : original;
            if (!float.IsFinite(value) || value != MathF.Truncate(value) || value < 0 || value > maximum)
                throw new NotSupportedException($"AI behavior for {id} value {value} is not qualified.");
            return (byte)value;
        }
    }

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
            var health = Health(reference);
            var injury = actor.Injury!;
            var damage = FalloutAnatomicalActorValues.Change(BodyParts(reference), limb, health.Base,
                injury.LimbDamage, FalloutActorValueOperation.Restore, value);
            actor.Injury = injury with { LimbDamage = damage ?? injury.LimbDamage };
        }
        else
        {
            var slot = FalloutActorValue.SourceSlot(name);
            var previous = actor.ActorValues.GetValueOrDefault(slot, new(SourceActorValue(actor, slot)));
            actor.ActorValues[slot] = FalloutActorValueCommands.Apply(previous,
                FalloutActorValueOperation.Restore, value, previous.Current);
        }
    }

    internal bool IsUnconscious(FalloutFormKey reference) => Actor(reference).Unconscious;
    internal void SetUnconscious(FalloutFormKey reference, bool unconscious) => Actor(reference).Unconscious = unconscious;
    internal void SetRestrained(FalloutFormKey reference, bool restrained) => Actor(reference).Restrained = restrained;
    internal void SetPlayerTeammate(FalloutFormKey reference, bool teammate) => Actor(reference).PlayerTeammate = teammate;

    internal void ChangeActorValue(FalloutFormKey reference, string name, string operation, double argument)
    {
        var instance = Actor(reference);
        var value = (float)FalloutActorValueCommands.Argument(operation, argument);
        var kind = FalloutActorValueCommands.Operation(operation) ?? throw new NotSupportedException($"Actor value operation {operation} is unbound.");
        if (kind == FalloutActorValueOperation.Restore)
        {
            RestoreActorValue(reference, name, value);
            return;
        }
        if (LimbActorValue(name) is { } limb)
        {
            var health = Health(reference);
            var injury = instance.Injury!;
            var damage = FalloutAnatomicalActorValues.Change(BodyParts(reference), limb, health.Base,
                injury.LimbDamage, kind, value);
            instance.Injury = injury with { LimbDamage = damage ?? injury.LimbDamage };
            return;
        }
        var slot = FalloutActorValueNames.Parse(name) == FalloutActorValueId.Health
            ? "health" : FalloutActorValue.SourceSlot(name);
        if (slot == "health" && kind != FalloutActorValueOperation.Damage)
            throw new NotSupportedException("Script health base/permanent mutation requires its derived-health contract.");
        FalloutActorValue previous = slot == "health" ? Health(reference) :
            instance.ActorValues.TryGetValue(slot, out var retained) ? retained : new(SourceActorValue(instance, slot));
        var changed = FalloutActorValueCommands.Apply(previous, kind, value, previous.Current);
        if (slot == "health" && (instance.Injury?.Dead == true || changed.Current <= 0))
            throw new NotSupportedException("Script lethal/dead health mutation requires its death-event and killer contract.");
        instance.ActorValues[slot] = changed;
    }
}
