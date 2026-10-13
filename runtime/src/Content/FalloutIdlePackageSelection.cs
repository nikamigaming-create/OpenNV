using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutIdlePackageSource(Guid Factory, Guid Process, Guid GameplayOwnership,
    FalloutFormKey Actor, string Stack, FalloutFormKey Package, string Sha256, ulong Revision);
internal sealed record FalloutIdlePackageRead(Guid Factory, Guid Process, Guid GameplayOwnership,
    FalloutFormKey Actor, string Stack, ulong Revision, FalloutIdleSelectedObject? Result);
internal sealed record FalloutIdlePackageRecordFields(byte Kind, bool HasIdleCollection)
{
    internal static FalloutIdlePackageRecordFields Read(FalloutPluginRecord record) =>
        new(FalloutPackageData.Read(record).Procedure,
            FalloutIdlePackageSelection.HasIdleCollection(record.ReadSubrecords().Select(field => field.Signature)));
}

internal static class FalloutIdlePackageSelection
{
    internal static bool HasIdleCollection(IEnumerable<string> fields) =>
        fields.Any(field => field is "IDLB" or "IDLC" or "IDLF" or "IDLN" or "IDLA" or "IDLT");
    internal static FalloutIdleSelectedObject? Read(Func<FalloutIdleSelectedObject?> primary,
        Func<FalloutIdleSelectedObject?> current)
    {
        ArgumentNullException.ThrowIfNull(primary); ArgumentNullException.ThrowIfNull(current);
        // The first read selects a virtual, not a retained package value. A
        // nonnull primary is read again; a null primary selects current.
        return primary() is not null ? primary() : current();
    }

    internal static FalloutIdleSelectedObject From(FalloutScriptPackage package, FalloutIdlePackageSource source)
    {
        ArgumentNullException.ThrowIfNull(package); ArgumentNullException.ThrowIfNull(source);
        if (source.Factory == Guid.Empty || source.Process == Guid.Empty || source.GameplayOwnership == Guid.Empty ||
            string.IsNullOrWhiteSpace(source.Stack) || package.Form != source.Package ||
            string.IsNullOrWhiteSpace(source.Actor.OwnerPlugin) || source.Actor.ObjectId == 0 ||
            source.Sha256.Length != 64 || source.Sha256.Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidDataException("Idle package lost its actual current factory, actor, process lease or winning record.");
        return new(package.Procedure, package.HasIdleCollection, source);
    }
}
