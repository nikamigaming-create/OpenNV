using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal static partial class RuntimeNativePlayerMoves
{
    internal static async Task<FalloutReferencePlacement> ApplyMainPending(FalloutReferenceWorld world, FalloutMainPlayerCellInvocation invocation,
        RuntimeNativePlayer player, FalloutFormKey activeCell, FalloutPlayerPendingRequest request,
        Func<FalloutReferencePlacement, Transform3D, Task> transfer)
    {
        world.PlayerMoves.SourcePending.Require(request);
        if (request is not { Kind: FalloutPlayerPendingKind.MoveTo, Move: { } move })
            throw new InvalidDataException("Actual Main native MoveTo consumer has a different pending request kind.");
        world.RequireCampaignMainPlayerInvocation(invocation, FalloutMainPlayerCellStep.PendingDestination);
        world.RequireSourcePlayerRawTransfer(world.ReadMainPlayerPendingPayload(request));
        var (placement, transform) = ResolveSourcePending(world, invocation, player, request);
        if (world.HasStandalonePlayerScene)
        {
            var raw = world.ReadMainPlayerPendingPayload(request);
            world.CampaignStandalonePlayerScene.PublishPlayerReferencePlacement(request.Identity,
                "actual-source-Main-Player-pending-rotation-store", FalloutReferencePlacementIngress.RawPending,
                raw.RotationX, raw.RotationY, raw.RotationZ);
        }
        if (placement.Cell == activeCell) player.Teleport(transform);
        else await transfer(placement, transform);
        // The actual caller still owns this payload. Physical publication does
        // not perform the original deferred-child/free/null-store tail.
        world.PlayerMoves.SourcePending.Require(request);
        world.RequireCampaignMainPlayerInvocation(invocation, FalloutMainPlayerCellStep.PendingDestination);
        return placement;
    }
}
