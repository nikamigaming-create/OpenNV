using OpenNV.Runtime.Content.Definitions;

namespace OpenNV.Runtime.World.Actors;

internal enum FalloutActorValueRead { Base, Permanent, Current }
internal enum FalloutActorValuePool { Permanent, Temporary, Damage }

internal sealed record FalloutActorValue(float Base, float Permanent = 0, float Temporary = 0, float Damage = 0)
{
    internal static string SourceSlot(int value) => SourceSlot((FalloutActorValueId)value);
    private static string SourceSlot(FalloutActorValueId id) =>
        id is >= FalloutActorValueId.Strength and <= FalloutActorValueId.Luck or
            >= FalloutActorValueId.Barter and <= FalloutActorValueId.Unarmed or FalloutActorValueId.RadiationRads ||
            FalloutActorValueNames.IsUser(id) || OpenNV.Runtime.Content.FalloutActorAiData.Contains(id) ? FalloutActorValueNames.Name(id) :
            throw new NotSupportedException($"Actor value {id} has no source base/formula owner.");
    internal static string SourceSlot(string name) => SourceSlot(FalloutActorValueNames.Parse(name));
    internal static int SourceIndex(string name)
    {
        var id = FalloutActorValueNames.Parse(SourceSlot(name));
        return FalloutActorValueNames.IsUser(id) ? -1 : (int)id;
    }
    internal float Current => Base + Permanent + Temporary + Damage;
    internal bool IsFinite => float.IsFinite(Base) && float.IsFinite(Permanent) && float.IsFinite(Temporary) &&
        float.IsFinite(Damage) && float.IsFinite(Current);

    internal static FalloutActorValueRead? Query(string method) => method.ToLowerInvariant() switch
    {
        "getav" or "getactorvalue" => FalloutActorValueRead.Current,
        "getbaseav" or "getbaseactorvalue" => FalloutActorValueRead.Base,
        "getpermanentactorvalue" => FalloutActorValueRead.Permanent,
        _ => null,
    };

    // The engine's ten user-defined actor values have no NPC/CREA base-data
    // field or derived formula. Their initial base and modifier pools are zero.
    internal static string UserSlot(string name) => UserSlot((int)FalloutActorValueNames.Parse(name));
    internal static string UserSlot(int value) => FalloutActorValueNames.IsUser((FalloutActorValueId)value)
        ? FalloutActorValueNames.Name((FalloutActorValueId)value)
        : throw new NotSupportedException($"Actor value slot {value} is not a user-defined value.");
}
