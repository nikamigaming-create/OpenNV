using System.Security.Cryptography;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal enum RuntimeSaveRequestOrigin { ScriptAutoSave, ScriptForceSave, PlayerInput, SessionMenu, NativeInteraction, NativeDoorTransport, NativePipBoyClose, NativeRestStart }
internal enum RuntimeSaveRequestDestination { Continue, NewSlot }
internal enum RuntimeSaveRequestDisposition { Pending, Writing, Completed, Failed, Cancelled }
internal enum RuntimeSaveInvocationDisposition { Entered, Suspended, Completed, Stopped, Abandoned }

internal sealed record RuntimeSaveNativeSite(Guid Session, ulong Generation, FalloutFormKey Player,
    FalloutFormKey Cell, FalloutFormKey? Reference = null, string? ReferenceSha256 = null,
    FalloutFormKey? PreviousCell = null, FalloutFormKey? ArrivalDoor = null, string? ArrivalDoorSha256 = null,
    FalloutRestAutoSaveSite? Rest = null);

internal sealed record RuntimeSaveInvocationRetirement(Guid Session, ulong Invocation,
    RuntimeSaveInvocationDisposition Disposition, string? Error = null,
    FalloutCompiledSliceReceipt? SuspendedSlice = null, Guid? RetiredSession = null, ulong? RetiredInvocation = null);

internal sealed record RuntimeSaveRequest(ulong Order, Guid Request, Guid Epoch, ulong RequestedPhase,
    RuntimeSaveRequestOrigin Origin, RuntimeSaveRequestDestination Destination, string DestinationPath,
    FalloutScriptManualSaveSite? Script, RuntimeSaveNativeSite? Native,
    RuntimeSaveInvocationRetirement? Invocation, RuntimeSaveRequestDisposition Disposition = RuntimeSaveRequestDisposition.Pending,
    string? Error = null, RuntimeSaveSlotMetadata? Committed = null, string? CommittedSha256 = null,
    Guid? HandoffEpoch = null, ulong? HandoffPhase = null);

internal sealed record RuntimeSaveRequestEpochHandoff(Guid PreviousEpoch, Guid CurrentEpoch,
    ulong LoadedPhase, ulong LastOrder, string LoadedPath, string LoadedSha256,
    RuntimeSaveProcessIdentity PreviousProcess, RuntimeSaveProcessIdentity CurrentProcess, RuntimeSaveRequestHandoffKind Kind);

internal sealed record RuntimeSaveRequestOrderSnapshot(string Schema, string SourceCompatibilityId,
    Guid Epoch, RuntimeSaveProcessIdentity Process, ulong LastOrder, ulong? CapturedOrder, IReadOnlyList<RuntimeSaveRequest> Requests,
    IReadOnlyList<RuntimeSaveRequestEpochHandoff> Handoffs);

// All persistent writers share this owner. A request's source/native origin is
// immutable; the complete capture is published only by the current head writer.
internal sealed partial class RuntimeSaveRequestOrder
{
    internal const string Schema = "opennv-save-request-order/v1";
    private readonly List<RuntimeSaveRequest> _requests = [];
    private readonly List<RuntimeSaveRequestEpochHandoff> _handoffs = [];
    private Func<ulong>? _phase;
    private string? _source, _continuePath;
    private Func<Guid, string>? _slotPath;
    private ulong _lastOrder, _lastPhase;
    private bool _restored;
    internal Guid Epoch { get; } = Guid.NewGuid();
    internal bool Bound => _source is not null;
    internal string SourceCompatibilityId => _source ?? throw new InvalidOperationException("Save queue has no selected source identity.");
    internal IReadOnlyList<RuntimeSaveRequest> Requests => _requests.AsReadOnly();
    internal RuntimeSaveRequest? Head => _requests.FirstOrDefault(row => row.Disposition is RuntimeSaveRequestDisposition.Pending or RuntimeSaveRequestDisposition.Writing or RuntimeSaveRequestDisposition.Failed);
    internal RuntimeSaveRequest? Writing => _requests.SingleOrDefault(row => row.Disposition == RuntimeSaveRequestDisposition.Writing);
    internal string? Failure => Head is { Disposition: RuntimeSaveRequestDisposition.Failed } failed ? failed.Error : null;
    internal ulong ObservedPhase => ObservePhase();

    internal void Bind(string source, string continuePath, Func<Guid, string> slotPath, Func<ulong> phase)
    {
        if (_source is not null || string.IsNullOrWhiteSpace(source))
            throw new InvalidOperationException("Save ordering requires one immutable source/writer binding.");
        ArgumentNullException.ThrowIfNull(slotPath); ArgumentNullException.ThrowIfNull(phase);
        var path = Path.GetFullPath(continuePath); var observed = phase();
        _source = source; _continuePath = path; _slotPath = slotPath; _phase = phase; _lastPhase = observed;
    }

