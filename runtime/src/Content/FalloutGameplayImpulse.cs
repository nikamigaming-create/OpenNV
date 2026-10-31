namespace OpenNV.Runtime.Content;

internal static class FalloutGameplayImpulse
{
    // Gameplay impacts use this owner, independently of the physics-collision
    // knock settings. Light bodies scale down; heavier bodies retain the force.
    internal static float Resolve(FalloutPluginStack records, float force, float mass, int havokLayer)
    {
        if (!float.IsFinite(force) || !float.IsFinite(mass) || mass <= 0 || havokLayer is < 0 or > 127)
            throw new InvalidDataException("Gameplay impulse input is invalid.");
        var multiplier = Read(havokLayer switch
        {
            8 or 29 => "fGameplayImpulseMultBiped",
            10 => "fGameplayImpulseMultProp",
            14 => "fGameplayImpulseMultTrap",
            20 => "fGameplayImpulseMultDebrisLarge",
            _ => "fGameplayImpulseMultClutter"
        });
        var minimum = Read("fGameplayImpulseMinMass");
        if (mass < minimum) multiplier *= mass / minimum;
        multiplier *= Read("fGameplayImpulseScale");
        var result = multiplier * force;
        return float.IsFinite(result) ? result : throw new InvalidDataException("Gameplay impulse is not finite.");

        float Read(string name) => FalloutGameSettingFloats.ReadRetained(records, name, nameof(FalloutGameplayImpulse));
    }
}
