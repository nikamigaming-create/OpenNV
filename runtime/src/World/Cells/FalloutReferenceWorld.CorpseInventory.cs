using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    private void BindInventoryRemoval(FalloutReferenceInstance instance)
    {
        if (instance.Inventory is { } inventory)
            inventory.Contents.PrepareChange = (before, after) => PrepareReferenceInventoryRemoval(instance, before, after);
    }

    private FalloutInventoryChangeLease? PrepareReferenceInventoryRemoval(FalloutReferenceInstance instance,
        FalloutOpeningInventoryGrant before, FalloutOpeningInventoryGrant after)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (instance.PrepareNativeInventoryRemoval is { } native) return native(before, after);
        if (instance.Injury?.Dead != true) return null;
        // State-only source inventory contracts have no presented equipment.
        // This does not admit a missing native pose or a complete checkpoint.
        if (instance.CorpseEquipment is null && instance.Ragdoll is null && instance.Engagement?.WeaponHandling is null &&
            instance.CaptureCorpseEquipment is null && instance.CanCaptureCorpseEquipment is null &&
            instance.CorpseEquipmentCaptureBlocker is null) return null;
        if (IsResident(instance.Reference) || instance.CaptureCorpseEquipment is not null ||
            instance.CanCaptureCorpseEquipment is not null || instance.StoppedRetirement is not null)
            throw new NotSupportedException("Corpse transfer requires its settled current equipment owner, not a pending native or audio-retirement binding.");
        if (instance.Ragdoll is not { Bodies.Count: > 0 } ragdoll || ragdoll.Bodies.Any(body => !body.Sleeping) ||
            HitEvents.SnapshotPending(instance.Reference).Count != 0)
            throw new NotSupportedException("Corpse transfer requires settled source bodies and no pending hit-event admission.");
        var captured = instance.Capture();
        if (captured.CorpseEquipment is not { } equipment) return null;
        var change = FalloutCorpseEquipmentInventoryChange.Prepare(records, equipment, before, after);
        var oldEquipment = instance.CorpseEquipment;
        var oldEngagement = instance.Engagement;
        return new(() =>
        {
            if (!ReferenceEquals(instance.CorpseEquipment, oldEquipment) || instance.Engagement != oldEngagement ||
                !FalloutPlayerInventory.SameSnapshot(instance.Inventory!.Contents.Capture(), after))
                throw new InvalidOperationException("Corpse inventory or independent equipment owner changed before transfer publication.");
            instance.CorpseEquipment = change.After.Copy();
            if (instance.Engagement is { } engagement)
                instance.Engagement = engagement with { WeaponHandling = change.After.WeaponHandling };
        }, () =>
        {
            instance.CorpseEquipment = oldEquipment;
            instance.Engagement = oldEngagement;
        });
    }
}
