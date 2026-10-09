using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Gameplay.State;

internal enum FalloutPlayerFurnitureKind { Sitting, Sleeping }
internal enum FalloutPlayerFurniturePhase { None, Approaching, Entering, Occupied, Exiting }
internal enum FalloutPlayerKnockdownPhase { Upright, Simulating, Recovering }
internal sealed record FalloutPlayerPhysicalFailure(long Attempt, string Operation, string Error,
    FalloutPlayerFurniturePhase FurniturePrefix, FalloutPlayerKnockdownPhase KnockdownPrefix);
internal sealed record FalloutPlayerPhysicalAnimation(FalloutFurnitureClipSnapshot Source,
    double Seconds, bool StartPending, int? AttemptedKeyOrdinal = null, long? AttemptedKeyCycle = null)
{
    internal void Validate()
    {
        (Source ?? throw new InvalidDataException("Player physical animation has no source.")).Validate();
        if (!double.IsFinite(Seconds) || Seconds < 0 || StartPending && Seconds != 0 ||
            (AttemptedKeyOrdinal is null) != (AttemptedKeyCycle is null) || AttemptedKeyOrdinal < 0 || AttemptedKeyCycle < 0)
            throw new InvalidDataException("Player physical animation clock or source-key cursor is invalid.");
    }
}
internal sealed record FalloutPlayerFurnitureSnapshot(FalloutFormKey Reference, string ReferenceSha256,
    FalloutFormKey Cell, string FurnitureSha256, string Model, string ModelSha256,
    FalloutFurnitureSeat Seat, FalloutPlayerFurnitureKind Kind, FalloutPlayerFurniturePhase Phase,
    float[] Occupied, float[] Approach, float[] Pose, IReadOnlyList<float[]>? Path, int Waypoint,
    double PhaseSeconds, float LookYaw, FalloutPlayerPhysicalAnimation? Animation,
    IReadOnlyDictionary<int, FalloutFurnitureClipSnapshot> Clips,
    float[] Placement, FalloutReferencePlacement ReferencePlacement,
    FalloutFurnitureClipSnapshot? Camera = null, string? CameraSkeletonSha256 = null,
    bool BedPublicationAttempted = false, bool? PendingTransfer = null)
{
    internal void Validate()
    {
        if (!FalloutActorFurnitureContinuation.ValidKey(Reference) || !FalloutActorFurnitureContinuation.ValidKey(Cell) ||
            !FalloutPlayerPhysicalSource.Digest(ReferenceSha256) || !FalloutPlayerPhysicalSource.Digest(FurnitureSha256) ||
            !FalloutPlayerPhysicalSource.Digest(ModelSha256) || string.IsNullOrWhiteSpace(Model) || Seat is null ||
            !FalloutActorFurnitureContinuation.ValidKey(Seat.Furniture) || Seat.Index is < 0 or >= 30 ||
            Seat.Marker is null || !float.IsFinite(Seat.Marker.Offset.X) || !float.IsFinite(Seat.Marker.Offset.Y) ||
            !float.IsFinite(Seat.Marker.Offset.Z) || Seat.Kind != Kind || Seat.MarkerId != Seat.Marker.PositionReference1 ||
            Seat.Marker.PositionReference1 != Seat.Marker.PositionReference2 ||
            Seat.PlacementOffset is not { Length: 3 } || Seat.PlacementOffset.Any(value => !float.IsFinite(value)) ||
            !float.IsFinite(Seat.HeadingDelta) || !Enum.IsDefined(Kind) || Phase is FalloutPlayerFurniturePhase.None ||
            !Enum.IsDefined(Phase) || !double.IsFinite(PhaseSeconds) || PhaseSeconds < 0 || !float.IsFinite(LookYaw) ||
            PendingTransfer is null || PendingTransfer == true && BedPublicationAttempted ||
            Clips is null || !Clips.Keys.Order().SequenceEqual(new[] { 1, 2, 4 }) ||
            Waypoint < 0 || Waypoint > (Path?.Count ?? 0) ||
            Path?.Any(point => point is not { Length: 3 } || point.Any(value => !float.IsFinite(value))) == true ||
            Phase != FalloutPlayerFurniturePhase.Approaching && Animation is null ||
            Phase == FalloutPlayerFurniturePhase.Approaching && Animation is not null ||
            (Camera is null) != (CameraSkeletonSha256 is null) ||
            CameraSkeletonSha256 is not null && !FalloutPlayerPhysicalSource.Digest(CameraSkeletonSha256) ||
            BedPublicationAttempted && (Kind != FalloutPlayerFurnitureKind.Sleeping || Phase is FalloutPlayerFurniturePhase.Approaching or FalloutPlayerFurniturePhase.Entering))
            throw new InvalidDataException("Player furniture continuation has no complete source phase/reservation.");
        foreach (var clip in Clips.Values) clip.Validate();
        Animation?.Validate(); Camera?.Validate();
        if (Animation is { } animation && animation.Source != Clips[Phase == FalloutPlayerFurniturePhase.Occupied ? 1 : (int)Phase])
            throw new InvalidDataException("Player furniture selected source differs from its actual phase.");
        FalloutActorFurnitureContinuation.ValidatePose(Occupied);
        FalloutActorFurnitureContinuation.ValidatePose(Approach);
        FalloutActorFurnitureContinuation.ValidatePose(Pose);
        FalloutActorFurnitureContinuation.ValidatePose(Placement);
        (ReferencePlacement ?? throw new InvalidDataException("Saved furniture has no authoritative reference placement.")).Validate();
        if (ReferencePlacement.Cell != Cell) throw new InvalidDataException("Saved furniture placement has a different source cell.");
    }
}
internal sealed record FalloutPlayerKnockdownSnapshot(FalloutPlayerKnockdownPhase Phase,
    string SkeletonPath, string SkeletonSha256, float[] Pose, FalloutActorRagdollState? Ragdoll,
    FalloutPlayerPhysicalAnimation? Recovery)
{
    internal void Validate()
    {
        if (Phase is FalloutPlayerKnockdownPhase.Upright || !Enum.IsDefined(Phase) ||
            string.IsNullOrWhiteSpace(SkeletonPath) || !FalloutPlayerPhysicalSource.Digest(SkeletonSha256) ||
            (Phase == FalloutPlayerKnockdownPhase.Simulating ? Ragdoll is null || Recovery is not null : Recovery is null || Ragdoll is not null))
            throw new InvalidDataException("Player knockdown continuation has an invalid physical owner.");
        FalloutActorFurnitureContinuation.ValidatePose(Pose);
        Ragdoll?.Validate(); Recovery?.Validate();
        if (Ragdoll is { } rig && !rig.SkeletonSha256.Equals(SkeletonSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Player ragdoll differs from its actual skeleton source.");
    }
}
internal sealed record FalloutPlayerPhysicalSnapshot(FalloutPlayerPhysicalSource Source, long Attempt,
    bool Sleeping, ulong RandomState, FalloutAnimationSoundEventsSnapshot Sounds,
    FalloutPlayerFurnitureSnapshot? Furniture = null, FalloutPlayerKnockdownSnapshot? Knockdown = null,
    FalloutPlayerPhysicalFailure? Failure = null, FalloutPlayerPhysicalPose? NativePose = null)
{
    internal void Validate()
    {
        (Source ?? throw new InvalidDataException("Player physical source is absent.")).Validate();
        (Sounds ?? throw new InvalidDataException("Player physical sound state is absent.")).Validate();
        Furniture?.Validate(); Knockdown?.Validate(); NativePose?.Validate();
        if (Attempt < 0 || Attempt == long.MaxValue || Sounds.Reference.ObjectId != 0x14 ||
            Furniture is not null && Knockdown is not null ||
            (Furniture is not null || Knockdown is not null) && NativePose is null ||
            Failure is null && (Furniture?.Animation?.AttemptedKeyOrdinal is not null || Knockdown?.Recovery?.AttemptedKeyOrdinal is not null) ||
            Failure is { } fault && (fault.Attempt <= 0 || fault.Attempt > Attempt ||
                string.IsNullOrWhiteSpace(fault.Operation) || string.IsNullOrWhiteSpace(fault.Error) ||
                fault.FurniturePrefix != (Furniture?.Phase ?? FalloutPlayerFurniturePhase.None) ||
                fault.KnockdownPrefix != (Knockdown?.Phase ?? FalloutPlayerKnockdownPhase.Upright)))
            throw new InvalidDataException("Player physical snapshot has an invalid source operation or committed prefix.");
    }
}
