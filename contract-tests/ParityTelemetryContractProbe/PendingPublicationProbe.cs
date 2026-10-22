using OpenNV.Runtime.Diagnostics.Parity;

internal static class PendingPublicationProbe
{
    internal static void Run()
    {
        TimelyCompletionObservedLate();
        CompletedLate();
        PendingTimeoutAndTerminalFault();
        QueueAndWorkerTiming();
        OwnerCanObserveStalledWriter();
        MarkerGeneration();
        SchedulerAndClockFaults();
        Console.WriteLine("OPENNV_PENDING_PUBLICATION_PASS deadlineMilliseconds=250 queueIncluded=true completionTimestamp=true " +
            "pendingAndCompletedLate=true oncePerWriteLoss=true queueAndWorkerSeparated=true ownerNotBlockedByWriter=true " +
            "originalFaultRetained=true markerGeneration=true cumulativeLossRetained=true injectedClockAndWriter=true");
    }

    private static void TimelyCompletionObservedLate()
    {
        var fixture = new Fixture();
        var pending = fixture.Start(() => { fixture.Now = 249; return Success(); });
        fixture.Now = 10; fixture.Run(); fixture.Now = 400;
        var result = pending.Observe();
        Require(result.Completed && !result.NewLoss && !result.Lost && result.Timing.TotalMilliseconds == 249 &&
            pending.CanClearFailureMarker(0), "Late owner observation rejected an on-time immutable completion.");
        pending.Wait();
    }

    private static void CompletedLate()
    {
        foreach (var completed in new double[] { 250, 251 })
        {
            var fixture = new Fixture();
            var pending = fixture.Start(() => { fixture.Now = completed; return Success(); });
            fixture.Now = 1; fixture.Run(); fixture.Now = 500;
            var result = pending.Observe();
            Require(result.Completed && result.NewLoss && result.Lost && result.Timing.TotalMilliseconds == completed &&
                !pending.CanClearFailureMarker(0) && !pending.Observe().NewLoss,
                "Already-completed late publication bypassed its exact deadline or duplicated loss.");
        }
    }

    private static void PendingTimeoutAndTerminalFault()
    {
        var fixture = new Fixture();
        var sourceFailure = new IOException("Injected original atomic-file refusal.");
        var pending = fixture.Start(() => throw sourceFailure);
        fixture.Now = 249;
        Require(!pending.Observe().Lost, "Pending writer failed before its interval.");
        fixture.Now = 250;
        Require(pending.Observe() is { Completed: false, NewLoss: true, Lost: true }, "Pending writer escaped its exact deadline.");
        for (var i = 0; i < 16; ++i)
        {
            ++fixture.CommandTicks;
            fixture.Now += 1;
            Require(!pending.Observe().NewLoss && fixture.Scheduled.Count == 1,
                "Repeated pending requests multiplied a writer or its loss.");
        }
        Require(fixture.CommandTicks == 16 && fixture.Writes == 0, "A pending writer blocked owner command progress.");
        fixture.Now = 300; fixture.Run();
        var result = pending.Observe();
        Require(result is { Completed: true, NewLoss: false, Lost: true } &&
            ReferenceEquals(result.Failure?.InnerException, sourceFailure) && !pending.CanClearFailureMarker(0),
            "Terminal failure lost its original source, cleared the marker or counted an existing timeout twice.");
        var direct = new Fixture();
        var fault = direct.Start(() => throw sourceFailure);
        direct.Now = 1; direct.Run();
        Require(fault.Observe() is { NewLoss: true, Receipt.Error: IOException } && !fault.Observe().NewLoss,
            "An on-time write fault was accepted or counted repeatedly.");
    }

    private static void QueueAndWorkerTiming()
    {
        var queued = new Fixture();
        var queueLate = queued.Start(() => { queued.Now = 261; return Success(); });
        queued.Now = 260; queued.Run();
        var queue = queueLate.Observe();
        Require(queue.NewLoss && queue.Timing.QueueMilliseconds == 260 && queue.Timing.WorkerMilliseconds == 1,
            "Queue delay escaped the deadline or became worker time.");
        var working = new Fixture();
        var workerLate = working.Start(() => { working.Now = 260; return Success(); });
        working.Now = 1; working.Run();
        var worker = workerLate.Observe();
        Require(worker.NewLoss && worker.Timing.QueueMilliseconds == 1 && worker.Timing.WorkerMilliseconds == 259,
            "Writer delay escaped the deadline or became queue time.");
    }

