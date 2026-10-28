using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

// The source package-start location belongs to the actor, not an activity or
// animation. The point is expressed in the owning CELL's source coordinates.
internal sealed record FalloutActorPackageStartLocation(FalloutFormKey Cell, float[] Position)
{
    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(Cell.OwnerPlugin) || Cell.ObjectId is 0 or > FalloutFormKey.ObjectIdMask ||
            Position is not { Length: 3 } || Position.Any(value => !float.IsFinite(value)))
            throw new InvalidDataException("Actor package-start location is invalid.");
    }

    internal FalloutActorPackageStartLocation Copy() => new(Cell, (float[])Position.Clone());
}

internal sealed partial class FalloutReferenceWorld
{
    internal void SetActorPackageStartLocation(FalloutFormKey reference, FalloutActorPackageStartLocation location)
    {
        location.Validate();
        if (records.GetEffective(location.Cell).Signature != "CELL")
            throw new InvalidDataException("Actor package-start location is not in a CELL.");
        var actor = Actor(reference);
        actor.PackageStartLocation = location.Copy();
    }

    internal FalloutActorPackageStartLocation? ActorPackageStartLocation(FalloutFormKey reference) =>
        Actor(reference).PackageStartLocation?.Copy();

    internal void CaptureActorPackageStartLocation(FalloutFormKey reference, float unitsToMetres)
    {
        var actor = Actor(reference);
        if (actor.QuerySpatialPlacement is null)
            throw new NotSupportedException("Actor package-start location requires its live spatial owner.");
        var placement = SpatialPlacement(reference, null, unitsToMetres);
        SetActorPackageStartLocation(reference, new(placement.Cell, placement.Position));
    }
}
