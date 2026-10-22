using System.Buffers.Binary;
using System.Numerics;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

// POBA has completed, but the first Sandbox activity could not be admitted.
// This preserves the stopped failure, not an unowned activity or route.
internal sealed record FalloutActorSandboxAdmissionFailure(FalloutFormKey Package, string PackageSha256,
    string Error, float[] Position, float[] Basis, ulong AiRandomState, double PollRemaining,
    FalloutScheduleTime? ScheduleTime, long QuestRevision, long ActivityRevision,
    FalloutActorActivitySnapshot Activity, long EventRevision, string LastEvent,
    FalloutFormKey LastPackage, string LastPackageSha256, FalloutFaceBlinkSnapshot? Blink = null)
{
    internal const string AdmissionErrorPrefix = "Sandbox candidate admission requires its activity owner: ";

    internal void Validate()
    {
        if (!FalloutActorFurnitureContinuation.ValidKey(Package) ||
            !FalloutActorFurnitureContinuation.ValidHash(PackageSha256) ||
            string.IsNullOrWhiteSpace(Error) || !Error.StartsWith(AdmissionErrorPrefix, StringComparison.Ordinal) ||
            Error.Length == AdmissionErrorPrefix.Length || Position is not { Length: 3 } || Basis is not { Length: 9 } ||
            Position.Concat(Basis).Any(value => !float.IsFinite(value)) ||
            !double.IsFinite(PollRemaining) || PollRemaining < 0 || QuestRevision < -1 || QuestRevision == long.MaxValue ||
            ActivityRevision < -1 || ActivityRevision == long.MaxValue || Activity is null || Activity.InCombat ||
            EventRevision <= 0 || EventRevision == long.MaxValue || LastEvent != "POBA" || LastPackage != Package ||
            !string.Equals(LastPackageSha256, PackageSha256, StringComparison.OrdinalIgnoreCase) ||
            ScheduleTime is { } time && (time.Month is < 0 or > 11 || time.Date is < 1 or > 31 ||
                time.Weekday is < 0 or > 6 || !float.IsFinite(time.Hour) || time.Hour is < 0 or >= 24))
            throw new InvalidDataException("Saved Sandbox admission failure is invalid.");
        var x = new Vector3(Basis[0], Basis[1], Basis[2]);
        var y = new Vector3(Basis[3], Basis[4], Basis[5]);
        var z = new Vector3(Basis[6], Basis[7], Basis[8]);
        var extent = x.LengthSquared();
        if (extent <= 0 || !float.IsFinite(extent) || Math.Abs(y.LengthSquared() - extent) > extent * .001 ||
            Math.Abs(z.LengthSquared() - extent) > extent * .001 || Math.Abs(Vector3.Dot(x, y)) > extent * .001 ||
            Math.Abs(Vector3.Dot(x, z)) > extent * .001 || Math.Abs(Vector3.Dot(y, z)) > extent * .001 ||
            Vector3.Dot(Vector3.Cross(x, y), z) <= 0)
            throw new InvalidDataException("Saved Sandbox admission pose has an invalid basis.");
        Activity.Validate();
        Blink?.Validate();
    }

    internal void Validate(FalloutPluginStack records, FalloutReferenceInstance actor)
    {
        Validate();
        if (records.GetEffective(actor.Base).Signature != "NPC_" || actor.PackageMotion is not null ||
            actor.PackageAssignment is { } assignment && (assignment.Package != Package || assignment.Done ||
                !string.Equals(assignment.Sha256, PackageSha256, StringComparison.OrdinalIgnoreCase)) ||
            actor.DeferredPackageContinuation is not null ||
            actor.PackageBindingFailure is not null || actor.FurnitureContinuation is not null ||
            actor.SelectionFailure is not null || actor.StoppedRetirement is not null ||
            actor.PendingPackageSelection is not null || actor.DialogueContinuation is not null ||
            actor.PackageIdle is not null)
            throw new InvalidDataException("Sandbox admission failure has a conflicting procedure owner.");
        var record = records.GetEffective(Package);
        if (record.Signature != "PACK" || !FalloutActorFurnitureContinuation.RecordHash(record)
            .Equals(PackageSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Saved Sandbox package differs from its winning source.");
        _ = FalloutSandboxPackage.Read(record);
        var owner = FalloutActorTemplateOwner.Resolve(records, records.GetEffective(actor.Base), 32, actor.Templates);
        if (!owner.ReadSubrecords().Where(field => field.Signature == "PKID").Any(field => field.Data.Length == 4 &&
            owner.Plugin.AdjustFormId(BinaryPrimitives.ReadUInt32LittleEndian(field.Data.Span)) == Package))
            throw new InvalidDataException("Stopped Sandbox is absent from the actor's winning package list.");
        if (Blink is not null && Blink.Settings != FalloutFaceBlinkSettings.Read(records))
            throw new InvalidDataException("Saved Sandbox blink settings differ from their source.");
    }

    internal void RestoreEvents(FalloutPluginStack records, FalloutReferenceInstance actor, FalloutPackageEvents events)
    {
        Validate(records, actor);
        var source = FalloutScriptPackage.Read(records.GetEffective(Package));
        // Both calls below must be guaranteed valid before either mutates the owner.
        if (events.Active is not null || events.Done || events.Error is not null || events.Revision != 0 ||
            events.LastEvent is not null || events.LastPackage is not null)
            throw new InvalidDataException("Sandbox admission restore requires a fresh event owner.");
        events.Restore(source, false);
        events.RestoreHistory(EventRevision, LastEvent, LastPackage);
    }

    internal FalloutActorSandboxAdmissionFailure Copy() => this with
    { Position = (float[])Position.Clone(), Basis = (float[])Basis.Clone(), Blink = Blink?.Copy() };
}
