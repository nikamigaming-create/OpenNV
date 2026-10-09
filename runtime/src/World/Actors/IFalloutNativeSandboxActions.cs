using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Actors;

// A producer owns the current original candidate registry/filter/order, chosen
// duration inputs, action publication and observed native completion. A package
// declaring no actions needs no such producer; an enabled arm cannot borrow an
// empty collection, elapsed timer, actor idle finish or package Done as a receipt.
internal interface IFalloutNativeSandboxActions
{
    FalloutSandboxTimerSample Timer();
    FalloutSandboxActionContext Context();
    float ElapsedActionSeconds();
    (uint Minimum, uint Maximum) RescanInterval();
    uint RepeatMilliseconds();
    FalloutSandboxDiscovery Discovery(FalloutSandboxPackage source, FalloutSandboxArea area);
    IReadOnlyList<FalloutSandboxCandidate> Discover(FalloutSandboxPackage source, FalloutSandboxArea area);
    float Duration(FalloutSandboxCandidate chosen);
    void Enter(FalloutSandboxCandidate chosen);
    void Advance(double seconds);
    bool ObserveReturned(FalloutSandboxCandidate chosen);
    void Retire(FalloutSandboxCandidate chosen);
}
