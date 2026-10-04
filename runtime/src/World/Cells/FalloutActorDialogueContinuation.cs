using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

// A settled procedure waiting for its authored dialogue target. An outstanding
// conversation, legacy route or moving native capsule remains a separate owner.
internal sealed record FalloutActorDialogueContinuation(FalloutActorPackageAssignment Assignment,
    long EventRevision, string? LastEvent, FalloutFormKey? LastPackage, string? LastPackageSha256,
    float[] Pose, float[] WaitPosition, bool WaitReached, bool NativeMovement, ulong RandomState,
    double PollRemaining, FalloutScheduleTime? ScheduleTime, FalloutFaceBlinkSnapshot? Blink,
    FalloutActorPackageIdleState IdleState, float[]? TargetPosition = null, float[]? TargetFloor = null)
{
    internal const string CaptureBlocker = "Dialogue target wait and source procedure continuation.";
    internal void Validate()
    {
        (Assignment ?? throw new InvalidDataException("Saved dialogue has no package assignment.")).Validate();
        if (Assignment.Done || !WaitReached || IdleState is null || !double.IsFinite(PollRemaining) || PollRemaining < 0 ||
            EventRevision <= 0 || EventRevision == long.MaxValue || LastEvent is not ("POBA" or "POCA" or "POEA") ||
            LastPackage is not { } last || !FalloutActorFurnitureContinuation.ValidKey(last) ||
            !FalloutActorFurnitureContinuation.ValidHash(LastPackageSha256) ||
            (TargetPosition is null) != (TargetFloor is null) ||
            ScheduleTime is { } time && (time.Month is < 0 or > 11 || time.Date is < 1 or > 31 || time.Weekday is < 0 or > 6 ||
                !float.IsFinite(time.Hour) || time.Hour is < 0 or >= 24))
            throw new InvalidDataException("Saved dialogue wait has an invalid lifecycle or source timer.");
        FalloutActorFurnitureContinuation.ValidatePose(Pose);
        static void Point(float[] point)
        {
            if (point is not { Length: 3 } || point.Any(value => !float.IsFinite(value)))
                throw new InvalidDataException("Saved dialogue position is invalid.");
        }
        Point(WaitPosition); if (TargetPosition is not null) { Point(TargetPosition); Point(TargetFloor!); }
        Blink?.Validate();
    }

    internal void Validate(FalloutPluginStack records, FalloutReferenceInstance actor)
    {
        Validate();
        if (records.GetEffective(actor.Base).Signature != "NPC_" || actor.PackageAssignment != Assignment ||
            actor.FurnitureContinuation is not null || actor.PackageBindingFailure is not null || actor.SelectionFailure is not null)
            throw new InvalidDataException("Saved dialogue wait conflicts with its actor procedure.");
        var package = records.GetEffective(Assignment.Package);
        if (package.Signature != "PACK" || FalloutActorFurnitureContinuation.RecordHash(package) != Assignment.Sha256 ||
            records.GetEffective(LastPackage!.Value).Signature != "PACK" ||
            FalloutActorFurnitureContinuation.RecordHash(records.GetEffective(LastPackage.Value)) != LastPackageSha256)
            throw new InvalidDataException("Saved dialogue wait differs from its winning source.");
        var source = FalloutScriptPackage.Read(package);
        var dialogue = FalloutDialoguePackage.Read(package);
        if (NativeMovement != (source.LocationType is null) || source.LocationType is not null &&
            (source.LocationType is not (0 or 2) || source.LocationRadius != 0) ||
            dialogue.TriggerLocation is not null && records.RuntimeFormId(dialogue.Target) != 0x14 && dialogue.ControlsTargetMovement)
            throw new InvalidDataException("Saved dialogue wait has no admitted source movement owner.");
        IdleState.Validate(records, Assignment.Package);
        if (Blink is not null && Blink.Settings != FalloutFaceBlinkSettings.Read(records))
            throw new InvalidDataException("Saved dialogue wait blink settings differ from their source.");
    }

    internal FalloutActorDialogueContinuation Copy() => this with
    {
        Pose = (float[])Pose.Clone(),
        WaitPosition = (float[])WaitPosition.Clone(),
        Blink = Blink?.Copy(),
        IdleState = IdleState.Copy(),
        TargetPosition = TargetPosition is null ? null : (float[])TargetPosition.Clone(),
        TargetFloor = TargetFloor is null ? null : (float[])TargetFloor.Clone()
    };
}
