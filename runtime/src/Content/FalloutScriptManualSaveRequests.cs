using System.Security.Cryptography;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutScriptManualSaveSite(ulong Invocation, FalloutFormKey Caller,
    FalloutFormKey Program, string RecordSha256, string ProgramSha256, int Statement);

internal sealed record FalloutScriptManualSaveReceipt(ulong Generation, Guid Slot, ulong RequestedPhase,
    IReadOnlyList<FalloutScriptManualSaveSite> Sites, string Disposition, string? Error,
    string? SlotPath = null, IReadOnlyList<FalloutScriptManualSaveInvocation>? Invocations = null);

internal sealed record FalloutScriptManualSaveInvocation(ulong Invocation, bool Ended,
    string? SourceError);

// ForceSave requests an ordinary new slot. The source invocation continues;
// capture belongs to a later engine phase after all entered source invocations
// have finished. A pending instruction cursor is never guessed or discarded.
internal sealed class FalloutScriptManualSaveRequests(FalloutPluginStack records)
{
    internal sealed class Entered(FalloutFormKey caller, FalloutFormKey program,
        string recordHash, string programHash, ulong invocation)
    {
        internal FalloutFormKey Caller { get; } = caller;
        internal FalloutFormKey Program { get; } = program;
        internal string RecordHash { get; } = recordHash;
        internal string ProgramHash { get; } = programHash;
        internal ulong Invocation { get; } = invocation;
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
        internal bool MoveNext(IEnumerator<bool> steps)
        {
            _owner.RequireNoFailure();
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

    private readonly Stack<Entered> _executing = [];
    private readonly HashSet<ulong> _entered = [];
    private readonly Dictionary<FalloutFormKey, string> _recordHashes = [];
    private readonly HashSet<ulong> _unclosedRequests = [];
    private readonly List<FalloutScriptManualSaveSite> _sites = [];
    private Func<Guid, RuntimeSaveSlotMetadata>? _writeNewSlot;
    private Func<ulong>? _enginePhase;
    private Action<FalloutScriptManualSaveReceipt>? _failed;
    private ulong _phase, _invocation, _generation;
    private bool _writing;
    private FalloutScriptManualSaveReceipt? _receipt;

    internal FalloutScriptManualSaveReceipt? Receipt => _receipt;
    internal bool Pending => _receipt?.Disposition == "pending";
    internal string? Error => _receipt?.Error;
    internal int EnteredInvocations => _entered.Count;
    internal bool WritingRequestedSlot => _writing;
    internal string? DeferredBy { get; private set; }
    private ulong CurrentPhase => _enginePhase?.Invoke() ?? _phase;

    internal void Bind(Func<Guid, RuntimeSaveSlotMetadata> writeNewSlot,
        Action<FalloutScriptManualSaveReceipt> failed, Func<ulong>? enginePhase = null)
    {
        RequireNoFailure();
        if (_writeNewSlot is not null) throw new InvalidOperationException("Script manual save writer already bound.");
        _writeNewSlot = writeNewSlot ?? throw new ArgumentNullException(nameof(writeNewSlot));
        _failed = failed ?? throw new ArgumentNullException(nameof(failed));
        _enginePhase = enginePhase;
    }

    private Invocation Enter(FalloutFormKey caller, FalloutPluginRecord program, FalloutGameModeProgram executing)
    {
        RequireNoFailure();
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
            executing.ProgramSha256, checked(++_invocation));
        _entered.Add(entered.Invocation);
        return new(this, entered);
    }

    internal IEnumerable<bool> Execute(FalloutFormKey caller, FalloutPluginRecord source,
        FalloutGameModeProgram program, IEnumerable<bool> steps)
    {
        using var invocation = Enter(caller, source, program);
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

    internal void Request(int statement)
    {
        RequireNoFailure();
        if (_executing.Count == 0 || statement < 0)
            throw new InvalidOperationException("ForceSave requires an entered source instruction.");
        var entered = _executing.Peek();
        if (!Pending)
        {
            _sites.Clear();
            _unclosedRequests.Clear();
            _receipt = new(checked(++_generation), Guid.NewGuid(), CurrentPhase, [], "pending", null);
        }
        _sites.Add(new(entered.Invocation, entered.Caller, entered.Program,
            entered.RecordHash, entered.ProgramHash, statement));
        _unclosedRequests.Add(entered.Invocation);
        var invocations = (_receipt!.Invocations ?? []).ToList();
        if (!invocations.Any(item => item.Invocation == entered.Invocation))
            invocations.Add(new(entered.Invocation, false, null));
        _receipt = _receipt with { RequestedPhase = CurrentPhase, Sites = _sites.ToArray(), Invocations = invocations.ToArray() };
        DeferredBy = "source-script-execution";
        if (_writeNewSlot is null)
        {
            const string error = "ForceSave has no ordinary manual-slot writer.";
            Fail(error);
            throw new NotSupportedException(error);
        }
    }

    private void Retire(Entered entered, string? error)
    {
        if (!_entered.Remove(entered.Invocation))
            throw new InvalidOperationException("Source execution retirement does not match its entered invocation.");
        _unclosedRequests.Remove(entered.Invocation);
        if (_receipt is not null)
            _receipt = _receipt! with
            {
                Invocations = (_receipt.Invocations ?? []).Select(item => item.Invocation == entered.Invocation
                    ? item with { Ended = true, SourceError = error } : item).ToArray()
            };
        // A stopped suffix is still stopped. The ordinary complete campaign
        // capture independently validates its retained script/fault state.
        // Retirement without an actual completed/stopped frame cannot save.
        if (error == "Source invocation retired without a completed execution or retained failure." &&
            Pending && _sites.Any(site => site.Invocation == entered.Invocation))
            Fail(error);
    }

    internal void AdvancePhase()
    {
        if (_writing) throw new InvalidOperationException("Cannot advance source phase while saving.");
        _phase = checked(_phase + 1);
    }

    internal bool Drain(Func<string?> normalSaveBlocker)
    {
        ArgumentNullException.ThrowIfNull(normalSaveBlocker);
        RequireNoFailure();
        if (!Pending) return false;
        if (CurrentPhase <= _receipt!.RequestedPhase || _entered.Count != 0 || _unclosedRequests.Count != 0)
        { DeferredBy = "source-script-execution"; return false; }
        string? blocker;
        try { blocker = normalSaveBlocker(); }
        catch (Exception error) when (RetainableFailure(error))
        { Fail(error.Message); return false; }
        if (blocker is not null)
        { DeferredBy = blocker; return false; }
        _writing = true;
        try
        {
            foreach (var site in _receipt.Sites)
                if (!Convert.ToHexString(SHA256.HashData(records.GetEffective(site.Program).ReadData()))
                    .Equals(site.RecordSha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Queued manual save differs from its winning source program.");
            var slot = _writeNewSlot!(_receipt.Slot);
            if (slot.Id != _receipt.Slot.ToString("N") || !File.Exists(slot.Path))
                throw new InvalidDataException("Manual save writer returned no matching committed new slot.");
            _receipt = _receipt with { Disposition = "completed", SlotPath = Path.GetFullPath(slot.Path) };
            DeferredBy = null;
            return true;
        }
        catch (Exception error) when (RetainableFailure(error))
        {
            Fail(error.Message);
            return false;
        }
        finally { _writing = false; }
    }

    private static bool RetainableFailure(Exception error) => error is IOException or UnauthorizedAccessException or
        InvalidDataException or InvalidOperationException or NotSupportedException or
        KeyNotFoundException or OverflowException or System.Text.Json.JsonException;

    private void Fail(string error)
    {
        _receipt = _receipt! with { Disposition = "failed", Error = error };
        DeferredBy = null;
        _failed?.Invoke(_receipt);
    }

    internal void RequireNoFailure()
    {
        if (Error is { } error) throw new NotSupportedException($"Source ForceSave request failed: {error}");
    }

    internal void RequireCapture()
    {
        RequireNoFailure();
        if (_entered.Count != 0 || Pending && !_writing)
            throw new NotSupportedException("Saving source script execution requires its completed engine phase.");
    }
}
