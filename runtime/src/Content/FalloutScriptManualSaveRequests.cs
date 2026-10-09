using System.Security.Cryptography;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutScriptManualSaveSite(ulong Invocation, FalloutFormKey Caller,
    FalloutFormKey Program, string RecordSha256, string ProgramSha256, int Statement,
    FalloutScriptResultAuthority Authority = FalloutScriptResultAuthority.SourceDiagnostic, string? ScopeSha256 = null,
    FalloutScriptSaveProgram? CompiledProgram = null);

internal sealed record FalloutScriptManualSaveReceipt(ulong Generation, Guid Slot, ulong RequestedPhase,
    IReadOnlyList<FalloutScriptManualSaveSite> Sites, string Disposition, string? Error,
    string? SlotPath = null, IReadOnlyList<FalloutScriptManualSaveInvocation>? Invocations = null);

internal sealed record FalloutScriptManualSaveInvocation(ulong Invocation, bool Ended,
    string? SourceError);

// ForceSave requests an ordinary new slot. The source invocation continues;
// capture belongs to a later engine phase after all entered source invocations
// have finished. A pending instruction cursor is never guessed or discarded.
internal sealed partial class FalloutScriptManualSaveRequests(FalloutPluginStack records)
{
    internal sealed class Entered(FalloutFormKey caller, FalloutFormKey program,
        string recordHash, string programHash, ulong invocation, FalloutScriptResultAuthority authority, string? scopeHash,
        Guid session, FalloutScriptSaveProgram? compiledProgram, Func<int>? sourceStatement)
    {
        internal FalloutFormKey Caller { get; } = caller;
        internal FalloutFormKey Program { get; } = program;
        internal string RecordHash { get; } = recordHash;
        internal string ProgramHash { get; } = programHash;
        internal ulong Invocation { get; } = invocation;
        internal FalloutScriptResultAuthority Authority { get; } = authority;
        internal string? ScopeHash { get; } = scopeHash;
        internal Guid Session { get; } = session;
        internal FalloutScriptSaveProgram? CompiledProgram { get; } = compiledProgram;
        internal Func<int>? SourceStatement { get; } = sourceStatement;
        internal int? ReachedInstruction { get; set; }
    }

    internal sealed class Invocation : IDisposable
    {
        private readonly FalloutScriptManualSaveRequests _owner;
        private readonly Entered _entered;
        private bool _finished;

        internal Invocation(FalloutScriptManualSaveRequests owner, Entered entered)
        { _owner = owner; _entered = entered; }

        internal void Complete() => Finish(null);
        internal void Stop(Exception failure) => Finish(failure.Message);
        internal void Suspend()
        {
            if (_finished) throw new InvalidOperationException("Source invocation already retired.");
            _finished = true; _owner.SuspendCompiled(_entered);
        }
        internal bool MoveNext(IEnumerator<bool> steps)
        {
            _entered.ReachedInstruction = null;
            _owner._executing.Push(_entered);
            try { return steps.MoveNext(); }
            finally
            {
                if (!ReferenceEquals(_owner._executing.Pop(), _entered))
                    throw new InvalidOperationException("Source execution scope changed during an instruction.");
            }
        }

        private void Finish(string? error)
        {
            if (_finished) throw new InvalidOperationException("Source invocation already retired.");
            _finished = true;
            _owner.Retire(_entered, error);
        }

        public void Dispose()
        {
            if (!_finished) Finish("Source invocation retired without a completed execution or retained failure.");
        }
    }

