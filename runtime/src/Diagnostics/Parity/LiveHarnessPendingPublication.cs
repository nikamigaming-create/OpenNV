namespace OpenNV.Runtime.Diagnostics.Parity;

internal sealed record LiveHarnessPublicationTiming(long Publication, long SnapshotFailureGeneration,
    double EnqueuedMilliseconds, double? WorkerStartedMilliseconds, double? CompletedMilliseconds)
{
    public double? QueueMilliseconds => WorkerStartedMilliseconds - EnqueuedMilliseconds;
    public double? WorkerMilliseconds => CompletedMilliseconds - WorkerStartedMilliseconds;
    public double? TotalMilliseconds => CompletedMilliseconds - EnqueuedMilliseconds;
}
internal sealed record LiveHarnessPublicationReceipt(LiveHarnessFileWrite? Write, Exception? Error,
    LiveHarnessPublicationTiming Timing);
internal sealed record LiveHarnessPublicationObservation(bool Completed, bool NewLoss, bool Lost,
    Exception? Failure, LiveHarnessPublicationReceipt? Receipt, LiveHarnessPublicationTiming Timing);

// One immutable snapshot, one writer, and one owner-side loss notification.
// The clock and scheduler are injected so queueing and completion boundaries
// can be proved without sleep, native timing or a healthy filesystem assumption.
internal sealed class LiveHarnessPendingPublication
{
    private readonly object _gate = new();
    private readonly Func<double> _clock;
    private readonly Func<LiveHarnessFileWrite> _write;
    private readonly TaskCompletionSource _settled = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private LiveHarnessPublicationTiming _timing;
    private LiveHarnessPublicationReceipt? _receipt;
    private double _lastClock;
    private bool _lossReported;

    private LiveHarnessPendingPublication(long publication, long failureGeneration,
        Func<double> clock, Func<LiveHarnessFileWrite> write)
    {
        _clock = clock; _write = write;
        var enqueued = clock();
        if (!double.IsFinite(enqueued) || enqueued < 0) throw new InvalidDataException("Publication clock is invalid.");
        _lastClock = enqueued;
        _timing = new(publication, failureGeneration, enqueued, null, null);
    }

    internal static LiveHarnessPendingPublication Start(long publication, long failureGeneration,
        Func<double> clock, Func<LiveHarnessFileWrite> write, Action<Action> schedule)
    {
        var pending = new LiveHarnessPendingPublication(publication, failureGeneration, clock, write);
        schedule(pending.Execute);
        return pending;
    }

    internal bool IsCompleted { get { lock (_gate) return _receipt is not null; } }
    internal LiveHarnessPublicationTiming Timing { get { lock (_gate) return _timing; } }
    internal void Wait() => _settled.Task.GetAwaiter().GetResult();

    private double ReadClock()
    {
        var now = _clock();
        if (!double.IsFinite(now) || now < _lastClock) throw new InvalidDataException("Publication clock regressed or became invalid.");
        _lastClock = now;
        return now;
    }

    private void Execute()
    {
        LiveHarnessFileWrite? written = null;
        Exception? failure = null;
        try
        {
            lock (_gate) _timing = _timing with { WorkerStartedMilliseconds = ReadClock() };
            written = _write(); // No I/O or engine access under the completion lock.
        }
        catch (Exception error) { failure = error; }
        lock (_gate)
        {
            double completed;
            try { completed = ReadClock(); }
            catch (Exception error) { completed = _lastClock; failure = failure is null ? error : new AggregateException(failure, error); }
            _timing = _timing with { CompletedMilliseconds = completed };
            // Receipt and completion time become observable atomically. Polling
            // never relies on a Task continuation's later completion signal.
            _receipt = new(written, failure, _timing);
        }
        _settled.SetResult();
    }

    internal LiveHarnessPublicationObservation Observe()
    {
        lock (_gate)
        {
            var now = ReadClock();
            var elapsed = (_receipt?.Timing.CompletedMilliseconds ?? now) - _timing.EnqueuedMilliseconds;
            var late = elapsed >= LiveHarnessPublicationCadence.IntervalMilliseconds;
            var lost = _receipt?.Error is not null || late || _lossReported;
            var newlyLost = lost && !_lossReported;
            if (newlyLost) _lossReported = true;
            Exception? failure = null;
            if (_receipt?.Error is { } error)
                failure = new IOException(FormattableString.Invariant($"Live-state publication writer failed: type={error.GetType().FullName} hresult=0x{error.HResult:X8} {error.Message}; ") + Describe(_timing, now), error);
            else if (late)
                failure = new IOException("Live-state publication exceeded its 250 ms interval; " + Describe(_timing, now));
            return new(_receipt is not null, newlyLost, lost, failure, _receipt, _timing);
        }
    }

    internal bool CanClearFailureMarker(long currentFailureGeneration)
    {
        lock (_gate)
            return _receipt is { Error: null } && !_lossReported &&
                _receipt.Timing.CompletedMilliseconds - _timing.EnqueuedMilliseconds < LiveHarnessPublicationCadence.IntervalMilliseconds &&
                _timing.SnapshotFailureGeneration == currentFailureGeneration;
    }

    private static string Describe(LiveHarnessPublicationTiming timing, double observed) =>
        FormattableString.Invariant($"publication={timing.Publication} snapshotFailureGeneration={timing.SnapshotFailureGeneration} queuedAtMs={timing.EnqueuedMilliseconds:R} workerStartedAtMs={timing.WorkerStartedMilliseconds:R} completedAtMs={timing.CompletedMilliseconds:R} observedAtMs={observed:R} queueMs={timing.QueueMilliseconds:R} workerMs={timing.WorkerMilliseconds:R}");
}
