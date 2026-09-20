using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    internal bool InSameCell(FalloutFormKey first, FalloutFormKey second,
        FalloutReferencePlacement player, float unitsToMetres)
    {
        if (!float.IsFinite(unitsToMetres) || unitsToMetres <= 0)
            throw new ArgumentOutOfRangeException(nameof(unitsToMetres));
        player.Validate();
        (FalloutFormKey? World, FalloutFormKey? Interior, int X, int Y) Location(FalloutFormKey key)
        {
            var isPlayer = key == records.RuntimeFormKey(0x14);
            var placement = isPlayer ? player : Placement(key);
            var cell = FalloutCellSceneReader.ReadDefinition(records, placement.Cell);
            if (cell.Worldspace is null) return (null, cell.FormKey, 0, 0);
            var x = placement.Position[0]; var y = placement.Position[1];
            if (!isPlayer)
            {
                var state = Get(key);
                var motion = state.CaptureEngagement?.Invoke()?.Position ?? state.Engagement?.Position ?? state.PackageMotion?.Position;
                if (motion is not null) { x = motion[0] / unitsToMetres; y = -motion[2] / unitsToMetres; }
            }
            return (cell.Worldspace, null, (int)MathF.Floor(x / 4096), (int)MathF.Floor(y / 4096));
        }
        return Location(first) == Location(second);
    }
}
