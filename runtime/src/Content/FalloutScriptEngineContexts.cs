namespace OpenNV.Runtime.Content;

internal enum FalloutScriptEngineCallKind { Compiled, ImmediateObject }
internal sealed record FalloutScriptEngineInvocation(Guid Session, ulong Invocation, string Scope);
internal sealed record FalloutScriptEngineCall(long Ordinal, long ThroughOrdinal, long Context, bool Cached,
    FalloutScriptEngineCallKind Kind, FalloutFormKey Target, FalloutFormKey Program,
    string RecordSha256, string ProgramSha256, float Seconds, FalloutFormKey? Action,
    IReadOnlyList<FalloutScriptEngineInvocation> Invocations, IReadOnlyList<FalloutScriptEngineCall> Children,
    string Disposition, string? FailureType, string? Error);
internal sealed record FalloutScriptEngineContextsSnapshot(string Source, long Calls, long Contexts,
    long? CachedContext, FalloutScriptEngineCall? LastCall);

// The shared source interpreter owns a cached outer object and fresh nested
// objects, restoring the actual parent's active pointer on return. These are
// C# lifetime identities; they are never exported as original ABI pointers.
internal sealed class FalloutScriptEngineContexts : IDisposable
{
    private readonly FalloutImmediateScriptSource _source;
    private readonly Stack<Call> _active = [];
    private long _calls, _contexts;
    private long? _cached;
    private Call? _last;
    private FalloutScriptEngineCall? _restoredLast;
    private bool _disposed;
    internal string? SaveBlocker => _active.Count == 0 ? null : "actual-source-script-interpreter-call-in-flight";
    internal object State => new { source = _source, calls = _calls, contexts = _contexts,
        cachedContext = _cached, active = _active.Select(call => call.Capture()).ToArray(),
        last = _last?.Capture() ?? _restoredLast, retired = _disposed };
    internal FalloutScriptEngineContexts(FalloutImmediateScriptSource source, FalloutScriptEngineContextsSnapshot? restore = null)
    {
        source.Validate(); _source = source;
        if (restore is null) return;
        Validate(restore);
        if (restore.Source != source.Identity) throw new InvalidDataException("Cold interpreter context changed selected source.");
        _calls = restore.Calls; _contexts = restore.Contexts; _cached = restore.CachedContext; _restoredLast = restore.LastCall;
        // The next cached C# object is reconstructed without any script call,
        // event filter, native instance or successful result being replayed.
    }
    internal Lease Enter(FalloutFormKey target, FalloutCompiledScriptProgram program, double seconds,
        FalloutFormKey? action, IFalloutCompiledEventLocalAuthority? locals, bool immediate)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!double.IsFinite(seconds) || seconds < 0 || !float.IsFinite((float)seconds))
            throw new NotSupportedException("Original script run has no finite Float32 time scalar.");
        if (immediate && (seconds != 0 || action is not null || locals is null || program.ScriptType != 0))
            throw new InvalidDataException("Immediate object script lost its real player/event-list/time-zero call.");
        if (!immediate && _active.TryPeek(out var parent) && parent.Immediate && parent.Target == target &&
            ReferenceEquals(parent.Program, program) && ReferenceEquals(parent.Locals, locals) && seconds == 0 && action is null)
            return new(this, parent, borrowed: true);
        var cached = _active.Count == 0;
        var context = cached ? (_cached ??= checked(++_contexts)) : checked(++_contexts);
        var call = new Call(checked(++_calls), context, cached, immediate, target, program, (float)seconds, action, locals);
        if (_active.TryPeek(out var outer)) outer.Children.Add(call); else _last = call;
        _active.Push(call); return new(this, call, borrowed: false);
    }
    internal void RequireImmediate(FalloutFormKey target, FalloutCompiledScriptProgram program,
        IFalloutCompiledEventLocalAuthority authority)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_active.TryPeek(out var call) || !call.Immediate || call.Target != target ||
            !ReferenceEquals(call.Program, program) || !ReferenceEquals(call.Locals, authority) || call.Seconds != 0 || call.Action is not null)
            throw new InvalidOperationException("Immediate event predicate has no actual current shared interpreter owner.");
    }
    internal sealed class Call(long ordinal, long context, bool cached, bool immediate, FalloutFormKey target,
        FalloutCompiledScriptProgram program, float seconds, FalloutFormKey? action, IFalloutCompiledEventLocalAuthority? locals)
    {
        internal long Ordinal { get; } = ordinal;
        internal long Context { get; } = context;
        internal bool Immediate { get; } = immediate;
        internal FalloutFormKey Target { get; } = target;
        internal FalloutCompiledScriptProgram Program { get; } = program;
        internal float Seconds { get; } = seconds;
        internal FalloutFormKey? Action { get; } = action;
        internal IFalloutCompiledEventLocalAuthority? Locals { get; } = locals;
        internal readonly List<Call> Children = [];
        internal readonly List<FalloutScriptEngineInvocation> Invocations = [];
        internal long ThroughOrdinal;
        internal string Disposition = "entered";
        internal string? FailureType, Error;
        internal FalloutScriptEngineCall Capture() => new(Ordinal, ThroughOrdinal == 0 ? Ordinal : ThroughOrdinal,
            Context, cached, Immediate ? FalloutScriptEngineCallKind.ImmediateObject : FalloutScriptEngineCallKind.Compiled,
            Target, Program.Source.FormKey, Program.Scope.RecordSha256, Program.ProgramSha256, Seconds, Action,
            Invocations.ToArray(), Children.Select(child => child.Capture()).ToArray(), Disposition, FailureType, Error);
    }
    internal sealed class Lease : IDisposable
    {
        private readonly FalloutScriptEngineContexts _owner;
        private readonly Call _call;
        private readonly bool _borrowed;
        private bool _retired, _returned;
        private Exception? _failure;
        internal Lease(FalloutScriptEngineContexts owner, Call call, bool borrowed)
        { _owner = owner; _call = call; _borrowed = borrowed; }
        internal void Observe(FalloutScriptManualSaveRequests.Entered actual)
        {
            RequireCurrent();
            if (actual.Session == Guid.Empty || actual.Invocation == 0 || actual.Caller != _call.Target ||
                actual.Program != _call.Program.Source.FormKey || actual.RecordHash != _call.Program.Scope.RecordSha256 ||
                actual.ProgramHash != _call.Program.ProgramSha256 || actual.ScopeHash is not { } scope)
                throw new InvalidDataException("Source interpreter observed another shared compiled invocation.");
            var row = new FalloutScriptEngineInvocation(actual.Session, actual.Invocation, scope);
            if (_call.Invocations.Contains(row)) throw new InvalidOperationException("Source interpreter repeated a shared entered lease.");
            _call.Invocations.Add(row);
        }
        internal void Returned() { RequireCurrent(); _returned = true; }
        internal void Fail(Exception error) { RequireCurrent(); _failure ??= error; }
        private void RequireCurrent()
        {
            if (_retired || !_owner._active.TryPeek(out var current) || !ReferenceEquals(current, _call))
                throw new InvalidOperationException("Source interpreter context is not the current actual nested caller.");
        }
        public void Dispose()
        {
            if (_retired) return;
            RequireCurrent(); _retired = true;
            if (_borrowed) return;
            if (_failure is null && !_returned)
                _failure = new InvalidOperationException("Source interpreter retired without the actual caller return.");
            _call.Disposition = _failure is null ? "returned" : "closed-failure";
            _call.FailureType = _failure is null ? null : _failure.GetType().FullName ?? _failure.GetType().Name;
            _call.Error = _failure?.Message; _call.ThroughOrdinal = _owner._calls;
            if (!ReferenceEquals(_owner._active.Pop(), _call))
                throw new InvalidOperationException("Source interpreter failed to restore its actual parent context.");
        }
    }
    internal FalloutScriptEngineContextsSnapshot Capture()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (SaveBlocker is { } blocker) throw new NotSupportedException(blocker);
        var saved = new FalloutScriptEngineContextsSnapshot(_source.Identity, _calls, _contexts, _cached,
            _last?.Capture() ?? _restoredLast);
        Validate(saved); return saved;
    }
    internal void RequireSources(FalloutPluginStack records)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if ((_last?.Capture() ?? _restoredLast) is { } root) Visit(root);
        void Visit(FalloutScriptEngineCall call)
        {
            var record = records.GetEffective(call.Program);
            var sha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(record.ReadData())).ToLowerInvariant();
            if (record.Signature is not ("SCPT" or "QUST" or "INFO" or "PACK" or "TERM") || sha != call.RecordSha256)
                throw new InvalidDataException("Saved interpreter call lost its unchanged original winning program bytes.");
            foreach (var child in call.Children) Visit(child);
        }
    }
    internal static void Validate(FalloutScriptEngineContextsSnapshot state)
    {
        if (state is null || !FalloutAdvancementRuntimeReceipt.Digest(state.Source) || state.Calls < 0 || state.Contexts < 0 ||
            (state.Calls == 0) != (state.Contexts == 0) || state.Contexts > state.Calls ||
            (state.Calls == 0) != (state.LastCall is null) || (state.Contexts == 0) != (state.CachedContext is null) ||
            state.CachedContext is not (null or 1))
            throw new InvalidDataException("Saved interpreter context constructor/lifetime is incomplete.");
        if (state.LastCall is not { } last) return;
        var next = last.Ordinal;
        long? nestedContext = null;
        Visit(last, null);
        if (next != checked(state.Calls + 1) || nestedContext is { } through && through != state.Contexts)
            throw new InvalidDataException("Interpreter call tree lost its nested committed suffix.");
        void Visit(FalloutScriptEngineCall call, FalloutScriptEngineCall? parent)
        {
            if (call.Ordinal != next++ || call.ThroughOrdinal < call.Ordinal || call.ThroughOrdinal > state.Calls ||
                call.Context <= 0 || call.Context > state.Contexts || call.Cached != (parent is null) ||
                call.Cached && call.Context != state.CachedContext || parent is not null && call.Context <= parent.Context ||
                !Enum.IsDefined(call.Kind) || !float.IsFinite(call.Seconds) || call.Seconds < 0 ||
                !FalloutAdvancementRuntimeReceipt.Digest(call.RecordSha256) || !FalloutAdvancementRuntimeReceipt.Digest(call.ProgramSha256) ||
                call.Target.ObjectId == 0 || call.Program.ObjectId == 0 || call.Invocations is null || call.Children is null ||
                call.Disposition is not ("returned" or "closed-failure") ||
                (call.Error is null) != (call.FailureType is null) || (call.Error is null) != (call.Disposition == "returned") ||
                call.Kind == FalloutScriptEngineCallKind.ImmediateObject && (call.Seconds != 0 || call.Action is not null) ||
                call.Invocations.Any(row => row.Session == Guid.Empty || row.Invocation == 0 || !FalloutAdvancementRuntimeReceipt.Digest(row.Scope)) ||
                call.Invocations.Distinct().Count() != call.Invocations.Count)
                throw new InvalidDataException("Saved interpreter call invented a context, time, execution lease or successful return.");
            if (parent is not null)
            {
                if (nestedContext is { } previous && call.Context != checked(previous + 1))
                    throw new InvalidDataException("Nested source interpreter reused or skipped an allocated context lifetime.");
                nestedContext = call.Context;
            }
            foreach (var child in call.Children) Visit(child, call);
            if (call.ThroughOrdinal != next - 1) throw new InvalidDataException("Interpreter child escaped its actual parent lifetime.");
        }
    }
    public void Dispose()
    {
        if (_disposed) return;
        if (_active.Count != 0) throw new InvalidOperationException("Shared interpreter cannot retire an active actual source call.");
        _disposed = true;
    }
}