    internal void RequireBinding(string source, string continuePath)
    {
        if (_source != source || !Path.GetFullPath(continuePath).Equals(_continuePath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Source save selection/destination changed after execution began.");
    }

    private ulong ObservePhase()
    {
        var phase = (_phase ?? throw new InvalidOperationException("Save ordering has no engine phase owner."))();
        if (phase < _lastPhase) throw new InvalidDataException("Save request engine phase regressed within its process epoch.");
        return _lastPhase = phase;
    }

    internal RuntimeSaveRequest Find(ulong order) => _requests.Single(row => row.Order == order);
    internal bool Before(ulong order) => Head is { } head && head.Order < order;
    internal RuntimeSaveRequest Enqueue(RuntimeSaveRequestOrigin origin, FalloutScriptManualSaveSite? script,
        RuntimeSaveInvocationRetirement? invocation, RuntimeSaveNativeSite? native)
    {
        if (!Enum.IsDefined(origin) || _source is null || Writing is not null)
            throw new InvalidOperationException("Save request cannot enter an unbound or writing queue.");
        var isScript = origin is RuntimeSaveRequestOrigin.ScriptAutoSave or RuntimeSaveRequestOrigin.ScriptForceSave;
        if (isScript != (script is not null && invocation is not null) || isScript == (native is not null) ||
            !isScript && (script is not null || invocation is not null) ||
            invocation is not null && (invocation.Session != Epoch || invocation.Invocation != script!.Invocation || invocation.Disposition != RuntimeSaveInvocationDisposition.Entered))
            throw new InvalidDataException("Save request lacks its exact source invocation or native input identity.");
        var id = Guid.NewGuid();
        var destination = origin is RuntimeSaveRequestOrigin.ScriptAutoSave or RuntimeSaveRequestOrigin.NativeInteraction or RuntimeSaveRequestOrigin.NativeDoorTransport or RuntimeSaveRequestOrigin.NativePipBoyClose or RuntimeSaveRequestOrigin.NativeRestStart
            ? RuntimeSaveRequestDestination.Continue : RuntimeSaveRequestDestination.NewSlot;
        var path = Path.GetFullPath(destination == RuntimeSaveRequestDestination.Continue ? _continuePath! : _slotPath!(id));
        if (destination == RuntimeSaveRequestDestination.NewSlot && File.Exists(path))
            throw new IOException("New save request destination already exists.");
        var order = checked(_lastOrder + 1); var phase = ObservePhase();
        var request = new RuntimeSaveRequest(order, id, Epoch, phase, origin, destination, path, script, native, invocation);
        _requests.Add(request); _lastOrder = order; return request;
    }

    internal void Retire(Guid session, ulong invocation, RuntimeSaveInvocationDisposition disposition, string? error,
        Guid? retiredSession = null, ulong? retiredInvocation = null)
    {
        if (disposition is not (RuntimeSaveInvocationDisposition.Completed or RuntimeSaveInvocationDisposition.Stopped or RuntimeSaveInvocationDisposition.Abandoned) ||
            (disposition != RuntimeSaveInvocationDisposition.Completed) != (error is not null) ||
            (retiredSession is null) != (retiredInvocation is null) || retiredSession == Guid.Empty || retiredInvocation == 0)
            throw new InvalidDataException("Save invocation retirement has no actual completion/failure owner.");
        foreach (var row in _requests.Where(row => row.Invocation is { } lease && lease.Session == session && lease.Invocation == invocation).ToArray())
        {
            var lease = row.Invocation!;
            if (lease.Disposition is not (RuntimeSaveInvocationDisposition.Entered or RuntimeSaveInvocationDisposition.Suspended))
                throw new InvalidOperationException("Save invocation already retired.");
            var next = row with
            {
                Invocation = lease with
                {
                    Disposition = disposition,
                    Error = error,
                    SuspendedSlice = null,
                    RetiredSession = retiredSession ?? session,
                    RetiredInvocation = retiredInvocation ?? invocation
                }
            };
            Replace(next);
            if (disposition == RuntimeSaveInvocationDisposition.Abandoned && row.Disposition == RuntimeSaveRequestDisposition.Pending)
                Fail(row.Order, error!);
        }
    }

    internal void Suspend(FalloutCompiledSliceReceipt actual)
    {
        if (actual.Session != Epoch || actual.Invocation == 0 || actual.Disposition != "suspended")
            throw new InvalidDataException("Save request suspension lacks its actual current compiled slice.");
        foreach (var row in _requests.Where(row => row.Invocation is { } lease && (lease.Session == actual.Session && lease.Invocation == actual.Invocation ||
            lease.Disposition == RuntimeSaveInvocationDisposition.Suspended && row.Script!.Caller == actual.Caller &&
            row.Script.ScopeSha256 == actual.EventScopeSha256 && row.Script.ProgramSha256 == actual.ProgramSha256)).ToArray())
        {
            var site = row.Script!;
            if (site.Caller != actual.Caller || site.Program != actual.Program || site.RecordSha256 != actual.RecordSha256 ||
                site.ProgramSha256 != actual.ProgramSha256 || site.ScopeSha256 != actual.EventScopeSha256 ||
                site.Statement >= actual.Cursor.NextOffset || actual.Cursor.CommittedInstructions == 0 || actual.Cursor.Completed)
                throw new InvalidDataException("Pending save does not precede its actual suspended instruction suffix.");
            Replace(row with
            {
                Invocation = row.Invocation! with
                {
                    Disposition = RuntimeSaveInvocationDisposition.Suspended,
                    SuspendedSlice = actual with { Cursor = actual.Cursor with { Branches = actual.Cursor.Branches.ToArray() } }
                }
            });
        }
    }

    internal bool InvocationRetired(RuntimeSaveRequest request) => request.Invocation is null ||
        request.Invocation.Disposition is RuntimeSaveInvocationDisposition.Completed or RuntimeSaveInvocationDisposition.Stopped;

    internal string? PreparationBlocker(ulong order)
    {
        if (Failure is { } failure) return "earlier-save-failed: " + failure;
        var manual = Find(order);
        if (manual.Disposition != RuntimeSaveRequestDisposition.Pending || (manual.HandoffEpoch ?? manual.Epoch) != Epoch)
            return "save-request-not-current-pending";
        return _requests.Any(row => row.Order <= order && row.Disposition == RuntimeSaveRequestDisposition.Pending && !InvocationRetired(row))
            ? "requesting-script-invocation-not-retired" : null;
    }

    internal RuntimeSaveSlotMetadata Write(ulong order, Func<RuntimeSaveRequest, RuntimeSaveSlotMetadata> writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        var row = Find(order); var phase = ObservePhase();
        if (Head?.Order != order || row.Disposition != RuntimeSaveRequestDisposition.Pending || (row.HandoffEpoch ?? row.Epoch) != Epoch ||
            !InvocationRetired(row) || phase <= (row.HandoffPhase ?? row.RequestedPhase))
            throw new NotSupportedException("Persistent writer lacks the retired head request and genuine later phase.");
        Replace(row with { Disposition = RuntimeSaveRequestDisposition.Writing });
        try
        {
            var committed = writer(Find(order));
            var expectedId = row.Destination == RuntimeSaveRequestDestination.NewSlot ? row.Request.ToString("N") : "current";
            if (committed.Id != expectedId || !Path.GetFullPath(committed.Path).Equals(row.DestinationPath, StringComparison.OrdinalIgnoreCase) || !File.Exists(committed.Path))
                throw new InvalidDataException("Save writer returned no matching committed request destination.");
            var digest = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(committed.Path))).ToLowerInvariant();
            Replace(Find(order) with { Disposition = RuntimeSaveRequestDisposition.Completed, Committed = committed, CommittedSha256 = digest });
            return committed;
        }
        catch (Exception error)
        {
            Fail(order, error.Message); throw;
        }
    }

