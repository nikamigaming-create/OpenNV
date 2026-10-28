using System.Buffers.Binary;
using System.Numerics;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

// Activity and destination selection have already consumed their source draws.
// This receipt describes a stopped fault, never a successful movement request.
internal sealed record FalloutSandboxMotionPrefix(FalloutSandboxActivityProgress Activity,
    FalloutWanderProgress Wander, bool SelectedActivity, bool SelectedDestination,
    ulong RandomBeforeSelection, ulong RandomBeforeMotion, ulong RandomAfterFailure)
{
    internal void Validate()
    {
        if (Activity is null || Wander is null) throw new InvalidDataException("Stopped Sandbox has no accepted request.");
        Activity.Validate(); Wander.Validate();
        if (Activity.Activity != FalloutSandboxActivity.Wander || RandomBeforeMotion != RandomAfterFailure)
            throw new InvalidDataException("Stopped Sandbox motion has an unowned consumed movement suffix.");
    }
    internal FalloutSandboxMotionPrefix Copy() => this with
    {
        Activity = Activity with { Preferences = (byte[])Activity.Preferences.Clone() },
        Wander = Wander with { Destination = (float[])Wander.Destination.Clone() }
    };
}

internal sealed class FalloutActorZeroTurnException() : NotSupportedException(SourceError)
{
    internal const string SourceError = "Creature zero turn speed has no native turn owner.";
}

internal sealed class FalloutSandboxMotionException(FalloutFormKey package,
    FalloutSandboxMotionPrefix prefix, Exception cause) :
    NotSupportedException(cause.Message, cause)
{
    internal FalloutFormKey Package { get; } = package;
    internal FalloutSandboxMotionPrefix Prefix { get; } = prefix.Copy();
    internal static bool CanRetain(Exception error) => error is FalloutActorZeroTurnException ||
        error is FileNotFoundException && IsMissingMovement(error.Message);
    internal static bool IsRetainedFailure(string message) => IsMissingMovement(message) ||
        message == FalloutActorZeroTurnException.SourceError;
    internal static bool IsMissingMovement(string message)
    {
        const string prefix = "Source actor group is absent: ";
        return message.StartsWith(prefix, StringComparison.Ordinal) && message[prefix.Length..].Split(',')[0] is
            "mtforward" or "mtfastforward" or "sneakmtforward" or "sneakmtfastforward";
    }
}

