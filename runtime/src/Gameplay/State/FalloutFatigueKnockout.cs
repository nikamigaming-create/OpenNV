namespace OpenNV.Runtime.Gameplay.State;

internal enum FalloutFatigueKnockoutPhase { Falling, Unconscious, GettingUp }

// Fatigue recovery and physical/animation completion are independent owners.
// No elapsed-time substitute can finish a ragdoll or its source get-up clip.
internal sealed record FalloutFatigueKnockout(FalloutFatigueKnockoutPhase Phase)
{
    internal bool Unconscious => Phase == FalloutFatigueKnockoutPhase.Unconscious;
    internal void Validate()
    {
        if (!Enum.IsDefined(Phase)) throw new InvalidDataException("Fatigue knockout phase is invalid.");
    }
}
