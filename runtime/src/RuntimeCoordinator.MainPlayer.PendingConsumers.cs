using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime;

public partial class RuntimeCoordinator
{
    private void ConsumeMainPlayerPendingFurniture(FalloutMainPlayerCellInvocation invocation,
        FalloutReferenceWorld world, FalloutPlayerPendingRequest request)
    {
        invocation.Require(FalloutMainPlayerCellStep.PendingFurniture);
        world.CampaignPlayerPendingConsumers.ConsumeFurniture(invocation, request, furniture =>
        {
            var choice = world.ReadPendingFurnitureChoice(furniture);
            if (choice is null) return; // Source null/non-FURN/no-free-marker arm.
            var events = _nativeReferenceEvents ?? throw new NotSupportedException("Pending furniture has no actual reference/native geometry owner.");
            var player = _nativePlayer ?? throw new InvalidOperationException("Pending furniture lost its actual Player.");
            var placed = _nativeActiveCell?.References.SingleOrDefault(reference => reference.FormKey == choice.Reference) ??
                throw new NotSupportedException("Pending source furniture has not been published in the actual target CELL graph.");
            world.RequirePendingFurnitureChoice(choice);
            player.BeginSourcePendingFurniture(invocation, choice, placed, events.PlayerFurniturePlacement(choice.Reference));
        });
    }
    private IFalloutPlayerTransferCallback RequireSourcePlayerPendingCallback(FalloutPlayerTransferCallback source) =>
        throw new NotSupportedException("source-Player-pending-callback-factory-native-handler-and-context-lease-unowned:" + source.Handler);
    private uint ReadSourcePlayerPostNullManagerWord() =>
        throw new NotSupportedException("source-Player-post-null-distinct-manager-word-constructor-and-writers-unowned");
}
