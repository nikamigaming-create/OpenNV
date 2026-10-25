namespace OpenNV.Runtime.World.Actors;

internal enum FalloutActorValueRead { Base, Permanent, Current }
internal enum FalloutActorValuePool { Permanent, Temporary, Damage }

internal sealed record FalloutActorValue(float Base, float Permanent = 0, float Temporary = 0, float Damage = 0)
{
    private static readonly string[] SpecialNames = ["strength", "perception", "endurance", "charisma", "intelligence", "agility", "luck"];
    private static readonly string[] SkillNames = ["barter", "bigguns", "energyweapons", "explosives", "lockpick", "medicine", "meleeweapons", "repair", "science", "smallguns", "sneak", "speech", "survival", "unarmed"];
    internal static string SourceSlot(int value) => value is >= 5 and <= 11 ? SpecialNames[value - 5] :
        value is >= 32 and <= 45 ? SkillNames[value - 32] : value == 54 ? "radiationrads" : UserSlot(value);
    internal static string SourceSlot(string name)
    {
        var canonical = name.ToLowerInvariant();
        if (canonical == "radiationrads") return canonical;
        if (canonical == "guns") canonical = "smallguns";
        return SpecialNames.Contains(canonical) || SkillNames.Contains(canonical) ? canonical : UserSlot(canonical);
    }
    internal static int SourceIndex(string name)
    {
        var canonical = SourceSlot(name);
        if (canonical == "radiationrads") return 54;
        var special = Array.IndexOf(SpecialNames, canonical);
        if (special >= 0) return special + 5;
        var skill = Array.IndexOf(SkillNames, canonical);
        return skill >= 0 ? skill + 32 : -1;
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
    internal static string UserSlot(string name)
    {
        var canonical = name.ToLowerInvariant();
        if (canonical.Length != 10 || !canonical.StartsWith("variable", StringComparison.Ordinal) ||
            canonical[8..] is not ("01" or "02" or "03" or "04" or "05" or "06" or "07" or "08" or "09" or "10"))
            throw new NotSupportedException($"Actor value {name} has no source base/formula owner.");
        return canonical;
    }

    internal static string UserSlot(int value) => value is >= 62 and <= 71
        ? "variable" + (value - 61).ToString("D2", System.Globalization.CultureInfo.InvariantCulture)
        : throw new NotSupportedException($"Actor value slot {value} is not a user-defined value.");
}
