using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed partial class FalloutStandalonePlayerSceneState
{
    internal void RequireOriginalInputInvocation(FalloutInputInvocation invocation)
    {
        if (invocation.PlayerProcess != _process)
            throw new InvalidOperationException("Original input query selected another actual Player process.");
        RequireFollowingContinuationInvocation(new(invocation.Main, invocation.Ordinal, invocation.Invocation));
        var owner = _inputManager ?? throw new InvalidOperationException("Original input query lost its current manager publication.");
        if (!ReferenceEquals(owner.ReadManagerOwner(), owner) || owner.Source != _stack)
            throw new InvalidOperationException("Original input query lost its actual source manager.");
    }

    internal FalloutOriginalInputQuery ReadFollowingSourceControl(FalloutPlayerSourceCaller caller, int control, int mode,
        Action<FalloutOriginalInputQuery> observe)
    {
        RequireFollowingContinuationInvocation(caller);
        var input = _inputManager ?? throw new InvalidOperationException("Following Player services have no actual input manager.");
        ArgumentNullException.ThrowIfNull(observe);
        var invocation = new FalloutInputInvocation(_process, caller.Main, caller.Ordinal, caller.Invocation);
        try
        {
            var query = input.ReadSourceControl(invocation, control, mode); observe(query); return query;
        }
        catch
        {
            if (input.LastSourceControlRead is { Step: FalloutOriginalInputQueryStep.Failed } failed &&
                failed.Invocation == invocation && failed.Control == control && failed.Mode == mode)
                observe(failed);
            throw;
        }
    }
}
