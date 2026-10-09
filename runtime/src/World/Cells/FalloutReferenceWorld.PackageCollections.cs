using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    private void RestorePackageCollection(FalloutReferenceInstance instance, FalloutReferenceSnapshot snapshot)
    {
        if (snapshot.PackageCollection is not { } saved) return;
        _ = Actor(instance.Reference);
        if (snapshot.PackageAssignment is not { } assignment || assignment.Package != saved.IdleState.Package)
            throw new InvalidDataException("Saved package collection differs from its actual actor assignment.");
        saved.Validate(records, assignment.Package);
        if (snapshot.PackageIdle is not null && saved.IdleState.ActiveAnimation is not null)
            throw new InvalidDataException("Cold actor has conflicting event and collection animation owners.");
        if (saved.UsesPackageBase && snapshot.PackageMotion?.Package != assignment.Package)
            throw new InvalidDataException("Cold collection lost its actually published package base.");
        instance.PackageCollection = saved.Copy();
    }

    private void ValidateSandboxSources(FalloutReferenceSnapshot snapshot)
    {
        if (snapshot.PackageMotion is not { Sandbox: { } saved } motion) return;
        _ = Actor(snapshot.Reference); saved.Validate();
        if (snapshot.PackageAssignment is not { Done: false } assignment || assignment.Package != motion.Package ||
            saved.Package != motion.Package || saved.PackageSha256 != motion.PackageSha256 || saved.Election is null || saved.Route is null)
            throw new InvalidDataException("Cold Sandbox has no actual continuous package, native route and election owners.");
        var source = FalloutSandboxPackage.Read(records.GetEffective(saved.Package));
        var area = source.LocationType == 2 ? saved.Area : source.Resolve(records, this, snapshot.Reference);
        if (saved.Area.Cell != area.Cell || saved.Area.Radius != area.Radius ||
            (saved.Area.Center is null) != (area.Center is null) || saved.Area.Center is { } center && !center.SequenceEqual(area.Center!))
            throw new InvalidDataException("Cold Sandbox area differs from its source location graph.");
        if (saved.SelectedIndex is not null)
            throw new NotSupportedException("Cold Sandbox native action requires its specific publication/clock continuation owner.");
    }
}
