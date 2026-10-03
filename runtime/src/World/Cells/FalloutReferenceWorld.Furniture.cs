using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    private readonly Dictionary<(FalloutFormKey Reference, int Index), FalloutFormKey> _furnitureSeats = [];

    internal bool ReserveFurnitureSeat(FalloutFormKey furniture, int index, FalloutFormKey actor)
    {
        if (index is < 0 or >= 30 || records.GetEffective(Get(furniture).Base).Signature != "FURN")
            throw new InvalidDataException("Furniture reservation has no source seat owner.");
        if (!IsEnabled(furniture) || !IsEnabled(actor)) return false;
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
}
