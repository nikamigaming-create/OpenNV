namespace OpenNV.Runtime.World.Actors;

internal enum FalloutActorValueRead { Base, Permanent, Current }
internal enum FalloutActorValuePool { Permanent, Temporary, Damage }

internal sealed record FalloutActorValue(float Base, float Permanent = 0, float Temporary = 0, float Damage = 0)
{
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
