using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Gameplay.State;

internal static partial class FalloutNativeCampaignSave
{
    private static void ValidateSourceOcclusion(FalloutPluginStack records,
        IReadOnlyList<FalloutRoomPortalSnapshot> graphs, FalloutCellProcessesSnapshot cells, string engineSha256)
    {
        var selected = FalloutDynamicPrimitiveSource.Supports(engineSha256)
            ? FalloutDynamicPrimitiveSource.Read(engineSha256) : null;
        var byCell = graphs.ToDictionary(row => row.Cell, FalloutFormKeyComparer.Instance);
        foreach (var graph in graphs)
        {
            if (graph.Occlusion is not { } occlusion || occlusion.Cell != graph.Cell ||
                occlusion.LastNativeGeneration != graph.LastNativeGeneration || occlusion.EngineSha256 != graph.EngineSha256 ||
                graph.EngineSha256 != engineSha256)
                throw new InvalidDataException("Current native CELL save lacks its correlated occlusion source/frame authority.");
            occlusion.Validate();
            var scene = FalloutCellSceneReader.Read(records, graph.Cell);
            var source = FalloutDynamicPrimitiveSource.Read(graph.EngineSha256);
            foreach (var row in occlusion.Planes)
            {
                var placed = scene.References.SingleOrDefault(reference => reference.FormKey == row.Reference) ??
                    throw new InvalidDataException("Saved occlusion rectangle has no exact winning reference in its source CELL.");
                var primitive = FalloutDynamicPrimitive.Read(source, records, placed);
                var membership = FalloutRoomMembership.Read(records, row.Reference);
                if (primitive.Role != FalloutDynamicPrimitiveRole.OcclusionPlane || primitive.SourceSha256 != row.SourceSha256 ||
                    membership.SourceSha256 != row.MembershipSha256 || membership.Room != row.Room)
                    throw new InvalidDataException("Current saved occlusion rectangle differs from its selected winning source.");
            }
        }
        foreach (var attachment in cells.Attachments.Where(row => !row.Retired && row.RootPublished))
            foreach (var child in attachment.Children.Where(row => row.NativeObjects.Count != 0 &&
                row.Phase is FalloutCellProcessChildPhase.Published or FalloutCellProcessChildPhase.SourceNoDraw))
            {
                var cell = child.Source.SourceCell;
                if (selected is null) continue;
                var factory = selected.Role(records, child.Source.Base);
                if (!byCell.TryGetValue(cell, out var graph))
                {
                    // A CELL without any primitive graph remains valid. A
                    // returning source plane cannot lose its native consumer.
                    if (factory == FalloutDynamicPrimitiveRole.OcclusionPlane)
                        throw new InvalidDataException("Published source plane omitted its current CELL visibility owner.");
                    continue;
                }
                if (factory == FalloutDynamicPrimitiveRole.OcclusionPlane &&
                    !graph.Occlusion!.Planes.Any(row => row.Reference == child.Source.Reference && row.SourceSha256 == child.Source.Sha256))
                    throw new InvalidDataException("Published source plane omitted its exact current/cold frame registration.");
            }
    }
}
