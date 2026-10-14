using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;
using OpenNV.Runtime.World.Physics;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed partial class FalloutStandalonePlayerSceneState
{
    private void ExecutePostActionControllerContext(Guid main, long ordinal, FalloutProcessSceneContextQuery query, int index)
    {
        using var lease = (_borrowPostProcessController ?? throw new NotSupportedException("source-Player-post-action-controller-owner-unbound"))(query);
        var controller = lease.Controller;
        var invocation = new FalloutControllerPostProcessInvocation(main, ordinal, _threadContext.Process,
            controller.Lineage.RuntimeProcess, _constructor, Environment.CurrentManagedThreadId, query, index,
            FalloutControllerPlayerSourceScope.PostActions);
        _enteredControllerContext = invocation;
        try
        {
            FalloutPlayerControllerContextConsumers.ExecutePostActions(invocation,
                (entered, field, observe) =>
                {
                    lease.RequireCurrent();
                    return controller.ReadPostProcessField(entered, field, RequireEnteredControllerContext, observe);
                },
                (entered, observe) =>
                {
                    lease.RequireCurrent();
                    return controller.ReadPostProcessFlags(entered, RequireEnteredControllerContext, observe);
                },
                () => ReadControllerNullBodyReference(invocation),
                trace =>
                {
                    RequireEnteredControllerContext(invocation);
                    var children = _postActionsCall!.ControllerChildren?.ToList() ?? [];
                    if (children.Count != 0 && children[^1].QueryOrdinal == index) children[^1] = trace;
                    else children.Add(trace);
                    _postActionsCall = _postActionsCall with { ControllerChildren = children.ToArray(), Changed = Next() };
                });
        }
        finally { _enteredControllerContext = null; }
    }

    private FalloutFormKey? ReadControllerNullBodyReference(FalloutControllerPostProcessInvocation invocation)
        => ReadNullControllerReference(invocation, () => _postActionsCall!.BodyReference,
            call => _postActionsCall = _postActionsCall! with { BodyReference = call, Changed = Next() });
}
