using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal sealed record FalloutProcessPackageObservation(Guid Process, FalloutCombatActorIdentity Actor,
    long Epoch, FalloutDetectionProcessLevel Level, long Changed,
    FalloutProcessPackageWords Current, bool CurrentAvailable,
    FalloutProcessPackageWords? Primary, bool? PrimaryAvailable, string? Failure);

internal sealed partial class FalloutActorProcessCommonState
{
    // Diagnostics observe committed fields, including a failed prefix. They
    // do not execute the process virtual or admit another gameplay consumer.
    internal FalloutProcessPackageObservation ObserveSourcePackageFields(FalloutFormKey actor, long epoch)
    {
        RequireNotBusy(); var current = Require(actor);
        if (current.Epoch != epoch || Identity(actor) != current.Source || current.Packages is not { } packages ||
            packages.ConstructedEpoch != epoch || packages.Current.Publication.Process != _process ||
            packages.Primary is { } primary && primary.Publication.Process != _process)
            throw new InvalidOperationException("Retained package observation lost its actual Actor/process/field ownership.");
        return new(_process, current.Source, epoch, current.Level, current.Changed,
            packages.Current.Fields, packages.Current.Available, packages.Primary?.Fields,
            packages.Primary?.Available, current.Boundary ?? (!packages.Current.Available
                ? FalloutProcessPackageState.CurrentWriterBoundary : packages.Primary is { Available: false }
                    ? FalloutProcessPackageState.PrimaryWriterBoundary : null));
    }
}
