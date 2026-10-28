using System.Buffers.Binary;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

// Selection and POBA are consumed, but no Sandbox activity or native motion
// has begun. The ordinary reference owns the base clock and independent pose.
internal sealed record FalloutActorPendingSandboxStart(FalloutFormKey Reference,
    FalloutFormKey PriorityOwner, string PrioritySha256, FalloutFormKey Package, string PackageSha256,
    float[] Pose, ulong RandomState, double PollRemaining, FalloutScheduleTime? ScheduleTime,
    long QuestRevision, long ActivityRevision, FalloutActorActivitySnapshot Activity,
    long EventRevision, string LastEvent, FalloutFormKey LastPackage,
    IReadOnlyList<FalloutIdleReplayCooldown> ReplayCooldowns, FalloutFaceBlinkSnapshot? Blink = null,
    FalloutReferencePlacement? SourcePlacement = null, long? SelectionAttempts = null)
{
    internal const string CaptureBlocker = "Sandbox continuation: awaiting first native movement observation.";

    internal void Validate()
    {
        if (!FalloutActorFurnitureContinuation.ValidKey(Reference) ||
            !FalloutActorFurnitureContinuation.ValidKey(PriorityOwner) || !FalloutActorFurnitureContinuation.ValidHash(PrioritySha256) ||
            !FalloutActorFurnitureContinuation.ValidKey(Package) || !FalloutActorFurnitureContinuation.ValidHash(PackageSha256) ||
            !double.IsFinite(PollRemaining) || PollRemaining < 0 || QuestRevision < -1 || QuestRevision == long.MaxValue ||
            ActivityRevision < -1 || ActivityRevision == long.MaxValue || Activity is null ||
            EventRevision <= 0 || EventRevision == long.MaxValue || LastEvent != "POBA" || LastPackage != Package ||
            ReplayCooldowns is null || SelectionAttempts is <= 0 or long.MaxValue ||
            ScheduleTime is { } time && (time.Month is < 0 or > 11 || time.Date is < 1 or > 31 || time.Weekday is < 0 or > 6 ||
                !float.IsFinite(time.Hour) || time.Hour is < 0 or >= 24))
            throw new InvalidDataException("Saved pending Sandbox start is invalid.");
        FalloutActorFurnitureContinuation.ValidatePose(Pose);
        Activity.Validate(); Blink?.Validate(); SourcePlacement?.Validate();
        var seen = new HashSet<FalloutFormKey>();
        foreach (var replay in ReplayCooldowns)
            if (replay is null || !seen.Add(replay.Idle) || !FalloutActorFurnitureContinuation.ValidKey(replay.Idle) ||
                !FalloutActorFurnitureContinuation.ValidHash(replay.Sha256) || !float.IsFinite(replay.Remaining) || replay.Remaining <= 0)
                throw new InvalidDataException("Pending Sandbox replay history is invalid or duplicated.");
    }

    private bool MatchesAssignment(FalloutActorPackageAssignment? assignment) => assignment is { Done: false } &&
        assignment.Package == Package && string.Equals(assignment.Sha256, PackageSha256, StringComparison.OrdinalIgnoreCase);

    private bool MatchesPlacement(FalloutReferencePlacement? placement) => SourcePlacement is null ? placement is null :
        placement is not null && placement.Cell == SourcePlacement.Cell && placement.Position.SequenceEqual(SourcePlacement.Position) &&
        placement.RotationRadians.SequenceEqual(SourcePlacement.RotationRadians);

    internal void ValidateShape(FalloutReferenceSnapshot actor)
    {
        Validate();
        if (actor.Reference != Reference || !MatchesAssignment(actor.PackageAssignment) || actor.Animation is null ||
            !MatchesPlacement(actor.Placement) || actor.ScriptError is not null || actor.ScriptStoppedFrame is not null ||
            actor.PackageMotion is not null || actor.PackageIdle is not null || actor.DeferredPackageContinuation is not null ||
            actor.PackageBindingFailure is not null || actor.PendingPackageSelection is not null || actor.SelectionFailure is not null ||
            actor.FurnitureContinuation is not null || actor.DialogueContinuation is not null ||
            actor.SandboxAdmissionFailure is not null || actor.InterruptedSandboxAdmission is not null || actor.SandboxMotionFailure is not null || actor.CreatureScriptIdle is not null)
            throw new InvalidDataException("Pending Sandbox has a missing clock or conflicting source/procedure owner.");
        FalloutActorAnimationState.Validate(actor.Animation);
        FalloutActorStoppedPose.Validate(actor);
    }

    internal void Validate(FalloutPluginStack records, FalloutReferenceInstance actor)
    {
        Validate();
        var assignment = actor.CapturePackageAssignment is { } capture ? capture() : actor.PackageAssignment;
        var signature = records.GetEffective(actor.Base).Signature;
        if (actor.Reference != Reference || signature is not ("NPC_" or "CREA") ||
            signature == "NPC_" && SelectionAttempts is not null ||
            signature == "CREA" && (SelectionAttempts is null || Blink is not null || ReplayCooldowns.Count != 0 ||
                RandomState != actor.SoundRandom.State || ActivityRevision != Activity.Revision) || !MatchesAssignment(assignment) ||
            actor.Animation.Resource.Length == 0 || !MatchesPlacement(actor.Placement) || actor.ScriptError is not null ||
            actor.ScriptStoppedFrame is not null || actor.PackageMotion is not null || actor.PackageIdle is not null ||
            actor.DeferredPackageContinuation is not null || actor.PackageBindingFailure is not null ||
            actor.PendingPackageSelection is not null || actor.SelectionFailure is not null || actor.StoppedRetirement is not null ||
            actor.FurnitureContinuation is not null || actor.DialogueContinuation is not null ||
            actor.SandboxAdmissionFailure is not null || actor.InterruptedSandboxAdmission is not null || actor.SandboxMotionFailure is not null || actor.CreatureScriptIdle is not null)
            throw new InvalidDataException("Pending Sandbox differs from its actual selected assignment or procedure boundary.");
        var owner = FalloutActorTemplateOwner.Resolve(records, records.GetEffective(actor.Base), 32, actor.Templates);
        if (owner.FormKey != PriorityOwner || FalloutActorFurnitureContinuation.RecordHash(owner) != PrioritySha256 ||
            !owner.ReadSubrecords().Where(field => field.Signature == "PKID").Any(field => field.Data.Length == 4 &&
                owner.Plugin.AdjustFormId(BinaryPrimitives.ReadUInt32LittleEndian(field.Data.Span)) == Package))
            throw new InvalidDataException("Pending Sandbox differs from its winning package priority owner.");
        var package = records.GetEffective(Package);
        if (package.Signature != "PACK" || !FalloutActorFurnitureContinuation.RecordHash(package)
            .Equals(PackageSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Pending Sandbox package differs from its winning source.");
        FalloutSandboxPackage.Read(package).RequireRetainedStart(records, actor);
        foreach (var replay in ReplayCooldowns)
        {
            var idle = records.GetEffective(replay.Idle);
            if (idle.Signature != "IDLE" || FalloutActorFurnitureContinuation.RecordHash(idle) != replay.Sha256 ||
                replay.Remaining > FalloutIdleAnimationData.Read(idle).ReplayDelaySeconds)
                throw new InvalidDataException("Pending Sandbox replay delay differs from its winning IDLE.");
        }
        if (Blink is not null && Blink.Settings != FalloutFaceBlinkSettings.Read(records))
            throw new InvalidDataException("Pending Sandbox blink settings differ from their winning source.");
    }

    internal void RestoreEvents(FalloutPluginStack records, FalloutReferenceInstance actor, FalloutPackageEvents events)
    {
        Validate(records, actor);
        if (events.Active?.Form == Package && !events.Done && events.Error is null &&
            events.Revision == EventRevision && events.LastEvent == LastEvent && events.LastPackage == LastPackage) return;
        if (events.Active is not null || events.Done || events.Error is not null || events.Revision != 0 ||
            events.LastEvent is not null || events.LastPackage is not null)
            throw new InvalidDataException("Pending Sandbox requires a fresh or exactly restored lifecycle.");
        events.Restore(FalloutScriptPackage.Read(records.GetEffective(Package)), false);
        events.RestoreHistory(EventRevision, LastEvent, LastPackage);
    }

    internal FalloutActorPendingSandboxStart Copy() => this with
    { Pose = (float[])Pose.Clone(), ReplayCooldowns = ReplayCooldowns.ToArray(), Blink = Blink?.Copy(), SourcePlacement = SourcePlacement?.Copy() };
}
