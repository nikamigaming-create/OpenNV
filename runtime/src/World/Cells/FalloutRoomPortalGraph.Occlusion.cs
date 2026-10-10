using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutRoomPortalGraph
{
    internal bool SeesView(int view, FalloutFormKey? room, FalloutVisibilitySphere bound, bool sourceMembershipKnown)
    {
        Require(); bound.Validate();
        if (!_closed || view < 0 || view >= Views.Count || _currentCameras.Count != Views.Count)
            throw new NotSupportedException("Room visibility has no correlated current native view.");
        // Field absence leaves automatic attachment uninspected. It can still
        // have the actual native camera frustum and CELL-global occlusion lane;
        // this does not declare it to belong to a fabricated outside room.
        if (!sourceMembershipKnown) return new FalloutVisibilityWindow(_currentCameras[view].Frustum).Sees(bound);
        if (room is { } key && !_neighbors.ContainsKey(key))
            throw new NotSupportedException("Source member room has no actual registered bound owner.");
        return Views[view].Visibility.Any(row => row.Room == room && row.Windows.Any(window => window.Sees(bound)));
    }
}