    internal RuntimeSaveRequestOrder Order { get; } = new();
    internal Guid ExecutionSession => Order.Epoch;
    private readonly Dictionary<ulong, (Entered Owner, string Disposition, int PrefixBefore,
        FalloutCompiledCursorSnapshot Cursor, string? Error)> _compiledSlices = [];
    internal void RequireCurrentCompiledReceipt(FalloutCompiledSliceReceipt receipt)
    {
        if (receipt.Session != ExecutionSession || !_compiledSlices.TryGetValue(receipt.Invocation, out var actual) ||
            actual.Owner.Caller != receipt.Caller || actual.Owner.Program != receipt.Program ||
            actual.Owner.RecordHash != receipt.RecordSha256 || actual.Owner.ProgramHash != receipt.ProgramSha256 ||
            actual.Owner.ScopeHash != receipt.EventScopeSha256 || actual.Disposition != receipt.Disposition ||
            actual.PrefixBefore != receipt.PrefixBefore || actual.Error != receipt.Error ||
            actual.Cursor.NextOffset != receipt.Cursor.NextOffset || actual.Cursor.CommittedInstructions != receipt.Cursor.CommittedInstructions ||
            actual.Cursor.BudgetSpent != receipt.Cursor.BudgetSpent || actual.Cursor.Completed != receipt.Cursor.Completed ||
            !actual.Cursor.Branches.SequenceEqual(receipt.Cursor.Branches))
            throw new InvalidDataException("Compiled scheduler receipt has no matching actual retired shared lease/prefix.");
        if (receipt.Disposition == "suspended") Order.Suspend(receipt);
        _compiledSlices.Remove(receipt.Invocation);
    }
    private readonly Dictionary<(FalloutFormKey Caller, string Scope, string Program), List<Entered>> _suspendedCompiled = [];
    private readonly Stack<Entered> _executing = [];
    private readonly HashSet<ulong> _entered = [];
    private readonly Dictionary<FalloutFormKey, string> _recordHashes = [];
    private readonly HashSet<ulong> _unclosedRequests = [];
    private Func<Guid, RuntimeSaveSlotMetadata>? _writeNewSlot;
    private Func<RuntimeSaveRequest, RuntimeSaveSlotMetadata>? _writeContinue;
    private Func<ulong>? _enginePhase;
    private Action<FalloutScriptManualSaveReceipt>? _failed;
    private ulong _phase, _invocation;
    internal FalloutScriptManualSaveReceipt? Receipt => Order.Requests.LastOrDefault(row => row.Script is not null) is { } row ? Project(row) : null;
    internal bool Pending => Order.Requests.Any(row => row.Disposition == RuntimeSaveRequestDisposition.Pending && !IsPlayerRequest(row));
    internal string? Error => Order.Failure;
    internal int EnteredInvocations => _entered.Count;
    internal bool WritingRequestedSlot => Order.Writing is not null;
    internal ulong ObservedEnginePhase => CurrentPhase;
    internal string? DeferredBy { get; private set; }
    private ulong CurrentPhase => _enginePhase?.Invoke() ?? _phase;

    internal void Bind(Func<Guid, RuntimeSaveSlotMetadata> writeNewSlot,
        Action<FalloutScriptManualSaveReceipt> failed, Func<ulong>? enginePhase = null,
        RuntimeSaveRequestBinding? binding = null,
        Func<RuntimeSaveRequest, RuntimeSaveSlotMetadata>? writeContinue = null)
    {
        if (_writeNewSlot is not null || binding is null)
            throw new InvalidOperationException("Save requests require one complete source/destination/phase binding.");
        _writeNewSlot = writeNewSlot ?? throw new ArgumentNullException(nameof(writeNewSlot));
        _failed = failed ?? throw new ArgumentNullException(nameof(failed));
        _writeContinue = writeContinue;
        _enginePhase = enginePhase;
        if (Order.Bound) Order.RequireBinding(binding.SourceCompatibilityId, binding.ContinuePath);
        else Order.Bind(binding.SourceCompatibilityId, binding.ContinuePath, binding.SlotPath, () => CurrentPhase);
    }

    private static bool IsPlayerRequest(RuntimeSaveRequest row) => row.Origin is RuntimeSaveRequestOrigin.PlayerInput or RuntimeSaveRequestOrigin.SessionMenu;
    private static FalloutScriptManualSaveReceipt Project(RuntimeSaveRequest row) => new(row.Order, row.Request, row.RequestedPhase,
        row.Script is null ? [] : [row.Script], row.Disposition.ToString().ToLowerInvariant(), row.Error, row.Committed?.Path,
        row.Invocation is null ? [] : [new(row.Invocation.Invocation,
            row.Invocation.Disposition is RuntimeSaveInvocationDisposition.Completed or RuntimeSaveInvocationDisposition.Stopped or RuntimeSaveInvocationDisposition.Abandoned,
            row.Invocation.Error)]);

