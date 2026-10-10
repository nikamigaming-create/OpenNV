namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutActorProcessRuntimeState
{
    // Cleanup metadata remains observable after a returned disposal. It cannot
    // execute, capture, restore, or clear a failed source invocation.
    internal FalloutMainNativeDeliveryBindings ReadMainNativeDeliveryBindings() => new(
        _process, _scriptCallerSource?.Identity, _scriptCallerLease, _scriptFrameLease,
        _scriptCallerInvocation?.Identity, MainScriptCallerSaveBlocker, _disposed);

    internal void RequireMainNativeDeliveryClosed()
    {
        var current = ReadMainNativeDeliveryBindings();
        if (!current.Closed)
            throw new InvalidOperationException("Actual Main native caller/fade delivery must close before dependent source retirement.");
    }

    internal void CloseMainNativeDeliveryLeases(ref IDisposable? caller, ref IDisposable? fade)
    {
        if (_scriptCallerManagedThread is { } thread && thread != System.Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("Main delivery retirement changed its actual constructor thread.");
        var before = ReadMainNativeDeliveryBindings();
        if (before.EnteredMain is not null)
            throw new NotSupportedException("Actual Main child must return or unwind before caller/fade retirement.");
        Validate(caller, FalloutMainNativeLeaseRole.Caller, before.Caller);
        Validate(fade, FalloutMainNativeLeaseRole.Fade, before.Fade);
        var failures = new List<Exception>();
        if (caller is not null)
        {
            try
            {
                caller.Dispose();
                if (_scriptCallerLease != Guid.Empty)
                    throw new InvalidOperationException("Returned caller disposal still owns its actual Main delivery binding.");
                caller = null;
            }
            catch (Exception error) { failures.Add(error); }
        }
        // The fade is a provider to this caller. A failed release retains both
        // handles, including a disposer that threw after its native/source
        // transition committed. Only an actual returned retry clears it.
        if (caller is null && _scriptCallerLease == Guid.Empty && fade is not null)
        {
            try
            {
                fade.Dispose();
                if (_scriptFrameLease != Guid.Empty)
                    throw new InvalidOperationException("Returned fade disposal still owns its actual source sampler binding.");
                fade = null;
            }
            catch (Exception error) { failures.Add(error); }
        }
        if (failures.Count != 0)
            throw new AggregateException("Main native caller/fade retirement retained actual failed handles.", failures);
        RequireMainNativeDeliveryClosed();

        void Validate(IDisposable? handle, FalloutMainNativeLeaseRole role, Guid bound)
        {
            if (handle is null)
            {
                if (bound != Guid.Empty)
                    throw new InvalidDataException("Main native retirement lost a still-owned delivery handle.");
                return;
            }
            if (handle is not IFalloutMainNativeDeliveryLease token || token.Process != before.Process ||
                token.SourceIdentity != before.SourceIdentity || token.Role != role || token.Identity == Guid.Empty ||
                token.ManagedThread != System.Environment.CurrentManagedThreadId ||
                bound != Guid.Empty && token.Identity != bound)
                throw new InvalidDataException("Main native retirement received a foreign, stale or untyped delivery handle.");
        }
    }
}
