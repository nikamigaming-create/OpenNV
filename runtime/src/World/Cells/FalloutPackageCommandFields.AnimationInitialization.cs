using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutActorPackageCommandFields
{
    internal const string ConfiguredAnimationWriterBoundary = "source-Player-animation-configured-CELL-publication-writer-unbound";
    internal const string AnimationViewBoundary = "source-Player-animation-current-scene-view-owner-unbound";
    private const string AnimationInitializationKind = "deferred-Player-animation-constructor-and-startup-prefix";
    internal FalloutPackageCommandAnimationInitialization? LastAnimationInitialization { get; private set; }
    internal bool HasConstructedPlayerAnimationInitialization => LastAnimationInitialization is not null;

    // This retains independent preceding source evidence at the actual command
    // ingress. It replays neither the constructor nor configured placement.
    // In particular it cannot return the physical or whole reference owner.
    internal FalloutPackageCommandAnimationInitialization InitializeConstructedPlayerAnimation(
        FalloutPackageCommandFieldInvocation invocation, FalloutInstallationSettings actualSettings)
    {
        if (HasConstructedPlayerAnimationInitialization)
            throw new InvalidOperationException("Player animation constructor admission cannot replay its returned or failed prefix.");
        Execute(invocation, AnimationInitializationKind, scope =>
        {
            ArgumentNullException.ThrowIfNull(actualSettings);
            if (!Actor.EnginePlayer || _values.AnimationOverride is not null || _values.FirstPersonAnimation is not null ||
                _operations.Any(value => value.InvalidatedLane == FalloutPackageCommandFieldLane.Animation ||
                    value.Stores.Any(store => store.Field is FalloutPackageCommandField.View or
                        FalloutPackageCommandField.AnimationOverride or FalloutPackageCommandField.FirstPersonAnimation)))
                throw new NotSupportedException("source-Player-animation-constructor-current-writer-history-unavailable");
            var cell = actualSettings.Require("General", "SStartingCell"); Guard(scope);
            scope.AnimationInitialization = scope.AnimationInitialization! with { ConfiguredCell = cell };
            if (cell.Length != 0) throw new NotSupportedException(ConfiguredAnimationWriterBoundary);
            var x = actualSettings.Require("General", "SStartingCellX"); Guard(scope);
            scope.AnimationInitialization = scope.AnimationInitialization with { ConfiguredGridX = x };
            // The source returns before Y/world/CELL lookups on this exact arm.
            if (x.Length != 0) throw new NotSupportedException(ConfiguredAnimationWriterBoundary);
            return true;
        }, scope => scope.AnimationInitialization = new(invocation, Source.Identity, Factory, Constructor,
            scope.Entered, scope.Entered, null, null, _values.AnimationOverride, _values.FirstPersonAnimation, false, null));
        // Execute has completed its final current-caller guard and retained the
        // returned receipt. Admission changes only field availability.
        Admit(FalloutPackageCommandFieldLane.Animation);
        return LastAnimationInitialization!;
    }

    private void RequireAnimationView(FalloutPackageCommandAnimationViewRead view)
    {
        if (view is null || view.Process == Guid.Empty || view.Constructor == Guid.Empty ||
            view.Stack != Stack || view.ExecutableSha256 != Source.Controls.Main.EngineSha256)
            throw new InvalidDataException("Player animation view lost its independent current scene, source or constructor.");
    }
}
