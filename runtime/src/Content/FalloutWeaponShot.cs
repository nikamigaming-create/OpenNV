using System.Buffers.Binary;

namespace OpenNV.Runtime.Content;

/// <summary>One source weapon/ammunition pairing, independent of player view or location.</summary>
internal sealed record FalloutWeaponShot(FalloutFormKey Weapon, FalloutFormKey Ammunition,
    FalloutProjectile Projectile, int Projectiles, int BaseDamage, float MinimumSpread, float Spread,
    FalloutFormKey? RecoveredItem, float RecoveryPercent, FalloutFormKey? ImpactDataSet,
    IReadOnlyList<FalloutFormKey> AmmoEffects)
{
    internal static FalloutWeaponShot Read(FalloutPluginStack records, FalloutFormKey weapon, FalloutFormKey ammunition)
    {
        var source = records.GetEffective(weapon);
        var ammo = records.GetEffective(ammunition);
        if (source.Signature != "WEAP" || ammo.Signature != "AMMO") throw new InvalidDataException("Shot needs WEAP and AMMO records.");
        var fields = source.ReadSubrecords().ToArray();
        var data = fields.Single(field => field.Signature == "DNAM").Data.Span;
        var item = fields.Single(field => field.Signature == "DATA").Data.Span;
        if (data.Length is not (120 or 124 or 136 or 200 or 204) || item.Length != 15)
            throw new NotSupportedException("Weapon shot record extent is unbound.");
        var type = BinaryPrimitives.ReadUInt32LittleEndian(data);
        if (type is < 3 or > 9) throw new NotSupportedException($"Weapon type {type} needs its melee/thrown attack owner.");
        var projectile = source.Plugin.AdjustOptionalFormId(BinaryPrimitives.ReadUInt32LittleEndian(data[36..]));
        var count = (int)data[42];
        var extra = ammo.ReadSubrecords().SingleOrDefault(field => field.Signature == "DAT2").Data.Span;
        FalloutFormKey? recovered = null;
        var recovery = 0f;
        if (!extra.IsEmpty)
        {
            if (extra.Length is not (12 or 20)) throw new NotSupportedException($"AMMO DAT2 extent {extra.Length} is unbound.");
            var ammoCount = BinaryPrimitives.ReadUInt32LittleEndian(extra);
            if (ammoCount > int.MaxValue) throw new InvalidDataException("AMMO projectile count exceeds runtime storage.");
            if (ammoCount != 0) count = (int)ammoCount;
            projectile = ammo.Plugin.AdjustOptionalFormId(BinaryPrimitives.ReadUInt32LittleEndian(extra[4..])) ?? projectile;
            if (extra.Length == 20)
            {
                recovered = ammo.Plugin.AdjustOptionalFormId(BinaryPrimitives.ReadUInt32LittleEndian(extra[12..]));
                recovery = FalloutProjectile.Number(extra, 16);
                if (recovery is < 0 or > 100) throw new InvalidDataException("Ammo recovery percentage is invalid.");
                if (recovered is { } form && records.GetEffective(form).Signature is not ("MISC" or "AMMO"))
                    throw new InvalidDataException("Consumed ammo return is neither MISC nor AMMO.");
            }
        }
        if (projectile is null || count <= 0) throw new InvalidDataException("Shot has no source projectile/count.");
        var impact = fields.SingleOrDefault(field => field.Signature == "INAM").Data;
        if (!impact.IsEmpty && impact.Length != 4) throw new InvalidDataException("Weapon impact FormID has an invalid extent.");
        var effects = ammo.ReadSubrecords().Where(field => field.Signature == "RCIL").Select(field =>
        {
            if (field.Data.Length != 4) throw new InvalidDataException("AMMO effect FormID has an invalid extent.");
            return ammo.Plugin.AdjustFormId(BinaryPrimitives.ReadUInt32LittleEndian(field.Data.Span));
        }).ToArray();
        var result = new FalloutWeaponShot(weapon, ammunition, FalloutProjectile.Read(records, projectile.Value), count,
            BinaryPrimitives.ReadInt16LittleEndian(item[12..]), FalloutProjectile.Number(data, 16), FalloutProjectile.Number(data, 20),
            recovered, recovery, impact.IsEmpty ? null : source.Plugin.AdjustOptionalFormId(BinaryPrimitives.ReadUInt32LittleEndian(impact.Span)), effects);
        if (result.BaseDamage < 0 || result.MinimumSpread < 0 || result.Spread < 0) throw new InvalidDataException("Shot damage/spread is invalid.");
        return result;
    }

    internal void RequireHitscan()
    {
        if (!Projectile.Hitscan || Projectile.Type is not (1 or 4) || (Projectile.Flags & 2) != 0 || Projectile.Explosion is not null)
            throw new NotSupportedException($"Projectile {Projectile.Form} needs flight/explosion simulation.");
        if (AmmoEffects.Count != 0) throw new NotSupportedException("Selected ammunition needs its AMEF modifier owner.");
        if (Projectiles != 1) throw new NotSupportedException("Multiple projectiles need their per-projectile spread owner.");
    }
}
