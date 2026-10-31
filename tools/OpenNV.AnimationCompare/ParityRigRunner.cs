namespace OpenNV.AnimationCompare;

internal sealed class ParityRigRunner
{
    private int _running;

    internal async Task<ParityRigRunResult> RunAsync(ParityRigPlan plan,
        IReadOnlyList<IParityRigEngineControl> engines, CancellationToken cancellationToken)
    {
        Validate(plan, engines);
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
            throw new InvalidOperationException("A controlled rig is already running.");
        var run = Guid.NewGuid();
        var cases = new List<ParityRigCaseResult>();
        var checkpoints = new List<ParityRigReceipt>();
        var restorations = new List<ParityRigReceipt>();
        var errors = new List<string>();
        // Record attempted saves too: an adapter can finish a save before a
        // lost receipt/timeout. Its unique identity is still the restore target.
        var attempted = new List<(IParityRigEngineControl Engine, string? Checkpoint)>();
        var confirmed = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            foreach (var engine in engines)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!plan.PreserveInitialState)
                {
                    // Explicit disposable diagnostic worlds still require
                    // bounded quiescence; they make no save/restore claim.
                    attempted.Add((engine, null));
                    continue;
                }
                var checkpoint = Guid.NewGuid().ToString("N");
                attempted.Add((engine, checkpoint));
                checkpoints.Add(await Execute(engine, Request("checkpoint.save", checkpoint), plan.CommandTimeout, cancellationToken));
                confirmed.Add(engine.Engine);
            }
            foreach (var item in plan.Cases)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (item.Blockers.Count != 0)
                {
                    cases.Add(new(item.Id, "unbound", [], item.Blockers));
                    continue;
                }
                var receipts = new List<ParityRigReceipt>();
                var caseErrors = new List<string>();
                try
                {
                    await ObserveBoth("rig.prepare", null, plan.CommandTimeout);
                    foreach (var action in item.Actions)
                    {
                        var observations = await ObserveBoth("rig.action", action, plan.CommandTimeout + action.ObservationDuration);
                        foreach (var receipt in observations)
                        {
                            foreach (var lane in action.RequiredEvidence)
                                if (!receipt.Evidence.Any(evidence => evidence.Lane == lane))
                                    throw new InvalidDataException($"{receipt.Engine}/{item.Id}/{action.Id} has no {lane} evidence.");
                        }
                    }
                }
                catch (Exception exception) when (Recoverable(exception))
                {
                    caseErrors.Add(exception.Message);
                    cases.Add(new(item.Id, "failed", receipts, caseErrors));
                    // A bad resource must not strand the rest of a large batch.
                    // Continue only after both engines prove no delayed command,
                    // input, capture or world transition can leak into the next case.
                    foreach (var engine in engines)
                    {
                        using var cleanup = new CancellationTokenSource(plan.RestoreTimeout);
                        var request = Request("rig.quiesce");
                        var quiet = await engine.QuiesceAsync(request, cleanup.Token).WaitAsync(cleanup.Token);
                        RequireReceipt(engine, request, quiet);
                        receipts.Add(quiet);
                    }
                    cancellationToken.ThrowIfCancellationRequested();
                    continue;
                }
                cases.Add(new(item.Id, "observed", receipts, caseErrors));

                async Task<ParityRigReceipt[]> ObserveBoth(string operation, ParityRigAction? action, TimeSpan timeout)
                {
                    var pending = engines.Select(engine => Execute(engine,
                        Request(operation, item: item, action: action), timeout, cancellationToken)).ToArray();
                    try { return await Task.WhenAll(pending); }
                    finally
                    {
                        // Preserve the successful peer and failed receipts even
                        // when WhenAll throws; partial evidence is still evidence.
                        foreach (var task in pending)
                            if (task.IsCompletedSuccessfully) receipts.Add(task.Result);
                            else if (task.Exception?.GetBaseException() is ParityRigControlFailure failure)
                                receipts.Add(failure.Receipt);
                    }
                }
            }
        }
        catch (Exception exception) when (Recoverable(exception))
        {
            errors.Add(exception.Message);
        }
        finally
        {
            // Cleanup deliberately ignores run cancellation. Each operation has
            // its own bound and each engine is attempted despite another failure.
            foreach (var (engine, checkpoint) in attempted)
            {
                try
                {
                    using var cleanup = new CancellationTokenSource(plan.RestoreTimeout);
                    var request = Request("rig.quiesce", checkpoint);
                    var quiet = await engine.QuiesceAsync(request, cleanup.Token).WaitAsync(cleanup.Token);
                    RequireReceipt(engine, request, quiet);
                    restorations.Add(quiet);
                    if (checkpoint is null) continue;
                    if (!confirmed.Contains(engine.Engine))
                        throw new InvalidOperationException($"{engine.Engine} checkpoint save was not confirmed; restore requires recovery of {checkpoint}.");
                    restorations.Add(await Execute(engine, Request("checkpoint.load", checkpoint), plan.RestoreTimeout, CancellationToken.None));
                }
                catch (Exception exception)
                {
                    errors.Add($"{engine.Engine} {(checkpoint is null ? "quiescence" : "restoration")} failed: {exception.Message}");
                }
            }
            Volatile.Write(ref _running, 0);
        }
        var attemptedCases = cases.Select(item => item.CaseId).ToHashSet(StringComparer.Ordinal);
        foreach (var item in plan.Cases)
            if (!attemptedCases.Contains(item.Id))
                cases.Add(new(item.Id, "not-run", [], ["The run ended before this case was attempted."]));
        return new(run, "controlled-rig-not-campaign-acceptance", plan.SourceStackHash, cases, checkpoints, restorations, errors,
            plan.PreserveInitialState ? "checkpoint-and-restore" : "disposable-world-no-restore");

        ParityRigRequest Request(string operation, string? checkpoint = null, ParityRigCase? item = null, ParityRigAction? action = null) =>
            new(run, Guid.NewGuid(), operation, plan.SourceStackHash, checkpoint, item, action, plan.Width, plan.Height, plan.TargetFramesPerSecond);
    }

    private static async Task<ParityRigReceipt> Execute(IParityRigEngineControl engine, ParityRigRequest request,
        TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        var receipt = await engine.ExecuteAsync(request, deadline.Token).WaitAsync(deadline.Token);
        RequireReceipt(engine, request, receipt);
        return receipt;
    }

    private static void RequireReceipt(IParityRigEngineControl engine, ParityRigRequest request, ParityRigReceipt receipt)
    {
        if (receipt.Run != request.Run || receipt.Request != request.Request || receipt.Engine != engine.Engine ||
            receipt.Operation != request.Operation || receipt.SourceStackHash != request.SourceStackHash)
            throw new InvalidDataException($"{engine.Engine} returned a stale or mismatched rig receipt for {request.Request:N}.");
        if (receipt.Disposition != "completed" || receipt.Error is not null)
            throw new ParityRigControlFailure(receipt);
        foreach (var evidence in receipt.Evidence)
            if (string.IsNullOrWhiteSpace(evidence.Lane) || string.IsNullOrWhiteSpace(evidence.Path) ||
                string.IsNullOrWhiteSpace(evidence.Schema) || !Hash(evidence.Sha256))
                throw new InvalidDataException($"{engine.Engine} returned incomplete rig evidence provenance.");
    }

    private static void Validate(ParityRigPlan plan, IReadOnlyList<IParityRigEngineControl> engines)
    {
        if (!Hash(plan.SourceStackHash) || engines.Count != 2 ||
            !engines.Select(engine => engine.Engine).ToHashSet(StringComparer.Ordinal).SetEquals(["retail", "opennv"]))
            throw new ArgumentException("A rig requires a source hash and exactly retail/OpenNV controls.");
        if (plan.CommandTimeout <= TimeSpan.Zero || plan.CommandTimeout > TimeSpan.FromMinutes(2) ||
            plan.RestoreTimeout <= TimeSpan.Zero || plan.RestoreTimeout > TimeSpan.FromMinutes(2) ||
            plan.Width != 3840 || plan.Height != 2160 || plan.TargetFramesPerSecond != 60)
            throw new ArgumentException("A rig requires bounded deadlines and the requested 3840x2160/60 target.");
        var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in plan.Cases)
        {
            if (string.IsNullOrWhiteSpace(item.Id) || !identities.Add(item.Id) || string.IsNullOrWhiteSpace(item.Family) ||
                string.IsNullOrWhiteSpace(item.SourceForm) || !Hash(item.BindingHash))
                throw new ArgumentException("A rig case requires unique identity and source binding provenance.");
            if (item.Blockers.Count == 0 && (string.IsNullOrWhiteSpace(item.Reference) || item.Actions.Count == 0))
                throw new ArgumentException($"Runnable case {item.Id} requires an actual reference and actions.");
            var actions = new HashSet<string>(StringComparer.Ordinal);
            foreach (var action in item.Actions)
                if (string.IsNullOrWhiteSpace(action.Id) || !actions.Add(action.Id) || string.IsNullOrWhiteSpace(action.Owner) ||
                    string.IsNullOrWhiteSpace(action.Operation) || action.ObservationDuration < TimeSpan.FromSeconds(.1) ||
                    action.ObservationDuration > TimeSpan.FromSeconds(8) || action.RequiredEvidence.Count == 0 ||
                    action.RequiredEvidence.Any(string.IsNullOrWhiteSpace))
                    throw new ArgumentException($"Rig case {item.Id} has an unbounded or unbound action.");
        }
    }

    private static bool Hash(string value) => value.Length == 64 && value.All(char.IsAsciiHexDigit);
    private static bool Recoverable(Exception exception) => exception is ArgumentException or InvalidOperationException or
        InvalidDataException or IOException or NotSupportedException or OperationCanceledException;
}
