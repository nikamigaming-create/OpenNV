using System.Buffers.Binary;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

// EVP has consumed its source selection, but the ordinary actor frame has not
// bound a procedure. This receipt does not run a predicate, begin or end event.
internal sealed record FalloutActorPendingPackageSelection(FalloutFormKey PriorityOwner, string PrioritySha256,
    FalloutFormKey? Package, string? PackageSha256, float[] Pose, ulong RandomState, double PollRemaining,
    FalloutScheduleTime? ScheduleTime, long QuestRevision, long ActivityRevision,
    FalloutActorActivitySnapshot Activity, FalloutPackageRetirement Retirement, FalloutFaceBlinkSnapshot? Blink,
    string? Error, FalloutFormKey? FailedPackage, string? FailedPackageSha256,
    FalloutActorPackageIdleState? IdleState, string? IdleError)
{
    internal const string CaptureBlocker = "Actor package selection awaits its native procedure continuation.";

    internal void Validate()
    {
        if (!FalloutActorFurnitureContinuation.ValidKey(PriorityOwner) || !FalloutActorFurnitureContinuation.ValidHash(PrioritySha256) ||
            Package is { } package && !FalloutActorFurnitureContinuation.ValidKey(package) ||
            (Package is null ? PackageSha256 is not null : !FalloutActorFurnitureContinuation.ValidHash(PackageSha256)) ||
            FailedPackage is { } failed && !FalloutActorFurnitureContinuation.ValidKey(failed) ||
            (FailedPackage is null ? FailedPackageSha256 is not null : !FalloutActorFurnitureContinuation.ValidHash(FailedPackageSha256)) ||
            Error is not null && string.IsNullOrWhiteSpace(Error) || IdleError is not null && string.IsNullOrWhiteSpace(IdleError) ||
            !double.IsFinite(PollRemaining) || PollRemaining < 0 || QuestRevision < -1 || ActivityRevision < -1 ||
            Activity is null || Retirement is null || IdleState?.ActiveAnimation is not null ||
            IdleState is not null && IdleState.Error != IdleError ||
            ScheduleTime is { } time && (time.Month is < 0 or > 11 || time.Date is < 1 or > 31 || time.Weekday is < 0 or > 6 ||
                !float.IsFinite(time.Hour) || time.Hour is < 0 or >= 24))
            throw new InvalidDataException("Saved pending package selection is invalid.");
        FalloutActorFurnitureContinuation.ValidatePose(Pose);
        Activity.Validate(); Retirement.Validate(); Blink?.Validate();
    }

    internal void Validate(FalloutPluginStack records, FalloutReferenceInstance actor)
    {
        Validate();
        if (records.GetEffective(actor.Base).Signature != "NPC_" || actor.PackageAssignment is not null ||
            actor.PackageBindingFailure is not null || actor.SelectionFailure is not null ||
            actor.FurnitureContinuation is not null || actor.DialogueContinuation is not null)
            throw new InvalidDataException("Pending package selection has an active or conflicting procedure.");
        var owner = FalloutActorTemplateOwner.Resolve(records, records.GetEffective(actor.Base), 32, actor.Templates);
        if (owner.FormKey != PriorityOwner || FalloutActorFurnitureContinuation.RecordHash(owner) != PrioritySha256)
            throw new InvalidDataException("Saved pending selection differs from its winning priority owner.");
        if (Package is { } package)
        {
            RequirePackage(records, package, PackageSha256!);
            if (!owner.ReadSubrecords().Where(field => field.Signature == "PKID").Any(field =>
                field.Data.Length == 4 && owner.Plugin.AdjustFormId(BinaryPrimitives.ReadUInt32LittleEndian(field.Data.Span)) == package))
                throw new InvalidDataException("Pending selection is absent from the actor's source package list.");
            _ = FalloutScriptPackage.Read(records.GetEffective(package));
        }
        if (FailedPackage is { } failed) RequirePackage(records, failed, FailedPackageSha256!);
        if (IdleState is { } idles) idles.Validate(records, idles.Package);
        Retirement.Validate(records);
        if (Blink is not null && Blink.Settings != FalloutFaceBlinkSettings.Read(records))
            throw new InvalidDataException("Pending selection blink settings differ from their source.");
    }

    private static void RequirePackage(FalloutPluginStack records, FalloutFormKey key, string hash)
    {
        var source = records.GetEffective(key);
        if (source.Signature != "PACK" || FalloutActorFurnitureContinuation.RecordHash(source) != hash)
            throw new InvalidDataException("Pending selection package differs from its winning source.");
    }

    internal FalloutActorPendingPackageSelection Copy() => this with
    { Pose = (float[])Pose.Clone(), Blink = Blink?.Copy(), IdleState = IdleState?.Copy() };
}
