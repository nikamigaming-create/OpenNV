using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed partial class FalloutSourceAnimationContextState
{
    // These pending-action pointers are independent from the two retained
    // animation pair slots. The same original constructor clears both.
    private FalloutIdleAnimationAction? _idleFirst = null, _idleSecond = null;
    internal FalloutIdleAnimationContext ReadIdleContext(FalloutLoadedAnimationRole role, Guid? expectedModel = null)
    {
        RequireIdle();
        if (Construction.Role != role || _failure is not null || _writer is not null ||
            LastBind is not { Returned: true } || _root is null || _manager is null ||
            expectedModel is { } model && _root.Node.Factory != model)
            throw new InvalidOperationException("Idle predicates lost their actual returned AnimData, independent role or current bound model.");
        _root.RequireCurrent(); _manager.RequireModelBinding(_root.LogicalScene);
        if (_idleFirst is not null || _idleSecond is not null)
            throw new NotSupportedException("source-idle-current-pending-action-payload-owner-unbound");
        return new(Identity, _idleFirst, _idleSecond);
    }
}
