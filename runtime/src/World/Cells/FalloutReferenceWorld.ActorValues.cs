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

    internal float ActorValue(FalloutFormKey reference, string name) =>
        name.Equals("health", StringComparison.OrdinalIgnoreCase) ? Health(reference).Current :
            Actor(reference).ActorValues.GetValueOrDefault(FalloutActorValue.UserSlot(name), new(0)).Current;

    internal bool IsUnconscious(FalloutFormKey reference) => Actor(reference).Unconscious;
    internal void SetUnconscious(FalloutFormKey reference, bool unconscious) => Actor(reference).Unconscious = unconscious;

    internal void ChangeActorValue(FalloutFormKey reference, string name, string operation, float value)
    {
        var instance = Actor(reference);
        var slot = FalloutActorValue.UserSlot(name);
        var previous = instance.ActorValues.GetValueOrDefault(slot, new(0));
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
