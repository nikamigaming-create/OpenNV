using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

// Selection has consumed its conditions, but has not changed the native
// procedure. It may coexist with an older route or physical furniture phase.
internal sealed record FalloutActorPackageChoice(FalloutFormKey Actor, long ScriptPackageRevision,
    FalloutFormKey? Package, string? Sha256)
{
    internal void Validate()
    {
        if (!FalloutActorFurnitureContinuation.ValidKey(Actor) || ScriptPackageRevision < 0 ||
            ScriptPackageRevision == long.MaxValue ||
            (Package is { } package ? !FalloutActorFurnitureContinuation.ValidKey(package) ||
                !FalloutActorFurnitureContinuation.ValidHash(Sha256) : Sha256 is not null))
            throw new InvalidDataException("Pending actor package choice has an invalid actor, epoch or winning source.");
    }

    internal FalloutPluginRecord? Bind(FalloutPluginStack records, FalloutReferenceInstance actor)
    {
        Validate();
        FalloutReferencePackageEvents.RequireActor(records, Actor);
        if (Actor != actor.Reference || actor.Deleted || ScriptPackageRevision != (actor.ScriptPackage?.Revision ?? 0))
            throw new InvalidDataException("Pending package choice belongs to a foreign or superseded actor assignment.");
        if (actor.ScriptPackage is { Package: { } assigned } && assigned != Package)
            throw new InvalidDataException("Pending choice discarded the actor's still-owned script override.");
        if (Package is not { } package) return null;
        var record = records.GetEffective(package);
        if (record.Signature != "PACK" || !FalloutActorFurnitureContinuation.RecordHash(record)
            .Equals(Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Pending actor package choice differs from its winning PACK.");
        _ = FalloutScriptPackage.Read(record);
        if (actor.ScriptPackage?.Package != package && !FalloutAiPackages.OnPriorityList(records, actor.Base, package, actor.Templates))
            throw new InvalidDataException("Pending package choice has neither the actor's override nor its authored priority owner.");
        return record;
    }
}
