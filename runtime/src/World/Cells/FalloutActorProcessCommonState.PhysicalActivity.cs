using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed record FalloutProcessPhysicalActivityFields(sbyte Value, long Stores)
{
    internal static FalloutProcessPhysicalActivityFields Constructed => new(0, 0);
}
internal sealed record FalloutProcessPhysicalActivityQuery(FalloutProcessSceneQuery Process, int Value);

internal sealed partial class FalloutActorProcessCommonState
{
    internal FalloutProcessPhysicalActivityQuery ReadPhysicalActivity(FalloutFormKey actor, long epoch)
    {
        var process = ReadSceneQueries(actor, epoch);
        if (process.Level is not (FalloutDetectionProcessLevel.High or FalloutDetectionProcessLevel.MiddleHigh))
            throw new NotSupportedException("source-Actor-physical-registration-lower-process-activity-virtual-unreviewed");
        var fields = Require(actor).SceneFields?.PhysicalActivity ??
            throw new InvalidDataException("Current process activity omitted its independent signed-byte constructor.");
        return new(process, fields.Value);
    }

    private static void ValidatePhysicalActivityFields(FalloutProcessCommonEntry entry)
    {
        var fields = entry.SceneFields?.PhysicalActivity;
        if ((entry.SceneFields is not null) != (fields is not null) || fields is not null &&
            fields != FalloutProcessPhysicalActivityFields.Constructed)
            throw new InvalidDataException("Process physical activity lost its source constructor or admitted an unowned writer.");
    }
}
