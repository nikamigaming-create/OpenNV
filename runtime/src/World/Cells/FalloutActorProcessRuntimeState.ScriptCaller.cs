using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutActorProcessRuntimeState
{
    internal const string ScriptCallerSchema = "opennv-source-Main-script-caller/v1";
    private FalloutMainScriptCallerSource? _scriptCallerSource;
    private FalloutMainScriptCall? _scriptCallerLast;
    private FalloutMainScriptInvocation? _scriptCallerInvocation;
    private Guid _scriptCallerLease;
    private string? _scriptCallerDeliveryOwner;
    private IFalloutMainScriptCallerConsumers? _scriptCallerConsumers;
    private ulong? _scriptCallerLastDeliveredFrame;
    private int? _scriptCallerManagedThread;
    private long _scriptCallerCalls, _scriptCallerReentry, _contextTimeWrites;
    private uint _contextTimeBits;
    private FalloutMainInterfaceCachedFields? _mainInterfaceCachedFields;
    private FalloutMainContextTimeWrite? _contextTimeLast;
    private FalloutActorProcessRuntimeHandoff? _scriptCallerCold;
    internal bool MainScriptCallerConstructed => _scriptCallerSource is not null;
    internal string? MainScriptCallerFailure => _scriptCallerLast?.Error;
    internal string? MainScriptCallerSaveBlocker => _scriptCallerInvocation is not null ? "source-Main-script-child-entered" :
        _scriptCallerLast is { Disposition: FalloutMainScriptCallerDisposition.Failed } call ?
            "source-Main-script-caller:" + call.Children.LastOrDefault()?.Step + ":" + call.Error : null;
    internal object? MainScriptCallerState => _scriptCallerSource is null ? null : new
    {
        source = _scriptCallerSource,
        sourceProcess = _process,
        calls = _scriptCallerCalls,
        last = _scriptCallerLast,
        cachedInterfaceFields = _mainInterfaceCachedFields,
        contextTimeBits = _contextTimeBits,
        contextTimeWrites = _contextTimeWrites,
        lastContextTimeWrite = _contextTimeLast,
        deliveryBound = _scriptCallerLease != Guid.Empty,
        deliveryOwner = _scriptCallerDeliveryOwner,
        lastDeliveredFrame = _scriptCallerLastDeliveredFrame,
        cold = _scriptCallerCold,
        blocker = MainScriptCallerSaveBlocker,
        wholeMain = "unowned-original-tail-after-this-sampling-segment"
    };
    internal void ConstructMainScriptCaller(FalloutMainScriptCallerSource source, FalloutMainScriptCallerSnapshot? saved)
    {
        RequireNotBusy(); source.Validate(); var immediate = ScriptFrameSource();
        if (_scriptCallerSource is not null || source != FalloutMainScriptCallerSource.Read(immediate) ||
            source.EngineSha256 != _source.ExecutableSha256)
            throw new InvalidOperationException("Actual Main caller must join its exact constructed cached-byte source once.");
        if (saved is null)
        {
            if (_scriptFrame is not { Frame: 0, LastSite: null })
                throw new InvalidDataException("A sampled Main field cannot invent a fresh caller constructor.");
            _scriptCallerSource = source; _contextTimeBits = source.InitialContextTimeBits;
            _mainInterfaceCachedFields = new(false, false, false, Next()); return;
        }
        ValidateMainScriptCaller(saved);
        if (saved.Source != source || saved.Stack != _stack || saved.Changed > _sequence || saved.CapturedProcess == _process)
            throw new InvalidDataException("Cold Main caller changed source, captured Main sequence or process epoch.");
        RequireMainScriptSamples(saved, _scriptFrame!);
        _scriptCallerSource = source; _scriptCallerCalls = saved.Calls; _scriptCallerLast = saved.LastCall;
        _mainInterfaceCachedFields = saved.CachedInterfaceFields;
        _contextTimeBits = saved.ContextTimeBits; _contextTimeWrites = saved.ContextTimeWrites; _contextTimeLast = saved.LastContextTimeWrite;
        _scriptCallerCold = new(saved.CapturedProcess, _process, Next());
        // No OS query, child, sampler, Steam pump or timer store is replayed.
        // Native delivery numbering resets under a fresh binding; source call
        // numbering and the failed actual prefix remain unchanged.
    }
    internal IDisposable BindMainScriptCaller(IFalloutMainScriptCallerConsumers consumers, string deliveryOwner)
    {
        RequireNotBusy(); ArgumentNullException.ThrowIfNull(consumers); ArgumentException.ThrowIfNullOrWhiteSpace(deliveryOwner);
        var source = MainScriptCallerSource();
        if (_scriptCallerLease != Guid.Empty || consumers.Source != source || string.IsNullOrWhiteSpace(consumers.Owner) ||
            _scriptCallerInvocation is not null)
            throw new InvalidDataException("Main script caller must reuse one genuine selected consumer/native delivery lifetime.");
        var lease = _scriptCallerLease = Guid.NewGuid(); _scriptCallerDeliveryOwner = deliveryOwner;
        _scriptCallerConsumers = consumers; _scriptCallerLastDeliveredFrame = null; _scriptCallerManagedThread = Environment.CurrentManagedThreadId;
        return new MainScriptCallerLease(this, lease);
    }
    private sealed class MainScriptCallerLease(FalloutActorProcessRuntimeState owner, Guid lease) : IDisposable
    {
        public void Dispose()
        {
            if (owner._scriptCallerLease != lease) return;
            if (owner._scriptCallerInvocation is not null) { owner.FaultMainScriptReentry(); throw new InvalidOperationException("Main delivery cannot retire an entered child."); }
            owner._scriptCallerLease = Guid.Empty; owner._scriptCallerDeliveryOwner = null; owner._scriptCallerConsumers = null; owner._scriptCallerManagedThread = null;
        }
    }
    private FalloutMainScriptCallerSource MainScriptCallerSource()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _scriptCallerSource ?? throw new NotSupportedException("source-Main-script-caller-constructor-unbound");
    }
    internal async Task ExecuteMainScriptCaller(ulong deliveryFrame, float deliveredSeconds)
    {
        RequireNotBusy(); RequireMainWriterOutsideWindow(); var source = MainScriptCallerSource();
        if (_scriptCallerInvocation is not null) { FaultMainScriptReentry(); throw new InvalidOperationException("Actual Main child cannot reenter its frame caller."); }
        if (_scriptCallerLast is { Disposition: FalloutMainScriptCallerDisposition.Failed } failed)
            throw new InvalidOperationException("Source Main caller refuses replay of its retained child: " + failed.Error);
        if (_scriptCallerLease == Guid.Empty || _scriptCallerConsumers is not { } consumers ||
            consumers.Source != source || _scriptCallerDeliveryOwner is null)
            throw new NotSupportedException("source-Main-script-caller-native-delivery-lifetime-unbound");
        if (_scriptCallerManagedThread != Environment.CurrentManagedThreadId) throw new InvalidOperationException("Main callback changed its actual C# native-delivery thread.");
        if (_scriptCallerLastDeliveredFrame is { } prior && deliveryFrame <= prior || !float.IsFinite(deliveredSeconds) || deliveredSeconds < 0)
            throw new InvalidDataException("Main delivery repeats a native callback or omits its finite Float32 adapter delta.");
        _scriptCallerLastDeliveredFrame = deliveryFrame;
        var ordinal = _scriptCallerCalls = checked(_scriptCallerCalls + 1); var identity = Guid.NewGuid(); var changed = Next();
        _scriptCallerLast = new(ordinal, identity, _process, consumers.Owner, _scriptCallerDeliveryOwner, deliveryFrame,
            BitConverter.SingleToUInt32Bits(deliveredSeconds), changed, changed, FalloutMainScriptCallerDisposition.Entered, [], null, null);
        var invocation = _scriptCallerInvocation = new(this, identity, ordinal, _process);
        try
        {
            // Its selected source body returns zero and writes nothing. The
            // original argument therefore establishes no clock ownership.
            Enter(FalloutMainScriptCallerStep.ClockPrelude, () => { });
            if (Read(FalloutMainScriptCallerStep.TabKey, () => consumers.AsyncKeyHighBit(invocation, 9)) &&
                Read(FalloutMainScriptCallerStep.AltKey, () => consumers.AsyncKeyHighBit(invocation, 18)))
            { Finish(FalloutMainScriptCallerDisposition.InputSuppressed); return; }
            Enter(FalloutMainScriptCallerStep.Prologue, () => consumers.Prologue(invocation));
            var menu = Read(FalloutMainScriptCallerStep.MenuGateBefore, () => consumers.MenuGate(invocation)) ||
                Read(FalloutMainScriptCallerStep.GuiModeBefore, () => consumers.GuiModeTwo(invocation));
            Store(FalloutMainScriptCallerStep.MenuBeforeStore, menu);
            Store(FalloutMainScriptCallerStep.FirstBeforeStore, Read(FalloutMainScriptCallerStep.FirstPredicateBefore, () => consumers.FirstInterfacePredicate(invocation)));
            Enter(FalloutMainScriptCallerStep.FirstSample, () => SampleScriptFrame(ordinal, FalloutMainScriptSampleSite.BeforeMainChildren));
            Store(FalloutMainScriptCallerStep.FinalBeforeStore, Read(FalloutMainScriptCallerStep.FinalPredicateBefore, () => consumers.FinalInterfacePredicate(invocation)));
            var foreign = Read(FalloutMainScriptCallerStep.ForeignMenu, () => consumers.ForeignActiveMenu(invocation));
            var kind = ReadInteger(FalloutMainScriptCallerStep.ContextKind, () => consumers.InterfaceContextKind(invocation));
            if (kind == 3 && !foreign) Enter(FalloutMainScriptCallerStep.KindThreePrelude, () => consumers.KindThreePrelude(invocation));
            await EnterAsync(FalloutMainScriptCallerStep.Player, () => consumers.Player(invocation));
            Enter(FalloutMainScriptCallerStep.SteamCallbacks, () => consumers.SteamCallbacks(invocation));
            var advance = !menu && !Read(FalloutMainScriptCallerStep.MainHold, () => consumers.MainHold(invocation));
            Enter(FalloutMainScriptCallerStep.TimedContexts, () => consumers.TimedContexts(invocation, advance));
            var afterMenu = Read(FalloutMainScriptCallerStep.MenuGateAfter, () => consumers.MenuGate(invocation)) ||
                Read(FalloutMainScriptCallerStep.GuiModeAfter, () => consumers.GuiModeTwo(invocation));
            Store(FalloutMainScriptCallerStep.MenuAfterStore, afterMenu);
            Store(FalloutMainScriptCallerStep.FirstAfterStore, Read(FalloutMainScriptCallerStep.FirstPredicateAfter, () => consumers.FirstInterfacePredicate(invocation)));
            Enter(FalloutMainScriptCallerStep.SecondSample, () => SampleScriptFrame(ordinal, FalloutMainScriptSampleSite.AfterMainChildren));
            Store(FalloutMainScriptCallerStep.FinalAfterStore, Read(FalloutMainScriptCallerStep.FinalPredicateAfter, () => consumers.FinalInterfacePredicate(invocation)));
            Finish(FalloutMainScriptCallerDisposition.ScopeReturned);
        }
        catch (Exception failure)
        {
            var type = failure.GetType().FullName ?? failure.GetType().Name;
            var children = _scriptCallerLast!.Children.ToArray();
            if (children.Length != 0 && children[^1].Returned is null)
                children[^1] = children[^1] with { FailureType = type, Error = Message(failure) };
            _scriptCallerLast = _scriptCallerLast with
            {
                Changed = Next(),
                Disposition = FalloutMainScriptCallerDisposition.Failed,
                Children = children,
                FailureType = type,
                Error = Message(failure)
            };
            throw;
        }
        finally { _scriptCallerInvocation = null; }

        void Finish(FalloutMainScriptCallerDisposition disposition) => _scriptCallerLast = _scriptCallerLast! with
        { Disposition = disposition, Changed = Next() };
        async Task EnterAsync(FalloutMainScriptCallerStep step, Func<Task> action)
        {
            RequireOwnerThread();
            var child = new FalloutMainScriptChild(step, Next(), null, null, null, null, null);
            _scriptCallerLast = _scriptCallerLast! with { Changed = child.Entered, Children = [.. _scriptCallerLast.Children, child] };
            var fault = _scriptCallerReentry;
            await action();
            RequireOwnerThread();
            if (fault != _scriptCallerReentry) throw new InvalidOperationException("Main asynchronous child swallowed a forbidden source caller reentry.");
            var children = _scriptCallerLast.Children.ToArray(); children[^1] = children[^1] with { Returned = Next() };
            _scriptCallerLast = _scriptCallerLast with { Children = children, Changed = _sequence };
        }
        void RequireOwnerThread()
        {
            if (_scriptCallerManagedThread != Environment.CurrentManagedThreadId)
                throw new InvalidOperationException("Main continuation left its actual native/platform owner thread.");
        }
        void Enter(FalloutMainScriptCallerStep step, Action action)
        {
            RequireOwnerThread();
            var child = new FalloutMainScriptChild(step, Next(), null, null, null, null, null);
            _scriptCallerLast = _scriptCallerLast! with { Changed = child.Entered, Children = [.. _scriptCallerLast.Children, child] };
            var fault = _scriptCallerReentry; action();
            if (fault != _scriptCallerReentry) throw new InvalidOperationException("Main child swallowed a forbidden source caller reentry.");
            var children = _scriptCallerLast.Children.ToArray(); children[^1] = children[^1] with { Returned = Next() };
            _scriptCallerLast = _scriptCallerLast with { Children = children, Changed = _sequence };
        }
        void Store(FalloutMainScriptCallerStep step, bool value)
        {
            Enter(step, () =>
            {
                var fields = _mainInterfaceCachedFields ?? throw new InvalidOperationException("Main interface cache was not constructed.");
                _mainInterfaceCachedFields = step switch
                {
                    FalloutMainScriptCallerStep.MenuBeforeStore or FalloutMainScriptCallerStep.MenuAfterStore => fields with { MenuGate = value, Changed = Next() },
                    FalloutMainScriptCallerStep.FirstBeforeStore or FalloutMainScriptCallerStep.FirstAfterStore => fields with { FirstPredicate = value, Changed = Next() },
                    FalloutMainScriptCallerStep.FinalBeforeStore or FalloutMainScriptCallerStep.FinalAfterStore => fields with { FinalPredicate = value, Changed = Next() },
                    _ => throw new InvalidDataException("Another Main source store targeted these cached interface fields.")
                };
            });
            var children = _scriptCallerLast!.Children.ToArray(); children[^1] = children[^1] with { Boolean = value };
            _scriptCallerLast = _scriptCallerLast with { Children = children };
        }
        bool Read(FalloutMainScriptCallerStep step, Func<bool> read)
        {
            var value = false; Enter(step, () => value = read());
            var children = _scriptCallerLast!.Children.ToArray(); children[^1] = children[^1] with { Boolean = value };
            _scriptCallerLast = _scriptCallerLast with { Children = children }; return value;
        }
        int ReadInteger(FalloutMainScriptCallerStep step, Func<int> read)
        {
            var value = 0;
            Enter(step, () =>
            {
                value = read();
                var children = _scriptCallerLast!.Children.ToArray(); children[^1] = children[^1] with { Integer = value };
                _scriptCallerLast = _scriptCallerLast with { Children = children };
                if (value is < sbyte.MinValue or > sbyte.MaxValue) throw new InvalidDataException("Main interface context query lost its original signed-byte result.");
            });
            return value;
        }
    }
    internal FalloutMainInterfaceCachedFields ReadMainInterfaceCachedFields(FalloutMainScriptInvocation invocation)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!ReferenceEquals(invocation.Owner, this) || !ReferenceEquals(_scriptCallerInvocation, invocation) || invocation.Process != _process)
            throw new InvalidOperationException("Main cached fields require the actual current child invocation.");
        return _mainInterfaceCachedFields ?? throw new InvalidOperationException("Main cached fields have no source constructor.");
    }
    private void FaultMainScriptReentry() => _scriptCallerReentry = checked(_scriptCallerReentry + 1);
    internal void RequireMainScriptChild(FalloutMainScriptInvocation invocation, FalloutMainScriptCallerStep step)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!ReferenceEquals(invocation.Owner, this) || !ReferenceEquals(_scriptCallerInvocation, invocation) ||
            invocation.Process != _process || invocation.Identity != _scriptCallerLast?.Invocation ||
            _scriptCallerLast?.Children.LastOrDefault() is not { Returned: null } child || child.Step != step)
            throw new InvalidOperationException("Main source consumer has no actual current child invocation.");
    }
    internal void AdvanceMainContextTime(FalloutMainScriptInvocation invocation, float delta)
    {
        RequireMainScriptChild(invocation, FalloutMainScriptCallerStep.TimedContexts);
        // A separate original field. The selected body has no finite/positive
        // guard or rest-hour/calendar reset. Preserve even nonfinite stores as
        // raw bits rather than importing another owner's sanitization.
        if (_contextTimeLast?.Invocation == invocation.Identity)
        { FaultMainScriptReentry(); throw new InvalidOperationException("Main timed child cannot store its context delta twice."); }
        var children = _scriptCallerLast!.Children;
        var menu = children.Any(child => (child.Step is FalloutMainScriptCallerStep.MenuGateBefore or FalloutMainScriptCallerStep.GuiModeBefore) && child.Boolean == true);
        if (menu || children.FirstOrDefault(child => child.Step == FalloutMainScriptCallerStep.MainHold)?.Boolean != false)
        { FaultMainScriptReentry(); throw new InvalidOperationException("Main context timer branch was not entered by the original current predicate."); }
        var before = _contextTimeBits; var after = (float)((double)BitConverter.UInt32BitsToSingle(before) + delta);
        _contextTimeBits = BitConverter.SingleToUInt32Bits(after); _contextTimeWrites = checked(_contextTimeWrites + 1);
        _contextTimeLast = new(_contextTimeWrites, invocation.Identity, before, BitConverter.SingleToUInt32Bits(delta), _contextTimeBits, Next());
    }
    internal FalloutMainScriptCallerSnapshot CaptureMainScriptCaller()
    {
        RequireNotBusy(); var source = MainScriptCallerSource();
        if (_scriptCallerInvocation is not null) { FaultMainScriptReentry(); throw new NotSupportedException("Saving entered Main script children requires their actual completion boundary."); }
        var snapshot = new FalloutMainScriptCallerSnapshot(ScriptCallerSchema, source, _stack, _process, _sequence,
            _scriptCallerCalls, _contextTimeBits, _contextTimeWrites, _contextTimeLast, _scriptCallerLast, _scriptCallerCold,
            _mainInterfaceCachedFields ?? throw new InvalidOperationException("Source Main cached fields are absent."));
        ValidateMainScriptCaller(snapshot); RequireMainScriptSamples(snapshot, _scriptFrame!); return snapshot;
    }
}
