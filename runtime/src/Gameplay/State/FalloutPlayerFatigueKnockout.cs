using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Actors;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutPlayerFatigueKnockout(FalloutFatigueKnockout State,
    FalloutActorRagdollState? Ragdoll = null, FalloutActorHitReaction? Recovery = null)
{
    internal void Validate()
    {
        if (State is null) throw new InvalidDataException("Player fatigue knockout has no phase.");
        State.Validate(); Ragdoll?.Validate(); Recovery?.Validate();
        if (State.Phase == FalloutFatigueKnockoutPhase.GettingUp
            ? Recovery is null || Ragdoll is not null
            : Ragdoll is null || Recovery is not null)
            throw new InvalidDataException("Player fatigue phase has no unique physical or source-animation continuation.");
        if (Ragdoll?.Cuts is { Count: > 0 })
            throw new NotSupportedException("Player fatigue knockout cannot own death/dismemberment state.");
    }
}
