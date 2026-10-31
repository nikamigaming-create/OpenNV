using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Actors;

internal partial class RuntimeNativeNpc
{
    private (bool Running, bool Sneaking, bool WeaponDrawn)? FurnitureTravelMovement()
    {
        if (_furnitureSandboxActivity || _packageIdleSource is not { Procedure: 6 } source) return null;
        // Both resident binding and restored furniture approaches retain the
        // original PACK declaration. Do not consult a preceding portal leg or
        // another procedure's retained movement when choosing locomotion.
        if (_aiPackage?.FormKey != source.Form || source.LocationType != 0 ||
            source.LocationReference != _reservedFurniture)
            throw new InvalidDataException("Furniture Travel movement differs from its active package and reserved destination.");
        return ((source.Flags & 0x2000) != 0, (source.Flags & 0x20000) != 0, (source.Flags & 0x800000) != 0);
    }

    private void BeginFurnitureTravel(FalloutPluginRecord package, FalloutFormKey furniture, bool initializing)
    {
        var world = _aiWorld ?? throw new NotSupportedException("Furniture Travel has no shared world owner.");
        var actor = Appearance.Reference!.Value;
        var goal = FalloutPackageTravelGoal.Read(_aiStack!, world, actor, package);
        if (goal.Furniture != furniture) throw new InvalidDataException("Furniture Travel differs from its winning package location.");
        var target = goal.Start(_aiStack!, world, actor);
        var retained = world.Get(actor).PackageMotion;
        if (!world.SameTravelRegion(world.Placement(actor).Cell, target.Cell) ||
            initializing && retained is { Travel: not null } && retained.Package == package.FormKey)
        {
            // The original PACK owns the route, activation and XTEL receipt.
            // Its furniture interaction cannot complete at the door threshold.
            BeginMarkerTravel(package, initializing, goal);
            return;
        }
        var placed = world.PlacedSource(furniture).Reference;
        // Preserve the existing resident cold-occupancy contract. Extending the
        // resolver to another part of the world must not add a placement snap.
        var initialOccupancy = initializing && _aiCell!.References.Any(reference => reference.FormKey == furniture);
        BeginFurniturePackage(package, placed, _aiStack!.GetEffective(placed.Base), initialOccupancy);
    }

    private void EnterFurnitureTravelRegion(FalloutFormKey furniture)
    {
        var world = _aiWorld!;
        var actor = Appearance.Reference!.Value;
        var package = _aiPackage ?? throw new InvalidDataException("Furniture approach has no retained package.");
        var placed = world.PlacedSource(furniture).Reference;
        // Rebinding after a committed portal is an unfinished physical approach,
        // never the cold initial-occupancy path which places an actor in a seat.
        if (!TryBeginFurniturePackage(package, placed, _aiStack!.GetEffective(placed.Base),
                initializing: false, parentOwnsLifecycle: true))
            throw new NotSupportedException("Furniture Travel has no unreserved source seat.");
        Combat!.RetirePackageMotionNavigation(package.FormKey);
        if (world.Get(actor).PackageMotion is { } motion)
            world.Get(actor).PackageMotion = motion with { Travel = null };
        _nativeMarkerTravel = null; _nativeMarkerTravelProgress = null;
        _nativeMarkerTravelDestination = null; _nativeMarkerTravelRestored = false;
        GD.Print($"OPENNV_NATIVE_FURNITURE_TRAVEL_HANDOFF reference={actor} package={package.FormKey} target={furniture} owner=physical-furniture-approach");
    }
}
