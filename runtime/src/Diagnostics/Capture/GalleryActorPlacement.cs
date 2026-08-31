using Godot;


using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Diagnostics.Capture;

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

    internal static PresentationFacingCorrection ApplyPresentationFacingCorrection(
        CellActorLoader.PlacedActor actor,
        string modelFrontAxis)
    {
        var localFront = modelFrontAxis switch
        {
            "negative-z" => Vector3.Forward,
            "positive-z" => Vector3.Back,
            _ => throw new InvalidOperationException(
                $"Unsupported gallery model-front axis: {modelFrontAxis}"),
        };
        var poseRotation = actor.Actor.PoseContract.Resolve().FacingRotation;
        var placementBasis = actor.Placement.GlobalBasis.Orthonormalized();
        var restWorldFront = placementBasis * localFront;
        var posedWorldFront = placementBasis *
            (new Basis(poseRotation) * localFront);
        restWorldFront.Y = 0.0f;
        posedWorldFront.Y = 0.0f;
        if (restWorldFront.IsZeroApprox() || posedWorldFront.IsZeroApprox())
            throw new InvalidOperationException(
                "Gallery actor facing pose has no finite horizontal direction.");

        restWorldFront = restWorldFront.Normalized();
        posedWorldFront = posedWorldFront.Normalized();
        var posedYawRadians = restWorldFront.SignedAngleTo(
            posedWorldFront,
            Vector3.Up);
        var appliedYawRadians = -posedYawRadians;
        var correctionBasis = new Basis(Vector3.Up, appliedYawRadians);
        var modelRoot = actor.Actor.Root;
        var rootBefore = modelRoot.GlobalTransform;
        modelRoot.GlobalTransform = new Transform3D(
            correctionBasis * rootBefore.Basis,
            rootBefore.Origin);
        var correctedWorldFront = correctionBasis * posedWorldFront;
        correctedWorldFront.Y = 0.0f;
        if (correctedWorldFront.IsZeroApprox())
            throw new InvalidOperationException(
                "Gallery actor facing correction produced no horizontal direction.");
        correctedWorldFront = correctedWorldFront.Normalized();
        var residualYawRadians = restWorldFront.SignedAngleTo(
            correctedWorldFront,
            Vector3.Up);

        return new PresentationFacingCorrection(
            poseRotation,
            restWorldFront,
            posedWorldFront,
            correctedWorldFront,
            posedYawRadians,
            appliedYawRadians,
            residualYawRadians,
            rootBefore,
            modelRoot.GlobalTransform,
            "inverse-horizontal-yaw-of-owned-animation-facing-pose-on-visual-root");
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

    internal readonly record struct PresentationFacingCorrection(
        Quaternion FacingPoseRotation,
        Vector3 RetailRootWorldFront,
        Vector3 PosedWorldFrontBeforeCorrection,
        Vector3 PosedWorldFrontAfterCorrection,
        float PosedYawRadians,
        float AppliedYawRadians,
        float ResidualYawRadians,
        Transform3D VisualRootBefore,
        Transform3D VisualRootAfter,
        string Derivation);
}
