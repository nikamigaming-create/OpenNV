using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal static class FalloutWeaponReloadSpeed
{
    internal static float Calculate(float agility, float sourceBase, float sourceModifier,
        IReadOnlyList<FalloutPerkEntry> perks, Func<FalloutCondition, float> conditions)
    {
        if (!float.IsFinite(agility) || !float.IsFinite(sourceBase) || !float.IsFinite(sourceModifier))
            throw new InvalidDataException("Reload speed has non-finite source actor values or settings.");
        var adjusted = agility - sourceBase;
        var rate = (float)(1d + (double)adjusted * sourceModifier);
        rate = FalloutPlayerPerkNumericEffects.Apply(37, rate, perks, conditions);
        if (!float.IsFinite(rate) || rate <= 0)
            throw new NotSupportedException("Reload requires its positive source clock; reverse/frozen playback is unbound.");
        return rate;
    }
}
