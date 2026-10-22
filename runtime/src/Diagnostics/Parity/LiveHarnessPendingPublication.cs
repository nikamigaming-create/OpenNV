namespace OpenNV.Runtime.Diagnostics.Parity;

internal sealed record LiveHarnessPublicationTiming(long Publication, long SnapshotFailureGeneration,
    double EnqueuedMilliseconds, double? WorkerStartedMilliseconds, double? CompletedMilliseconds)
{
    // Every boundary uses the pending owner's monotonic clock. A phase's
    // elapsed time includes any descheduling; it is not a disk-service timer.
    public LiveHarnessFilePhase? FilePhase { get; init; }
    public double? TextWriteStartedMilliseconds { get; init; }
    public double? TextWriteCompletedMilliseconds { get; init; }
    public double? CommitStartedMilliseconds { get; init; }
    public double? CommitCompletedMilliseconds { get; init; }
    public double? QueueMilliseconds => WorkerStartedMilliseconds - EnqueuedMilliseconds;
    public double? WorkerMilliseconds => CompletedMilliseconds - WorkerStartedMilliseconds;
    public double? TotalMilliseconds => CompletedMilliseconds - EnqueuedMilliseconds;
    public double? TextWriteMilliseconds => TextWriteCompletedMilliseconds - TextWriteStartedMilliseconds;
    public double? CommitMilliseconds => CommitCompletedMilliseconds - CommitStartedMilliseconds;
    public double? ReceiptMilliseconds => CompletedMilliseconds - CommitCompletedMilliseconds;
}
internal sealed record LiveHarnessPublicationReceipt(LiveHarnessFileWrite? Write, Exception? Error,
    LiveHarnessPublicationTiming Timing);
internal sealed record LiveHarnessPublicationObservation(bool Completed, bool NewLoss, bool Lost,
    Exception? Failure, LiveHarnessPublicationReceipt? Receipt, LiveHarnessPublicationTiming Timing);

internal sealed record LiveHarnessPublicationError(string Type, int HResult, string Message, bool MessageTruncated)
{
    internal static LiveHarnessPublicationError? From(Exception? error) => error is null ? null :
        new(error.GetType().FullName ?? error.GetType().Name, error.HResult,
            error.Message.Length <= 2048 ? error.Message : error.Message[..2048], error.Message.Length > 2048);
}

internal sealed record LiveHarnessFailedPublication(LiveHarnessPublicationTiming Timing, bool Completed,
    LiveHarnessFileWrite? Write, LiveHarnessPublicationError? Failure, LiveHarnessPublicationError? WriterError);

// Owner-thread history retains the completion of a previously overdue writer.
// Healthy successors update current timing without erasing the failed receipt.
internal sealed class LiveHarnessPublicationHistory
{
    internal LiveHarnessPublicationTiming? LatestTiming { get; private set; }
    internal LiveHarnessFailedPublication? LastFailed { get; private set; }

    internal void Observe(LiveHarnessPublicationObservation observation)
    {
        LatestTiming = observation.Timing;
        if (observation.Lost)
            LastFailed = new(observation.Timing, observation.Completed, observation.Receipt?.Write,
                LiveHarnessPublicationError.From(observation.Failure), LiveHarnessPublicationError.From(observation.Receipt?.Error));
    }
}

// One immutable snapshot, one writer, and one owner-side loss notification.
// The clock and scheduler are injected so queueing and completion boundaries
// can be proved without sleep, native timing or a healthy filesystem assumption.
internal sealed class LiveHarnessPendingPublication
{
    private readonly object _gate = new();
    private readonly Func<double> _clock;
    private readonly Func<Action<LiveHarnessFilePhase>, LiveHarnessFileWrite> _write;
    private readonly TaskCompletionSource _settled = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private LiveHarnessPublicationTiming _timing;
    private LiveHarnessPublicationReceipt? _receipt;
    private double _lastClock;
    private bool _lossReported;

    private LiveHarnessPendingPublication(long publication, long failureGeneration,
        Func<double> clock, Func<Action<LiveHarnessFilePhase>, LiveHarnessFileWrite> write)
    {
        _clock = clock; _write = write;
        var enqueued = clock();
        if (!double.IsFinite(enqueued) || enqueued < 0) throw new InvalidDataException("Publication clock is invalid.");
        _lastClock = enqueued;
        _timing = new(publication, failureGeneration, enqueued, null, null);
    }

    internal static LiveHarnessPendingPublication Start(long publication, long failureGeneration,
        Func<double> clock, Func<LiveHarnessFileWrite> write, Action<Action> schedule) =>
        Start(publication, failureGeneration, clock, _ => write(), schedule);

