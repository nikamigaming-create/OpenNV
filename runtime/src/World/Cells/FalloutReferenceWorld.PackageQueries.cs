using OpenNV.Runtime.Content;

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
        if (IsResident(reference)) throw new NotSupportedException($"Resident actor {reference} has no active reference package owner.");
        return (UnloadedPackages ?? throw new NotSupportedException($"Actor {reference} has no active reference package owner."))
            .CurrentPackage(reference);
    }
}
