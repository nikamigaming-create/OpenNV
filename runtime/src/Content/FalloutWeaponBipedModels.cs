using System.Buffers.Binary;

namespace OpenNV.Runtime.Content;

internal static class FalloutWeaponBipedModels
{
    internal static IReadOnlyList<FalloutNpcAppearancePart> Read(FalloutPluginStack records, FalloutFormKey weapon,
        bool female, IFalloutFormLists? formLists = null)
    {
        var source = records.GetEffective(weapon);
        if (source.Signature != "WEAP") throw new InvalidDataException("Weapon biped models require WEAP.");
        var fields = source.ReadSubrecords().Where(field => field.Signature == "BIPL").ToArray();
        if (fields.Length == 0) return [];
        if (fields.Length != 1 || fields[0].Data.Length != 4)
            throw new InvalidDataException("Weapon BIPL has an invalid source declaration.");
        var list = source.Plugin.AdjustOptionalFormId(BinaryPrimitives.ReadUInt32LittleEndian(fields[0].Data.Span));
        if (list is null) return [];
        var parts = new List<FalloutNpcAppearancePart>();
        foreach (var entry in FalloutFormList.Entries(records, list.Value, formLists))
        {
            if (entry is not { } key) throw new InvalidDataException("Weapon biped list has a null member.");
            var addon = records.GetEffective(key);
            if (addon.Signature != "ARMA") throw new InvalidDataException("Weapon biped list member is not ARMA.");
            var slots = addon.ReadSubrecords().Where(field => field.Signature == "BMDT").ToArray();
            if (slots.Length != 1 || slots[0].Data.Length != 8)
                throw new InvalidDataException("Weapon biped addon has an invalid slot declaration.");
            var femaleModel = female && FalloutNpcAppearanceResolver.PathField(addon, "MOD3", "meshes", false) is not null;
            parts.Add(FalloutNpcAppearanceResolver.ReadModel(records, addon, "armor-addon", femaleModel ? "MOD3" : "MODL",
                femaleModel ? "MO3S" : "MODS", femaleModel ? "MOSD" : "MODD",
                BinaryPrimitives.ReadUInt32LittleEndian(slots[0].Data.Span), null));
        }
        return parts;
    }

    internal static FalloutNpcAppearance Apply(FalloutPluginStack records, FalloutNpcAppearance appearance,
        FalloutFormKey? weapon, IFalloutFormLists? formLists = null)
    {
        if (appearance.BipedWeapon is not null)
            throw new InvalidOperationException("Weapon biped replacement requires the original outfit selection.");
        var addons = weapon is { } key ? Read(records, key, appearance.Female, formLists) : [];
        return appearance with { Models = ReplaceModels(appearance.Models, addons, appearance.Race), BipedWeapon = weapon };
    }

    internal static IReadOnlyList<FalloutNpcAppearancePart> ReplaceModels(IReadOnlyList<FalloutNpcAppearancePart> outfit,
        IReadOnlyList<FalloutNpcAppearancePart> addons, FalloutFormKey? race = null)
    {
        var models = outfit.ToList();
        foreach (var addon in addons.Where(part => part.ModelPath is not null))
        {
            // Weapon is registered after the outfit. A colliding outfit
            // model loses all its slot aliases; race parts lose that slot.
            var displaced = models.Where(part => part.Source != race && (part.BipedSlots & addon.BipedSlots) != 0)
                .Select(part => part.Source).ToHashSet();
            models.RemoveAll(part => (part.BipedSlots & addon.BipedSlots) != 0 || displaced.Contains(part.Source));
            models.Add(addon);
        }
        return models;
    }
}
