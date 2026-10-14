using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

// Replace only the duplicate-lane predicate in the existing save contract.
// Remaining Main, Actor, module and native-resource owners stay independent.
internal static class FalloutReferenceColdSaveReadiness
{
    internal static void RequireCurrentFields(FalloutProcessCommonSnapshot common, FalloutStandalonePlayerSceneSnapshot scene)
    {
        var reference = common.ReferenceOwners ?? throw new NotSupportedException("source-save-current-reference-owner-graph-unbound");
        foreach (var fields in common.CommandFields ?? [])
            if (fields.Availability.Any(value =>
                    (value.Lane is FalloutPackageCommandFieldLane.ScriptExtra or FalloutPackageCommandFieldLane.SavedPosition or
                        FalloutPackageCommandFieldLane.Animation) && !value.Available))
                throw new NotSupportedException("source-save-current-package-Extra-or-animation-writer-unbound");
        foreach (var lane in new[] { FalloutReferencePositionLane.Position, FalloutReferencePositionLane.PositionMode,
            FalloutReferencePositionLane.PersistentCell })
            if (reference.Position.UnownedWriters.TryGetValue(lane, out var boundary)) throw new NotSupportedException(boundary);
        if (reference.Position.Values.PositionMode != 0)
            throw new NotSupportedException("source-save-current-adjusted-position-owner-unbound");
        var parent = reference.Parent ?? throw new NotSupportedException("source-save-current-World-ParentCELL-history-unbound");
        _ = new FalloutReferenceParentCellValidation(parent);
        if (parent.Retired) throw new NotSupportedException("source-save-current-World-ParentCELL-retired");
        var rotation = scene.ReferenceOrientation ?? throw new NotSupportedException("source-save-current-raw-reference-orientation-unbound");
        FalloutPlayerReferenceOrientationOwner.Validate(rotation);
        if (rotation.Failure is not null || rotation.Retired) throw new NotSupportedException("source-save-current-raw-reference-orientation-unreturned");
    }
}
