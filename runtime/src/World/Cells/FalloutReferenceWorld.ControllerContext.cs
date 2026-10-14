using OpenNV.Runtime.World.Physics;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    internal IFalloutEarlyControllerLease BorrowStandalonePlayerContextController(FalloutProcessSceneContextQuery query)
    {
        RequireNoFunctionalFailure();
        var actor = ActorProcesses.Read(_enginePlayer);
        if (query.Process.Actor != actor.Source || query.Process.Epoch != actor.Epoch || query.Process.Level != actor.Level ||
            query.Process.Stack != _processRuntimeStack || query.Context is null)
            throw new InvalidOperationException("Player controller context no longer belongs to the actual current process.");
        var lease = ProcessCommon.BorrowEarlyController(_enginePlayer, actor.Epoch) ??
            throw new InvalidOperationException("Nonnull Player context has no living controller lease.");
        try
        {
            lease.RequireCurrent();
            if (lease.Controller.Identity != query.Context || lease.Controller.Lineage.CommonProcess != query.Process.Process)
                throw new InvalidOperationException("Player context changed its actual controller/Common identity.");
            return lease;
        }
        catch { lease.Dispose(); throw; }
    }
}
