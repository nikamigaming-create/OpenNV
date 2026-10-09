using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime;

public partial class RuntimeCoordinator
{
    private IFalloutPlayerTransferController RequireSourcePlayerPendingController(FalloutMainPlayerCellInvocation invocation)
    {
        invocation.Require(FalloutMainPlayerCellStep.PendingSceneScalar);
        return (_nativePlayer ?? throw new InvalidOperationException("Pending scalar has no actual Player physics body."))
            .RequireSourcePendingController(invocation);
    }
    private void RequireSourcePlayerRawTransferFactory(FalloutPlayerTransferPayload payload) =>
        (_nativeReferences ?? throw new InvalidOperationException("Raw Player transfer has no actual current world."))
            .RequireSourcePlayerRawTransfer(payload);

    private FalloutReferencePlacement ReadSourcePlayerRawPlacement(FalloutMainPlayerCellInvocation invocation, FalloutPlayerPendingRequest request) =>
        (_nativeReferences ?? throw new InvalidOperationException("Raw Player transfer has no actual source world."))
            .ReadSourcePlayerRawPlacement(invocation, request);
}
