namespace OpenNV.AnimationCompare;

// A controlled rig is diagnostic preparation, never ordinary campaign evidence.
// Adapters invoke actual engine owners. This protocol grants no mesh/pose proxy lane.
internal sealed record ParityRigCase(string Id, string Family, string SourceForm, string BindingHash,
    string? EntryReference, string? Reference, IReadOnlyList<ParityRigAction> Actions,
    IReadOnlyList<string> Blockers, string? TargetAnchorReference = null, bool? Restrained = null);

internal sealed record ParityRigAction(string Id, string Owner, string Operation, string Argument,
    TimeSpan ObservationDuration, IReadOnlyList<string> RequiredEvidence);

internal sealed record ParityRigPlan(string SourceStackHash, IReadOnlyList<ParityRigCase> Cases,
    TimeSpan CommandTimeout, TimeSpan RestoreTimeout, int Width = 3840, int Height = 2160,
    int TargetFramesPerSecond = 60, bool PreserveInitialState = true);

internal sealed record ParityRigRequest(Guid Run, Guid Request, string Operation, string SourceStackHash,
    string? Checkpoint = null, ParityRigCase? Case = null, ParityRigAction? Action = null,
    int Width = 3840, int Height = 2160, int TargetFramesPerSecond = 60);

internal sealed record ParityRigEvidence(string Lane, string Path, string Schema, string Sha256);

// Completed means the requested engine owner has finished, not that a transport
// accepted its command. Evidence lanes remain independent of that completion.
internal sealed record ParityRigReceipt(Guid Run, Guid Request, string Engine, string Operation,
    string SourceStackHash, string Disposition, string? Error,
    IReadOnlyList<ParityRigEvidence> Evidence);

internal sealed class ParityRigControlFailure(ParityRigReceipt receipt) : IOException(
    $"{receipt.Engine}/{receipt.Operation}/{receipt.Request:N}: {receipt.Disposition}: {receipt.Error}")
{
    internal ParityRigReceipt Receipt { get; } = receipt;
}

internal interface IParityRigEngineControl
{
    string Engine { get; }

    // Implementations must use fresh correlated receipts, poll asynchronous
    // engine owners to terminal state, and fail unsupported actions explicitly.
    Task<ParityRigReceipt> ExecuteAsync(ParityRigRequest request, CancellationToken cancellationToken);

    // Cancel/release every input lease and pending rig action, then prove no
    // delayed command can execute. Restoration must not race a timed-out action.
    Task<ParityRigReceipt> QuiesceAsync(ParityRigRequest request, CancellationToken cancellationToken);
}

internal sealed record ParityRigCaseResult(string CaseId, string Disposition,
    IReadOnlyList<ParityRigReceipt> Receipts, IReadOnlyList<string> Errors);

internal sealed record ParityRigRunResult(Guid Run, string EvidenceKind, string SourceStackHash,
    IReadOnlyList<ParityRigCaseResult> Cases, IReadOnlyList<ParityRigReceipt> Checkpoints,
    IReadOnlyList<ParityRigReceipt> Restorations, IReadOnlyList<string> Errors, string WorldPolicy);
