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
    FalloutFormKey LastPackage, string LastPackageSha256, FalloutFaceBlinkSnapshot? Blink = null,
    FalloutReferencePlacement? SourcePlacement = null)
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
        SourcePlacement?.Validate();
    }

    internal void Validate(FalloutPluginStack records, FalloutReferenceInstance actor)
    {
        Validate();
        if (SourcePlacement is { } placement && (records.GetEffective(placement.Cell).Signature != "CELL" ||
            actor.Placement is not { } authoritative || placement.Cell != authoritative.Cell ||
            !placement.Position.SequenceEqual(authoritative.Position) ||
            !placement.RotationRadians.SequenceEqual(authoritative.RotationRadians)))
            throw new InvalidDataException("Stopped Sandbox relocation differs from authoritative placement.");
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

    internal void RestoreActivity(FalloutPluginStack records, FalloutReferenceInstance actor, FalloutActorActivityState activity)
    {
        Validate(records, actor);
        activity.Restore(Activity);
    }

    internal FalloutActorSandboxAdmissionFailure Relocate(FalloutReferencePlacement placement)
    {
        Validate(); placement.Validate();
        return Copy() with { SourcePlacement = placement.Copy() };
    }

    // Source placement is local to the resident cell root, like ordinary
    // reference materialization. The retained global pose is only the fallback
    // when no later authoritative relocation has superseded it.
    internal Godot.Transform3D ResolveTransform(Godot.Transform3D sourceParent, float unitsToMetres, float referenceScale)
    {
        Validate();
        if (!float.IsFinite(unitsToMetres) || unitsToMetres <= 0 ||
            !float.IsFinite(referenceScale) || referenceScale <= 0 ||
            !sourceParent.IsFinite() || !float.IsFinite(sourceParent.Basis.Determinant()) || sourceParent.Basis.Determinant() <= 0)
            throw new InvalidDataException("Stopped Sandbox relocation has no valid source transform owner.");
        if (SourcePlacement is { } placement)
        {
            var transformed = sourceParent * new Godot.Transform3D(
                OpenNV.Runtime.Formats.Gamebryo.GamebryoCoordinate.ConvertReferenceEuler(
                    new(placement.RotationRadians[0], placement.RotationRadians[1], placement.RotationRadians[2]), referenceScale),
                OpenNV.Runtime.Formats.Gamebryo.GamebryoCoordinate.ConvertVector(
                    new(placement.Position[0], placement.Position[1], placement.Position[2])) * unitsToMetres);
            if (!transformed.IsFinite()) throw new InvalidDataException("Stopped Sandbox source transform overflowed.");
            return transformed;
        }
        return new(new Godot.Basis(new(Basis[0], Basis[1], Basis[2]), new(Basis[3], Basis[4], Basis[5]),
            new(Basis[6], Basis[7], Basis[8])), new(Position[0], Position[1], Position[2]));
    }

    internal FalloutActorSandboxAdmissionFailure Copy() => this with
    { Position = (float[])Position.Clone(), Basis = (float[])Basis.Clone(), Blink = Blink?.Copy(), SourcePlacement = SourcePlacement?.Copy() };
}

// Restoring a cold actor is preparation until it actually enters the tree.
// A failed detached candidate must never publish an empty native lifecycle.
internal sealed class FalloutSandboxAdmissionStaging
{
    private readonly FalloutReferenceInstance _actor;
    private readonly FalloutActorSandboxAdmissionFailure _initialReceipt;
    private readonly FalloutActorAnimationSnapshot _initialClock;
    internal FalloutActorAnimationState Clock { get; } = new();
    internal bool HeadTrackingRequired { get; }
    internal string? HeadTrackingCaptureBlocker { get; }
    private bool _live;
    internal bool IsLive => _live && PublicationError is null;
    internal string? PublicationError { get; private set; }

    internal FalloutSandboxAdmissionStaging(FalloutReferenceInstance actor)
    {
        _initialReceipt = actor.SandboxAdmissionFailure ?? throw new InvalidDataException("Sandbox staging requires a retained stopped owner.");
        _initialReceipt.Validate();
        _actor = actor;
        _initialClock = actor.Animation.Capture() ?? throw new InvalidDataException("Stopped Sandbox staging has no base clock.");
        Clock.Restore(_initialClock);
        HeadTrackingRequired = actor.HeadTrackingRequired;
        HeadTrackingCaptureBlocker = actor.HeadTrackingCaptureBlocker;
    }

    internal void EnterLive() => EnterLive(static () => { });

    internal void EnterLive(Action publish)
    {
        if (_live) return;
        if (PublicationError is not null)
            throw new InvalidDataException("Stopped Sandbox publication already failed: " + PublicationError);
        try
        {
            ValidateAuthority();
            publish();
            ValidateAuthority();
            _actor.Animation.Restore(Clock.Capture()!);
            _live = true;
        }
        catch (Exception error)
        {
            PublicationError = error.Message;
            throw;
        }
    }

    private void ValidateAuthority()
    {
        if (!ReferenceEquals(_actor.SandboxAdmissionFailure, _initialReceipt))
            throw new InvalidDataException("Stopped Sandbox authority changed during detached staging.");
        if (_actor.Animation.Capture() != _initialClock)
            throw new InvalidDataException("Stopped Sandbox source clock changed during detached staging.");
    }

    internal void RequirePublished()
    {
        if (!IsLive)
            throw new InvalidDataException("Stopped Sandbox publication did not commit: " + (PublicationError ?? "still staged"));
    }

    internal bool Retire(Action liveRetirement)
    {
        if (!IsLive) return false;
        liveRetirement();
        return true;
    }
}