    private Invocation Enter(FalloutFormKey caller, FalloutPluginRecord program, string executingHash,
        FalloutScriptResultAuthority authority, string? scopeHash, Action<Entered>? observeInvocation,
        FalloutScriptSaveProgram? compiledProgram = null, Func<int>? sourceStatement = null)
    {
        if (executingHash is null || executingHash.Length != 64 || executingHash.Any(value => !Uri.IsHexDigit(value)))
            throw new InvalidDataException("Script invocation has no decoded program identity.");
        if (records.RuntimeFormId(caller) != 0x14 &&
            records.GetEffective(caller).Signature is not ("REFR" or "ACHR" or "ACRE" or "QUST") &&
            !(caller == program.FormKey && program.Signature == "SCPT"))
            throw new InvalidDataException("Script save invocation caller has no source instance.");
        var winning = records.GetEffective(program.FormKey);
        if (winning.Signature is not ("SCPT" or "QUST" or "INFO" or "PACK" or "TERM") ||
            winning.Plugin != program.Plugin || winning.HeaderOffset != program.HeaderOffset)
            throw new InvalidDataException("Script save invocation differs from its winning program owner.");
        if (!_recordHashes.TryGetValue(program.FormKey, out var recordHash))
            _recordHashes.Add(program.FormKey, recordHash =
                Convert.ToHexString(SHA256.HashData(program.ReadData())).ToLowerInvariant());
        var entered = new Entered(caller, program.FormKey, recordHash,
            executingHash, checked(++_invocation), authority, scopeHash, ExecutionSession, compiledProgram, sourceStatement);
        _entered.Add(entered.Invocation);
        var lease = new Invocation(this, entered);
        try { observeInvocation?.Invoke(entered); return lease; }
        catch { lease.Dispose(); throw; }
    }

    internal IEnumerable<bool> Execute(FalloutFormKey caller, FalloutPluginRecord source,
        FalloutGameModeProgram program, IEnumerable<bool> steps, Action<Entered>? observeInvocation = null, string? scopeHash = null) =>
        ExecuteOwned(caller, source, program.ProgramSha256, steps, FalloutScriptResultAuthority.SourceDiagnostic, scopeHash, observeInvocation,
            sourceStatement: () => program.LastStatement);

    internal IEnumerable<bool> ExecuteCompiled(FalloutFormKey caller, FalloutPluginRecord source,
        string programSha256, IEnumerable<bool> steps, string scopeHash, Action<Entered>? observeInvocation = null,
        FalloutScriptSaveProgram? compiledProgram = null) =>
        ExecuteOwned(caller, source, programSha256, steps, FalloutScriptResultAuthority.CompiledVanilla, scopeHash, observeInvocation, compiledProgram);

    private IEnumerable<bool> ExecuteOwned(FalloutFormKey caller, FalloutPluginRecord source,
        string programSha256, IEnumerable<bool> steps, FalloutScriptResultAuthority authority,
        string? scopeHash, Action<Entered>? observeInvocation, FalloutScriptSaveProgram? compiledProgram = null, Func<int>? sourceStatement = null)
    {
        using var invocation = Enter(caller, source, programSha256, authority, scopeHash, observeInvocation, compiledProgram, sourceStatement);
        using var enumerator = steps.GetEnumerator();
        while (true)
        {
            bool advanced;
            try { advanced = invocation.MoveNext(enumerator); }
            catch (Exception error)
            {
                invocation.Stop(error);
                throw;
            }
            if (!advanced) { invocation.Complete(); yield break; }
            yield return enumerator.Current;
        }
    }

    internal IEnumerable<bool> ExecuteCompiledSlice(FalloutFormKey caller, FalloutPluginRecord source,
        string programHash, IEnumerable<bool> steps, string scope, FalloutCompiledExecutionCursor cursor,
        Func<bool> canContinue, Action<Entered>? observeInvocation, FalloutScriptSaveProgram? compiledProgram = null)
    {
        RequireSuspendedPrefix(caller, scope, programHash, cursor.State);
        Entered? entered = null;
        var prefixBefore = cursor.State.CommittedInstructions;
        using var invocation = Enter(caller, source, programHash, FalloutScriptResultAuthority.CompiledVanilla, scope,
            actual => { entered = actual; observeInvocation?.Invoke(actual); }, compiledProgram);
        using var enumerator = steps.GetEnumerator();
        while (true)
        {
            bool advanced = false, suspended;
            try
            {
                suspended = !cursor.State.Completed && !canContinue();
                if (!suspended) advanced = invocation.MoveNext(enumerator);
            }
            catch (Exception error)
            {
                RetireSuspended(caller, scope, programHash, error.Message, entered!);
                invocation.Stop(error);
                _compiledSlices.Add(entered!.Invocation, (entered!, "closed-failure", prefixBefore,
                    cursor.State with { Branches = cursor.State.Branches.ToArray() }, error.Message)); throw;
            }
            if (suspended)
            {
                invocation.Suspend();
                _compiledSlices.Add(entered!.Invocation, (entered!, "suspended", prefixBefore,
                    cursor.State with { Branches = cursor.State.Branches.ToArray() }, null)); yield break;
            }
            if (!advanced)
            {
                RetireSuspended(caller, scope, programHash, null, entered!);
                invocation.Complete();
                _compiledSlices.Add(entered!.Invocation, (entered!, "completed", prefixBefore,
                    cursor.State with { Branches = cursor.State.Branches.ToArray() }, null)); yield break;
            }
            yield return enumerator.Current;
        }
    }

