using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Gameplay.State;

// A consumed source door transition, retained only until the destination body
// resumes Accompany. It is not final arrival and never replays OnActivate.
internal sealed record FalloutAccompanyPortalArrival(FalloutFormKey SourceDoor, FalloutFormKey DestinationDoor)
{
    internal void Validate()
    {
        static bool Key(FalloutFormKey value) => value.ObjectId != 0 && !string.IsNullOrWhiteSpace(value.OwnerPlugin);
        if (!Key(SourceDoor) || !Key(DestinationDoor) || SourceDoor == DestinationDoor)
            throw new InvalidDataException("Accompany portal receipt has invalid source identities.");
    }

    internal void Validate(FalloutPluginStack records, FalloutReferenceWorld world, FalloutFormKey actor,
        FalloutPluginRecord package)
    {
        Validate();
        if (records.GetEffective(actor).Signature is not ("ACHR" or "ACRE") ||
            records.GetEffective(world.Get(actor).Base).Signature is not ("NPC_" or "CREA"))
            throw new InvalidDataException("Follower portal receipt has no actor continuation owner.");
        _ = FalloutFollowContinuation.ReadResident(package);
        if (world.Get(actor).PackageAssignment is not { Done: false } assignment || assignment.Package != package.FormKey)
            throw new InvalidDataException("Accompany portal receipt has no matching unfinished package assignment.");
        world.ValidateCommittedPortalPlacement(actor, SourceDoor, DestinationDoor);
    }
}
