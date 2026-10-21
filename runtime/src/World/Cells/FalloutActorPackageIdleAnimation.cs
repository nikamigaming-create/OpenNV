using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

// The already selected collection IDLE. Its KF phase is independent of the
// furniture base pose and of the collection cursor and replay delays.
internal sealed record FalloutActorPackageIdleAnimation(FalloutFormKey Idle, string IdleSha256,
    string Resource, string Sha256, FalloutIdleAnimationPlaybackSnapshot Clock, long Revision,
    FalloutActorResidualPose? ResidualPose = null, IReadOnlyList<FalloutActorIdleObjectBinding>? Objects = null)
{
    internal void Validate()
    {
        if (!FalloutActorFurnitureContinuation.ValidKey(Idle) || !FalloutActorFurnitureContinuation.ValidHash(IdleSha256) ||
            !FalloutActorFurnitureContinuation.ValidHash(Sha256) || string.IsNullOrWhiteSpace(Resource) ||
            Clock is null || Revision <= 0 || Revision == long.MaxValue)
            throw new InvalidDataException("Saved package collection animation has invalid source ownership.");
        Clock.Validate();
        if (Objects is not null)
        {
            if (Objects.Any(value => value is null)) throw new InvalidDataException("Saved idle animation object is absent.");
            foreach (var binding in Objects) binding.Validate();
            if (Objects.Select(value => value.Form).Distinct().Count() != Objects.Count)
                throw new InvalidDataException("Saved idle animation objects repeat a source owner.");
        }
        (ResidualPose ?? throw new InvalidDataException("Saved collection animation is missing its residual pose.")).Validate();
        if (Clock.Complete) throw new InvalidDataException("Saved collection animation has already released its pose.");
    }

    internal void Validate(FalloutPluginStack records, FalloutFormKey package)
    {
        ValidateSource(records);
        if (!FalloutScriptPackage.Read(records.GetEffective(package)).Idles.Contains(Idle))
            throw new InvalidDataException("Saved collection animation is absent from its source package.");
    }

    internal void ValidateSource(FalloutPluginStack records)
    {
        Validate();
        var record = records.GetEffective(Idle);
        if (record.Signature != "IDLE" || FalloutActorFurnitureContinuation.RecordHash(record) != IdleSha256)
            throw new InvalidDataException("Saved collection animation differs from its winning IDLE.");
        var source = FalloutActorIdleSource.Resolve(records, record);
        if (!source.AnimationPath.Equals(Resource, StringComparison.OrdinalIgnoreCase) ||
            !FalloutIdleAnimationData.Read(record).AdmitsAdditionalLoops(Clock.SelectedAdditionalLoops))
            throw new InvalidDataException("Saved collection resource or chosen repeats differ from the source IDLE.");
        FalloutActorIdleObjectBinding.ValidateSources(records, source, Objects);
    }

    internal FalloutActorPackageIdleAnimation Copy() => this with { ResidualPose = ResidualPose?.Copy(), Objects = Objects?.ToArray() };
}
