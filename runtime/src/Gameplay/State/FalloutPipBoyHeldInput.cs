using System.Runtime.ExceptionServices;

namespace OpenNV.Runtime.Gameplay.State;

// This process-local input lease records requests, not successful gameplay
// outcomes. Construction/cold attachment requires a real release before a held
// control can request focus. Source revocation cannot replay the same hold.
internal sealed class FalloutPipBoyHeldInput
{
    private ExceptionDispatchInfo? _failure;
    internal bool AwaitRelease { get; private set; } = true;
    internal bool RequestedFocus { get; private set; }
    internal bool Retired { get; private set; }
    internal bool Tracked { get; private set; }
    internal bool Held { get; private set; }
    internal FalloutPipBoyInputBlocker Blocker { get; private set; } = FalloutPipBoyInputBlocker.Unbound;
    internal long RequestOrdinal { get; private set; }
    internal bool LastRequestReturned { get; private set; }
    internal string? Failure => _failure?.SourceException.ToString();
    internal string? RetirementFailure { get; private set; }

    internal void Observe(bool tracked, bool held, FalloutPipBoyInputBlocker blocker, Action<bool> request, bool actualFocus = false)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireActive();
        if (!Enum.IsDefined(blocker)) throw new ArgumentOutOfRangeException(nameof(blocker));
        Tracked = tracked; Held = held; Blocker = blocker;
        if (!tracked) AwaitRelease = true;
        else if (!held) AwaitRelease = false;
        else if (blocker != FalloutPipBoyInputBlocker.None) AwaitRelease = true;
        var desired = tracked && held && blocker == FalloutPipBoyInputBlocker.None && !AwaitRelease;
        Deliver(desired, request, !desired && actualFocus && (!tracked || blocker != FalloutPipBoyInputBlocker.None));
    }

    internal void Retire(Action<bool> request, bool actualFocus = false)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (Retired) return;
        try
        {
            // Cleanup is a distinct once-only delivery even when an opening
            // callback committed a prefix and then failed. The original fault
            // stays retained and cannot authorize another opening request.
            // An explicit page/open request may own actual focus independently
            // of this held grip. Retire that living route as well, but never
            // replay a failed close that already entered its callback.
            Deliver(false, request, actualFocus && _failure is null);
        }
        catch (Exception error)
        {
            RetirementFailure = error.ToString();
            throw;
        }
        finally { AwaitRelease = true; Retired = true; }
    }

    private void Deliver(bool focused, Action<bool> request, bool force = false)
    {
        if (RequestedFocus == focused && !force) return;
        RequestedFocus = focused;
        RequestOrdinal = checked(RequestOrdinal + 1);
        LastRequestReturned = false;
        try { request(focused); LastRequestReturned = true; }
        catch (Exception error)
        {
            _failure ??= ExceptionDispatchInfo.Capture(error);
            throw;
        }
    }

    private void RequireActive()
    {
        if (Retired) throw new ObjectDisposedException(nameof(FalloutPipBoyHeldInput));
        _failure?.Throw();
    }
}
