using Godot;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.World.Actors;

internal sealed record SourcePackagePlacement(
    string Kind,
    string TargetFormId,
    Transform3D SourceTransform);

internal static class GamebryoPackagePlacement
{
    internal static SourcePackagePlacement FromCellReference(
        string kind,
        string targetFormId,
        Vector3 positionCellUnits,
        Quaternion rotationGodot,
        Vector3 actorScale)
    {
        if (!rotationGodot.IsNormalized())
            throw new InvalidOperationException(
                "Source package reference rotation is not normalized.");
        return Require(new SourcePackagePlacement(
            kind,
            targetFormId,
            new Transform3D(
                new Basis(rotationGodot).Scaled(actorScale),
                positionCellUnits)));
    }

    internal static SourcePackagePlacement FromPlanarGameReferenceMarker(
        string targetFormId,
        Vector3 positionGameUnits,
        Vector3 rotationRadians,
        float scale,
        Vector3 cellOriginGameUnits)
    {
        if (!positionGameUnits.IsFinite() || !rotationRadians.IsFinite() ||
            !Mathf.IsZeroApprox(rotationRadians.X) ||
            !Mathf.IsZeroApprox(rotationRadians.Y) ||
            !float.IsFinite(scale) || scale <= 0.0f)
            throw new InvalidOperationException(
                "Source package reference-marker transform is unsupported.");
        return Require(new SourcePackagePlacement(
            "referenceMarker",
            targetFormId,
            new Transform3D(
                new Basis(Vector3.Up, -rotationRadians.Z)
                    .Scaled(Vector3.One * scale),
                GamebryoCoordinate.ConvertVector(
                    positionGameUnits - cellOriginGameUnits))));
    }

    internal static SourcePackagePlacement FromFurnitureMarker(
        string targetFormId,
        Transform3D furnitureTransform,
        Vector3 markerOffsetCellUnits,
        Quaternion markerRotationGodot,
        Vector3 actorPlacementOffsetCellUnits,
        Quaternion actorHeadingDeltaGodot,
        Vector3 actorScale)
    {
        var rootOffset = markerOffsetCellUnits - actorPlacementOffsetCellUnits;
        var marker = furnitureTransform * new Transform3D(
            new Basis(markerRotationGodot),
            rootOffset);
        var actor = marker * new Transform3D(
            new Basis(actorHeadingDeltaGodot),
            Vector3.Zero);
        return Require(new SourcePackagePlacement(
            "nearReference",
            targetFormId,
            new Transform3D(
                actor.Basis.Orthonormalized().Scaled(actorScale),
                marker.Origin)));
    }

    internal static void Publish(
        CellActorLoader.PlacedActor actor,
        SourcePackagePlacement placement,
        float supportHeightCellUnits = 0.0f)
    {
        var adjusted = AdjustSupportHeight(placement.SourceTransform, supportHeightCellUnits);
        actor.Placement.Transform = adjusted;
        if (!HorizontalPositionAndFacingPreserved(placement.SourceTransform, adjusted))
            throw new InvalidOperationException(
                "Package placement changed source horizontal position or facing.");
        actor.Placement.SetMeta("opennv_package_target_form_id", placement.TargetFormId);
        actor.Placement.SetMeta("opennv_package_target_kind", placement.Kind);
    }

    internal static Transform3D AdjustSupportHeight(
        Transform3D source,
        float supportHeightCellUnits)
    {
        if (!float.IsFinite(supportHeightCellUnits))
            throw new InvalidOperationException(
                "Package placement support-height adjustment is invalid.");
        var adjusted = new Transform3D(
            source.Basis,
            source.Origin + Vector3.Up * supportHeightCellUnits);
        if (!HorizontalPositionAndFacingPreserved(source, adjusted))
            throw new InvalidOperationException(
                "Package support-height adjustment changed horizontal placement.");
        return adjusted;
    }

    internal static Transform3D AtSupportHeight(
        Transform3D source,
        float supportHeightCellUnits)
    {
        if (!float.IsFinite(supportHeightCellUnits))
            throw new InvalidOperationException(
                "Package placement support height is invalid.");
        var adjusted = new Transform3D(
            source.Basis,
            new Vector3(
                source.Origin.X,
                supportHeightCellUnits,
                source.Origin.Z));
        RequireSupportHeightOnly(source, adjusted);
        return adjusted;
    }

    internal static void RequireSupportHeightOnly(
        Transform3D source,
        Transform3D adjusted)
    {
        if (!source.IsFinite() || !adjusted.IsFinite() ||
            !HorizontalPositionAndFacingPreserved(source, adjusted))
            throw new InvalidOperationException(
                "Actor grounding changed source horizontal position or facing.");
    }

    internal static PackageRootTransfer TransferRoot(
        CellActorLoader.PlacedActor actor,
        Vector3 sourceDisplacementGodotGameUnits)
    {
        var transfer = CalculateRootTransfer(
            actor.Placement.Transform,
            sourceDisplacementGodotGameUnits);
        actor.Placement.Transform = transfer.After;
        return transfer;
    }

    internal static PackageRootTransfer CalculateRootTransfer(
        Transform3D before,
        Vector3 sourceDisplacementGodotGameUnits)
    {
        if (!before.IsFinite() || !sourceDisplacementGodotGameUnits.IsFinite())
            throw new InvalidOperationException("Package root transfer is non-finite.");
        var displacement = before.Basis.Orthonormalized() *
            sourceDisplacementGodotGameUnits;
        var after = new Transform3D(before.Basis, before.Origin + displacement);
        if (!after.Basis.IsEqualApprox(before.Basis))
            throw new InvalidOperationException("Package root transfer changed source facing.");
        return new PackageRootTransfer(before, after, displacement);
    }

    private static SourcePackagePlacement Require(SourcePackagePlacement placement)
    {
        if (string.IsNullOrWhiteSpace(placement.Kind) ||
            string.IsNullOrWhiteSpace(placement.TargetFormId) ||
            !placement.SourceTransform.IsFinite() ||
            placement.SourceTransform.Basis.Determinant() <= 0.0f)
            throw new InvalidOperationException(
                "Source package placement contract is invalid.");
        return placement;
    }

    private static bool HorizontalPositionAndFacingPreserved(
        Transform3D source,
        Transform3D adjusted) =>
        Mathf.IsEqualApprox(source.Origin.X, adjusted.Origin.X) &&
        Mathf.IsEqualApprox(source.Origin.Z, adjusted.Origin.Z) &&
        source.Basis.IsEqualApprox(adjusted.Basis);
}

internal readonly record struct PackageRootTransfer(
    Transform3D Before,
    Transform3D After,
    Vector3 AppliedDisplacement);
