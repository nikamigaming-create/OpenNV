using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutTravelPackage(FalloutFormKey Form, uint Flags, int LocationType,
    FalloutFormKey? Reference, int Radius)
{
    internal bool Running => (Flags & 0x2000) != 0;
    internal bool Sneaking => (Flags & 0x20000) != 0;
    internal bool ContinueIfPlayerNear => (Flags & 0x200) != 0;
    internal bool MustReach => (Flags & 6) != 0;
    internal bool OncePerDay => (Flags & 0x400) != 0;
    internal bool WeaponDrawn => (Flags & 0x800000) != 0;
    internal bool ContinueCombat => (Flags & 0x04000000) != 0;
    internal bool NoCombatAlert => (Flags & 0x08000000) != 0;

    // GECK IsContinuingPackagePCNear defines "near" as the same CELL;
    // the package flag also prevents iterating to done while that hold applies.
    internal bool HoldsForPlayer(bool playerInSameCell) => ContinueIfPlayerNear && playerInSameCell;
    internal bool RetainsSelection(bool complete, bool playerInSameCell, bool forced) =>
        !forced && !complete && (MustReach || HoldsForPlayer(playerInSameCell));

    internal void RequireCombatContinuation(bool engaged, bool complete)
    {
        if (ContinueCombat && engaged && !complete)
            throw new NotSupportedException("Travel during combat requires its shared destination movement and firing owner.");
    }

    internal static FalloutTravelPackage Read(FalloutPluginRecord record, bool ownsIdleCollection = false)
    {
        var source = FalloutScriptPackage.Read(record);
        if (source.Procedure != 6 || source.LocationType is not (0 or 2 or 3))
            throw new NotSupportedException("Travel requires its reference-marker, current-location or editor-location owner.");
        var data = FalloutPackageData.Read(record);
        const uint supported = 2 | 4 | 0x200 | 0x400 | 0x1000 | 0x2000 | 0x20000 | 0x800000 | 0x0c000000 | FalloutScriptPackage.HeadTrackingOffFlag |
            FalloutScriptPackage.WeaponsUnequippedFlag;
        var flags = data.Flags;
        // Skip Fallout Behavior disables the optional behavior lane. Retained
        // declarations in that lane do not add a second travel procedure.
        if ((flags & ~supported) != 0 ||
            (flags & 0x1000) == 0 && data.BehaviorFlags != 0 || data.SpecificFlags is not (null or 0) ||
            record.ReadSubrecords().Any(field => field.Signature is "PLD2" or "PTDT" or "PTD2") ||
            !ownsIdleCollection && source.Idles.Count != 0)
            throw new NotSupportedException($"Travel {record.FormKey} has unowned inputs: " +
                $"flags=0x{flags & ~supported:x8}, behavior=0x{data.BehaviorFlags:x4}, " +
                $"specific={(data.SpecificFlags is { } specific ? $"0x{specific:x4}" : "absent")}, " +
                $"targets=[{string.Join(',', record.ReadSubrecords().Where(field => field.Signature is "PLD2" or "PTDT" or "PTD2").Select(field => field.Signature))}], " +
                $"idles={source.Idles.Count}, idleOwner={ownsIdleCollection}.");
        return new(record.FormKey, flags, source.LocationType.Value,
            source.LocationReference, source.LocationRadius);
    }

    internal FalloutTravelProgress Start(FalloutPluginStack records, FalloutReferenceWorld world, FalloutFormKey actor,
        float[]? currentLocation = null)
    {
        if (records.GetEffective(world.Get(actor).Base).Signature is not ("NPC_" or "CREA"))
            throw new InvalidDataException("Travel caller is not an actor.");
        FalloutReferencePlacement location;
        if (LocationType == 2)
        {
            if (currentLocation is null || records.GetEffective(world.Get(actor).Base).Signature != "NPC_")
                throw new NotSupportedException("Current-location Travel requires its observed native NPC start pose.");
            location = world.Placement(actor) with { Position = (float[])currentLocation.Clone() };
        }
        else if (LocationType == 3) location = world.EditorPlacement(actor);
        else
        {
            var target = Reference ?? throw new InvalidDataException("Travel reference location is absent.");
            var state = world.Get(target);
            var form = records.GetEffective(state.Base);
            // A Travel location uses the marker's position. IDLM animation
            // selection belongs to packages that use the marker, not Travel.
            if (state.Deleted || state.DeletePending ||
                form.Signature != "IDLM" && !FalloutNewVegasBuiltinForms.IsInternalStatic(form.Signature, records.RuntimeFormId(form.FormKey)))
                throw new NotSupportedException("Travel destination requires its non-marker interaction owner.");
            location = world.Placement(target);
        }
        location.Validate();
        return new(location.Cell, (float[])location.Position.Clone(), false);
    }

    internal void Validate(FalloutPluginStack records, FalloutReferenceWorld world, FalloutFormKey actor,
        FalloutTravelProgress progress)
    {
        progress.Validate();
        if (LocationType == 2)
        {
            // Current location is latched when this package starts, then owns
            // its saved anchor independently of later displacement. Loading
            // must not substitute the actor's newer pose for that destination.
            if (records.GetEffective(world.Get(actor).Base).Signature != "NPC_" ||
                records.GetEffective(progress.Cell).Signature != "CELL" || progress.PortalDoor is not null || progress.PortalArrival is not null)
                throw new NotSupportedException("Saved current-location Travel has no supported native NPC anchor.");
        }
        else
        {
            var source = Start(records, world, actor);
            if (source.Cell != progress.Cell || !source.Location.SequenceEqual(progress.Location))
                throw new InvalidDataException("Saved Travel location differs from its winning reference owner.");
        }
        if (records.GetEffective(world.Get(actor).Base).Signature == "NPC_")
            ValidateNativeNpcRoute(records, world, actor, progress);
        else
        {
            if (WeaponDrawn || ContinueCombat || NoCombatAlert || ContinueIfPlayerNear || progress.RouteCell is not null ||
                progress.PortalDoor is not null || progress.NavigationSha256 is not null)
                throw new NotSupportedException("Saved creature Travel has an unowned native weapon, continuing selection or route continuation.");
            if (progress.PortalArrival is not null)
            {
                if (world.Get(actor).PackageAssignment is not { Done: false } assignment || assignment.Package != Form)
                    throw new InvalidDataException("Saved creature Travel portal has no matching unfinished package assignment.");
                world.ValidateTravelPortalArrival(actor, progress);
            }
        }
    }

    internal static void ValidateNativeNpcRoute(FalloutPluginStack records, FalloutReferenceWorld world,
        FalloutFormKey actor, FalloutTravelProgress progress)
    {
        if (progress.PortalArrival is not null) { world.ValidateTravelPortalArrival(actor, progress); return; }
        if (progress.NavigationSha256 is null || !progress.Complete &&
            progress.RouteWaypoints is not { Length: > 0 } && progress.RouteFailure is null && !progress.RoutePending)
            throw new NotSupportedException("Saved NPC destination lacks its native route cursor and source identity.");
        var navigation = progress.RouteCell is { } routeCell
            ? world.TravelNavigation(routeCell) : FalloutNavigationSources.Cell(records, progress.Cell);
        if (progress.RouteCell is { } currentCell && world.TravelRegion(currentCell) != world.TravelRegion(world.Placement(actor).Cell))
            throw new InvalidDataException("Saved destination route is outside the actor's authoritative placement.");
        if (progress.PortalDoor is { } door)
        {
            if (progress.Complete || world.TravelRegion(world.Placement(door).Cell) != world.TravelRegion(world.Placement(actor).Cell))
                throw new InvalidDataException("Saved portal is outside its unfinished native route leg.");
            world.ValidateTravelPortalLeg(actor, door, progress.Cell);
        }
        else if (world.TravelRegion(world.Placement(actor).Cell) != world.TravelRegion(progress.Cell))
            throw new InvalidDataException("Saved destination lacks its other-region portal leg.");
        if (!progress.NavigationSha256.Equals(navigation.SourceSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Saved NPC destination route differs from its winning NAVM source.");
    }
}
