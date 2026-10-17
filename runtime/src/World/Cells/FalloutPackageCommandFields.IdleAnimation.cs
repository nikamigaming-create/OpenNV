namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutActorPackageCommandFields
{
    internal Guid? ReadIdleFirstAnimation(FalloutProcessSceneQuery query)
    {
        if (_busy) { ++_fault; throw new InvalidOperationException("Idle animation pointer reentered its actual field owner."); }
        if (_retired || _failure is not null || query.Actor != Actor || query.Process != Process || query.Epoch != Epoch ||
            query.Stack != Stack || _thread is { } thread && thread != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("Idle animation pointer changed its real Actor fields, process epoch or current thread.");
        RequireLane(FalloutPackageCommandFieldLane.Animation);
        if (_values.AnimationOverride is not null)
            throw new NotSupportedException("source-Player-idle-current-animation-override-context-owner-unbound");
        return _values.FirstPersonAnimation;
    }
}
