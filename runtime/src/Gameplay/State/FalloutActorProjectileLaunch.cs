namespace OpenNV.Runtime.Gameplay.State;

// These values belong to the actor process, not an engagement or weapon.
// Absence in an older save means unknown history, not a request to reset it.
internal sealed record FalloutActorProjectileLaunch(float LegacySpreadFactor, float AimChaseRadians)
{
    internal static FalloutActorProjectileLaunch NewProcess() => new(0, 0);

    internal void Validate()
    {
        if (!float.IsFinite(LegacySpreadFactor) || LegacySpreadFactor < 0 || !float.IsFinite(AimChaseRadians))
            throw new InvalidDataException("Actor projectile launch process state is invalid.");
    }

    internal FalloutActorProjectileLaunch Advance(double delta, float chaseMultiplier, float animationMultiplier)
    {
        Validate();
        if (!double.IsFinite(delta) || delta < 0 || !float.IsFinite(chaseMultiplier) ||
            !float.IsFinite(animationMultiplier))
            throw new InvalidDataException("Actor aim-chase advancement is invalid.");
        var next = (float)(AimChaseRadians - delta * AimChaseRadians * chaseMultiplier * animationMultiplier);
        if (!float.IsFinite(next)) throw new InvalidDataException("Actor aim-chase advancement overflowed.");
        if (AimChaseRadians > 0 && next <= 0 || AimChaseRadians < 0 && next >= 0 ||
            next >= -1e-6f && next <= 1e-6f) next = 0;
        return this with { AimChaseRadians = next };
    }
}
