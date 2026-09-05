namespace OpenNV.Runtime.Content;

internal static class FalloutNpcAppearanceHairShape
{
    // FNV ARMO/ARMA BMDT Hat flag (xEdit's published record contract).
    private const uint HatSlot = 0x00000400;

    internal static string? Select(FalloutNpcAppearance appearance, FalloutNpcAppearancePart part)
    {
        if (part.Role != "hair") return null;
        var equipped = appearance.Armor.Where(armor => appearance.EquippedArmor.Contains(armor.Source));
        var slots = equipped.SelectMany(armor => armor.Addons.Prepend(armor.Model))
            .Aggregate(0u, (mask, model) => mask | model.BipedSlots);
        return (slots & HatSlot) != 0 ? "Hat" : "NoHat";
    }
}
