using System.Buffers.Binary;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutWeaponModification(int Slot, FalloutFormKey Item, uint Effect, float Value, float Secondary);

internal static class FalloutWeaponModifications
{
    internal static IReadOnlyList<FalloutWeaponModification> Read(FalloutPluginStack records, FalloutFormKey form)
    {
        var weapon = records.GetEffective(form);
        if (weapon.Signature != "WEAP") throw new InvalidDataException("Weapon modification needs a WEAP source.");
        var fields = weapon.ReadSubrecords().ToArray();
        var data = fields.Single(field => field.Signature == "DNAM").Data;
        var result = new List<FalloutWeaponModification>();
        for (var slot = 0; slot < 3; slot++)
        {
            var field = fields.SingleOrDefault(field => field.Signature == "WMI" + (slot + 1)).Data;
            if (field.IsEmpty) continue;
            if (field.Length != 4) throw new InvalidDataException("Weapon mod FormID extent is invalid.");
            var raw = BinaryPrimitives.ReadUInt32LittleEndian(field.Span);
            if (raw == 0) continue;
            if (data.Length < 164) throw new NotSupportedException("Weapon modification effects are absent from DNAM.");
            var item = weapon.Plugin.AdjustFormId(raw);
            if (records.GetEffective(item).Signature != "IMOD") throw new InvalidDataException("Weapon modification does not resolve to IMOD.");
            var effect = BinaryPrimitives.ReadUInt32LittleEndian(data.Span[(140 + slot * 4)..]);
            var value = BinaryPrimitives.ReadSingleLittleEndian(data.Span[(152 + slot * 4)..]);
            var secondary = data.Length >= 196 ? BinaryPrimitives.ReadSingleLittleEndian(data.Span[(184 + slot * 4)..]) : 0;
            if (effect > 16 || !float.IsFinite(value) || !float.IsFinite(secondary)) throw new InvalidDataException("Weapon modification effect is invalid.");
            result.Add(new(slot, item, effect, value, secondary));
        }
        return result;
    }

    internal static void RequireRuntimeEffect(FalloutWeaponModification modification)
    {
        if (modification.Effect is 5 or 6 or 7 or 13 or 15)
            throw new NotSupportedException($"Weapon modification effect {modification.Effect} requires its ammo regeneration, equip timing or VATS owner.");
    }

    private static IReadOnlyList<FalloutWeaponModification> Installed(FalloutPluginStack records, FalloutFormKey weapon, byte mask)
    {
        if ((mask & ~7) != 0) throw new InvalidDataException("Installed weapon modification mask is invalid.");
        if (mask == 0) return [];
        var mods = Read(records, weapon).Where(value => (mask & (1 << value.Slot)) != 0).ToArray();
        if (mods.Aggregate(0, (bits, value) => bits | (1 << value.Slot)) != mask)
            throw new InvalidDataException("Installed modification has no winning source slot.");
        foreach (var mod in mods) RequireRuntimeEffect(mod);
        return mods;
    }

    internal static float Damage(FalloutPluginStack records, FalloutFormKey weapon, byte mask, float damage) =>
        Math.Max(0, damage + Installed(records, weapon, mask).Where(mod => mod.Effect == 1).Sum(mod => mod.Value));

    internal static float Weight(FalloutPluginStack records, FalloutFormKey weapon, byte mask, float weight) =>
        Math.Max(0, weight - Installed(records, weapon, mask).Where(mod => mod.Effect == 4).Sum(mod => mod.Value));

    internal static FalloutWeaponShot Apply(FalloutPluginStack records, FalloutWeaponShot shot, byte mask)
    {
        foreach (var mod in Installed(records, shot.Weapon, mask))
            shot = mod.Effect switch
            {
                3 => shot with { MinimumSpread = Math.Max(0, shot.MinimumSpread * (1 - mod.Value)), Spread = Math.Max(0, shot.Spread * (1 - mod.Value)) },
                9 => shot with { Projectile = shot.Projectile with { Speed = Math.Max(0, shot.Projectile.Speed + mod.Value) } },
                12 => shot with { Projectiles = checked(shot.Projectiles + (int)mod.Value) },
                _ => shot
            };
        return shot;
    }

    internal static FalloutWeaponPresentation Apply(FalloutPluginStack records, FalloutWeaponPresentation weapon, byte mask, bool firstPerson)
    {
        var mods = Installed(records, weapon.Form, mask);
        if (mask == 0) return weapon;
        var source = records.GetEffective(weapon.Form);
        var fields = source.ReadSubrecords().ToArray();
        var model = firstPerson ? fields.SingleOrDefault(field => field.Signature == "WNM" + mask).Data : default;
        if (!model.IsEmpty)
        {
            if (model.Length != 4) throw new InvalidDataException("Modified first-person model FormID extent is invalid.");
            var raw = BinaryPrimitives.ReadUInt32LittleEndian(model.Span);
            if (raw != 0)
                weapon = weapon with { Model = FalloutNpcAppearanceResolver.ReadModel(records, records.GetEffective(source.Plugin.AdjustFormId(raw)), "weapon", "MODL", "MODS", "MODD", 0, null) };
        }
        else if (!firstPerson && fields.SingleOrDefault(field => field.Signature == "MWD" + mask).Data is { IsEmpty: false } path)
        {
            var value = FalloutDialogueTopic.Text(path.Span).Replace('\\', '/');
            if (!value.StartsWith("meshes/", StringComparison.OrdinalIgnoreCase)) value = "meshes/" + value;
            if (weapon.Model is { } original) weapon = weapon with { Model = original with { ModelPath = value, AlternateTextures = [] } };
        }
        foreach (var mod in mods)
            weapon = mod.Effect switch
            {
                2 => weapon with { ClipSize = checked((byte)Math.Clamp(weapon.ClipSize + (int)mod.Value, 0, byte.MaxValue)) },
                8 => weapon with { AttackMultiplier = weapon.AttackMultiplier * (1 + mod.Value), AttackShotsPerSecond = weapon.AttackShotsPerSecond * (1 + mod.Value) },
                10 => weapon with { ConditionHealth = checked(weapon.ConditionHealth + (int)mod.Value) },
                14 => weapon with { SightFieldOfViewDegrees = Math.Clamp(weapon.SightFieldOfViewDegrees - mod.Value, 1, 179) },
                _ => weapon
            };
        if (mods.Any(mod => mod.Effect is 11 or 16))
        {
            var replacement = fields.FirstOrDefault(field => field.Signature == (firstPerson ? "WMS2" : "WMS1")).Data;
            if (!replacement.IsEmpty)
            {
                if (replacement.Length != 4) throw new InvalidDataException("Modified weapon sound FormID extent is invalid.");
                var sound = source.Plugin.AdjustOptionalFormId(BinaryPrimitives.ReadUInt32LittleEndian(replacement.Span));
                if (sound is { } key)
                {
                    var sounds = weapon.Sounds.ToDictionary(pair => pair.Key, pair => pair.Value);
                    sounds["shoot"] = key;
                    weapon = weapon with { Sounds = sounds };
                }
            }
        }
        return weapon;
    }
}
