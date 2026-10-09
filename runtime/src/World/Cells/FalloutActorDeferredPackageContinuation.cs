using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

// Source selection and Start have occurred without a resident procedure. The
// saved boundary owns that deferred initialization, never arrival or motion.
internal sealed record FalloutActorDeferredPackageContinuation(FalloutFormKey Reference,
    FalloutActorPackageAssignment Assignment, long Revision)
{
    internal void Validate()
    {
        (Assignment ?? throw new InvalidDataException("Deferred package assignment is absent.")).Validate();
        if (!FalloutActorFurnitureContinuation.ValidKey(Reference) || Assignment.Done || Revision <= 0 || Revision == long.MaxValue)
            throw new InvalidDataException("Saved deferred package boundary is invalid.");
    }

    internal void Validate(FalloutPluginStack records, FalloutReferenceSnapshot actor)
    {
        ValidateShape(actor);
        RequireSource(records);
    }

    private void RequireSource(FalloutPluginStack records)
    {
        Validate();
        FalloutReferencePackageEvents.RequireActor(records, Reference);
        var source = records.GetEffective(Assignment.Package);
        if (source.Signature != "PACK" || !FalloutActorFurnitureContinuation.RecordHash(source)
            .Equals(Assignment.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Deferred package differs from its winning source.");
        _ = FalloutScriptPackage.Read(source);
    }

    internal void ValidateShape(FalloutReferenceSnapshot actor)
    {
        Validate();
        if (actor.Reference != Reference || actor.PackageAssignment != Assignment ||
            actor.Animation is not null || actor.PackageMotion is not null || actor.PackageIdle is not null ||
            actor.PackageBindingFailure is not null || actor.SelectionFailure is not null || actor.PendingPackageSelection is not null ||
            actor.FurnitureContinuation is not null || actor.DialogueContinuation is not null ||
            actor.Engagement is not null || actor.Ragdoll is not null || actor.HitReaction is not null ||
            actor.HeadTracking is not null || actor.CorpseEquipment is not null || actor.ObjectAnimations is not null || actor.KnockedDown || actor.Injury?.Dead == true ||
            actor.AnimationSoundEvents is not null || actor.HitReactionFaults is not null)
            throw new InvalidDataException("Deferred package has a foreign assignment or already-started native continuation.");
    }

    internal bool CanCapture(FalloutReferenceInstance actor) =>
        actor.Reference == Reference && actor.PackageAssignment == Assignment && actor.DeferredPackageContinuation == this &&
        actor.ProcedureCaptureBlocker == FalloutUnloadedActorPackages.ContinuationBlocker &&
        HasNoNativeState(actor);

    private static bool HasNoNativeState(FalloutReferenceInstance actor) =>
        actor.CapturePackageAssignment is null && actor.QueryCurrentPackage is null && actor.Animation.Resource.Length == 0 &&
        actor.CaptureHeadTracking is null && actor.CaptureEngagement is null && actor.CaptureRagdoll is null &&
        actor.CaptureCorpseEquipment is null && actor.CaptureObjectAnimations is null && actor.ObjectAnimations is null &&
        actor.PackageMotion is null && actor.PackageIdle is null && actor.PackageBindingFailure is null &&
        actor.SelectionFailure is null && actor.PendingPackageSelection is null && actor.FurnitureContinuation is null &&
        actor.DialogueContinuation is null && actor.Engagement is null && actor.Ragdoll is null && actor.HitReaction is null &&
        actor.HeadTracking is null && !actor.HeadTrackingRequired && actor.CorpseEquipment is null &&
        !actor.KnockedDown && actor.Injury?.Dead != true && !actor.HasHitReactionHistory && actor.AnimationSoundCaptureDiagnostic is null;

    internal void BindNative(FalloutPluginStack records, FalloutReferenceInstance actor, FalloutPackageEvents events)
    {
        RequireSource(records);
        if (actor.Reference != Reference || actor.PackageAssignment != Assignment || actor.DeferredPackageContinuation != this ||
            actor.ProcedureCaptureBlocker != FalloutUnloadedActorPackages.ContinuationBlocker ||
            events.Active is not null || events.Revision != 0 || events.Error is not null)
            throw new InvalidOperationException("Deferred package requires a fresh native lifecycle.");
        Assignment.Bind(records, events);
        events.RestoreHistory(Revision, "POBA", Assignment.Package);
        actor.DeferredPackageContinuation = null;
        if (actor.ProcedureCaptureBlocker == FalloutUnloadedActorPackages.ContinuationBlocker)
            actor.ProcedureCaptureBlocker = null;
    }

    internal static FalloutActorDeferredPackageContinuation? Capture(FalloutPluginStack records,
        FalloutReferenceInstance actor, FalloutPackageEvents events)
    {
        if (events.Active is null || events.Done || events.Error is not null ||
            events.LastEvent != "POBA" || events.LastPackage != events.Active.Form || events.Revision <= 0 || !HasNoNativeState(actor))
            return null;
        var assignment = actor.PackageAssignment ??
            throw new InvalidDataException("Deferred package lost its actual assignment and script epoch.");
        var result = new FalloutActorDeferredPackageContinuation(actor.Reference, assignment, events.Revision);
        result.Validate();
        return result;
    }
}
