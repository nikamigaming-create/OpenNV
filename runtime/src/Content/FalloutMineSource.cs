using System.Security.Cryptography;

namespace OpenNV.Runtime.Content;

/// <summary>Source declaration shared by authored and actor-deployed proximity mines.</summary>
internal sealed record FalloutMineSource(FalloutProjectile Projectile, string Sha256)
{
    internal static FalloutMineSource? Read(FalloutPluginStack records, FalloutFormKey key)
    {
        if (records.GetEffective(key).Signature != "PROJ") return null;
        var projectile = FalloutProjectile.Read(records, key);
        if (!projectile.IsProximityMine && !projectile.IsRemoteMine) return null;
        return new(projectile, Convert.ToHexString(SHA256.HashData(records.GetEffective(key).ReadData())).ToLowerInvariant());
    }

    internal float Radius(FalloutPluginStack records, bool exterior)
    {
        var multiplier = exterior ? records.NumericSettings.Float("fMineExteriorRadiusMult") : 1;
        var radius = Projectile.ExplosionAltTriggerProximity * multiplier;
        return float.IsFinite(radius) && radius > 0 ? radius :
            throw new InvalidDataException("Source mine proximity radius is invalid.");
    }

    internal double TriggerDelay(FalloutPluginStack records, bool creature, float explosives)
    {
        if (!float.IsFinite(explosives)) throw new InvalidDataException("Triggering actor Explosives value is not finite.");
        // GECK Projectile: proximity countdown = fMinesDelayMin + Timer *
        // triggering character's Explosives / 100; creatures use zero skill.
        // Native character AV conversion truncates before multiplication.
        var delay = records.NumericSettings.Float("fMinesDelayMin") +
            Projectile.ExplosionAltTriggerTimer * (creature ? 0 : Math.Truncate(explosives)) / 100;
        return double.IsFinite(delay) && delay >= 0 ? delay :
            throw new InvalidDataException("Source mine trigger delay is invalid.");
    }

    internal FalloutFormKey PickupWeapon(FalloutPluginStack records)
    {
        var weapon = Projectile.DefaultWeapon ?? throw new NotSupportedException("Placed mine has no source default weapon for pickup.");
        if (records.GetEffective(weapon).Signature != "WEAP")
            throw new InvalidDataException("Mine default weapon source is not WEAP.");
        return weapon;
    }
}
