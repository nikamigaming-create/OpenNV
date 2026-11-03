using Godot;

namespace OpenNV.Runtime.World.Cells;

// Ground-actor pathing locations keep their physical position and separately
// identify a source triangle found by a bounded vertical query. These are
// source game units, independent of the actor's model/reference scale.
internal static class NativeActorNavigationHeight
{
    internal const float MinimumPlaneDelta = -64, MaximumPlaneDelta = 180;

    internal static bool ColumnIntersects(Vector3 point, Vector3 minimum, Vector3 maximum) =>
        point.X >= minimum.X && point.X <= maximum.X && point.Y >= minimum.Y && point.Y <= maximum.Y &&
        minimum.Z <= point.Z + MaximumPlaneDelta && maximum.Z >= point.Z + MinimumPlaneDelta;

    internal static bool Admits(float physicalHeight, float sourcePlaneHeight)
    {
        if (!float.IsFinite(physicalHeight) || !float.IsFinite(sourcePlaneHeight)) return false;
        var delta = sourcePlaneHeight - physicalHeight;
        return delta >= MinimumPlaneDelta && delta <= MaximumPlaneDelta;
    }
}