    private static void OwnerCanObserveStalledWriter()
    {
        var limit = TimeSpan.FromSeconds(5);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var observed = new ManualResetEventSlim();
        long now = 0;
        Action? work = null;
        var pending = LiveHarnessPendingPublication.Start(1, 0, () => Volatile.Read(ref now), () =>
        {
            entered.Set();
            if (!release.Wait(limit + limit)) throw new TimeoutException("Controlled writer was not released.");
            Volatile.Write(ref now, 300);
            return Success();
        }, action => work = action);
        Exception? writerFailure = null;
        Exception? ownerFailure = null;
        var commands = 0;
        var owner = new Thread(() =>
        {
            try
            {
                Require(!pending.Observe().Lost, "Pending writer failed before its interval.");
                Volatile.Write(ref now, 250); ++commands;
                Require(pending.Observe() is { NewLoss: true, Completed: false },
                    "Stalled write did not expose its owner deadline.");
            }
            catch (Exception error) { ownerFailure = error; }
            finally { observed.Set(); }
        }) { IsBackground = true };
        var writer = new Thread(() =>
        {
            try { work!(); }
            catch (Exception error) { writerFailure = error; }
        }) { IsBackground = true };
        var ownerStarted = false;
        Volatile.Write(ref now, 1);
        writer.Start();
        try
        {
            Require(entered.Wait(limit), "Controlled writer did not enter its held I/O callback.");
            Volatile.Write(ref now, 249);
            owner.Start(); ownerStarted = true;
            Require(observed.Wait(limit), "A held writer prevented the independent owner from observing its deadline.");
            if (ownerFailure is not null) throw new InvalidDataException("Independent publication owner failed.", ownerFailure);
        }
        finally
        {
            // Also release/join after the timeout assertion: a negative control
            // that accidentally holds _gate across I/O must not strand threads.
            release.Set();
            var writerJoined = writer.Join(limit + limit);
            var ownerJoined = !ownerStarted || owner.Join(limit + limit);
            Require(writerJoined && ownerJoined, "Controlled publication threads did not retire after release.");
        }
        if (writerFailure is not null) throw new InvalidDataException("Controlled writer failed.", writerFailure);
        Require(commands == 1 && pending.Observe() is { Completed: true, Lost: true, NewLoss: false },
            "Completing an overdue in-flight writer blocked commands or duplicated loss.");
    }

    private static void MarkerGeneration()
    {
        long cumulativeLoss = 1;
        var stale = new Fixture();
        var priorGeneration = stale.Start(() => Success(), generation: 0);
        stale.Now = 1; stale.Run(); _ = priorGeneration.Observe();
        Require(!priorGeneration.CanClearFailureMarker(cumulativeLoss), "Old snapshot erased a newer owner failure marker.");
        var fresh = new Fixture();
        var currentGeneration = fresh.Start(() => Success(), generation: cumulativeLoss);
        fresh.Now = 1; fresh.Run();
        Require(!currentGeneration.Observe().Lost && currentGeneration.CanClearFailureMarker(cumulativeLoss) &&
            currentGeneration.Timing.SnapshotFailureGeneration == 1 && cumulativeLoss == 1,
            "Fresh snapshot could not clear its marker or reset the cumulative loss counter.");
        Require(!currentGeneration.CanClearFailureMarker(2), "A later failure raced an already-successful snapshot's generation.");
    }

    private static void SchedulerAndClockFaults()
    {
        var original = new InvalidOperationException("Injected scheduler refusal.");
        var wrote = false;
        try
        {
            _ = LiveHarnessPendingPublication.Start(1, 0, () => 0,
                () => { wrote = true; return Success(); }, _ => throw original);
            throw new InvalidDataException("Scheduler failure was accepted.");
        }
        catch (InvalidOperationException error) when (ReferenceEquals(error, original)) { }
        Require(!wrote, "Refused scheduling executed a writer.");
        var fixture = new Fixture { Now = 10 };
        var pending = fixture.Start(Success);
        fixture.Now = 9;
        try { _ = pending.Observe(); throw new InvalidOperationException("Regressed owner clock was accepted."); }
        catch (InvalidDataException) { }
        fixture.Run(); // The worker also sees the invalid clock and must settle as an explicit fault.
        fixture.Now = 10;
        pending.Wait();
        Require(pending.Observe() is { Completed: true, NewLoss: true, Receipt.Error: not null } &&
            fixture.Writes == 0 && !pending.CanClearFailureMarker(0), "Worker clock failure hung, ran I/O or cleared the marker.");
        var dual = new Fixture { Now = 10 };
        var sourceFailure = new IOException("Injected I/O failure before a regressed completion clock.");
        var both = dual.Start(() => { dual.Now = 9; throw sourceFailure; });
        dual.Run(); dual.Now = 10; both.Wait();
        var dualResult = both.Observe();
        Require(dualResult is { NewLoss: true, Receipt.Error: AggregateException } &&
            ((AggregateException)dualResult.Receipt!.Error!).InnerExceptions.Any(error => ReferenceEquals(error, sourceFailure)) &&
            ((AggregateException)dualResult.Receipt.Error!).InnerExceptions.Any(error => error is InvalidDataException),
            "Completion clock fault overwrote the original writer failure.");
    }

    private static LiveHarnessFileWrite Success() => new(1, 1, null, null);
    private static void Require(bool value, string message) { if (!value) throw new InvalidDataException(message); }
    private sealed class Fixture
    {
        internal double Now;
        internal int Writes;
        internal int CommandTicks;
        internal List<Action> Scheduled { get; } = [];
        internal LiveHarnessPendingPublication Start(Func<LiveHarnessFileWrite> write, long generation = 0) =>
            LiveHarnessPendingPublication.Start(1, generation, () => Now, () => { ++Writes; return write(); }, Scheduled.Add);
        internal void Run() => Scheduled.Single()();
    }
}