    private void SuspendCompiled(Entered entered)
    {
        if (entered.Authority != FalloutScriptResultAuthority.CompiledVanilla || entered.ScopeHash is null || !_entered.Remove(entered.Invocation))
            throw new InvalidOperationException("Compiled suspension has no matching actual lease.");
        if (!_unclosedRequests.Contains(entered.Invocation)) return;
        var key = (entered.Caller, entered.ScopeHash!, entered.ProgramHash);
        if (!_suspendedCompiled.TryGetValue(key, out var values)) _suspendedCompiled.Add(key, values = []);
        values.Add(entered);
        // The actual scheduler receipt binds the retained suffix after this
        // lease retires; RequireCurrentCompiledReceipt performs that join.
    }

    private void RetireSuspended(FalloutFormKey caller, string scope, string program, string? error, Entered actual)
    {
        if (!_suspendedCompiled.Remove((caller, scope, program), out var values)) return;
        foreach (var entered in values)
        {
            _unclosedRequests.Remove(entered.Invocation);
            Order.Retire(entered.Session, entered.Invocation, error is null ? RuntimeSaveInvocationDisposition.Completed : RuntimeSaveInvocationDisposition.Stopped,
                error, actual.Session, actual.Invocation);
        }
    }

    internal void ReachedCompiledInstruction(int offset)
    {
        if (_executing.Count == 0 || offset < 0 || _executing.Peek().Authority != FalloutScriptResultAuthority.CompiledVanilla)
            throw new InvalidOperationException("SCDA save site has no actual shared executing lease.");
        _executing.Peek().ReachedInstruction = offset;
    }

    internal void Request(int statement) => RequestSource(RuntimeSaveRequestOrigin.ScriptForceSave, statement);
    internal void RequestAutoSave() => RequestSource(RuntimeSaveRequestOrigin.ScriptAutoSave, null);

    private void RequestSource(RuntimeSaveRequestOrigin origin, int? suppliedStatement)
    {
        RequireNoFailure();
        if (_executing.Count == 0) throw new InvalidOperationException("Source save requires an actual entered instruction.");
        var entered = _executing.Peek();
        if (entered.Authority != FalloutScriptResultAuthority.CompiledVanilla)
            throw new NotSupportedException("Persistent script requests require their actual winning SCDA instruction/scope owner; a diagnostic statement cannot substitute it.");
        var statement = entered.Authority == FalloutScriptResultAuthority.CompiledVanilla
            ? entered.ReachedInstruction : entered.SourceStatement?.Invoke();
        if (statement is null or < 0 || suppliedStatement is { } supplied && supplied != statement ||
            entered.Authority == FalloutScriptResultAuthority.CompiledVanilla && entered.CompiledProgram is null)
            throw new InvalidOperationException("Source save has no matching actual instruction/scope cursor.");
        var site = new FalloutScriptManualSaveSite(entered.Invocation, entered.Caller, entered.Program,
            entered.RecordHash, entered.ProgramHash, statement.Value, entered.Authority, entered.ScopeHash, entered.CompiledProgram);
        if (site.CompiledProgram is { } compiled) compiled.RequireSave(records, site, origin);
        Order.Enqueue(origin, site, new(entered.Session, entered.Invocation, RuntimeSaveInvocationDisposition.Entered), null);
        _unclosedRequests.Add(entered.Invocation);
        DeferredBy = "source-script-execution";
    }

