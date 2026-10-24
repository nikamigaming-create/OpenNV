using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

// Source selection and Start have occurred without a resident procedure. An
// optional superseded assignment owns the retained body's existing motion and
// base clock, never motion or arrival for the incoming assignment.
internal sealed record FalloutActorDeferredPackageContinuation(FalloutFormKey Reference,
    FalloutActorPackageAssignment Assignment, long Revision, FalloutActorPackageAssignment? RetainedAssignment = null)
{
    internal void Validate()
    {
        (Assignment ?? throw new InvalidDataException("Deferred package assignment is absent.")).Validate();
        if (!FalloutActorFurnitureContinuation.ValidKey(Reference) || Assignment.Done || Revision <= 0 || Revision == long.MaxValue)
            throw new InvalidDataException("Saved deferred package boundary is invalid.");
        RetainedAssignment?.Validate();
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
        if (RetainedAssignment is { } retained)
        {
            var previous = records.GetEffective(retained.Package);
            if (previous.Signature != "PACK" || !FalloutActorFurnitureContinuation.RecordHash(previous)
                .Equals(retained.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Deferred native predecessor differs from its winning source.");
            _ = FalloutScriptPackage.Read(previous);
        }
    }

    internal void ValidateShape(FalloutReferenceSnapshot actor)
    {
        Validate();
        if (actor.Reference != Reference || actor.PackageAssignment != Assignment ||
            !MatchesNativePredecessor(actor.Animation, actor.PackageMotion) || actor.PackageIdle is not null ||
            actor.PackageBindingFailure is not null || actor.SelectionFailure is not null || actor.PendingPackageSelection is not null ||
            actor.FurnitureContinuation is not null || actor.DialogueContinuation is not null ||
            actor.Engagement is not null || actor.Ragdoll is not null || actor.HitReaction is not null ||
            actor.HeadTracking is not null || actor.CorpseEquipment is not null || actor.ObjectAnimations is not null || actor.KnockedDown || actor.Injury?.Dead == true ||
            actor.SandboxAdmissionFailure is not null || actor.SandboxMotionFailure is not null || actor.CreatureScriptIdle is not null ||
            RetainedAssignment is null && actor.AnimationSoundEvents is not null || actor.HitReactionFaults is not null)
            throw new InvalidDataException("Deferred package has a foreign assignment or already-started native continuation.");
    }

    internal bool CanCapture(FalloutReferenceInstance actor) =>
        actor.Reference == Reference && actor.PackageAssignment == Assignment && actor.DeferredPackageContinuation == this &&
        actor.ProcedureCaptureBlocker == FalloutUnloadedActorPackages.ContinuationBlocker &&
        HasUnloadedState(actor);

    private bool MatchesNativePredecessor(FalloutActorAnimationSnapshot? animation, FalloutActorPackageMotion? motion)
    {
        if (RetainedAssignment is not { } retained) return animation is null && motion is null;
        if (animation is null || motion is null || motion.Package != retained.Package ||
            !string.Equals(motion.PackageSha256, retained.Sha256, StringComparison.OrdinalIgnoreCase)) return false;
        FalloutActorAnimationState.Validate(animation);
        motion.Validate();
        return true;
    }

    private bool HasUnloadedState(FalloutReferenceInstance actor) => RetainedAssignment is null
        ? HasNoNativeState(actor)
        : HasNoProcedureOwners(actor) && MatchesNativePredecessor(actor.Animation.Capture(), actor.PackageMotion) &&
            actor.AnimationSoundsCaptureReady;

    private static bool HasNoNativeState(FalloutReferenceInstance actor) =>
        HasNoProcedureOwners(actor) && actor.Animation.Resource.Length == 0 && actor.PackageMotion is null &&
        actor.AnimationSoundCaptureDiagnostic is null;

    private static bool HasNoProcedureOwners(FalloutReferenceInstance actor) =>
        actor.CapturePackageAssignment is null && actor.QueryCurrentPackage is null &&
        actor.CaptureHeadTracking is null && actor.CaptureEngagement is null && actor.CaptureRagdoll is null &&
        actor.CaptureCorpseEquipment is null && actor.CaptureObjectAnimations is null && actor.ObjectAnimations is null &&
        actor.PackageIdle is null && actor.PackageBindingFailure is null &&
        actor.SelectionFailure is null && actor.PendingPackageSelection is null && actor.FurnitureContinuation is null &&
        actor.DialogueContinuation is null && actor.Engagement is null && actor.Ragdoll is null && actor.HitReaction is null &&
        actor.HeadTracking is null && !actor.HeadTrackingRequired && actor.CorpseEquipment is null &&
        !actor.KnockedDown && actor.Injury?.Dead != true && !actor.HasHitReactionHistory &&
        actor.SandboxAdmissionFailure is null && actor.SandboxMotionFailure is null && actor.CreatureScriptIdle is null;

    internal void BindNative(FalloutPluginStack records, FalloutReferenceInstance actor, FalloutPackageEvents events)
    {
        RequireSource(records);
        if (actor.Reference != Reference || actor.PackageAssignment != Assignment || actor.DeferredPackageContinuation != this ||
            actor.ProcedureCaptureBlocker != FalloutUnloadedActorPackages.ContinuationBlocker ||
            RetainedAssignment is not null && !MatchesNativePredecessor(actor.Animation.Capture(), actor.PackageMotion) ||
            events.Active is not null || events.Revision != 0 || events.LastEvent is not null || events.LastPackage is not null || events.Error is not null)
            throw new InvalidOperationException("Deferred package requires a fresh native lifecycle.");
        Assignment.Bind(records, events);
        events.RestoreHistory(Revision, "POBA", Assignment.Package);
        // A superseded body still owns its last pose, not the incoming route.
        // Native motion publication consumes this boundary after the handoff.
        if (RetainedAssignment is null)
        {
            actor.DeferredPackageContinuation = null;
            if (actor.ProcedureCaptureBlocker == FalloutUnloadedActorPackages.ContinuationBlocker)
                actor.ProcedureCaptureBlocker = null;
        }
    }

    internal static FalloutActorDeferredPackageContinuation? Capture(FalloutPluginStack records,
        FalloutReferenceInstance actor, FalloutPackageEvents events, FalloutActorPackageAssignment? retainedAssignment = null)
    {
        if (events.Active is null || events.Done || events.Error is not null ||
            events.LastEvent != "POBA" || events.LastPackage != events.Active.Form || events.Revision <= 0)
            return null;
        var assignment = FalloutActorPackageAssignment.Capture(records, events)!;
        var result = new FalloutActorDeferredPackageContinuation(actor.Reference, assignment, events.Revision, retainedAssignment);
        result.Validate();
        if (!result.HasUnloadedState(actor)) return null;
        result.RequireSource(records);
        return result;
    }
}
