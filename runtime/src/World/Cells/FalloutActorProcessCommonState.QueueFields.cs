using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutActorProcessCommonState
{
    internal FalloutActorProcessFact<byte> ReadSourceRequestFlags(FalloutFormKey actor, long epoch)
    {
        RequireNotBusy(); var current = Require(actor);
        if (current.Epoch != epoch || current.Phase is FalloutProcessCommonPhase.Retired or FalloutProcessCommonPhase.OldRetired ||
            current.Gameplay is null || current.Gameplay.Retired)
            return new(null, "actual-source-common-request-flags-process-owner-unavailable");
        return new(current.Scalars.Flags, "actual-source-common-request-flags/" + current.Changed, current.Boundary);
    }
}
