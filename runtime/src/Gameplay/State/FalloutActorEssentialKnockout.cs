namespace OpenNV.Runtime.Gameplay.State;

// The retained update clock is independent of script unconsciousness and of
// the physical ragdoll/get-up owners. Healing at expiry is consumed once.
internal sealed record FalloutActorEssentialKnockout(float RemainingSeconds,
    bool RecoveryRequested = false, bool Unconscious = false)
{
    internal void Validate()
    {
        if (!float.IsFinite(RemainingSeconds) || RemainingSeconds < 0 && !RecoveryRequested ||
            RecoveryRequested && RemainingSeconds > 0)
            throw new InvalidDataException("Essential knockout has an invalid retained clock.");
    }
}
