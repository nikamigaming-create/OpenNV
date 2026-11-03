using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Content;

internal static class FalloutTravelArrivalHeading
{
    internal static float? Read(FalloutPluginStack records, FalloutReferenceWorld world, FalloutPackageTravelGoal goal)
    {
        if (goal.LocationType != 0 || goal.Reference is not { } reference || goal.Furniture is not null) return null;
        var marker = records.RuntimeFormKey(0x34);
        if (world.Get(reference).Base != marker) return null;
        if (records.GetEffective(marker).Signature != "STAT")
            throw new InvalidDataException("The engine heading-marker base is not a winning STAT.");
        var rotation = world.Placement(reference).RotationRadians;
        if (rotation.Length != 3 || !float.IsFinite(rotation[2]))
            throw new InvalidDataException("Travel heading marker has no finite current rotation.");
        return rotation[2];
    }
}
