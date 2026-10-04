using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal sealed record FalloutFurnitureClipSnapshot(FalloutFormKey Idle, string IdleSha256,
    string Resource, string Sha256)
{
    internal void Validate()
    {
        if (!FalloutActorFurnitureContinuation.ValidKey(Idle) || string.IsNullOrWhiteSpace(Resource) ||
            !FalloutActorFurnitureContinuation.ValidHash(IdleSha256) || !FalloutActorFurnitureContinuation.ValidHash(Sha256))
            throw new InvalidDataException("Saved furniture animation source is invalid.");
    }
}

// A source-selected search or physical chair phase. Pending capsule searches
// remain refused until their route and contact continuation are owned.
internal sealed record FalloutActorFurnitureContinuation(FalloutActorPackageAssignment Assignment,
    long EventRevision, string? LastEvent, FalloutFormKey? LastPackage, string? LastPackageSha256,
    int Phase, double SearchRemaining, float[] Pose, FalloutFormKey? SelectedPackage,
    FalloutFormKey? PendingPackage, ulong RandomState, double PollRemaining,
    FalloutScheduleTime? ScheduleTime, FalloutFaceBlinkSnapshot? Blink,
    FalloutFormKey? Furniture = null, string? FurnitureSha256 = null, string? Model = null,
    string? ModelSha256 = null, FalloutFurnitureSeat? Seat = null, float[]? Occupied = null,
    FalloutFurnitureClipSnapshot? Clip = null, bool InitialPlacement = false, FalloutActorPackageIdleState? IdleState = null)
{
    internal const string CaptureBlocker = "Furniture search, reservation and physical animation continuation.";
    internal static bool ValidKey(FalloutFormKey value) => !string.IsNullOrWhiteSpace(value.OwnerPlugin) &&
        value.ObjectId is > 0 and <= FalloutFormKey.ObjectIdMask;
    internal static bool ValidHash(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);

    internal void Validate()
    {
        (Assignment ?? throw new InvalidDataException("Saved furniture has no package assignment.")).Validate();
        if (Phase is not (0 or 2 or 3 or 4) || !double.IsFinite(SearchRemaining) || SearchRemaining is < 0 or > .5 ||
            !double.IsFinite(PollRemaining) || PollRemaining < 0 || EventRevision < 0 || EventRevision == long.MaxValue ||
            SelectedPackage is { } selected && !ValidKey(selected) || PendingPackage is { } pending && !ValidKey(pending) ||
            (EventRevision == 0 ? LastEvent is not null || LastPackage is not null || LastPackageSha256 is not null :
                LastEvent is not ("POBA" or "POCA" or "POEA") || LastPackage is not { } previous || !ValidKey(previous) || !ValidHash(LastPackageSha256)) ||
            Assignment.Done != (Phase is 3 or 4) ||
            ScheduleTime is { } time && (time.Month is < 0 or > 11 || time.Date is < 1 or > 31 || time.Weekday is < 0 or > 6 ||
                !float.IsFinite(time.Hour) || time.Hour is < 0 or >= 24))
            throw new InvalidDataException("Saved furniture phase, source selection or lifecycle is invalid.");
        ValidatePose(Pose);
        Blink?.Validate();
        if (Phase == 0)
        {
            if (Furniture is not null || FurnitureSha256 is not null || Model is not null || ModelSha256 is not null ||
                Seat is not null || Occupied is not null || Clip is not null || InitialPlacement || PendingPackage is not null)
                throw new InvalidDataException("Furniture search has an unowned seat or transition.");
            return;
        }
        if (Furniture is not { } reference || !ValidKey(reference) || !ValidHash(FurnitureSha256) ||
            string.IsNullOrWhiteSpace(Model) || !ValidHash(ModelSha256) || Seat is null || !ValidKey(Seat.Furniture) ||
            Seat.Index is < 0 or >= 30 || Seat.Marker is null || Seat.MarkerId != Seat.Marker.PositionReference1 ||
            Seat.Marker.PositionReference1 != Seat.Marker.PositionReference2 ||
            Seat.PlacementOffset is not { Length: 3 } || Seat.PlacementOffset.Any(value => !float.IsFinite(value)) ||
            !float.IsFinite(Seat.HeadingDelta) || !float.IsFinite(Seat.Marker.Offset.X) ||
            !float.IsFinite(Seat.Marker.Offset.Y) || !float.IsFinite(Seat.Marker.Offset.Z) || Clip is null ||
            Phase != 4 && PendingPackage is not null && PendingPackage != SelectedPackage)
            throw new InvalidDataException("Saved physical furniture has no complete source reservation or animation.");
        ValidatePose(Occupied!);
        Clip.Validate();
    }

    internal static void ValidatePose(float[] pose)
    {
        if (pose is not { Length: 12 } || pose.Any(value => !float.IsFinite(value)))
            throw new InvalidDataException("Saved furniture pose has an invalid extent or value.");
        var x = new Vector3(pose[0], pose[1], pose[2]); var y = new Vector3(pose[3], pose[4], pose[5]);
        var z = new Vector3(pose[6], pose[7], pose[8]); var extent = x.LengthSquared();
        if (extent <= 0 || !float.IsFinite(extent) || Math.Abs(y.LengthSquared() - extent) > extent * .001 ||
            Math.Abs(z.LengthSquared() - extent) > extent * .001 || Math.Abs(Vector3.Dot(x, y)) > extent * .001 ||
            Math.Abs(Vector3.Dot(x, z)) > extent * .001 || Math.Abs(Vector3.Dot(y, z)) > extent * .001 ||
            Vector3.Dot(Vector3.Cross(x, y), z) <= 0)
            throw new InvalidDataException("Saved furniture pose has an invalid basis.");
    }

    internal void Validate(FalloutPluginStack records, FalloutReferenceWorld world, FalloutReferenceInstance actor)
    {
        Validate();
        if (records.GetEffective(actor.Base).Signature != "NPC_" || actor.PackageAssignment != Assignment)
            throw new InvalidDataException("Saved furniture disagrees with its NPC or package owner.");
        void RequireHash(FalloutFormKey form, string signature, string hash)
        {
            var record = records.GetEffective(form);
            if (record.Signature != signature || !RecordHash(record).Equals(hash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Saved furniture differs from its winning source declaration.");
        }
        RequireHash(Assignment.Package, "PACK", Assignment.Sha256);
        if (LastPackage is { } last) RequireHash(last, "PACK", LastPackageSha256!);
        if (SelectedPackage is { } selected && records.GetEffective(selected).Signature != "PACK" ||
            PendingPackage is { } pending && records.GetEffective(pending).Signature != "PACK")
            throw new InvalidDataException("Saved furniture selection is not a source PACK.");
        var package = records.GetEffective(Assignment.Package);
        var source = FalloutScriptPackage.Read(package);
        if (IdleState is { } idles) idles.Validate(records, Assignment.Package);
        else if (source.Idles.Count != 0) throw new NotSupportedException("Furniture package idles require their retained collection continuation.");
        if (source.Procedure == 0) _ = FalloutFindFurniturePackage.Read(package);
        else if (source.Procedure != 6 || source.LocationType != 0 || source.LocationRadius != 0 ||
            source.LocationReference != Furniture) throw new InvalidDataException("Saved furniture has no supported source procedure.");
        if (Phase == 0 && source.Procedure != 0) throw new InvalidDataException("Saved search has no source Find Furniture procedure.");
        if (Furniture is { } furniture)
        {
            RequireHash(Seat!.Furniture, "FURN", FurnitureSha256!);
            if (world.Get(furniture).Base != Seat.Furniture) throw new InvalidDataException("Saved seat has a different source furniture base.");
            var flags = records.GetEffective(Seat.Furniture).ReadSubrecords().Single(field => field.Signature == "MNAM").Data;
            if (flags.Length != 4 || (BinaryPrimitives.ReadUInt32LittleEndian(flags.Span) & (1u << Seat.Index)) == 0)
                throw new InvalidDataException("Saved reservation has no enabled source marker.");
            RequireHash(Clip!.Idle, "IDLE", Clip.IdleSha256);
            if (actor.Animation.Resource.Length == 0 || !actor.Animation.Resource.Equals(Clip.Resource, StringComparison.OrdinalIgnoreCase) ||
                !actor.Animation.Sha256.Equals(Clip.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Saved furniture clock differs from its selected animation.");
        }
        if (Blink is not null && Blink.Settings != FalloutFaceBlinkSettings.Read(records))
            throw new InvalidDataException("Saved furniture blink settings differ from the winning source.");
    }

    internal static string RecordHash(FalloutPluginRecord record) => Convert.ToHexString(SHA256.HashData(record.ReadData()));
    internal FalloutActorFurnitureContinuation Copy() => this with
    {
        Pose = (float[])Pose.Clone(),
        Occupied = Occupied is null ? null : (float[])Occupied.Clone(),
        Seat = Seat is null ? null : Seat with { PlacementOffset = (float[])Seat.PlacementOffset.Clone() },
        Blink = Blink?.Copy(),
        IdleState = IdleState?.Copy()
    };
}
