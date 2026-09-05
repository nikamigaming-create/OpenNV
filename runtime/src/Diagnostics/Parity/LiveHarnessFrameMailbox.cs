namespace OpenNV.Runtime.Diagnostics.Parity;

// One pending display frame and one wakeup. This is deliberately separate from
// the loss-detecting parity trace: preview replacement must never stall input.
internal sealed class LiveHarnessFrameMailbox
{
    private readonly object _sync = new();
    private LiveHarnessSurface? _pending;
    private bool _wakePending;
    private long _replaced;

    internal long Replaced { get { lock (_sync) return _replaced; } }

    internal bool Publish(LiveHarnessSurface frame)
    {
        lock (_sync)
        {
            if (_pending is not null) ++_replaced;
            _pending = frame;
            if (_wakePending) return false;
            _wakePending = true;
            return true;
        }
    }

    internal LiveHarnessSurface? TakeLatest()
    {
        lock (_sync)
        {
            var frame = _pending;
            _pending = null;
            _wakePending = false;
            return frame;
        }
    }
}
