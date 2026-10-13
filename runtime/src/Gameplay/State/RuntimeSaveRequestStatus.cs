using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

// A live read view of the actual queue. Compiled programs and gameplay
// snapshots remain in detailed diagnostics and persistence, not this status.
internal sealed record RuntimeSaveRequestStatus(ulong Order, Guid Request, Guid Epoch,
    ulong RequestedPhase, RuntimeSaveRequestOrigin Origin, RuntimeSaveRequestDestination Destination,
    RuntimeSaveRequestDisposition Disposition, string? Error, string? CommittedSha256,
    RuntimeSaveInvocationDisposition? InvocationDisposition, string? InvocationError,
    FalloutScriptSaveSiteStatus? Script, RuntimeSaveNativeSite? Native)
{
    internal static RuntimeSaveRequestStatus Read(RuntimeSaveRequest request) => new(request.Order,
        request.Request, request.Epoch, request.RequestedPhase, request.Origin, request.Destination,
        request.Disposition, request.Error, request.CommittedSha256,
        request.Invocation?.Disposition, request.Invocation?.Error,
        request.Script is null ? null : FalloutScriptSaveSiteStatus.Read(request.Script), request.Native);
}

internal sealed record FalloutScriptSaveSiteStatus(ulong Invocation, FalloutFormKey Caller,
    FalloutFormKey Program, string RecordSha256, string ProgramSha256, int Statement,
    FalloutScriptResultAuthority Authority, string? ScopeSha256)
{
    internal static FalloutScriptSaveSiteStatus Read(FalloutScriptManualSaveSite site) => new(site.Invocation,
        site.Caller, site.Program, site.RecordSha256, site.ProgramSha256, site.Statement, site.Authority, site.ScopeSha256);
}

internal sealed record FalloutScriptManualSaveStatus(ulong Generation, Guid Slot, ulong RequestedPhase,
    IReadOnlyList<FalloutScriptSaveSiteStatus> Sites, string Disposition, string? Error, string? SlotPath,
    IReadOnlyList<FalloutScriptManualSaveInvocation>? Invocations)
{
    internal static FalloutScriptManualSaveStatus? Read(FalloutScriptManualSaveReceipt? receipt) =>
        receipt is null ? null : new(receipt.Generation, receipt.Slot, receipt.RequestedPhase,
            receipt.Sites.Select(FalloutScriptSaveSiteStatus.Read).ToArray(), receipt.Disposition, receipt.Error, receipt.SlotPath,
            receipt.Invocations?.ToArray());
}

internal sealed record RuntimeSaveRequestStatusView(IReadOnlyList<RuntimeSaveRequestStatus> Ordered,
    FalloutScriptManualSaveStatus? SourceManual, string? SourceManualDeferredBy,
    int EnteredInvocations, bool WritingRequestedSlot, string? Error, string? DeferredBy,
    IReadOnlyList<string> AllSaveBlockers, string ActiveContinuationSaving,
    object? SourceMainScriptCaller = null, object? SourceMainUtilities = null);
