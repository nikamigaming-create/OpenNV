using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.World.Actors;

internal interface IFalloutSandboxNativeActionConsumer
{
    float GameHour { get; }
    void Enter(FalloutSandboxCandidate selected);
    void Advance(double seconds);
    void RequestRetirement(FalloutSandboxCandidate selected);
    bool ObserveRetired(FalloutSandboxCandidate selected);
    FalloutSandboxNativeIdleContinuation Capture(FalloutSandboxCandidate selected);
    void Restore(FalloutSandboxNativeIdleContinuation saved);
}
