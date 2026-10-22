using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using Godot;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    internal FalloutWanderProgress BeginPackageWander(FalloutFormKey actor, Vector3 center, float radius,
        FalloutNumericGameSettings settings, FalloutGlobalState? globals, float unitsToMetres)
    {
        var destination = SamplePackageWander(actor, center, radius, settings, globals, unitsToMetres);
        var cell = ReferenceSpatialCell(Get(actor));
        var point = destination.Point;
        return new(cell, TravelNavigation(cell).SourceSha256, [point.X, point.Y, point.Z], false);
    }

    internal void ValidatePackageWander(FalloutFormKey actor, FalloutWanderProgress progress)
    {
        progress.Validate();
        if (TravelRegion(progress.Cell) != TravelRegion(ReferenceSpatialCell(Get(actor))))
            throw new NotSupportedException("Wander movement request needs an other-region route owner.");
        if (!progress.NavigationSha256.Equals(TravelNavigation(progress.Cell).SourceSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Saved wander destination differs from its winning navigation source.");
    }

    internal FalloutWanderDestination SamplePackageWander(FalloutFormKey actor, Vector3 center, float radius,
        FalloutNumericGameSettings settings, FalloutGlobalState? globals, float unitsToMetres,
        CancellationToken cancellation = default)
    {
        var instance = Get(actor);
        if (records.GetEffective(instance.Base).Signature is not ("NPC_" or "CREA"))
            throw new InvalidDataException("Package wandering requires an actor reference.");
        var placement = SpatialPlacement(actor, null, unitsToMetres);
        var graph = TravelNavigation(ReferenceSpatialCell(instance));
        var position = new Vector3(placement.Position[0], placement.Position[1], placement.Position[2]);
        return graph.FindPackageWanderDestination(position, center, radius, settings,
            instance.SoundRandom.NextUnitFloat, instance.SoundRandom.NextBounded, triangle =>
            {
                var doors = graph.DoorAssociations.Where(association =>
                    association.Mesh.ToString().Equals(triangle.Mesh, StringComparison.OrdinalIgnoreCase) &&
                    association.Source.Triangle == triangle.Triangle).ToArray();
                if (doors.Length == 0) return 0;
                if (doors.Length != 1)
                    throw new NotSupportedException("Wander triangle has multiple door associations without a selection owner.");
                if (doors[0].Error is { } error) throw new InvalidDataException(error);
                return PackageWanderDoorCost(doors[0].Source.Door, globals);
            }, cancellation);
    }

    // Ambient wandering asks with an empty access context. This is a search
    // cost, not permission for an actor to activate or unlock the door later.
    internal float? PackageWanderDoorCost(FalloutFormKey door, FalloutGlobalState? globals)
    {
        var instance = Get(door);
        var basis = records.GetEffective(instance.Base);
        if (basis.Signature != "DOOR")
            throw new InvalidDataException("Wander door association does not identify a placed door.");
        if (GetLocked(door) != 0)
        {
            var ownership = Ownership(door);
            var faction = ownership.Owner is { } owner && records.GetEffective(owner).Signature == "FACT";
            if (!faction)
            {
                if (ownership.Owner is null || ownership.Global is not { } condition) return null;
                var value = (globals ?? throw new NotSupportedException("Wander door access requires its global state."))
                    .Get(condition);
                if (value == 0) return null;
            }
        }
        var flags = basis.ReadSubrecords().Where(field => field.Signature == "FNAM").ToArray();
        if (flags.Length > 1 || flags.Length == 1 && flags[0].Data.Length != 1)
            throw new InvalidDataException("Door FNAM has an invalid extent.");
        return flags.Length == 1 && (flags[0].Data.Span[0] & 8) != 0 ? 100 : 0;
    }
}
