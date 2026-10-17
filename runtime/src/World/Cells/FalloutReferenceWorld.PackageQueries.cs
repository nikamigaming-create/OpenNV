using OpenNV.Runtime.Content;
using System.Security.Cryptography;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    internal FalloutActorQueries ActorQueries { get; } = new();
    internal FalloutUnloadedActorPackages? UnloadedPackages { get; set; }

    internal FalloutFormKey? CurrentPackage(FalloutFormKey reference)
    {
        var instance = Get(reference);
        if (records.GetEffective(reference).Signature is not ("ACHR" or "ACRE"))
            throw new InvalidDataException($"Current-package target {reference} is not an actor.");
        if (instance.QueryCurrentPackage is { } query) return query();
        if (IsResident(reference))
        {
            // The committed portal retires its native body before the next
            // residency publication. Its receipt owns the unchanged package
            // through that handoff; it does not select or execute a new one.
            if (instance.PackageAssignment is { Done: false } assignment &&
                instance.PackageMotion is { } motion &&
                (motion.Travel is { PortalArrival: not null, Complete: false } || motion.AccompanyPortal is not null) &&
                assignment.Package == motion.Package)
            {
                assignment.Validate();
                motion.Validate();
                var source = records.GetEffective(assignment.Package);
                var hash = Convert.ToHexString(SHA256.HashData(source.ReadData()));
                if (!hash.Equals(assignment.Sha256, StringComparison.OrdinalIgnoreCase) ||
                    !hash.Equals(motion.PackageSha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Portal handoff package differs from its winning source.");
                if (motion.Travel is { } travel)
                    FalloutTravelPackage.Read(source, ownsIdleCollection: true).Validate(records, this, reference, travel);
                else motion.AccompanyPortal!.Validate(records, this, reference, source);
                return assignment.Package;
            }
            throw new NotSupportedException($"Resident actor {reference} has no active reference package owner.");
        }
        return (UnloadedPackages ?? throw new NotSupportedException($"Actor {reference} has no active reference package owner."))
            .CurrentPackage(reference);
    }
}
