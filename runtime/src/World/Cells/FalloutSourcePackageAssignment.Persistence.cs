using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal static partial class FalloutSourcePackageAssignment
{
    internal static void Validate(FalloutSourcePackageAssignmentCall call, FalloutProcessCommonSnapshot common)
    {
        ArgumentNullException.ThrowIfNull(call); call.Source.Validate(); call.Invocation.Actor.Validate();
        if (call.AssignmentSource is not { } assignment || assignment.Fields != call.Source)
            throw new InvalidDataException("Package command omitted its independent ordered source selector.");
        assignment.Validate();
        if (call.Source.Controls.Main.EngineSha256 != common.ExecutableSha256 || call.Stack != common.Stack ||
            call.Invocation.SourceCommand == Guid.Empty || call.Invocation.Process == Guid.Empty || call.Invocation.Epoch < 1 ||
            call.Invocation.ThreadId < 1 || common.Publications?.Any(publication => publication.Process == call.Invocation.Process) != true ||
            string.IsNullOrWhiteSpace(call.Producer) || call.Package.ObjectId == 0 || string.IsNullOrWhiteSpace(call.Package.OwnerPlugin) ||
            call.ReturnedChildren is null || !Enum.IsDefined(call.Step) || call.Step is not (FalloutSourcePackageAssignmentStep.Returned or FalloutSourcePackageAssignmentStep.Failed) ||
            !common.Current.Concat(common.Retired).Any(entry => entry.Source == call.Invocation.Actor && entry.Epoch == call.Invocation.Epoch) ||
            common.PackageRegistry?.Packages.Any(package => package.Form == call.Package) != true)
            throw new InvalidDataException("Package command lost its actual source/form/Actor/process/thread history.");
        var expected = new List<FalloutSourcePackageAssignmentStep> { FalloutSourcePackageAssignmentStep.SpecialPackage };
        if (call.SpecialSelected == true) expected.Add(FalloutSourcePackageAssignmentStep.SpecialCallback);
        expected.Add(FalloutSourcePackageAssignmentStep.Process);
        if (call.Process is not null)
        {
            expected.Add(FalloutSourcePackageAssignmentStep.PreviousPackage);
            if (call.PreviousPackage?.Fields.Fields.Package is not null)
            {
                expected.Add(FalloutSourcePackageAssignmentStep.PreviousEvent);
                expected.Add(FalloutSourcePackageAssignmentStep.PreviousCallback);
            }
            expected.AddRange([FalloutSourcePackageAssignmentStep.ProcessFlag, FalloutSourcePackageAssignmentStep.StartEvent,
                FalloutSourcePackageAssignmentStep.StartCallback, FalloutSourcePackageAssignmentStep.Procedure,
                FalloutSourcePackageAssignmentStep.SavedPosition, FalloutSourcePackageAssignmentStep.PackageFlag,
                FalloutSourcePackageAssignmentStep.ActorAssignment, FalloutSourcePackageAssignmentStep.TailProcess]);
            if (call.TailProcess is not null)
            {
                expected.Add(FalloutSourcePackageAssignmentStep.Tier);
                if (call.Tier is not (null or 0))
                {
                    expected.Add(FalloutSourcePackageAssignmentStep.Clock);
                    if (call.Tier == 1) expected.Add(FalloutSourcePackageAssignmentStep.TierCallbacks);
                }
            }
            if (call.TailProcess is null || call.Tier is null or 0)
            {
                expected.Add(FalloutSourcePackageAssignmentStep.PlayerIdentity);
                if (call.IsPlayer == true) expected.Add(FalloutSourcePackageAssignmentStep.FourthStore);
            }
        }
        if (call.ReturnedChildren.Count > expected.Count || !call.ReturnedChildren.SequenceEqual(expected.Take(call.ReturnedChildren.Count)) ||
            (call.Step == FalloutSourcePackageAssignmentStep.Returned ? call.ReturnedChildren.Count != expected.Count ||
                call.Result != true || call.FailedAt is not null || call.Failure is not null :
                call.Result is not null || string.IsNullOrWhiteSpace(call.Failure) || call.FailedAt !=
                    (call.ReturnedChildren.Count == expected.Count ? FalloutSourcePackageAssignmentStep.Returned : expected[call.ReturnedChildren.Count])))
            throw new InvalidDataException("Package command lost its ordered returned prefix or certified an unfinished child.");
        if (call.ReturnedChildren.Contains(FalloutSourcePackageAssignmentStep.SpecialPackage) && call.SpecialSelected is null ||
            call.ReturnedChildren.Contains(FalloutSourcePackageAssignmentStep.Tier) && call.Tier is null ||
            call.ReturnedChildren.Contains(FalloutSourcePackageAssignmentStep.Procedure) && call.Procedure is null ||
            call.ReturnedChildren.Contains(FalloutSourcePackageAssignmentStep.Clock) && call.ClockBits is null ||
            call.ReturnedChildren.Contains(FalloutSourcePackageAssignmentStep.PlayerIdentity) && call.IsPlayer is null)
            throw new InvalidDataException("Package command omitted a returned source getter result.");
        if (!Entered(FalloutSourcePackageAssignmentStep.SpecialPackage) && (call.FirstPrimary is not null || call.SpecialPackage is not null || call.SpecialSelected is not null) ||
            !Entered(FalloutSourcePackageAssignmentStep.Process) && call.Process is not null ||
            !Entered(FalloutSourcePackageAssignmentStep.PreviousPackage) && call.PreviousPackage is not null ||
            !Entered(FalloutSourcePackageAssignmentStep.Procedure) && call.Procedure is not null ||
            !Entered(FalloutSourcePackageAssignmentStep.TailProcess) && call.TailProcess is not null ||
            !Entered(FalloutSourcePackageAssignmentStep.Tier) && call.Tier is not null ||
            !Entered(FalloutSourcePackageAssignmentStep.Clock) && call.ClockBits is not null ||
            !Entered(FalloutSourcePackageAssignmentStep.PlayerIdentity) && call.IsPlayer is not null)
            throw new InvalidDataException("Package command invented descendant results before entering their source step.");
        foreach (var process in new[] { call.Process, call.TailProcess })
            if (process is not null && (process.Actor != call.Invocation.Actor || process.Epoch != call.Invocation.Epoch ||
                process.Process != call.Invocation.Process || process.Stack != common.Stack || process.ExecutableSha256 != common.ExecutableSha256 ||
                process.GameplayOwnership == Guid.Empty || process.Changed > common.Sequence))
                throw new InvalidDataException("Package command borrowed a foreign current-process read.");
        foreach (var read in new[] { call.FirstPrimary, call.SpecialPackage, call.PreviousPackage })
            if (read is not null)
            {
                if (read.Process.Actor != call.Invocation.Actor || read.Process.Process != call.Invocation.Process ||
                    read.Process.Epoch != call.Invocation.Epoch)
                    throw new InvalidDataException("Package command substituted another actual Actor/process/epoch's read.");
                FalloutProcessPackageState.ValidateRead(read, common.ExecutableSha256, common.Stack, common);
            }
        if (call.FirstPrimary is { Slot: not FalloutProcessPackageSlot.Primary } ||
            call.PreviousPackage is { Slot: not FalloutProcessPackageSlot.Current } ||
            call.SpecialPackage is { } selected && (call.FirstPrimary is null ||
                selected.Slot != (call.FirstPrimary.Fields.Fields.Package is null ? FalloutProcessPackageSlot.Current : FalloutProcessPackageSlot.Primary)) ||
            call.Process is { Level: FalloutDetectionProcessLevel.High } && call.ReturnedChildren.Contains(FalloutSourcePackageAssignmentStep.SpecialPackage) && call.FirstPrimary is null ||
            call.ReturnedChildren.Contains(FalloutSourcePackageAssignmentStep.SpecialPackage) && call.FirstPrimary is not null && call.SpecialPackage is null)
            throw new InvalidDataException("Package command changed its independent Primary first read, reread/current choice or previous Current slot.");
        if (call.FirstPrimary is { } first && call.SpecialPackage is { } second &&
            (second.Process.Changed < first.Process.Changed || call.Process is { } initial && initial.Changed < second.Process.Changed) ||
            call.Process is { } before && call.PreviousPackage is { } previous && previous.Process.Changed < before.Changed ||
            call.PreviousPackage is { } prior && call.TailProcess is { } tail && tail.Changed < prior.Process.Changed)
            throw new InvalidDataException("Package command reordered its actual independent process/slot reads.");
        if (call.ReturnedChildren.Contains(FalloutSourcePackageAssignmentStep.SpecialPackage))
        {
            var special = call.SpecialPackage?.Fields.Fields.Package is { } package &&
                common.PackageRegistry!.Packages.Single(value => value.Form == package).Kind == SpecialPackageKind;
            if (call.SpecialSelected != special)
                throw new InvalidDataException("Package command changed its actual selected package kind predicate.");
        }
        if (call.Step == FalloutSourcePackageAssignmentStep.Returned && call.ConsumerEntered)
            throw new InvalidDataException("Returned package command retained an entered child scope.");
        bool Entered(FalloutSourcePackageAssignmentStep step) => call.ReturnedChildren.Contains(step) || call.FailedAt == step;
    }
}
