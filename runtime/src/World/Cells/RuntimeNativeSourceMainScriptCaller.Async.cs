using Godot;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class RuntimeNativeSourceMainScriptCaller
{
    private Task? _activeMainCall;
    private bool _stopMainDelivery;
    internal bool CanDestroyAfterRetirement => _activeMainCall is null && _callerLease is null && _fadeLease is null;
    private void DeliverMain(ulong frame, float seconds)
    {
        if (_stopMainDelivery || _activeMainCall is { IsCompleted: false }) return;
        ObserveReturnedMainCall();
        if (_failure is not null) return;
        _activeMainCall = _world.ExecuteCampaignMainScriptCaller(frame, seconds);
        // Completed synchronous children still publish an immediate failure.
        // Awaited Player work retains the same source invocation until return.
        ObserveReturnedMainCall();
    }
    private void ObserveReturnedMainCall()
    {
        if (_activeMainCall is not { IsCompleted: true } call) return;
        try { call.GetAwaiter().GetResult(); }
        catch (Exception failure) { PublishMainFailure(failure); }
        _activeMainCall = null;
    }
    private void PublishMainFailure(Exception failure)
    {
        _failure ??= failure; ProcessMode = ProcessModeEnum.Disabled;
        try { _failed(failure); }
        catch (Exception publication) { _failure = new AggregateException("Main source failure and error publication both failed.", _failure, publication); }
        GD.PushError("OPENNV_SOURCE_MAIN_CALLER_REFUSED " + _failure);
    }
    internal async Task StopAndDrain()
    {
        _stopMainDelivery = true; ProcessMode = ProcessModeEnum.Disabled;
        // The real coordinator requests/cancels actual native reads first.
        // This only awaits the entered source invocation and observes its
        // original result; it never fabricates source completion on shutdown.
        if (_activeMainCall is { } call)
        {
            try { await call; }
            catch (Exception failure) { PublishMainFailure(failure); }
            finally { if (ReferenceEquals(_activeMainCall, call)) _activeMainCall = null; }
        }
    }
    private void RequireMainCallDrained()
    {
        _stopMainDelivery = true;
        if (_activeMainCall is { IsCompleted: false })
            throw new NotSupportedException("Actual Main Player/native child must unwind before source caller/fade retirement.");
        ObserveReturnedMainCall();
    }
}
