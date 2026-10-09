using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal partial class RuntimeNativePlayer
{
    private bool _furniturePendingTransfer;
    internal void BeginSourcePendingFurniture(FalloutMainPlayerCellInvocation invocation, FalloutPendingFurnitureChoice selected,
        FalloutPlacedReference reference, Transform3D nativePlacement)
    {
        invocation.Require(FalloutMainPlayerCellStep.PendingFurniture);
        var world = _physicalWorld ?? throw new InvalidOperationException("Pending furniture has no actual Player source world.");
        world.RequirePendingFurnitureChoice(selected);
        if (reference.FormKey != selected.Reference || reference.Base != selected.Base || _physicalRecords is null || _physicalQuests is null)
            throw new InvalidDataException("Pending furniture changed actual Player source/reference ownership.");
        // This enters the real physical body, root-motion, source IDLE/KF,
        // reservation, events/audio and camera owner. It is not bed activation's
        // separate sleep-menu publication and never creates a proxy actor.
        ActivateFurniture(_physicalRecords, _physicalQuests, reference, nativePlacement, selected.Cell, world,
            sourceMarker: selected.Marker, pendingTransfer: true);
        invocation.Require(FalloutMainPlayerCellStep.PendingFurniture);
    }
}
