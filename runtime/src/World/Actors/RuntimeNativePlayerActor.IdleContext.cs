using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Actors;

internal sealed partial class RuntimeNativePlayerActor
{
    private readonly FalloutIdleAnimationContext _sourceIdleContext = new(Guid.NewGuid(), null, null);
    internal FalloutIdleAnimationContext ReadIdleAnimationContext()
    {
        if (Error is not null || !_first || _idle is null || !IsInsideTree() || IsQueuedForDeletion())
            throw new NotSupportedException("Player animation context lost its actual first-person animation factory.");
        // The admitted factory begins with two independent null pending action
        // slots. Active transitions require their real action/context writer;
        // playing pixels or a matching KF name cannot manufacture those fields.
        if (_actionClip is not null || _movement is not null || _active != _idle)
            throw new NotSupportedException("source-Player-animation-pending-action-transition-writer-unbound");
        return _sourceIdleContext;
    }
}
