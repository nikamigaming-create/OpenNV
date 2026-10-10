using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Gameplay.State;

internal static partial class FalloutNativeCampaignSave
{
    private static void ValidateRoomPortals(IReadOnlyList<FalloutRoomPortalSnapshot>? graphs,
        FalloutCellProcessesSnapshot? cells)
    {
        if (graphs is null || cells is null || graphs.Any(row => row is null) ||
            graphs.Select(row => row.Cell).Distinct(FalloutFormKeyComparer.Instance).Count() != graphs.Count)
            throw new InvalidDataException("Campaign save lacks its exact current source room/portal graph list.");
        foreach (var row in graphs)
        {
            row.Validate(); FalloutPortalCullingSource.Read(row.EngineSha256).RequireCurrent();
            if (row.Occlusion is not { } occlusion || occlusion.Cell != row.Cell ||
                occlusion.EngineSha256 != row.EngineSha256 || occlusion.LastNativeGeneration != row.LastNativeGeneration)
                throw new InvalidDataException("Current CELL visibility save omitted its correlated source occlusion owner.");
            occlusion.Validate();
            if (!cells.Attachments.Any(owner => !owner.Retired && owner.RootPublished &&
                owner.Failure is null && owner.CellEpochs.ContainsKey(row.Cell)))
                throw new InvalidDataException("Saved portal graph has no genuinely published current CELL attachment.");
        }
    }
}
