using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    private readonly Dictionary<(FalloutFormKey Reference, int Index), FalloutFormKey> _idleMarkerReservations = [];

    // Reservation and physical occupancy are distinct source states. The caller
    // must supply the actual occupancy owner; absence is not an empty marker.
    internal bool IdleMarkerAvailable(FalloutFormKey marker, int index, FalloutFormKey actor,
        Func<FalloutFormKey, int, bool> occupied)
    {
        ArgumentNullException.ThrowIfNull(occupied);
        if (!IdleMarkerAdmission(marker, index, actor)) return false;
        return !_idleMarkerReservations.ContainsKey((marker, index)) && !occupied(marker, index);
    }

    internal bool ReserveIdleMarker(FalloutFormKey marker, int index, FalloutFormKey actor,
        Func<FalloutFormKey, int, bool> occupied)
    {
        ArgumentNullException.ThrowIfNull(occupied);
        if (!IdleMarkerAdmission(marker, index, actor) || occupied(marker, index)) return false;
        var slot = (marker, index);
        if (_idleMarkerReservations.TryGetValue(slot, out var owner)) return owner == actor;
        _idleMarkerReservations.Add(slot, actor);
        return true;
    }

    internal void ReleaseIdleMarker(FalloutFormKey marker, int index, FalloutFormKey actor)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var slot = (marker, index);
        if (_idleMarkerReservations.TryGetValue(slot, out var owner) && owner == actor)
            _idleMarkerReservations.Remove(slot);
    }

    internal bool OwnsIdleMarker(FalloutFormKey marker, int index, FalloutFormKey actor)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _idleMarkerReservations.TryGetValue((marker, index), out var owner) && owner == actor;
    }

    private bool IdleMarkerAdmission(FalloutFormKey marker, int index, FalloutFormKey actor)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var markerRecord = records.GetEffective(marker);
        if (index != 0 || markerRecord.Signature != "REFR" ||
            records.GetEffective(FalloutDialogueTopic.RequiredForm(markerRecord, "NAME")).Signature != "IDLM")
            throw new InvalidDataException("Idle marker reservation requires source REFR-to-IDLM slot zero.");
        var actorRecord = records.GetEffective(actor);
        if (actorRecord.Signature is not ("ACHR" or "ACRE"))
            throw new InvalidDataException("Idle marker reservation requires a placed source actor.");
        var actorBase = records.GetEffective(FalloutDialogueTopic.RequiredForm(actorRecord, "NAME"));
        if (!(actorRecord.Signature == "ACHR" && actorBase.Signature == "NPC_" ||
            actorRecord.Signature == "ACRE" && actorBase.Signature == "CREA"))
            throw new InvalidDataException("Idle marker reservation has no matching NPC or creature source.");
        var instance = Get(marker);
        var participant = Actor(actor);
        return !instance.Deleted && !instance.DeletePending && !instance.Taken && IsEnabled(marker) &&
            !participant.Deleted && !participant.DeletePending && !participant.Taken && IsEnabled(actor);
    }
}
