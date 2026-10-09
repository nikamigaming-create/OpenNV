namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutMainPlayerPendingState
{
    internal void ReleaseSceneOwnedChild(FalloutMainPlayerCellInvocation invocation) =>
        Run(invocation, FalloutMainPlayerCellStep.SceneChild,
            () => AssignOwnedChild(null, "actual-original-Player-virtual-null-owned-child-store"));
}
