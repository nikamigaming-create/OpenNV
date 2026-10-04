using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    private readonly Dictionary<(FalloutFormKey Reference, int Index), FalloutFormKey> _furnitureSeats = [];

    internal int GetSitting(FalloutFormKey reference, Func<FalloutFormKey, int>? live = null)
    {
        var player = records.RuntimeFormId(reference) == 0x14;
        if (!player) _ = Actor(reference);
        var state = live is not null ? live(reference) : !player && Actor(reference).QuerySitting is { } query ? query() :
            !player && Actor(reference).FurnitureContinuation is { } retained ? retained.Phase :
            throw new NotSupportedException($"GetSitting actor {reference} has no physical furniture owner.");
        if (state is < 0 or > 4) throw new InvalidDataException("Physical sitting owner returned an invalid procedure state.");
        return state;
    }

    internal bool ReserveFurnitureSeat(FalloutFormKey furniture, int index, FalloutFormKey actor)
    {
        if (index is < 0 or >= 30 || records.GetEffective(Get(furniture).Base).Signature != "FURN")
            throw new InvalidDataException("Furniture reservation has no source seat owner.");
        var player = records.RuntimeFormId(actor) == 0x14;
        if (!player) _ = Actor(actor);
        if (!IsEnabled(furniture) || !player && !IsEnabled(actor)) return false;
        var seat = (furniture, index);
        if (_furnitureSeats.TryGetValue(seat, out var occupant)) return occupant == actor;
        _furnitureSeats.Add(seat, actor);
        return true;
    }

    internal void ReleaseFurnitureSeat(FalloutFormKey furniture, int index, FalloutFormKey actor)
    {
        var seat = (furniture, index);
        if (_furnitureSeats.TryGetValue(seat, out var occupant) && occupant == actor) _furnitureSeats.Remove(seat);
    }

    internal bool OwnsFurnitureSeat(FalloutFormKey furniture, int index, FalloutFormKey actor) =>
        _furnitureSeats.TryGetValue((furniture, index), out var occupant) && occupant == actor;
}
