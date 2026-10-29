using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal enum FalloutSandboxMarkerPhase { Approach, Readiness, Dispatch, Playback, Retiring }

internal sealed record FalloutActorSandboxMarkerContinuation(FalloutActorPackageAssignment Assignment,
    long EventRevision, string? LastEvent, FalloutFormKey? LastPackage, string? LastPackageSha256,
    ulong RandomState, double PollRemaining, FalloutScheduleTime? ScheduleTime, FalloutFormKey? SelectedPackage,
    FalloutActorActivitySnapshot Activity, FalloutFaceBlinkSnapshot? Blink,
    FalloutFormKey Marker, FalloutFormKey Source, string SourceSha256, FalloutSandboxMarkerPhase Phase,
    float[]? Floor, bool PendingSelectionAccepted = false, FalloutFormKey? PendingPackage = null, bool PendingSelectionForced = false)
{
    internal const string CaptureBlocker = "Sandbox marker approach, request and reservation continuation.";
    internal FalloutFormKey Package => Assignment.Package;
    internal string PackageSha256 => Assignment.Sha256;

    internal void Validate()
    {
        Assignment.Validate(); Activity.Validate(); Blink?.Validate();
        if (!FalloutActorFurnitureContinuation.ValidKey(Package) || !FalloutActorFurnitureContinuation.ValidKey(Marker) ||
            !FalloutActorFurnitureContinuation.ValidKey(Source) || !FalloutActorFurnitureContinuation.ValidHash(PackageSha256) ||
            !FalloutActorFurnitureContinuation.ValidHash(SourceSha256) || !Enum.IsDefined(Phase) ||
            Floor is not null && (Floor.Length != 3 || Floor.Any(value => !float.IsFinite(value))) ||
            PendingPackage is { } pending && !FalloutActorFurnitureContinuation.ValidKey(pending) ||
            !PendingSelectionAccepted && (PendingPackage is not null || PendingSelectionForced) ||
            PendingSelectionAccepted && Phase != FalloutSandboxMarkerPhase.Retiring || Assignment.Done ||
            !double.IsFinite(PollRemaining) || PollRemaining < 0 || EventRevision < 0 || EventRevision == long.MaxValue ||
            (EventRevision == 0 ? LastEvent is not null || LastPackage is not null || LastPackageSha256 is not null :
                LastEvent is not ("POBA" or "POCA" or "POEA") || LastPackage is null || !FalloutActorFurnitureContinuation.ValidHash(LastPackageSha256)) ||
            SelectedPackage is { } selected && !FalloutActorFurnitureContinuation.ValidKey(selected))
            throw new InvalidDataException("Saved Sandbox marker has invalid source, physical or selection state.");
    }

    internal void Validate(FalloutPluginStack records, FalloutReferenceWorld world, FalloutReferenceInstance actor)
    {
        Validate();
        var package = records.GetEffective(Package);
        var marker = world.Get(Marker);
        var source = records.GetEffective(Source);
        if (records.GetEffective(actor.Base).Signature != "NPC_" || package.Signature != "PACK" || source.Signature != "IDLM" ||
            !FalloutActorFurnitureContinuation.RecordHash(package).Equals(PackageSha256, StringComparison.OrdinalIgnoreCase) ||
            !FalloutActorFurnitureContinuation.RecordHash(source).Equals(SourceSha256, StringComparison.OrdinalIgnoreCase) || marker.Base != Source ||
            actor.PackageMotion is not { Sandbox: { Activity: FalloutSandboxActivity.IdleMarker } activity } motion ||
            motion.Package != Package || activity.Target != Marker || actor.PackageAssignment is not { Done: false } assignment || assignment.Package != Package)
            throw new InvalidDataException("Saved Sandbox marker disagrees with its source or parent package.");
        _ = FalloutSandboxPackage.Read(package);
        if (LastPackage is { } previous && !FalloutActorFurnitureContinuation.RecordHash(records.GetEffective(previous))
            .Equals(LastPackageSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Saved Sandbox marker event package differs from its source.");
        if (Blink is not null && Blink.Settings != FalloutFaceBlinkSettings.Read(records))
            throw new InvalidDataException("Saved Sandbox marker blink settings differ from source.");
        if (Phase == FalloutSandboxMarkerPhase.Playback && actor.IdleRequests is null)
            throw new InvalidDataException("Sandbox marker playback has no retained source request owner.");
    }

    internal FalloutActorSandboxMarkerContinuation Copy() => this with { Floor = Floor?.ToArray(), Blink = Blink?.Copy() };
}