    internal static LiveHarnessPendingPublication Start(long publication, long failureGeneration,
        Func<double> clock, Func<Action<LiveHarnessFilePhase>, LiveHarnessFileWrite> write, Action<Action> schedule)
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

    private void ObserveFilePhase(LiveHarnessFilePhase phase)
    {
        lock (_gate)
        {
            var expected = _timing.FilePhase switch
            {
                null => LiveHarnessFilePhase.TextWriteStarted,
                LiveHarnessFilePhase.TextWriteStarted => LiveHarnessFilePhase.TextWriteCompleted,
                LiveHarnessFilePhase.TextWriteCompleted => LiveHarnessFilePhase.CommitStarted,
                LiveHarnessFilePhase.CommitStarted => LiveHarnessFilePhase.CommitCompleted,
                _ => throw new InvalidDataException("Publication file phase continued after commit completion."),
            };
            if (_receipt is not null || _timing.WorkerStartedMilliseconds is null || phase != expected)
                throw new InvalidDataException("Publication file phase is out of order.");
            var now = ReadClock();
            _timing = phase switch
            {
                LiveHarnessFilePhase.TextWriteStarted => _timing with { FilePhase = phase, TextWriteStartedMilliseconds = now },
                LiveHarnessFilePhase.TextWriteCompleted => _timing with { FilePhase = phase, TextWriteCompletedMilliseconds = now },
                LiveHarnessFilePhase.CommitStarted => _timing with { FilePhase = phase, CommitStartedMilliseconds = now },
                LiveHarnessFilePhase.CommitCompleted => _timing with { FilePhase = phase, CommitCompletedMilliseconds = now },
                _ => throw new InvalidDataException("Publication file phase is unsupported."),
            };
        }
    }

    private void Execute()
    {
        LiveHarnessFileWrite? written = null;
        Exception? failure = null;
        try
        {
            lock (_gate) _timing = _timing with { WorkerStartedMilliseconds = ReadClock() };
            written = _write(ObserveFilePhase); // No I/O or engine access under the completion lock.
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
        LiveHarnessPublicationTiming timing;
        LiveHarnessPublicationReceipt? receipt;
        double now;
        bool late, lost, newlyLost;
        lock (_gate)
        {
            now = ReadClock();
            timing = _timing;
            receipt = _receipt;
            var elapsed = (receipt?.Timing.CompletedMilliseconds ?? now) - timing.EnqueuedMilliseconds;
            late = elapsed >= LiveHarnessPublicationCadence.IntervalMilliseconds;
            lost = receipt?.Error is not null || late || _lossReported;
            newlyLost = lost && !_lossReported;
            if (newlyLost) _lossReported = true;
        }
        // Formatting and virtual exception metadata do not own publication
        // state. Keep them outside the worker's phase/completion gate and use
        // only this immutable observation, even if the worker advances later.
        Exception? failure = null;
        if (receipt?.Error is { } error)
            failure = new IOException(FormattableString.Invariant($"Live-state publication writer failed: type={error.GetType().FullName} hresult=0x{error.HResult:X8} {error.Message}; ") + Describe(timing, now), error);
        else if (late)
            failure = new IOException("Live-state publication exceeded its 250 ms interval; " + Describe(timing, now));
        return new(receipt is not null, newlyLost, lost, failure, receipt, timing);
    }

    internal bool CanClearFailureMarker(long currentFailureGeneration)
    {
        lock (_gate)
            return _receipt is { Error: null } && !_lossReported &&
                _receipt.Timing.CompletedMilliseconds - _timing.EnqueuedMilliseconds < LiveHarnessPublicationCadence.IntervalMilliseconds &&
                _timing.SnapshotFailureGeneration == currentFailureGeneration;
    }

    private static string Describe(LiveHarnessPublicationTiming timing, double observed) =>
        FormattableString.Invariant($"publication={timing.Publication} snapshotFailureGeneration={timing.SnapshotFailureGeneration} queuedAtMs={timing.EnqueuedMilliseconds:R} workerStartedAtMs={timing.WorkerStartedMilliseconds:R} completedAtMs={timing.CompletedMilliseconds:R} observedAtMs={observed:R} queueMs={timing.QueueMilliseconds:R} workerMs={timing.WorkerMilliseconds:R} filePhase={timing.FilePhase} textWriteStartedAtMs={timing.TextWriteStartedMilliseconds:R} textWriteCompletedAtMs={timing.TextWriteCompletedMilliseconds:R} commitStartedAtMs={timing.CommitStartedMilliseconds:R} commitCompletedAtMs={timing.CommitCompletedMilliseconds:R}");
}