    internal RuntimeSaveRequest RequestNative(RuntimeSaveRequestOrigin origin, RuntimeSaveNativeSite site)
    {
        RuntimeSaveRequestOrder.ValidateNative(records, origin, site);
        if (origin is RuntimeSaveRequestOrigin.ScriptAutoSave or RuntimeSaveRequestOrigin.ScriptForceSave)
            throw new InvalidDataException("Native save cannot manufacture a script origin.");
        return Order.Enqueue(origin, null, null, site);
    }

    private void Retire(Entered entered, string? error)
    {
        if (!_entered.Remove(entered.Invocation)) throw new InvalidOperationException("Source execution retirement differs from its entered invocation.");
        _unclosedRequests.Remove(entered.Invocation);
        var abandoned = error == "Source invocation retired without a completed execution or retained failure.";
        Order.Retire(entered.Session, entered.Invocation, abandoned ? RuntimeSaveInvocationDisposition.Abandoned :
            error is null ? RuntimeSaveInvocationDisposition.Completed : RuntimeSaveInvocationDisposition.Stopped, error);
    }

    internal void AdvancePhase()
    {
        if (WritingRequestedSlot) throw new InvalidOperationException("Cannot advance an active persistent writer phase.");
        _phase = checked(_phase + 1);
    }

    internal bool Drain(Func<string?> normalSaveBlocker)
    {
        ArgumentNullException.ThrowIfNull(normalSaveBlocker);
        if (Order.Head is not { Disposition: RuntimeSaveRequestDisposition.Pending } row || IsPlayerRequest(row)) return false;
        if (CurrentPhase <= (row.HandoffPhase ?? row.RequestedPhase) || _entered.Count != 0 || !Order.InvocationRetired(row))
        { DeferredBy = "source-script-execution"; return false; }
        string? blocker;
        try { blocker = normalSaveBlocker(); }
        catch (Exception error) when (RetainableFailure(error)) { Fail(row.Order, error.Message); return false; }
        if (blocker is not null) { DeferredBy = blocker; return false; }
        try
        {
            Order.Write(row.Order, writing => writing.Destination == RuntimeSaveRequestDestination.NewSlot
                ? (_writeNewSlot ?? throw new NotSupportedException("ForceSave has no actual new-slot writer."))(writing.Request)
                : (_writeContinue ?? throw new NotSupportedException("Auto/native save has no actual Continue writer."))(writing));
            DeferredBy = null; return true;
        }
        catch (Exception error) when (RetainableFailure(error))
        {
            DeferredBy = null;
            _failed?.Invoke(Project(Order.Find(row.Order)));
            return false;
        }
    }

    private static bool RetainableFailure(Exception error) => error is IOException or UnauthorizedAccessException or
        InvalidDataException or InvalidOperationException or NotSupportedException or
        KeyNotFoundException or OverflowException or System.Text.Json.JsonException;

    private void Fail(ulong order, string error)
    {
        Order.Fail(order, error); DeferredBy = null; _failed?.Invoke(Project(Order.Find(order)));
    }

    internal void RequireNoFailure()
    {
        if (Error is { } error) throw new NotSupportedException("Persistent save request failed: " + error);
    }

    private readonly Dictionary<string, string> _compiledResultFailures = [];
    internal void RetainCompiledResultFailure(FalloutScriptScope scope, FalloutFormKey caller, int committedSteps, Exception error) =>
        _compiledResultFailures.TryAdd(caller + ":" + scope.ScopeSha256,
            $"Compiled result {scope.Source.FormKey}/{scope.Kind} committed {committedSteps} steps then refused: {error.Message}");
    internal IReadOnlyList<string> CompiledResultFailures => _compiledResultFailures.Values.ToArray();
    internal string? CompiledResultCaptureBlocker => _compiledResultFailures.Count == 0 ? null :
        "Saving a failed compiled result requires its independently owned committed-prefix continuation. " +
        string.Join(" | ", CompiledResultFailures);

    internal void RequireCapture()
    {
        RequireNoFailure();
        if (CompiledResultCaptureBlocker is { } resultFailure) throw new NotSupportedException(resultFailure);
        if (_entered.Count != 0)
            throw new NotSupportedException("Saving source script execution requires its genuinely retired instruction slice.");
        if (Order.Bound) _ = Order.Capture();
    }
}
