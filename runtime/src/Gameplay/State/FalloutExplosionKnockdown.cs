using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal static class FalloutExplosionKnockdown
{
    internal static bool ShouldFall(FalloutPluginStack records, FalloutExplosion explosion, float damage,
        float healthBefore, float maximumHealth, float agility, Func<float> nextRandom)
    {
        if (!float.IsFinite(damage) || damage < 0 || !float.IsFinite(healthBefore) || healthBefore <= 0 ||
            !float.IsFinite(maximumHealth) || maximumHealth <= 0 || !float.IsFinite(agility) || agility < 0)
            throw new InvalidDataException("Explosion knockdown inputs are invalid.");
        if (explosion.KnocksDownAlways) return true;
        if (!explosion.KnocksDownByFormula) return false;
        float Setting(string name) => FalloutGameSettingFloats.Read(records, name);
        if (damage > maximumHealth * Setting("fKnockdownBaseHealthThreshold") / 100) return true;
        if (damage < healthBefore * Setting("fKnockdownCurrentHealthThreshold") / 100) return false;
        var denominator = Setting("fKnockdownAgilMult") * agility * 10 + Setting("fKnockdownAgilBase");
        var numerator = damage * Setting("fKnockdownDamageMult") + Setting("fKnockdownDamageBase");
        var maximum = Setting("fKnockdownChance");
        if (denominator < 0 || numerator < 0 || maximum is < 0 or > 1)
            throw new InvalidDataException("Explosion knockdown settings are invalid.");
        var chance = denominator == 0 ? maximum : Math.Min(maximum, numerator / denominator);
        var random = nextRandom();
        if (!float.IsFinite(random) || random is < 0 or >= 1) throw new InvalidDataException("Knockdown random value is invalid.");
        return chance * 1000 > MathF.Floor(random * 1000);
    }
}
