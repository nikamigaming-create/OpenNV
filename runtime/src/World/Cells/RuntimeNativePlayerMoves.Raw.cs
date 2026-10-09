using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.World.Cells;

internal static partial class RuntimeNativePlayerMoves
{
    internal static (FalloutReferencePlacement Placement, Transform3D Transform) ResolveSourcePending(
        FalloutReferenceWorld world, FalloutMainPlayerCellInvocation invocation, RuntimeNativePlayer player, FalloutPlayerPendingRequest request)
    {
        world.RequireCampaignMainPlayerInvocation(invocation, FalloutMainPlayerCellStep.PendingDestination);
        var payload = world.ReadMainPlayerPendingPayload(request);
        world.RequireSourcePlayerRawTransfer(payload);
        var placement = world.ReadSourcePlayerRawPlacement(invocation, request);
        return (placement, RawTransform(placement, player.UnitsToMeters));
    }
    private static Transform3D RawTransform(FalloutReferencePlacement placement, float unitsToMeters)
    {
        placement.Validate();
        if (!float.IsFinite(unitsToMeters) || unitsToMeters <= 0) throw new InvalidDataException("Raw Player transform has no source coordinate scale.");
        return new(GamebryoCoordinate.ConvertReferenceEuler(new(placement.RotationRadians[0], placement.RotationRadians[1], placement.RotationRadians[2]), 1),
            GamebryoCoordinate.ConvertVector(new(placement.Position[0], placement.Position[1], placement.Position[2])) * unitsToMeters);
    }
}
