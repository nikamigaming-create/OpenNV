using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime;

public partial class RuntimeCoordinator
{
    private void ResetSourcePlayerTransferSky(FalloutMainPlayerCellInvocation invocation, FalloutReferenceWorld world,
        FalloutPlayerPendingRequest request, FalloutPlayerTransferPayload payload)
    {
        world.RequireSourcePlayerRawTransfer(payload);
        if (payload.TransferArgument == 0) return;
        invocation.Require(FalloutMainPlayerCellStep.PendingDestination); BindActualNativeQueuedCallerThread();
        var sky = _nativeSkyLighting ?? throw new InvalidOperationException("Source Player transfer has no actual shared Sky.");
        var returned = sky.ResetSourceTransfer(invocation, world, request, payload);
        sky.SourceTransfer!.RequireReturn(invocation, request, returned);
        // Only the actual source reset has returned. Pending target projection,
        // TLS, calling-thread FISTP, native movement and post-null consumers
        // remain independent and keep their original refusal/return ordering.
    }
}
