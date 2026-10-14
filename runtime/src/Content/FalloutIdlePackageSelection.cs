using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutIdlePackageSource(Guid Factory, Guid Process, Guid GameplayOwnership,
    FalloutFormKey Actor, string Stack, FalloutFormKey Package, string Sha256, ulong Revision,
    Guid? RegistryFactory = null, string? RegistrySource = null, FalloutProcessPackageSlot? Slot = null);
internal sealed record FalloutIdlePackageRead(Guid Factory, Guid Process, Guid GameplayOwnership,
    FalloutFormKey Actor, string Stack, ulong Revision, FalloutIdleSelectedObject? Result,
    FalloutProcessPackageRead? ProcessPackage = null, Guid? RegistryFactory = null, string? RegistrySource = null);
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
        RequireSource(package.Form, source);
        return new(package.Procedure, package.HasIdleCollection, source);
    }

    internal static FalloutIdleSelectedObject From(FalloutPackageRuntimeObject package, FalloutIdlePackageSource source)
    {
        ArgumentNullException.ThrowIfNull(package); ArgumentNullException.ThrowIfNull(source);
        RequireSource(package.Form, source);
        if (source.RegistryFactory is not { } factory || factory == Guid.Empty || source.Slot is not { } slot || !Enum.IsDefined(slot) ||
            string.IsNullOrWhiteSpace(source.RegistrySource) || source.Sha256 != package.RecordSha256)
            throw new InvalidDataException("Idle selected object lost its same winning runtime PACK registry/record/slot.");
        // PKDT kind is independent from the mutable runtime procedure. Signed
        // source loading retains the original byte in the idle selector.
        return new(unchecked((byte)package.Kind), package.HasIdleCollection, source);
    }

    private static void RequireSource(FalloutFormKey package, FalloutIdlePackageSource source)
    {
        if (source.Factory == Guid.Empty || source.Process == Guid.Empty || source.GameplayOwnership == Guid.Empty ||
            string.IsNullOrWhiteSpace(source.Stack) || package != source.Package ||
            string.IsNullOrWhiteSpace(source.Actor.OwnerPlugin) || source.Actor.ObjectId == 0 ||
            source.Sha256.Length != 64 || source.Sha256.Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidDataException("Idle package lost its actual current factory, actor, process lease or winning record.");
    }
}
