using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

// A failed source predicate selection, with no active procedure. A later poll
// can reevaluate normally; restoring never repeats the consumed random reads.
internal sealed record FalloutActorSelectionFailure(FalloutFormKey Candidate, string Sha256, int Condition,
    string Error, float[] Pose, ulong RandomState, double PollRemaining, FalloutScheduleTime? ScheduleTime,
    bool SourceSelectionKnown, FalloutPackageRetirement Retirement, FalloutFaceBlinkSnapshot? Blink)
{
    internal void Validate()
    {
        if (!FalloutActorFurnitureContinuation.ValidKey(Candidate) || !FalloutActorFurnitureContinuation.ValidHash(Sha256) ||
            Condition < 0 || string.IsNullOrWhiteSpace(Error) || !double.IsFinite(PollRemaining) || PollRemaining < 0 || Retirement is null ||
            ScheduleTime is { } time && (time.Month is < 0 or > 11 || time.Date is < 1 or > 31 || time.Weekday is < 0 or > 6 ||
                !float.IsFinite(time.Hour) || time.Hour is < 0 or >= 24))
            throw new InvalidDataException("Saved failed AI selection is invalid.");
        FalloutActorFurnitureContinuation.ValidatePose(Pose);
        Retirement.Validate(); Blink?.Validate();
    }

    internal void Validate(FalloutPluginStack records, FalloutReferenceInstance actor)
    {
        Validate();
        if (records.GetEffective(actor.Base).Signature != "NPC_" || actor.PackageAssignment is not null ||
            actor.PackageBindingFailure is not null || actor.FurnitureContinuation is not null)
            throw new InvalidDataException("Failed selection has an active or conflicting procedure owner.");
        var package = records.GetEffective(Candidate);
        if (package.Signature != "PACK" || FalloutActorFurnitureContinuation.RecordHash(package) != Sha256 ||
            Condition >= FalloutCondition.Read(package).Count)
            throw new InvalidDataException("Saved failed predicate differs from its winning source.");
        var owner = FalloutActorTemplateOwner.Resolve(records, records.GetEffective(actor.Base), 32, actor.Templates);
        if (!owner.ReadSubrecords().Where(field => field.Signature == "PKID").Any(field => field.Data.Length == 4 &&
            owner.Plugin.AdjustFormId(System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(field.Data.Span)) == Candidate))
            throw new InvalidDataException("Failed predicate is absent from the actor's source package priority list.");
        Retirement.Validate(records);
        if (Blink is not null && Blink.Settings != FalloutFaceBlinkSettings.Read(records))
            throw new InvalidDataException("Saved failed selection blink settings differ from their source.");
    }

    internal FalloutActorSelectionFailure Copy() => this with { Pose = (float[])Pose.Clone(), Blink = Blink?.Copy() };
}
