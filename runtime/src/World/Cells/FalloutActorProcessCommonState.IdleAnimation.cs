using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed record FalloutProcessIdleAnimationUpdate(FalloutProcessSceneQuery Before, long Changed,
    FalloutActorIdleAnimationFields? BeforeFields, FalloutActorIdleAnimationFields? AfterFields,
    FalloutActorIdleAnimationTrace Trace);

internal sealed partial class FalloutActorProcessCommonState
{
    internal FalloutProcessIdleAnimationUpdate? LastIdleAnimationUpdate { get; private set; }
    internal FalloutProcessIdleAnimationUpdate UpdateIdleAnimation(FalloutFormKey actor, long epoch,
        IFalloutActorIdleAnimationConsumers consumers)
    {
        var before = ReadSceneQueries(actor, epoch); var current = Require(actor);
        var initial = current.SceneFields?.IdleAnimation;
        if (before.Level == FalloutDetectionProcessLevel.High && initial is null)
            throw new InvalidDataException("High idle update omitted its independent constructor fields.");
        _busy = true; var fault = _callbackFault;
        try
        {
            FalloutActorIdleAnimationUpdate.Execute(before.Level, consumers,
                () => Require(actor).SceneFields!.IdleAnimation!, Store, Observe);
            return LastIdleAnimationUpdate!;
        }
        catch (Exception error) { HoldMutation(actor, error.Message); throw; }
        finally { _busy = false; }
        void Store(FalloutActorIdleAnimationFields value)
        {
            Check(); current = Require(actor);
            _current[actor] = current with
            { SceneFields = current.SceneFields! with { IdleAnimation = value }, Changed = Next() };
        }
        void Observe(FalloutActorIdleAnimationTrace trace)
        {
            // Retain a child's failed prefix even when that failure is the
            // callback ownership check itself. A second check must not erase it.
            if (trace.Failure is null) Check();
            current = Require(actor);
            LastIdleAnimationUpdate = new(before, current.Changed, initial, current.SceneFields?.IdleAnimation, trace);
        }
        void Check()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_callbackFault != fault || Require(actor).Epoch != epoch || Require(actor).Gameplay?.Ownership != before.GameplayOwnership)
                throw new InvalidOperationException("High idle child lost its actual process lease or swallowed reentry.");
        }
    }
    private static void ValidateIdleAnimationFields(FalloutProcessCommonEntry entry)
    {
        var fields = entry.SceneFields?.IdleAnimation;
        if ((entry.Level == FalloutDetectionProcessLevel.High && entry.SceneFields is not null) != (fields is not null) ||
            fields is not null && (fields.Stores < 0 || fields.Flag != 0 || fields.Action != -1 || fields.CurrentIdle is not null ||
                fields.Stores == 0 && fields != FalloutActorIdleAnimationFields.Constructed))
            throw new InvalidDataException("High idle fields lost their actual constructor or admitted an unowned animation/action writer.");
    }
}
