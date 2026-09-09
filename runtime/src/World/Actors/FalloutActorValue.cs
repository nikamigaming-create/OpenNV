namespace OpenNV.Runtime.World.Actors;

internal sealed record FalloutActorValue(float Base, float Permanent = 0, float Temporary = 0, float Damage = 0)
{
    internal float Current => Base + Permanent + Temporary + Damage;
    internal bool IsFinite => float.IsFinite(Base) && float.IsFinite(Permanent) && float.IsFinite(Temporary) &&
        float.IsFinite(Damage) && float.IsFinite(Current);

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
}
