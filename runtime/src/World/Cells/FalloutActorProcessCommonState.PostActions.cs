using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed record FalloutProcessPostActionUpdate(FalloutProcessSceneQuery Before, long Changed,
    FalloutProcessPostActionScalars? BeforeScalars, FalloutProcessPostActionScalars? AfterScalars,
    uint? ScaledTimerBits, int Stores, bool Returned);
internal sealed record FalloutProcessDetectionActionUpdate(FalloutProcessSceneQuery Before, long Changed,
    FalloutProcessDetectionActionScalars? BeforeScalars, FalloutProcessDetectionActionScalars? AfterScalars,
    uint? ScaledTimerBits, uint? SettingBits, int Stores, bool Returned);

internal sealed partial class FalloutActorProcessCommonState
{
    internal FalloutProcessPostActionUpdate UpdatePostActions(FalloutFormKey actor, long epoch, Func<float> readScaledTimer)
    {
        ArgumentNullException.ThrowIfNull(readScaledTimer);
        var before = ReadSceneQueries(actor, epoch);
        if (before.Level is not (FalloutDetectionProcessLevel.High or FalloutDetectionProcessLevel.MiddleHigh))
            return new(before, before.Changed, null, null, null, 0, true);
        var current = Require(actor); var fields = current.SceneFields!;
        var scalars = fields.PostActionScalars;
        FalloutProcessPostActionScalars? after; uint? timer = null; int stores;
        if (scalars is { SubtractsTimer: true })
        {
            var seconds = Callback(readScaledTimer);
            timer = BitConverter.SingleToUInt32Bits(seconds); after = scalars.Subtract(seconds); stores = 1;
        }
        else
        {
            if (fields.PostActionContext is not null)
                throw new NotSupportedException("source-process-post-action-nonnull-context-spatial-consumers-unbound");
            // The MiddleHigh virtual has the same caller but zero getter and
            // empty setters. High owns both ordered stores on the null arm.
            after = scalars?.Publish(0); stores = scalars is null ? 0 : 2;
        }
        var changed = stores == 0 ? before.Changed : Next();
        _current[actor] = current with { SceneFields = fields with { PostActionScalars = after }, Changed = changed };
        return new(before, changed, scalars, after, timer, stores, true);
    }

    internal FalloutProcessDetectionActionUpdate UpdateDetectionAction(FalloutFormKey actor, long epoch,
        Func<float> readScaledTimer, Func<float> readSourceTimer)
    {
        ArgumentNullException.ThrowIfNull(readScaledTimer); ArgumentNullException.ThrowIfNull(readSourceTimer);
        var before = ReadSceneQueries(actor, epoch);
        if (before.Level != FalloutDetectionProcessLevel.High)
            return new(before, before.Changed, null, null, null, null, 0, true);
        var current = Require(actor); var fields = current.SceneFields!;
        var scalars = fields.DetectionActionScalars ?? throw new InvalidOperationException("High detection action fields were not constructed.");
        FalloutProcessDetectionActionScalars after; uint? timer = null, setting = null; int stores;
        if (scalars.SubtractsTimer)
        {
            var seconds = Callback(readScaledTimer); timer = BitConverter.SingleToUInt32Bits(seconds);
            after = scalars.Subtract(seconds); stores = 1;
        }
        else
        {
            var seconds = Callback(readSourceTimer); setting = BitConverter.SingleToUInt32Bits(seconds);
            after = scalars.Reset(seconds); stores = 2;
        }
        var changed = Next();
        _current[actor] = current with { SceneFields = fields with { DetectionActionScalars = after }, Changed = changed };
        return new(before, changed, scalars, after, timer, setting, stores, true);
    }

    private static void ValidatePostActionFields(FalloutProcessCommonEntry entry)
    {
        if (entry.SceneFields is not { } fields) return;
        if ((entry.Level == FalloutDetectionProcessLevel.High) != (fields.PostActionScalars is not null) ||
            (entry.Level == FalloutDetectionProcessLevel.High) != (fields.DetectionActionScalars is not null) ||
            fields.PostActionScalars is { } post && (post.Updates < 0 || post.Updates == 0 && post != FalloutProcessPostActionScalars.Constructed ||
                post.ValueBits != 0) ||
            fields.DetectionActionScalars is { } action && (action.Updates < 0 || action.Updates == 0 && action != FalloutProcessDetectionActionScalars.Constructed ||
                action.ValueBits != 0))
            throw new InvalidDataException("Process post-action fields omitted their constructor or admitted an unowned context/value writer.");
    }
}
