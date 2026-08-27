using Godot;

namespace OpenNV.Runtime;

internal static class GalleryActorPlacement
{
    internal static Replay Apply(
        CellSceneLoader.LoadedCell loaded,
        CellActorLoader.PlacedActor actor,
        GalleryRetailEvidence.PresentationReference presentation,
        RuntimeConfiguration configuration)
    {
        var source = presentation.Actor;
        var localPosition = loaded.GameToCellUnits(source.WorldTranslationGameUnits);
        if (!localPosition.IsFinite() || !source.WorldBasis.IsFinite())
            throw new InvalidOperationException(
                "Gallery retail actor root is not a finite transform.");

        actor.Placement.Transform = new Transform3D(source.WorldBasis, localPosition);
        var expectedWorldPosition = loaded.GameToWorld(source.WorldTranslationGameUnits);
        var measuredWorldPosition = actor.Placement.GlobalPosition;
        var positionErrorMeters = measuredWorldPosition.DistanceTo(expectedWorldPosition);
        var positionErrorGameUnits =
            positionErrorMeters / configuration.World.GameUnitsToMeters;
        var expectedWorldBasis = loaded.Root.GlobalBasis * source.WorldBasis;
        var basisError = MaximumBasisComponentError(
            actor.Placement.GlobalBasis,
            expectedWorldBasis);
        var passed =
            positionErrorGameUnits <=
                configuration.ActorParity.CameraPositionToleranceGameUnits &&
            basisError <= configuration.ActorParity.PoseRotationToleranceRadians;
        if (!passed)
            throw new InvalidOperationException(
                "Gallery actor could not replay the matched retail root transform.");

        return new Replay(
            source.WorldTranslationGameUnits,
            localPosition,
            expectedWorldPosition,
            measuredWorldPosition,
            source.WorldScale,
            positionErrorMeters,
            positionErrorGameUnits,
            basisError,
            true,
            "same-frame-retail-actor-root-relative-to-compiled-cell-origin");
    }

    private static float MaximumBasisComponentError(Basis left, Basis right) =>
        MathF.Max(
            MathF.Max(left.X.DistanceTo(right.X), left.Y.DistanceTo(right.Y)),
            left.Z.DistanceTo(right.Z));

    internal readonly record struct Replay(
        Vector3 RetailWorldTranslationGameUnits,
        Vector3 CellLocalPositionGameUnits,
        Vector3 ExpectedWorldPositionMeters,
        Vector3 MeasuredWorldPositionMeters,
        float RetailWorldScale,
        float PositionErrorMeters,
        float PositionErrorGameUnits,
        float BasisError,
        bool Passed,
        string Derivation);
}
