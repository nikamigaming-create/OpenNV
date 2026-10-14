namespace OpenNV.Runtime.World.Physics;

internal sealed partial class FalloutEarlyController
{
    // This read borrows the controller's existing constructor/helper/reset
    // flags. It does not make another field owner from the selected bit.
    internal FalloutControllerPostProcessFlagsRead ReadPostProcessFlags(FalloutControllerPostProcessInvocation invocation,
        Action<FalloutControllerPostProcessInvocation> requireActual, Action<FalloutControllerPostProcessFlagsRead> observe)
    {
        RequireCurrent(); ArgumentNullException.ThrowIfNull(requireActual); ArgumentNullException.ThrowIfNull(observe);
        if (_readingPostProcess) { ++_postProcessReadFault; throw new InvalidOperationException("Controller flags getter reentered its current field reader."); }
        if (invocation.SourceScope != FalloutControllerPlayerSourceScope.PostActions || invocation.QueryOrdinal != 1)
            throw new InvalidDataException("Controller flags read has no original second post-action context scope.");
        var call = new FalloutControllerPostProcessFlagsRead(invocation, Lineage, Identity, null, _sequence, false, null);
        var fault = _postProcessReadFault; var sequence = _sequence; _readingPostProcess = true;
        try
        {
            Check(); observe(call); Check();
            call = call with { Flags = _flags, Returned = true }; observe(call); Check(); return call;
        }
        catch (Exception error)
        { call = call with { Returned = false, Failure = error.Message }; observe(call); throw; }
        finally { _readingPostProcess = false; }
        void Check() => RequirePostProcessInvocation(invocation, fault, sequence, requireActual);
    }

    private void RequirePostProcessInvocation(FalloutControllerPostProcessInvocation invocation, long fault, long sequence,
        Action<FalloutControllerPostProcessInvocation> requireActual)
    {
        RequireCurrent();
        var process = invocation.Context.Process;
        if (_postProcessReadFault != fault || _sequence != sequence || invocation.Main == Guid.Empty || invocation.MainOrdinal < 1 ||
            invocation.PlayerProcess != Lineage.ThreadProcess || invocation.RuntimeProcess != Lineage.RuntimeProcess ||
            !Enum.IsDefined(invocation.SourceScope) || invocation.QueryOrdinal is not (0 or 1) || invocation.Constructor != Lineage.Constructor ||
            invocation.Context.Context != Identity || process.Process != Lineage.CommonProcess || process.Actor != Lineage.Actor ||
            process.Epoch != Lineage.Epoch || process.Level != Lineage.Level || process.Stack != Lineage.Stack ||
            process.ExecutableSha256 != _factory.Inputs.EngineSha256 || invocation.ThreadId != Lineage.Thread ||
            invocation.ThreadId != Environment.CurrentManagedThreadId || process.GameplayOwnership == Guid.Empty)
            throw new InvalidOperationException("Controller context read lost its actual Actor/Common/Main/constructor/thread.");
        requireActual(invocation);
        if (_postProcessReadFault != fault || _sequence != sequence)
            throw new InvalidOperationException("Controller context guard swallowed field reentry or a current store.");
    }
}
