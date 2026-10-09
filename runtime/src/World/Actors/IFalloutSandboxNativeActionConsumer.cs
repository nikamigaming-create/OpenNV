using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Actors;

internal interface IFalloutSandboxNativeActionConsumer
{
    void Enter(FalloutSandboxCandidate selected);
    void Advance(double seconds);
    void RequestRetirement(FalloutSandboxCandidate selected);
    bool ObserveRetired(FalloutSandboxCandidate selected);
}