    internal void Cancel(ulong order, string reason)
    {
        var row = Find(order);
        if (row.Origin is not (RuntimeSaveRequestOrigin.PlayerInput or RuntimeSaveRequestOrigin.SessionMenu) ||
            row.Disposition != RuntimeSaveRequestDisposition.Pending || string.IsNullOrWhiteSpace(reason))
            throw new InvalidOperationException("Cancellation must own this exact pending player request.");
        Replace(row with { Disposition = RuntimeSaveRequestDisposition.Cancelled, Error = reason });
    }

    internal void Fail(ulong order, string error)
    {
        var row = Find(order);
        if (string.IsNullOrWhiteSpace(error) || row.Disposition is not (RuntimeSaveRequestDisposition.Pending or RuntimeSaveRequestDisposition.Writing))
            throw new InvalidOperationException("Save failure has no current request or actual cause.");
        Replace(row with { Disposition = RuntimeSaveRequestDisposition.Failed, Error = error });
    }
    private void Replace(RuntimeSaveRequest request) => _requests[_requests.FindIndex(row => row.Order == request.Order)] = request;

    internal RuntimeSaveRequestOrderSnapshot Capture()
    {
        if (_source is null) throw new InvalidOperationException("Save queue cannot capture an unbound selection.");
        if (_requests.Any(row => row.Disposition == RuntimeSaveRequestDisposition.Pending && row.Invocation?.Disposition == RuntimeSaveInvocationDisposition.Entered))
            throw new NotSupportedException("Save queue capture requires actual requesting invocation retirement or an owned suspended slice.");
        return new(Schema, _source, Epoch, RuntimeSaveProcessIdentity.Current, _lastOrder, Writing?.Order, _requests.ToArray(), _handoffs.ToArray());
    }
}
