using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.World.Actors;

internal partial class RuntimeNativeNpc
{
    // The common procedure producer refuses a matching owned current/queued
    // request except state one, or a matching outgoing request in any state.
    // Direct script factory calls deliberately do not use this admission gate.
    internal static bool ProcedureIdleAlreadyOwned(FalloutFormKey idle,
        (FalloutFormKey Idle, int State)? current, (FalloutFormKey Idle, int State)? queued,
        IEnumerable<FalloutFormKey> outgoing) =>
        current is { } active && active.Idle == idle && active.State != 1 ||
        queued is { } pending && pending.Idle == idle && pending.State != 1 || outgoing.Contains(idle);

    internal bool QueueProcedureIdle(FalloutPluginStack stack, FalloutFormKey idle, string procedure,
        Func<bool> ownerValid, Action<Action<RuntimeNativeNifAnimation, string>> activate)
    {
        if (procedure is not ("furniture" or "eat")) throw new InvalidDataException("Unknown procedure idle owner.");
        var history = FalloutActorLastIdleRequest.Requested(stack, idle);
        var state = _aiReferenceState ?? throw new InvalidOperationException("Procedure idle has no reference owner.");
        RefreshProcedureIdleRequest();
        if (!ownerValid()) throw new InvalidOperationException("Procedure idle has no current physical owner.");
        if (ProcedureIdleAlreadyOwned(idle,
            _currentIdleRequest is { } current ? (current.Idle, current.State) : null,
            _queuedIdleRequest is { } queued ? (queued.Idle, queued.State) : null,
            _outgoingIdles.Select(value => value.Request.Idle))) return false;
        _queuedIdleRequest?.Inactivate();
        var request = new IdleRequest(stack, idle, procedure) { Procedure = procedure, ProcedureOwnerValid = ownerValid };
        request.ProcedureActivation = () => activate((animation, resource) => BindProcedureIdleRequest(request, animation, resource));
        _queuedIdleRequest = request;
        state.LastIdleRequest = history;
        ProcessQueuedIdle(stack);
        return true;
    }

    private void BindProcedureIdleRequest(IdleRequest request, RuntimeNativeNifAnimation animation, string resource)
    {
        if (!ReferenceEquals(_queuedIdleRequest, request))
            throw new InvalidOperationException("Procedure activation lost its queued request.");
        if (_currentIdleRequest is { Procedure: not null, State: 2 } previous)
        {
            if (ProcedureSequenceOwned(previous))
                throw new NotSupportedException("Procedure replacement has not released its previous physical sequence.");
            previous.Inactivate();
        }
        else RetainOutgoingIdle(animation);
        _idleEaseIn = null;
        request.Activate(animation, resource);
        _currentIdleRequest = request;
        _queuedIdleRequest = null;
    }

    private bool ProcedureSequenceOwned(IdleRequest request) => request.Procedure switch
    {
        "furniture" => _furnitureClip?.Idle == request.Idle && ReferenceEquals(_baseAnimation?.Sequence, request.Sequence),
        "eat" => Combat?.OwnsEatIdle(request.Idle, request.Sequence) == true,
        _ => throw new InvalidDataException("Idle request has no procedure sequence owner.")
    };

    private void RefreshProcedureIdleRequest()
    {
        if (_queuedIdleRequest is { Procedure: not null } queued && queued.ProcedureOwnerValid?.Invoke() != true)
        {
            // The accepted request remains history, but stale physical work
            // cannot publish after its package, meal or reservation changes.
            queued.Inactivate();
            _queuedIdleRequest = null;
        }
        if (_currentIdleRequest is { Procedure: not null, State: 2 } request && !ProcedureSequenceOwned(request))
            request.Inactivate();
    }

    private void CancelProcedureIdle(string procedure)
    {
        if (_queuedIdleRequest?.Procedure != procedure) return;
        _queuedIdleRequest.Inactivate();
        _queuedIdleRequest = null;
    }
}
