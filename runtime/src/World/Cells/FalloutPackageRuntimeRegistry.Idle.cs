using System.Security.Cryptography;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutPackageRuntimeRegistry
{
    // The Common caller supplies its actually entered Current/Primary read.
    // Immutable winning metadata is already loaded on the same runtime object;
    // a getter does not assign, retain, read GameHour or run a package effect.
    internal FalloutIdlePackageRead ReadIdlePackage(FalloutProcessPackageRead selected)
    {
        RequireCurrent(); RequireIdleSlot(selected, _executable, _stack);
        var process = selected.Process; var fields = selected.Fields;
        var revision = checked((ulong)fields.Changed);
        FalloutIdleSelectedObject? value = null;
        if (fields.Fields.Package is { } form)
        {
            var package = Require(form);
            var source = new FalloutIdlePackageSource(fields.Publication.Factory, process.Process, process.GameplayOwnership,
                process.Actor.Reference, _stack, form, package.RecordSha256, revision, Factory, Source, selected.Slot);
            value = FalloutIdlePackageSelection.From(package, source);
        }
        return new(fields.Publication.Factory, process.Process, process.GameplayOwnership, process.Actor.Reference,
            _stack, revision, value, selected, Factory, Source);
    }

    private static void RequireIdleSlot(FalloutProcessPackageRead selected, string executable, string stack)
    {
        if (selected is null || selected.Process is not { } process || selected.Fields is not { } fields ||
            process.Actor is null || process.Process == Guid.Empty || process.GameplayOwnership == Guid.Empty ||
            process.Stack != stack || process.ExecutableSha256 != executable || process.Epoch < 1 ||
            process.Level is not (FalloutDetectionProcessLevel.High or FalloutDetectionProcessLevel.MiddleHigh) ||
            !Enum.IsDefined(selected.Slot) || fields.Slot != selected.Slot || fields.Source != FalloutProcessPackageState.Source(executable) ||
            !fields.Available || fields.Fields is null || fields.Publication is null || fields.Publication.Process != process.Process ||
            fields.Publication.Factory == Guid.Empty || fields.Changed < 0 || fields.Changed > process.Changed)
            throw new InvalidDataException("Idle package omitted its returned current Common slot, class, lease or field source.");
        process.Actor.Validate();
    }

    internal static void ValidateIdlePackageRead(FalloutIdlePackageRead read, FalloutProcessCommonSnapshot common,
        FalloutPluginStack records)
    {
        if (read?.ProcessPackage is not { } selected || common.PackageRegistry is not { } registry)
            throw new InvalidDataException("Source idle package lost its selected slot or actual registry parent.");
        RequireIdleSlot(selected, common.ExecutableSha256, common.Stack);
        FalloutProcessPackageState.ValidateRead(selected, common.ExecutableSha256, common.Stack, common);
        if (read.RegistryFactory is not { } factory || factory == Guid.Empty || registry.Publications is null ||
            !registry.Publications.Any(publication => publication.Factory == factory) || read.RegistrySource != registry.Source ||
            registry.Source != SourceFor(common.ExecutableSha256) || read.Factory != selected.Fields.Publication.Factory ||
            read.Process != selected.Process.Process || read.GameplayOwnership != selected.Process.GameplayOwnership ||
            read.Actor != selected.Process.Actor.Reference || read.Stack != common.Stack || read.Revision != checked((ulong)selected.Fields.Changed))
            throw new InvalidDataException("Idle package changed its actual registry, Common process/lease, slot factory or revision.");
        if (selected.Fields.Fields.Package is not { } form)
        {
            if (read.Result is not null) throw new InvalidDataException("Source null idle slot returned an invented package.");
            return;
        }
        var package = registry.Packages.SingleOrDefault(value => value.Form == form) ??
            throw new InvalidDataException("Idle selected PACK is absent from its actual registry parent.");
        var record = records.GetEffective(form);
        var sha = Convert.ToHexString(SHA256.HashData(record.ReadData())).ToLowerInvariant();
        var metadata = FalloutIdlePackageRecordFields.Read(record);
        var source = new FalloutIdlePackageSource(read.Factory, read.Process, read.GameplayOwnership, read.Actor,
            read.Stack, form, sha, read.Revision, factory, registry.Source, selected.Slot);
        if (package.RecordSha256 != sha || unchecked((byte)package.Kind) != metadata.Kind ||
            read.Result != new FalloutIdleSelectedObject(metadata.Kind, metadata.HasIdleCollection, source))
            throw new InvalidDataException("Idle package changed its winning source bytes, raw PKDT kind or nullable idle collection.");
    }
}
