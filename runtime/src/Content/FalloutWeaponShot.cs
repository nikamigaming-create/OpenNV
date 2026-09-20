using System.Buffers.Binary;

namespace OpenNV.Runtime.Content;

/// <summary>One source weapon/ammunition pairing, independent of player view or location.</summary>
internal sealed record FalloutWeaponShot(FalloutFormKey Weapon, FalloutFormKey? Ammunition,
    FalloutProjectile Projectile, int Projectiles, int BaseDamage, float MinimumSpread, float Spread,
    FalloutFormKey? RecoveredItem, float RecoveryPercent, FalloutFormKey? ImpactDataSet,
    IReadOnlyList<FalloutAmmoEffect> AmmoEffects)
{
    internal uint WeaponAnimationType { get; init; }
    internal byte AttackAnimation { get; init; }
    internal int SkillActorValue { get; init; }
    internal int StrengthRequirement { get; init; }
    internal int SkillRequirement { get; init; }

    internal float ResolvedMinimumSpread
    {
        get
        {
            var value = MinimumSpread;
            foreach (var effect in AmmoEffects.Where(effect => effect.Type == FalloutAmmoEffect.Spread))
                value = effect.Apply(value);
            return float.IsFinite(value) && value >= 0
                ? value
                : throw new InvalidDataException("Ammo-adjusted minimum spread is invalid.");
        }
    }

    internal static FalloutWeaponShot Read(FalloutPluginStack records, FalloutFormKey weapon, FalloutFormKey ammunition)
        => Read(records, weapon, ammunition, hasAmmunitionSource: true);

    internal static FalloutWeaponShot Read(FalloutPluginStack records, FalloutFormKey weapon,
        FalloutFormKey? ammunition, bool hasAmmunitionSource)
    {
        var source = records.GetEffective(weapon);
        if (source.Signature != "WEAP") throw new InvalidDataException("Shot needs a WEAP record.");
        var fields = source.ReadSubrecords().ToArray();
        var data = fields.Single(field => field.Signature == "DNAM").Data.Span;
        var item = fields.Single(field => field.Signature == "DATA").Data.Span;
        if (data.Length is not (120 or 124 or 136 or 200 or 204) || item.Length != 15)
            throw new NotSupportedException("Weapon shot record extent is unbound.");
        if (ammunition is null && hasAmmunitionSource || ammunition is not null && !hasAmmunitionSource)
            throw new InvalidDataException("Weapon shot ammunition selection differs from the source WEAP.");
        var type = BinaryPrimitives.ReadUInt32LittleEndian(data);
        if (type is not (3 or 4 or 5 or 6 or 7 or 8 or 9 or 10 or 13))
            throw new NotSupportedException($"Weapon type {type} needs its melee, mine or specialized attack owner.");
        var attackAnimation = data[41];
        if (type is 10 or 13 && attackAnimation is not (114 or 120 or 126 or 132 or 138 or 150 or 156 or 162))
            throw new NotSupportedException($"Thrown weapon {weapon} has no source AttackThrow animation.");
        var projectile = source.Plugin.AdjustOptionalFormId(BinaryPrimitives.ReadUInt32LittleEndian(data[36..]));
        var count = (int)data[42];
        var ammo = ammunition is { } ammoKey ? records.GetEffective(ammoKey) : null;
        if (ammo is not null && ammo.Signature != "AMMO") throw new InvalidDataException("Selected shot ammunition is not AMMO.");
        var extra = ammo is null
            ? ReadOnlySpan<byte>.Empty
            : ammo.ReadSubrecords().SingleOrDefault(field => field.Signature == "DAT2").Data.Span;
        FalloutFormKey? recovered = null;
        var recovery = 0f;
        if (!extra.IsEmpty)
        {
            if (extra.Length is not (12 or 20)) throw new NotSupportedException($"AMMO DAT2 extent {extra.Length} is unbound.");
            var ammoCount = BinaryPrimitives.ReadUInt32LittleEndian(extra);
            if (ammoCount > int.MaxValue) throw new InvalidDataException("AMMO projectile count exceeds runtime storage.");
            if (ammoCount != 0) count = (int)ammoCount;
            projectile = ammo!.Plugin.AdjustOptionalFormId(BinaryPrimitives.ReadUInt32LittleEndian(extra[4..])) ?? projectile;
            if (extra.Length == 20)
            {
                recovered = ammo!.Plugin.AdjustOptionalFormId(BinaryPrimitives.ReadUInt32LittleEndian(extra[12..]));
                recovery = FalloutProjectile.Number(extra, 16);
                if (recovery is < 0 or > 100) throw new InvalidDataException("Ammo recovery percentage is invalid.");
                if (recovered is { } form && records.GetEffective(form).Signature is not ("MISC" or "AMMO"))
                    throw new InvalidDataException("Consumed ammo return is neither MISC nor AMMO.");
            }
        }
        if (projectile is null || count <= 0) throw new InvalidDataException("Shot has no source projectile/count.");
        var impact = fields.SingleOrDefault(field => field.Signature == "INAM").Data;
        if (!impact.IsEmpty && impact.Length != 4) throw new InvalidDataException("Weapon impact FormID has an invalid extent.");
        var effects = ammo?.ReadSubrecords().Where(field => field.Signature == "RCIL").Select(field =>
        {
            if (field.Data.Length != 4) throw new InvalidDataException("AMMO effect FormID has an invalid extent.");
            return FalloutAmmoEffect.Read(records,
                ammo!.Plugin.AdjustFormId(BinaryPrimitives.ReadUInt32LittleEndian(field.Data.Span)));
        }).ToArray() ?? [];
        var result = new FalloutWeaponShot(weapon, ammunition, FalloutProjectile.Read(records, projectile.Value), count,
            BinaryPrimitives.ReadInt16LittleEndian(item[12..]), FalloutProjectile.Number(data, 16), FalloutProjectile.Number(data, 20),
            recovered, recovery, impact.IsEmpty ? null : source.Plugin.AdjustOptionalFormId(BinaryPrimitives.ReadUInt32LittleEndian(impact.Span)), effects)
        {
            WeaponAnimationType = type,
            AttackAnimation = attackAnimation,
            SkillActorValue = BinaryPrimitives.ReadInt32LittleEndian(data[104..]),
            StrengthRequirement = data.Length >= 172 ? checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data[168..])) : 0,
            SkillRequirement = data.Length >= 204 ? checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data[200..])) : 0,
        };
        if (type is 10 or 13 && result.Projectile.IsInstantRayAttack)
            throw new NotSupportedException($"Thrown weapon {weapon} needs a source projectile flight.");
        if (result.BaseDamage < 0 || result.MinimumSpread < 0 || result.Spread < 0) throw new InvalidDataException("Shot damage/spread is invalid.");
        return result;
    }

    internal void RequireInstantRay()
    {
        if (Projectiles <= 0) throw new InvalidDataException("Instant-ray projectile count is not positive.");
        var flameFlagsWithoutExplosion = Projectile.Type == 8 && Projectile.ExplosionSource is null
            ? (ushort)0x008c
            : (ushort)0;
        if (((Projectile.Flags & 2) != 0) != (Projectile.ExplosionSource is not null))
            throw new InvalidDataException($"Projectile {Projectile.Form} explosion flag and reference disagree.");
        if (!Projectile.IsInstantRayAttack || Projectile.Type is not (1 or 4 or 8) ||
            (Projectile.Flags & ~(ushort)(1 | flameFlagsWithoutExplosion)) != 0 ||
            Projectile.Type == 8 && Projectile.ExplosionSource is not null ||
            Projectile.Explosion is not null || Projectile.HasExplicitRotation)
            throw new NotSupportedException($"Projectile {Projectile.Form} needs flight, explosion, alternate-trigger or flag simulation.");
        RequireSupportedAmmoEffects();
    }

    internal void RequireRuntimeAttackOwner()
    {
        if (Projectile.IsInstantRayAttack)
        {
            RequireInstantRay();
            return;
        }
        RequireSupportedAmmoEffects();
        if (Projectile.Type is not (1 or 2 or 8) || Projectile.Speed <= 0 || Projectile.Model is null ||
            (Projectile.Flags & 0x0800) != 0 || Projectile.HasExplicitRotation)
            throw new NotSupportedException($"Projectile {Projectile.Form} needs its source flight, orientation or ammo-effect owner.");
        if (((Projectile.Flags & 2) != 0) != (Projectile.ExplosionSource is not null))
            throw new InvalidDataException($"Projectile {Projectile.Form} explosion flag and reference disagree.");
        if (Projectile.HasAlternateTrigger && !(Projectile.Type == 8 && Projectile.ExplosionSource is null))
            throw new NotSupportedException($"Projectile {Projectile.Form} needs its alternative-trigger owner.");
        Projectile.ExplosionSource?.RequireRuntimeDamageOwner();
    }

    private void RequireSupportedAmmoEffects()
    {
        // New Vegas AMEF fatigue modifiers are a source-defined no-op.
        var unsupported = AmmoEffects.FirstOrDefault(effect => effect.Type > FalloutAmmoEffect.WeaponCondition &&
            effect.Type != FalloutAmmoEffect.Fatigue);
        if (unsupported is not null)
            throw new NotSupportedException($"Ammo effect {unsupported.Form} type {unsupported.Type} needs its runtime owner.");
    }
}
