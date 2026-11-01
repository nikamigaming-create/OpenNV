using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    private FalloutReferencePlacement CompassQuestPortal(FalloutQuestTarget target,
        FalloutReferencePlacement player, FalloutFormKey destination, float unitsToMetres)
    {
        var graph = _travelPortals ??= new(records);
        FalloutFormKey door;
        // GECK Quest Objectives Tab: QSTA bit zero ignores locks; otherwise the
        // path attempts to avoid locked doors "if possible". Prefer an unlocked
        // route, retaining a locked route only when no unlocked route exists.
        // https://geckwiki.com/index.php/Quest_Objectives_Tab
        // Multiple
        // first exits require native path cost qualification; source record order
        // is never a tie breaker for a player-facing compass direction.
        if ((target.Flags & 1) != 0)
            door = graph.FindUniqueFirstDoor(player.Cell, destination, CanActivate);
        else
        {
            try
            {
                door = graph.FindUniqueFirstDoor(player.Cell, destination,
                    reference => CanActivate(reference) && GetLocked(reference) == 0);
            }
            catch (FalloutCampaignPortalUnavailableException)
            {
                door = graph.FindUniqueFirstDoor(player.Cell, destination, CanActivate);
            }
        }
        // Keep the quest target identity on the marker. Only its spatial anchor
        // becomes the actual source-connected door, as in native teleportLinks.
        return SpatialPlacement(door, player, unitsToMetres);
    }
}
