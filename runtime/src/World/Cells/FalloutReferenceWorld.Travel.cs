using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    private FalloutCampaignPortalGraph? _travelPortals;
    private readonly Dictionary<FalloutFormKey, CellNavigationGraph> _travelNavigation = [];

    internal FalloutFormKey TravelRegion(FalloutFormKey cell) =>
        FalloutCellSceneReader.ReadDefinition(records, cell).Worldspace ?? cell;

    internal CellNavigationGraph TravelNavigation(FalloutFormKey cell)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var region = TravelRegion(cell);
        if (_travelNavigation.TryGetValue(region, out var navigation)) return navigation;
        _travelNavigation.Add(region, navigation = FalloutNavigationSources.Region(records, cell));
        return navigation;
    }

    internal FalloutFormKey? TravelPortal(FalloutFormKey actor, FalloutFormKey destination)
    {
        var origin = Placement(actor).Cell;
        if (TravelRegion(origin) == TravelRegion(destination)) return null;
        var route = (_travelPortals ??= new(records)).Find(origin, destination,
            door => CanUseTravelPortal(actor, door), TravelRegion);
        return route.Count == 0 ? null : route[0];
    }

    internal bool CanUseTravelPortal(FalloutFormKey actor, FalloutFormKey door)
    {
        var source = records.GetEffective(door);
        if (!CanActivate(door) || (source.Flags & 0x100) != 0 || !CanNpcOpenDoor(door, actor)) return false;
        TravelNavigation(Placement(door).Cell).RequireDoorAssociations(door);
        if (Get(door).ScriptError is { } error) throw new NotSupportedException($"Travel door {door} retains a source failure: {error}");
        var parent = source.ReadSubrecords().Where(field => field.Signature == "XAPD").ToArray();
        if (parent.Length > 1 || parent.Length == 1 && parent[0].Data.Length != 1)
            throw new InvalidDataException("Travel door activation-parent declaration is invalid.");
        return parent.Length == 0 || parent[0].Data.Span[0] == 0;
    }

    internal FalloutReferencePlacement TravelPortalArrival(FalloutFormKey door, out FalloutFormKey destination)
    {
        var source = records.GetEffective(door);
        if (source.Signature != "REFR" || records.GetEffective(Get(door).Base).Signature != "DOOR")
            throw new InvalidDataException("Travel portal is not a winning placed door.");
        var teleport = FalloutCellSceneReader.ReadTeleport(source) ?? throw new InvalidDataException("Travel portal has no XTEL.");
        if (teleport.Flags != 0) throw new NotSupportedException("Travel portal has unowned XTEL flags.");
        destination = teleport.Door;
        if (records.GetEffective(Get(destination).Base).Signature != "DOOR")
            throw new InvalidDataException("Travel portal destination is not a placed door.");
        var placement = new FalloutReferencePlacement(Placement(destination).Cell,
            (float[])teleport.Position.Clone(), (float[])teleport.RotationRadians.Clone());
        placement.Validate();
        return placement;
    }

    internal void ValidateTravelPortalArrival(FalloutFormKey actor, FalloutTravelProgress progress)
    {
        var receipt = progress.PortalArrival ?? throw new InvalidDataException("Travel has no committed portal receipt.");
        ValidateCommittedPortalPlacement(actor, receipt.SourceDoor, receipt.DestinationDoor);
        ValidateRemainingTravelRoute(Placement(actor).Cell, progress.Cell);
    }

    internal void ValidateCommittedPortalPlacement(FalloutFormKey actor, FalloutFormKey door, FalloutFormKey destinationDoor)
    {
        var expected = TravelPortalArrival(door, out var destination);
        var actual = Placement(actor);
        if (destination != destinationDoor || actual.Cell != expected.Cell ||
            !actual.Position.SequenceEqual(expected.Position) || !actual.RotationRadians.SequenceEqual(expected.RotationRadians))
            throw new InvalidDataException("Saved Travel portal arrival differs from its accepted source XTEL placement.");
    }

    internal void ValidateTravelPortalLeg(FalloutFormKey actor, FalloutFormKey door, FalloutFormKey target)
    {
        if (TravelRegion(Placement(door).Cell) != TravelRegion(Placement(actor).Cell))
            throw new InvalidDataException("Saved Travel portal is outside the actor's current region.");
        var arrival = TravelPortalArrival(door, out _);
        ValidateRemainingTravelRoute(arrival.Cell, target);
    }

    private void ValidateRemainingTravelRoute(FalloutFormKey origin, FalloutFormKey destination)
    {
        if (TravelRegion(origin) == TravelRegion(destination)) return;
        // Locks can change after the preceding event. Persistence validates
        // topology; the next resident approach rechecks current admission.
        _ = (_travelPortals ??= new(records)).Find(origin, destination, _ => true, TravelRegion);
    }
}
