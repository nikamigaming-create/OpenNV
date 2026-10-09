using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    // The original player bed branch asks whether a marker is occupied. It
    // does not acquire a reservation or publish a lying-down actor procedure.
    internal bool IsFurnitureSeatOccupied(FalloutFormKey furniture, int index)
    {
        if (index is < 0 or >= 30 || records.GetEffective(Get(furniture).Base).Signature != "FURN")
            throw new InvalidDataException("Furniture occupancy query has no actual source marker owner.");
        return _furnitureSeats.ContainsKey((furniture, index));
    }

    internal FalloutRestObservation ObserveRestBedEnabled(FalloutRestRequest request, FalloutFormKey currentCell)
    {
        var bed = RequireCurrentRestBed(request);
        if (Placement(bed.Reference).Cell != currentCell)
            return new(FalloutRestFactState.Denied, $"current-bed-placement:{bed.Reference}",
                "The source bed is not in the player's current attached CELL.");
        return new(IsEnabled(bed.Reference) ? FalloutRestFactState.Satisfied : FalloutRestFactState.Denied,
            $"authoritative-bed-enable:{bed.Reference}:{bed.ReferenceSha256}",
            IsEnabled(bed.Reference) ? null : "The source bed's current enable graph is disabled.");
    }

    internal FalloutRestObservation ObserveRestBedOwnership(FalloutRestRequest request)
    {
        var bed = RequireCurrentRestBed(request);
        var ownership = Ownership(bed.Reference);
        var owner = ownership.Owner;
        var identity = $"source-bed-ownership:{bed.Reference}:{Access(bed.Reference).Sha256}:{owner}";
        if (owner is null) return new(FalloutRestFactState.Satisfied, identity);
        var player = records.RuntimeFormKey(0x14);
        if (owner == ActorBase(player)) return new(FalloutRestFactState.Satisfied, identity);
        var declared = records.GetEffective(owner.Value);
        if (declared.Signature == "FACT")
        {
            // This bed caller uses faction membership. XRNK is independently
            // read/validated by Ownership; no rank threshold is invented here.
            var member = ActorFactions(player).GetValueOrDefault(owner.Value, (sbyte)-1) >= 0;
            return new(member ? FalloutRestFactState.Satisfied : FalloutRestFactState.Denied,
                identity, member ? null : "The current player is not a member of the source owner faction.");
        }
        if (ownership.Global is not null)
            return new(FalloutRestFactState.Unowned, identity,
                "Non-faction conditional ownership requires its independently admitted original global/access consumer.");
        if (declared.Signature is "NPC_" or "CREA")
            return new(FalloutRestFactState.Denied, identity, "The source bed belongs to another actor base.");
        return new(FalloutRestFactState.Unowned, identity,
            "Placed-actor ownership requires its original base/extra ownership adjustment consumer.");
    }

    internal FalloutRestObservation ObserveRestBedOccupancy(FalloutRestRequest request,
        Func<FalloutSleepWaitBed, FalloutRestObservation> observeCurrentModel)
    {
        ArgumentNullException.ThrowIfNull(observeCurrentModel);
        var bed = RequireCurrentRestBed(request);
        var model = observeCurrentModel(bed);
        model.Validate();
        if (model.State != FalloutRestFactState.Satisfied) return model;
        // Only source-enabled markers of the currently published model can
        // satisfy this fact. A reservation occupied by the player also remains
        // occupied; menu admission never silently releases another procedure.
        var available = bed.EnabledMarkers.Any(index => !IsFurnitureSeatOccupied(bed.Reference, index));
        return new(available ? FalloutRestFactState.Satisfied : FalloutRestFactState.Denied,
            $"actual-bed-model-and-seat-occupancy:{bed.Reference}:{bed.ModelSha256}",
            available ? null : "Every enabled source bed marker is occupied by a current actor reservation.");
    }

    private FalloutSleepWaitBed RequireCurrentRestBed(FalloutRestRequest request)
    {
        request.Validate();
        var bed = request.BedSource ?? throw new InvalidDataException("Bed fact has no typed source bed request.");
        if (records.GetEffective(bed.Reference).Signature != "REFR" || Get(bed.Reference).Base != bed.Base)
            throw new InvalidDataException("Rest bed differs from its actual placed reference/base owner.");
        bed.RequireCurrent(FalloutSleepWaitBed.ReadCurrent(records, bed.Reference));
        return bed;
    }
}
