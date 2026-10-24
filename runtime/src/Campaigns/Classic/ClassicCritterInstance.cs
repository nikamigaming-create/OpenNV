using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Campaigns.Classic;

/// <summary>The eleven serialized MAP critter words, distinct from the in-memory combat struct.</summary>
internal sealed record ClassicCritterInstance(int Reaction, int DamageLastTurn, int Maneuver, int ActionPoints,
    int Results, int AiPacket, int Team, int WhoHitMe, int HitPoints, int Radiation, int Poison)
{
    internal bool Dead => HitPoints <= 0 || (Results & 0x80) != 0;

    internal static int ScriptState(int results, int hitPoints, int animation)
    {
        if ((results & 0x8081) != 0) return hitPoints <= 0 || (results & 0x80) != 0 ? 1 : 2;
        return (animation is 48 or 49 ? 2 : 0) | (results & 0x7c);
    }

    internal static ClassicCritterInstance Read(Fallout1NativeMapObject source)
    {
        if (source.Prototype.ObjectType != 1 || source.InstanceValues.Count != 11)
            throw new InvalidDataException("MAP critter requires its eleven source instance words.");
        var words = source.InstanceValues;
        return new(words[0], words[1], words[2], words[3], words[4], words[5], words[6],
            words[7], words[8], words[9], words[10]);
    }
}
