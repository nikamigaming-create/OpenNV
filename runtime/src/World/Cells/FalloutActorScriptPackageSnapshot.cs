using System.Security.Cryptography;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

// The script slot belongs to a placed reference, independently of the current
// physical procedure. A removed slot retains its epoch so an older completion
// cannot retire a later assignment of the same PACK.
internal sealed record FalloutActorScriptPackageSnapshot(FalloutFormKey Actor, long Revision,
    FalloutFormKey? Package, string? Sha256, bool Pending)
{
    internal void Validate()
    {
        if (!FalloutActorFurnitureContinuation.ValidKey(Actor) || Revision <= 0 || Revision == long.MaxValue ||
            (Package is { } package ? !FalloutActorFurnitureContinuation.ValidKey(package) ||
                !FalloutActorFurnitureContinuation.ValidHash(Sha256) : Sha256 is not null || Pending))
            throw new InvalidDataException("Saved actor script-package slot has an invalid owner, epoch or winning source.");
    }

    internal void Validate(FalloutPluginStack records, FalloutReferenceInstance actor)
    {
        Validate();
        FalloutReferencePackageEvents.RequireActor(records, Actor);
        if (actor.Reference != Actor || records.GetEffective(actor.Base).Signature is not ("NPC_" or "CREA") ||
            actor.Deleted && Package is not null)
            throw new InvalidDataException("Saved script package belongs to a foreign, retired or non-actor reference.");
        if (Package is not { } package) return;
        var source = records.GetEffective(package);
        if (source.Signature != "PACK" || !Convert.ToHexString(SHA256.HashData(source.ReadData()))
            .Equals(Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Saved actor script package differs from its winning PACK.");
        _ = FalloutScriptPackage.Read(source);
    }

    internal void ValidateContinuation(FalloutReferenceSnapshot actor)
    {
        Validate();
        if (Actor != actor.Reference || actor.Deleted && Package is not null ||
            actor.PackageAssignment is { ScriptPackageRevision: > 0 } assignment &&
            (assignment.ScriptPackageRevision > Revision || assignment.ScriptPackageRevision == Revision &&
                (Pending || assignment.Package != Package)) ||
            Package is not null && !Pending &&
                (actor.PackageAssignment is not { } active || active.Package != Package || active.ScriptPackageRevision != Revision))
            throw new InvalidDataException("Saved script-package epoch differs from its actual actor procedure boundary.");
    }
}
