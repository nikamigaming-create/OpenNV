using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed record FalloutPendingFurnitureChoice(FalloutFormKey Reference, FalloutFormKey Base,
    FalloutFormKey Cell, string ReferenceSha256, string BaseSha256, uint SourceFlags, uint UsedMask, int Marker);

internal sealed partial class FalloutReferenceWorld
{
    internal FalloutPendingFurnitureChoice? ReadPendingFurnitureChoice(FalloutFormKey reference)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (reference == _enginePlayer)
        {
            var playerBase = records.GetEffective(records.RuntimeFormKey(7));
            if (playerBase.IsDeleted || playerBase.Signature != "NPC_")
                throw new InvalidDataException("Pending Player target lost its exact winning engine actor base.");
            return null; // The actual type query's false arm; no synthetic REFR is created.
        }
        var sourceReference = records.GetEffective(reference);
        if (sourceReference.IsDeleted || sourceReference.Signature is not ("REFR" or "ACHR" or "ACRE" or "PGRE" or "PMIS"))
            throw new InvalidDataException("Pending furniture pointer is not a current winning source reference.");
        var instance = Get(reference);
        var furniture = records.GetEffective(instance.Base);
        if (furniture.IsDeleted) throw new InvalidDataException("Pending target base is deleted in the exact winning source.");
        if (furniture.Signature != "FURN") return null; // Actual source type query's false arm.
        var flags = FalloutPendingFurnitureSource.Flags(furniture);
        uint used = 0;
        foreach (var entry in _furnitureSeats.Where(entry => entry.Key.Reference == reference))
        {
            if (entry.Key.Index is < 0 or >= 30 || entry.Value != _enginePlayer && Actor(entry.Value).Deleted)
                throw new InvalidDataException("Pending marker use mask retained a retired/foreign physical seat owner.");
            used |= 1u << entry.Key.Index;
        }
        // Original pending selector passes true: its separately declared reserved
        // mask is not read. It must not manufacture a reservation query.
        var selected = FalloutPendingFurnitureSource.FirstMarker(flags, used, 0, ignoreReserved: true);
        if (selected < 0) return null;
        return new(reference, instance.Base, Placement(reference).Cell,
            FalloutActorFurnitureContinuation.RecordHash(sourceReference), FalloutActorFurnitureContinuation.RecordHash(furniture),
            flags, used, selected);
    }
    internal void RequirePendingFurnitureChoice(FalloutPendingFurnitureChoice selected)
    {
        if (ReadPendingFurnitureChoice(selected.Reference) != selected)
            throw new InvalidDataException("Pending source furniture marker changed before physical publication.");
    }
}
