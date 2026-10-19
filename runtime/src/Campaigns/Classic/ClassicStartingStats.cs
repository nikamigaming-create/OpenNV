using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Campaigns.Classic;

/// <summary>Level-one, unequipped classic stats; traits modify allocation once.</summary>
internal sealed record ClassicStartingStats(int[] Special, int HitPoints, int ArmorClass, int ActionPoints)
{
    internal static ClassicStartingStats From(ClassicCharacterProfile profile)
    {
        profile.Validate();
        bool Trait(int id) => profile.Traits.Contains(id);
        var special = SpecialAt(profile);
        return new(special, 15 + special[0] + 2 * special[2], Trait(5) ? 0 : special[5],
            Math.Max(1, 5 + special[5] / 2 - (Trait(1) ? 2 : 0)));
    }

    internal static int[] SpecialAt(ClassicCharacterProfile profile, int nightPersonAdjustment = 0)
    {
        profile.Validate();
        if (nightPersonAdjustment is < -1 or > 1) throw new ArgumentOutOfRangeException(nameof(nightPersonAdjustment));
        bool Trait(int id) => profile.Traits.Contains(id);
        return profile.Special.Select((value, index) => Math.Clamp(value + (Trait(15) ? 1 : 0) +
            (index == 0 && Trait(1) ? 2 : 0) + (index == 5 && Trait(2) ? 1 : 0) +
            (index is 1 or 4 && Trait(13) ? nightPersonAdjustment : 0), 1, 10)).ToArray();
    }
}
