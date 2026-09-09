using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Campaigns.Classic;

/// <summary>Level-one, unequipped classic stats; traits modify allocation once.</summary>
internal sealed record ClassicStartingStats(int[] Special, int HitPoints, int ArmorClass, int ActionPoints)
{
    internal static ClassicStartingStats From(ClassicCharacterProfile profile)
    {
        profile.Validate();
        bool Trait(int id) => profile.Traits.Contains(id);
        var special = profile.Special.Select((value, index) => Math.Clamp(value + (Trait(15) ? 1 : 0) +
            (index == 0 && Trait(1) ? 2 : 0) + (index == 5 && Trait(2) ? 1 : 0), 1, 10)).ToArray();
        return new(special, 15 + special[0] + 2 * special[2], Trait(5) ? 0 : special[5],
            Math.Max(1, 5 + special[5] / 2 - (Trait(1) ? 2 : 0)));
    }
}
