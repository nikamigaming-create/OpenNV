using System.Text.Json;
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
        AtomicFilePhasesAndSnapshots();
        PhaseTimingAndHistory();
        PhaseFaults();
        DiagnosticFormattingDoesNotOwnCompletion();
        Console.WriteLine("OPENNV_PENDING_PUBLICATION_PASS deadlineMilliseconds=250 queueIncluded=true completionTimestamp=true " +
            "pendingAndCompletedLate=true oncePerWriteLoss=true queueAndWorkerSeparated=true ownerNotBlockedByWriter=true " +
            "originalFaultRetained=true markerGeneration=true cumulativeLossRetained=true injectedClockAndWriter=true " +
            "actualAtomicPhases=true durableSnapshots=true phaseTiming=true completedLossHistory=true boundedErrors=true " +
            "diagnosticFormattingOutsideCompletionGate=true");
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
        var pending = LiveHarnessPendingPublication.Start(1, 0, () => Volatile.Read(ref now), phase =>
        {
            phase(LiveHarnessFilePhase.TextWriteStarted);
            entered.Set();
            if (!release.Wait(limit + limit)) throw new TimeoutException("Controlled writer was not released.");
            Volatile.Write(ref now, 300);
            phase(LiveHarnessFilePhase.TextWriteCompleted);
            phase(LiveHarnessFilePhase.CommitStarted);
            phase(LiveHarnessFilePhase.CommitCompleted);
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
        })
        { IsBackground = true };
        var writer = new Thread(() =>
        {
            try { work!(); }
            catch (Exception error) { writerFailure = error; }
        })
        { IsBackground = true };
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

    private static void AtomicFilePhasesAndSnapshots()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-publication-phases-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "live.json");
            File.WriteAllText(path, "old-complete-snapshot");
            var phases = new List<LiveHarnessFilePhase>();
            using (var priorReader = new StreamReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete)))
            {
                var result = LiveHarnessAtomicFile.Write(path, "new-complete-snapshot", phase =>
                {
                    phases.Add(phase);
                    if (phase != LiveHarnessFilePhase.CommitCompleted)
                        Require(File.ReadAllText(path) == "old-complete-snapshot", "File publication exposed new text before atomic commit.");
                    if (phase is LiveHarnessFilePhase.TextWriteCompleted or LiveHarnessFilePhase.CommitStarted)
                        Require(File.ReadAllText(path + ".pending") == "new-complete-snapshot", "Text completion was reported before the immutable pending file was complete.");
                    if (phase == LiveHarnessFilePhase.CommitCompleted)
                        Require(File.ReadAllText(path) == "new-complete-snapshot" && !File.Exists(path + ".pending"),
                            "Commit completion was reported before the destination snapshot replaced its pending file.");
                });
                Require(phases.SequenceEqual(Enum.GetValues<LiveHarnessFilePhase>()) && result.Attempts >= 1 &&
                    priorReader.ReadToEnd() == "old-complete-snapshot", "Atomic phase order or the existing reader's old complete snapshot was lost.");
            }
            phases.Clear();
            try
            {
                LiveHarnessAtomicFile.Write(Path.Combine(directory, "absent", "live.json"), "new", phases.Add);
                throw new InvalidOperationException("Absent-directory text write was accepted.");
            }
            catch (DirectoryNotFoundException) { }
            Require(phases.SequenceEqual([LiveHarnessFilePhase.TextWriteStarted]), "Failed text I/O invented a completed text or commit phase.");
            phases.Clear();
            try
            {
                LiveHarnessAtomicFile.Write(path, "not-committed", phase =>
                {
                    phases.Add(phase);
                    // Deliberately remove only this fixture's pending file to
                    // exercise an actual atomic-commit I/O failure.
                    if (phase == LiveHarnessFilePhase.CommitStarted) File.Delete(path + ".pending");
                });
                throw new InvalidOperationException("Missing pending snapshot was committed.");
            }
            catch (IOException) { }
            Require(phases.SequenceEqual([LiveHarnessFilePhase.TextWriteStarted, LiveHarnessFilePhase.TextWriteCompleted, LiveHarnessFilePhase.CommitStarted]) &&
                File.ReadAllText(path) == "new-complete-snapshot", "Commit failure invented completion or replaced the last durable snapshot.");
        }
        finally
        {
            foreach (var path in Directory.EnumerateFiles(directory)) File.Delete(path);
            Directory.Delete(directory);
        }
    }

    private static void PhaseTimingAndHistory()
    {
        foreach (var heldPhase in new[] { LiveHarnessFilePhase.TextWriteStarted, LiveHarnessFilePhase.CommitStarted, LiveHarnessFilePhase.CommitCompleted })
        {
            var fixture = new Fixture();
            var history = new LiveHarnessPublicationHistory();
            var losses = 0;
            LiveHarnessPendingPublication? pending = null;
            pending = fixture.Start(phase =>
            {
                fixture.Now = 2; phase(LiveHarnessFilePhase.TextWriteStarted); Hold(LiveHarnessFilePhase.TextWriteStarted);
                ++fixture.Now; phase(LiveHarnessFilePhase.TextWriteCompleted);
                ++fixture.Now; phase(LiveHarnessFilePhase.CommitStarted); Hold(LiveHarnessFilePhase.CommitStarted);
                ++fixture.Now; phase(LiveHarnessFilePhase.CommitCompleted); Hold(LiveHarnessFilePhase.CommitCompleted);
                ++fixture.Now; return Success();
            }, publication: 155);
            fixture.Now = 1; fixture.Run();
            var completed = pending.Observe(); history.Observe(completed);
            Require(completed is { Completed: true, Lost: true, NewLoss: false, Receipt.Error: null } &&
                !pending.CanClearFailureMarker(0) && losses == 1 && history.LastFailed is { Completed: true, Write: not null, WriterError: null } &&
                history.LastFailed.Timing == completed.Timing && history.LastFailed.Failure is not null,
                "A pending timeout lost its eventual successful completion, duplicated its loss or cleared its marker.");
            var timing = completed.Timing;
            var expected = heldPhase switch
            {
                LiveHarnessFilePhase.TextWriteStarted => (Text: 259d, Commit: 1d, Receipt: 1d),
                LiveHarnessFilePhase.CommitStarted => (Text: 1d, Commit: 257d, Receipt: 1d),
                _ => (Text: 1d, Commit: 1d, Receipt: 256d),
            };
            Require(timing.QueueMilliseconds == 1 && timing.TextWriteMilliseconds == expected.Text &&
                timing.CommitMilliseconds == expected.Commit && timing.ReceiptMilliseconds == expected.Receipt,
                "Measured text, commit or receipt delay changed clock domain or was attributed to the wrong phase.");
            var retained = history.LastFailed;
            var healthy = new Fixture { Now = 300 };
            var successor = healthy.Start(phase =>
            {
                healthy.Now = 302; phase(LiveHarnessFilePhase.TextWriteStarted);
                healthy.Now = 303; phase(LiveHarnessFilePhase.TextWriteCompleted);
                healthy.Now = 304; phase(LiveHarnessFilePhase.CommitStarted);
                healthy.Now = 305; phase(LiveHarnessFilePhase.CommitCompleted);
                healthy.Now = 306; return Success();
            }, generation: losses, publication: 156);
            healthy.Now = 301; healthy.Run();
            var next = successor.Observe(); history.Observe(next);
            Require(!next.Lost && !next.NewLoss && successor.CanClearFailureMarker(losses) && losses == 1 &&
                ReferenceEquals(history.LastFailed, retained) && history.LatestTiming?.Publication == 156,
                "Healthy publication erased completed failure history or changed its failure generation.");
            var json = JsonSerializer.SerializeToElement(history.LastFailed);
            Require(json.GetProperty("Completed").GetBoolean() && json.GetProperty("Timing").GetProperty("Publication").GetInt64() == 155 &&
                json.GetProperty("WriterError").ValueKind == JsonValueKind.Null,
                "Retained completed timeout cannot be serialized independently of a healthy successor.");

            void Hold(LiveHarnessFilePhase phase)
            {
                if (phase != heldPhase) return;
                fixture.Now = 249;
                var onTime = pending!.Observe(); history.Observe(onTime);
                Require(!onTime.Lost && history.LastFailed is null, "File phase failed before the exact publication deadline.");
                fixture.Now = 250;
                var late = pending.Observe(); history.Observe(late);
                if (late.NewLoss) ++losses;
                Require(late is { Completed: false, Lost: true, NewLoss: true } && late.Timing.FilePhase == phase &&
                    late.Timing.CompletedMilliseconds is null && history.LastFailed is { Completed: false },
                    "In-flight publication inferred a different phase or invented its completion.");
                if (phase == LiveHarnessFilePhase.TextWriteStarted) Require(late.Timing.TextWriteCompletedMilliseconds is null, "Pending text write invented completion.");
                if (phase == LiveHarnessFilePhase.CommitStarted) Require(late.Timing.CommitCompletedMilliseconds is null, "Pending commit invented completion.");
                fixture.Now = 260;
            }
        }
    }

    private static void PhaseFaults()
    {
        foreach (var failedPhase in new[] { LiveHarnessFilePhase.TextWriteStarted, LiveHarnessFilePhase.CommitStarted })
        {
            var fixture = new Fixture();
            var sourceError = new IOException(new string('x', 2200));
            var pending = fixture.Start(phase =>
            {
                fixture.Now = 2; phase(LiveHarnessFilePhase.TextWriteStarted);
                if (failedPhase == LiveHarnessFilePhase.CommitStarted)
                {
                    fixture.Now = 3; phase(LiveHarnessFilePhase.TextWriteCompleted);
                    fixture.Now = 4; phase(LiveHarnessFilePhase.CommitStarted);
                }
                throw sourceError;
            });
            fixture.Now = 1; fixture.Run();
            var result = pending.Observe();
            var history = new LiveHarnessPublicationHistory(); history.Observe(result);
            Require(result.NewLoss && result.Completed && !pending.Observe().NewLoss && ReferenceEquals(result.Receipt?.Error, sourceError) &&
                result.Timing.FilePhase == failedPhase && result.Timing.CommitCompletedMilliseconds is null &&
                history.LastFailed?.WriterError is { Type: "System.IO.IOException", MessageTruncated: true, Message.Length: 2048 } error &&
                error.HResult == sourceError.HResult && !pending.CanClearFailureMarker(0),
                "Phase failure lost its source exception, invented completion or escaped its bounded retained projection.");
            _ = JsonSerializer.Serialize(history.LastFailed);
            if (failedPhase == LiveHarnessFilePhase.TextWriteStarted)
                Require(result.Timing.TextWriteCompletedMilliseconds is null, "Failed text write acquired a completion timestamp.");
        }
        var invalid = new Fixture();
        var outOfOrder = invalid.Start(phase => { phase(LiveHarnessFilePhase.CommitStarted); return Success(); });
        invalid.Now = 1; invalid.Run();
        Require(outOfOrder.Observe() is { NewLoss: true, Receipt.Error: InvalidDataException, Timing.FilePhase: null },
            "Out-of-order phase marker was silently accepted.");
        var regressed = new Fixture { Now = 10 };
        var clockFault = regressed.Start(phase =>
        {
            phase(LiveHarnessFilePhase.TextWriteStarted);
            regressed.Now = 10; phase(LiveHarnessFilePhase.TextWriteCompleted);
            return Success();
        });
        regressed.Now = 11; regressed.Run(); regressed.Now = 11;
        Require(clockFault.Observe() is
        {
            NewLoss: true, Receipt.Error: not null,
            Timing.FilePhase: LiveHarnessFilePhase.TextWriteStarted, Timing.TextWriteCompletedMilliseconds: null
        },
            "File phase accepted a regressed clock or hid its failure.");
    }

    private static void DiagnosticFormattingDoesNotOwnCompletion()
    {
        var limit = TimeSpan.FromSeconds(5);
        using var formatting = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var accessed = new ManualResetEventSlim();
        var sourceError = new BlockingMessageIOException(formatting, release, limit + limit);
        var fixture = new Fixture();
        var pending = fixture.Start(phase =>
        {
            phase(LiveHarnessFilePhase.TextWriteStarted);
            phase(LiveHarnessFilePhase.TextWriteCompleted);
            phase(LiveHarnessFilePhase.CommitStarted);
            phase(LiveHarnessFilePhase.CommitCompleted);
            throw sourceError;
        }, publication: 539);
        fixture.Now = 1; fixture.Run();
        LiveHarnessPublicationObservation? observation = null;
        LiveHarnessPublicationTiming? terminalTiming = null;
        Exception? observerFailure = null, accessorFailure = null;
        var observer = new Thread(() =>
        {
            try { observation = pending.Observe(); }
            catch (Exception error) { observerFailure = error; }
        })
        { IsBackground = true };
        var accessor = new Thread(() =>
        {
            try
            {
                terminalTiming = pending.Timing;
                Require(pending.IsCompleted && terminalTiming is { FilePhase: LiveHarnessFilePhase.CommitCompleted, CompletedMilliseconds: 1 } &&
                    !pending.CanClearFailureMarker(0), "Diagnostic formatting concealed terminal publication or its actual file phase.");
                pending.Wait();
            }
            catch (Exception error) { accessorFailure = error; }
            finally { accessed.Set(); }
        })
        { IsBackground = true };
        var accessorStarted = false;
        observer.Start();
        try
        {
            Require(formatting.Wait(limit), "The controlled exception formatter was not entered.");
            accessor.Start(); accessorStarted = true;
            Require(accessed.Wait(limit), "Virtual diagnostic formatting held the worker phase/completion gate.");
        }
        finally
        {
            // Also release and join on the old implementation's failure: the
            // negative control must not strand either controlled thread.
            release.Set();
            var observerJoined = observer.Join(limit + limit);
            var accessorJoined = !accessorStarted || accessor.Join(limit + limit);
            Require(observerJoined && accessorJoined, "Controlled diagnostic threads did not retire after release.");
        }
        if (observerFailure is not null) throw new InvalidDataException("Controlled diagnostic observer failed.", observerFailure);
        if (accessorFailure is not null) throw new InvalidDataException("Independent terminal publication accessor failed.", accessorFailure);
        Require(observation is { Completed: true, Lost: true, NewLoss: true } &&
            ReferenceEquals(observation.Receipt?.Error, sourceError) && ReferenceEquals(observation.Failure?.InnerException, sourceError) &&
            ReferenceEquals(observation.Timing, terminalTiming) && !pending.Observe().NewLoss,
            "Diagnostic formatting changed its immutable receipt, original error identity or exactly-once loss admission.");
        var history = new LiveHarnessPublicationHistory(); history.Observe(observation!);
        var retained = history.LastFailed;
        Require(retained is { Completed: true, WriterError: not null } &&
            retained.WriterError.Type == sourceError.GetType().FullName && retained.WriterError.HResult == sourceError.HResult &&
            retained.WriterError.Message == "Controlled writer error.", "Formatting lost the actual terminal writer error from retained history.");
        var healthy = new Fixture { Now = 10 };
        var successor = healthy.Start(Success, generation: 1);
        healthy.Now = 11; healthy.Run(); history.Observe(successor.Observe());
        Require(ReferenceEquals(history.LastFailed, retained) && successor.CanClearFailureMarker(1) && !pending.CanClearFailureMarker(1),
            "A healthy successor erased diagnostic failure history or cleared the failed publication's marker capability.");
        _ = JsonSerializer.Serialize(history.LastFailed);
    }

    private sealed class BlockingMessageIOException(ManualResetEventSlim entered, ManualResetEventSlim release, TimeSpan limit)
        : IOException("Controlled writer error.")
    {
        public override string Message
        {
            get
            {
                entered.Set();
                if (!release.Wait(limit)) throw new TimeoutException("Controlled diagnostic formatter was not released.");
                return base.Message;
            }
        }
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
        internal LiveHarnessPendingPublication Start(Func<Action<LiveHarnessFilePhase>, LiveHarnessFileWrite> write,
            long generation = 0, long publication = 1) =>
            LiveHarnessPendingPublication.Start(publication, generation, () => Now, phase => { ++Writes; return write(phase); }, Scheduled.Add);
        internal void Run() => Scheduled.Single()();
    }
}
