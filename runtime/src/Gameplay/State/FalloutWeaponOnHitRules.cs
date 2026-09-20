using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal static class FalloutWeaponOnHitRules
{
    internal const uint Normal = 0;
    internal const uint DismemberOnly = 1;
    internal const uint ExplodeOnly = 2;
    internal const uint NoDismemberOrExplode = 3;

    internal static bool AllowsDismember(uint behavior, FalloutBodyPart part) => behavior switch
    {
        Normal or DismemberOnly => (part.Flags & 0x01) != 0,
        ExplodeOnly or NoDismemberOrExplode => false,
        _ => throw new NotSupportedException($"Weapon On Hit behavior {behavior} is unbound.")
    };

    internal static bool ShouldDismember(uint behavior, FalloutBodyPart part, int chancePercent, float randomUnit)
    {
        if (!AllowsDismember(behavior, part)) return false;
        if (chancePercent is < 0 or > 100 || !float.IsFinite(randomUnit) || randomUnit is < 0 or >= 1)
            throw new InvalidDataException("Source weapon dismemberment chance or random sample is invalid.");
        return randomUnit * 100 < chancePercent;
    }
}