internal sealed record FalloutActorSandboxMotionFailure(FalloutFormKey Reference, FalloutFormKey Base,
    string BaseSha256, FalloutFormKey ModelOwner, string ModelOwnerSha256,
    FalloutFormKey Package, string PackageSha256, string Error, FalloutSandboxMotionPrefix Prefix,
    float[] Position, float[] Basis, float[] Velocity, FalloutActorResidualPose Pose,
    FalloutActorAnimationSnapshot Animation, FalloutActorPackageMotion? PriorMotion,
    ulong SoundRandomState, FalloutActorActivitySnapshot Activity, double PollRemaining,
    FalloutScheduleTime? ScheduleTime, long QuestRevision, long SelectionAttempts,
    long EventRevision, string LastEvent, FalloutFormKey LastPackage)
{
    internal string CaptureBlocker => FalloutSandboxPackage.CaptureBlockerPrefix + Error;

    internal void Validate()
    {
        if (!FalloutActorFurnitureContinuation.ValidKey(Reference) ||
            !FalloutActorFurnitureContinuation.ValidKey(Base) || !FalloutActorFurnitureContinuation.ValidKey(ModelOwner) ||
            !FalloutActorFurnitureContinuation.ValidKey(Package) ||
            !FalloutActorFurnitureContinuation.ValidHash(BaseSha256) ||
            !FalloutActorFurnitureContinuation.ValidHash(ModelOwnerSha256) ||
            !FalloutActorFurnitureContinuation.ValidHash(PackageSha256) ||
            string.IsNullOrWhiteSpace(Error) || !FalloutSandboxMotionException.IsRetainedFailure(Error) ||
            Prefix is null || Pose is null || Animation is null || Activity is null || Activity.InCombat ||
            Position is not { Length: 3 } || Basis is not { Length: 9 } || Velocity is not { Length: 3 } ||
            Position.Concat(Basis).Concat(Velocity).Any(value => !float.IsFinite(value)) ||
            !double.IsFinite(PollRemaining) || PollRemaining < 0 || QuestRevision < -1 || QuestRevision == long.MaxValue ||
            SelectionAttempts <= 0 || SelectionAttempts == long.MaxValue ||
            EventRevision <= 0 || EventRevision == long.MaxValue || LastEvent != "POBA" || LastPackage != Package ||
            ScheduleTime is { } time && (time.Month is < 0 or > 11 || time.Date is < 1 or > 31 ||
                time.Weekday is < 0 or > 6 || !float.IsFinite(time.Hour) || time.Hour is < 0 or >= 24))
            throw new InvalidDataException("Saved stopped Sandbox motion is invalid.");
        Prefix.Validate(); Pose.Validate(); Activity.Validate(); FalloutActorAnimationState.Validate(Animation);
        var x = new Vector3(Basis[0], Basis[1], Basis[2]);
        var y = new Vector3(Basis[3], Basis[4], Basis[5]);
        var z = new Vector3(Basis[6], Basis[7], Basis[8]);
        var extent = x.LengthSquared();
        if (extent <= 0 || !float.IsFinite(extent) || Math.Abs(y.LengthSquared() - extent) > extent * .001 ||
            Math.Abs(z.LengthSquared() - extent) > extent * .001 || Math.Abs(Vector3.Dot(x, y)) > extent * .001 ||
            Math.Abs(Vector3.Dot(x, z)) > extent * .001 || Math.Abs(Vector3.Dot(y, z)) > extent * .001 ||
            Vector3.Dot(Vector3.Cross(x, y), z) <= 0)
            throw new InvalidDataException("Saved stopped Sandbox pose has an invalid basis.");
        if (PriorMotion is { } motion)
        {
            motion.Validate();
            if (motion.Package != Package || !motion.PackageSha256.Equals(PackageSha256, StringComparison.OrdinalIgnoreCase) ||
                !motion.Animation.Equals(Animation.Resource, StringComparison.OrdinalIgnoreCase) ||
                !motion.AnimationSha256.Equals(Animation.Sha256, StringComparison.OrdinalIgnoreCase) ||
                motion.Patrol is not null || motion.Escort is not null || motion.EditorTravel is not null ||
                motion.Travel is not null || motion.Guard is not null || motion.AccompanyPortal is not null || motion.DialogueCompleted)
                throw new InvalidDataException("Stopped Sandbox retains an independent prior motion owner.");
        }
    }

    internal void Validate(FalloutPluginStack records, FalloutReferenceInstance actor)
    {
        Validate();
        if (actor.Reference != Reference || actor.Base != Base || records.GetEffective(Base).Signature != "CREA" ||
            actor.PackageBindingFailure is not null || actor.SandboxAdmissionFailure is not null ||
            actor.DeferredPackageContinuation is not null || actor.SelectionFailure is not null ||
            actor.PendingPackageSelection is not null || actor.StoppedRetirement is not null ||
            actor.FurnitureContinuation is not null || actor.DialogueContinuation is not null || actor.PackageIdle is not null ||
            actor.Engagement is not null || actor.Unconscious || actor.Restrained || actor.KnockedDown ||
            actor.PackageAssignment is { } assignment && (assignment.Package != Package || assignment.Done ||
                !assignment.Sha256.Equals(PackageSha256, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Stopped Sandbox motion conflicts with its actor or procedure owners.");
        if (actor.Animation.Capture() != Animation || actor.SoundRandom.State != SoundRandomState ||
            !SameMotion(PriorMotion, actor.PackageMotion))
            throw new InvalidDataException("Stopped Sandbox clock, randomness or prior observation differs from reference state.");
        RequireHash(Base, BaseSha256); RequireHash(ModelOwner, ModelOwnerSha256); RequireHash(Package, PackageSha256);
        var package = records.GetEffective(Package);
        if (package.Signature != "PACK") throw new InvalidDataException("Stopped Sandbox source is not PACK.");
        FalloutSandboxPackage.Read(package).RequireRetainedStart(records, actor);
        var model = FalloutActorTemplateOwner.Resolve(records, records.GetEffective(Base), 64, actor.Templates);
        if (model.FormKey != ModelOwner) throw new InvalidDataException("Stopped Sandbox model owner changed.");
        if (Error == FalloutActorZeroTurnException.SourceError)
        {
            var turn = model.ReadSubrecords().Where(field => field.Signature == "TNAM").ToArray();
            var stats = FalloutActorTemplateOwner.Resolve(records, records.GetEffective(Base), 2, actor.Templates);
            var speed = stats.ReadSubrecords().Where(field => field.Signature == "ACBS").ToArray();
            if (turn.Length != 1 || turn[0].Data.Length != 4 ||
                BinaryPrimitives.ReadSingleLittleEndian(turn[0].Data.Span) != 0 ||
                speed.Length != 1 || speed[0].Data.Length != 24 ||
                BinaryPrimitives.ReadUInt16LittleEndian(speed[0].Data.Span[14..]) == 0)
                throw new InvalidDataException("Stopped Sandbox zero-turn failure differs from its source movement fields.");
        }
        var owner = FalloutActorTemplateOwner.Resolve(records, records.GetEffective(Base), 32, actor.Templates);
        if (!owner.ReadSubrecords().Any(field => field.Signature == "PKID" && field.Data.Length == 4 &&
            owner.Plugin.AdjustFormId(BinaryPrimitives.ReadUInt32LittleEndian(field.Data.Span)) == Package))
            throw new InvalidDataException("Stopped Sandbox is absent from the winning actor package list.");

        void RequireHash(FalloutFormKey key, string hash)
        {
            if (!FalloutActorFurnitureContinuation.RecordHash(records.GetEffective(key)).Equals(hash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Stopped Sandbox source {key} changed.");
        }
    }

    internal void RestoreEvents(FalloutPluginStack records, FalloutReferenceInstance actor, FalloutPackageEvents events)
    {
        Validate(records, actor);
        if (events.Active is not null || events.Done || events.Error is not null || events.Revision != 0 ||
            events.LastEvent is not null || events.LastPackage is not null)
            throw new InvalidDataException("Stopped Sandbox restoration requires fresh source events.");
        var source = FalloutScriptPackage.Read(records.GetEffective(Package));
        events.Restore(source, false);
        events.RestoreHistory(EventRevision, LastEvent, LastPackage);
    }

    private static bool SameMotion(FalloutActorPackageMotion? left, FalloutActorPackageMotion? right)
    {
        if (left is null || right is null) return left is null && right is null;
        return left.Package == right.Package && left.PackageSha256.Equals(right.PackageSha256, StringComparison.OrdinalIgnoreCase) &&
            left.Animation.Equals(right.Animation, StringComparison.OrdinalIgnoreCase) &&
            left.AnimationSha256.Equals(right.AnimationSha256, StringComparison.OrdinalIgnoreCase) &&
            left.Seconds == right.Seconds && left.StartPending == right.StartPending &&
            left.Position.SequenceEqual(right.Position) && left.Rotation.SequenceEqual(right.Rotation) &&
            right.Patrol is null && right.Escort is null && right.EditorTravel is null && right.Travel is null &&
            right.Guard is null && right.AccompanyPortal is null && !right.DialogueCompleted &&
            (left.Wander is null ? right.Wander is null : right.Wander is { } wander &&
                left.Wander.Cell == wander.Cell && left.Wander.NavigationSha256.Equals(wander.NavigationSha256, StringComparison.OrdinalIgnoreCase) &&
                left.Wander.Destination.SequenceEqual(wander.Destination) && left.Wander.Arrived == wander.Arrived) &&
            (left.Sandbox is null ? right.Sandbox is null : right.Sandbox is { } sandbox &&
                left.Sandbox.Activity == sandbox.Activity && left.Sandbox.StartedHour == sandbox.StartedHour &&
                left.Sandbox.Duration == sandbox.Duration && left.Sandbox.Target == sandbox.Target &&
                left.Sandbox.Preferences.SequenceEqual(sandbox.Preferences));
    }

    internal FalloutActorSandboxMotionFailure Copy() => this with
    {
        Prefix = Prefix.Copy(),
        Position = (float[])Position.Clone(),
        Basis = (float[])Basis.Clone(),
        Velocity = (float[])Velocity.Clone(),
        Pose = Pose.Copy(),
        PriorMotion = PriorMotion?.Copy()
    };
}
