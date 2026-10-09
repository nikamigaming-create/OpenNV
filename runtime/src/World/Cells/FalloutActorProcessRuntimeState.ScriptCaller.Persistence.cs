using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutActorProcessRuntimeState
{
    private void RequireMainScriptClosureBoundary(bool retiring)
    {
        if (_scriptCallerInvocation is not null)
        {
            FaultMainScriptReentry(); throw new InvalidOperationException("Actual Main child cannot capture/retire its current source process.");
        }
        if (retiring && _scriptCallerLease != Guid.Empty)
            throw new InvalidOperationException("Actual Main native delivery must close before source process retirement.");
    }
    private void RequireMainScriptSampleCaller(FalloutMainScriptSampleSite site)
    {
        if (!MainScriptCallerConstructed) return; // Focused field-only contract owner.
        var step = site == FalloutMainScriptSampleSite.BeforeMainChildren ? FalloutMainScriptCallerStep.FirstSample :
            FalloutMainScriptCallerStep.SecondSample;
        try
        {
            RequireMainScriptChild(_scriptCallerInvocation ??
                throw new InvalidOperationException("Main sampling requires its actual living caller."), step);
        }
        catch { FaultMainScriptReentry(); throw; }
    }
    internal FalloutMainScriptFrameSnapshot CaptureMainScriptFrameEvidence()
    {
        RequireNotBusy(); _ = ScriptFrameSource();
        if (_scriptCallerInvocation is not null) { FaultMainScriptReentry(); throw new NotSupportedException("Main caller is entered; source-field capture is not a completion receipt."); }
        var allowFailed = _scriptCallerLast is { Disposition: FalloutMainScriptCallerDisposition.Failed };
        ValidateScriptFrame(_scriptFrame!, allowFailed);
        return _scriptFrame!;
    }
    internal static void RequireMainScriptSamples(FalloutMainScriptCallerSnapshot caller, FalloutMainScriptFrameSnapshot field)
    {
        var failed = caller.LastCall is { Disposition: FalloutMainScriptCallerDisposition.Failed };
        ValidateScriptFrame(field, failed);
        if (field.Source != caller.Source.ImmediateSource || field.Stack != caller.Stack || field.Changed > caller.Changed)
            throw new InvalidDataException("Main source field and child invocation changed source, stack or mutation order.");
        var call = caller.LastCall;
        if (call is null)
        {
            if (field.Frame != 0) throw new InvalidDataException("Fresh Main caller omitted an actual source sample.");
            return;
        }
        var sampled = call.Children.Where(child => child.Step is FalloutMainScriptCallerStep.FirstSample or FalloutMainScriptCallerStep.SecondSample).ToArray();
        if (sampled.Length == 0)
        {
            if (field.Frame >= call.Ordinal) throw new InvalidDataException("Main field fabricated a write before its current caller reached either site.");
            return;
        }
        var last = sampled[^1]; var site = last.Step == FalloutMainScriptCallerStep.FirstSample ?
            FalloutMainScriptSampleSite.BeforeMainChildren : FalloutMainScriptSampleSite.AfterMainChildren;
        if (field.Frame != call.Ordinal || field.LastSite != site || field.Changed < last.Entered ||
            last.Returned is { } returned && (field.Changed >= returned || field.Error is not null) ||
            last.Returned is null && field.Error is null ||
            sampled.Length == 1 && !failed || call.Disposition == FalloutMainScriptCallerDisposition.ScopeReturned &&
                (sampled.Length != 2 || field.Error is not null))
            throw new InvalidDataException("Main caller did not retain its exact attempted/returned two source writes.");
    }
    internal static void ValidateMainScriptCaller(FalloutMainScriptCallerSnapshot value)
    {
        if (value is null || value.Schema != ScriptCallerSchema || value.Source is null ||
            string.IsNullOrWhiteSpace(value.Stack) || value.CapturedProcess == Guid.Empty || value.Changed < 1 ||
            value.Calls < 0 || value.ContextTimeWrites < 0 || value.ContextTimeWrites > value.Calls ||
            (value.Calls == 0) != (value.LastCall is null) || (value.ContextTimeWrites == 0) != (value.LastContextTimeWrite is null) ||
            value.ContextTimeWrites == 0 && value.ContextTimeBits != value.Source.InitialContextTimeBits ||
            value.CachedInterfaceFields is null || value.CachedInterfaceFields.Changed < 1 || value.CachedInterfaceFields.Changed > value.Changed ||
            value.Calls == 0 && (value.CachedInterfaceFields.MenuGate || value.CachedInterfaceFields.FirstPredicate || value.CachedInterfaceFields.FinalPredicate))
            throw new InvalidDataException("Main script caller omitted its source constructor, call prefix or independent timer state.");
        value.Source.Validate();
        if (value.ColdHandoff is { } cold && (cold.PreviousProcess == Guid.Empty || cold.PreviousProcess == cold.CurrentProcess ||
            cold.CurrentProcess != value.CapturedProcess || cold.Sequence < 1 || cold.Sequence > value.Changed))
            throw new InvalidDataException("Main caller cold state lost its actual same-runtime process handoff.");
        if (value.LastContextTimeWrite is { } write)
        {
            var added = BitConverter.UInt32BitsToSingle(write.AddedBits);
            var before = BitConverter.UInt32BitsToSingle(write.BeforeBits);
            var next = FalloutSourceMainFamily.ContextClockValue(value.Source.EngineSha256, before, added);
            if (write.Mutation != value.ContextTimeWrites || write.Invocation == Guid.Empty || write.Changed < 1 ||
                write.Changed > value.Changed || write.AfterBits != value.ContextTimeBits ||
                write.AfterBits != BitConverter.SingleToUInt32Bits(next) || write.Mutation == 1 && write.BeforeBits != value.Source.InitialContextTimeBits)
                throw new InvalidDataException("Main timed-context field did not preserve its independent wide-add/Float32 store.");
        }
        if (value.LastCall is not { } call) return;
        if (call.Ordinal != value.Calls || call.Invocation == Guid.Empty || call.SourceProcess == Guid.Empty ||
            call.SourceProcess != value.CapturedProcess && value.ColdHandoff is null ||
            string.IsNullOrWhiteSpace(call.Consumer) || string.IsNullOrWhiteSpace(call.DeliveryOwner) ||
            !float.IsFinite(BitConverter.UInt32BitsToSingle(call.DeliveredSecondsBits)) ||
            BitConverter.UInt32BitsToSingle(call.DeliveredSecondsBits) < 0 || call.Entered < 1 || call.Changed <= call.Entered ||
            call.Changed > value.Changed || !Enum.IsDefined(call.Disposition) ||
            call.Disposition == FalloutMainScriptCallerDisposition.Entered || call.Children is null || call.Children.Count == 0 ||
            (call.Disposition == FalloutMainScriptCallerDisposition.Failed) != (call.Error is not null) ||
            (call.Error is null) != (call.FailureType is null) || call.FailureType is not null && string.IsNullOrWhiteSpace(call.FailureType))
            throw new InvalidDataException("Main script call invented a source process, child return or lost its retained failure.");
        long prior = call.Entered; var values = new Dictionary<FalloutMainScriptCallerStep, FalloutMainScriptChild>();
        foreach (var child in call.Children)
        {
            if (child is null || !Enum.IsDefined(child.Step) || !values.TryAdd(child.Step, child) || child.Entered <= prior ||
                child.Entered >= call.Changed || child.Returned is { } returned && (returned <= child.Entered || returned >= call.Changed) ||
                (child.Error is null) != (child.FailureType is null) || child.Error is not null && child.Returned is not null ||
                child.FailureType is not null && string.IsNullOrWhiteSpace(child.FailureType) ||
                child.Returned is null && (child != call.Children[^1] || call.Disposition != FalloutMainScriptCallerDisposition.Failed) ||
                child.Boolean is not null && child.Integer is not null)
                throw new InvalidDataException("Main script child lost its exact entered/returned failure order.");
            prior = child.Returned ?? child.Entered;
        }
        var expected = new List<FalloutMainScriptCallerStep>();
        var stopped = false;
        if (value.Source.HasNewVegasChildren) Add(FalloutMainScriptCallerStep.ClockPrelude);
        Add(FalloutMainScriptCallerStep.TabKey);
        if (Bool(FalloutMainScriptCallerStep.TabKey)) Add(FalloutMainScriptCallerStep.AltKey);
        var suppressed = Bool(FalloutMainScriptCallerStep.TabKey) && Bool(FalloutMainScriptCallerStep.AltKey);
        if (!suppressed)
        {
            Add(FalloutMainScriptCallerStep.Prologue); Add(FalloutMainScriptCallerStep.MenuGateBefore);
            if (!Bool(FalloutMainScriptCallerStep.MenuGateBefore)) Add(FalloutMainScriptCallerStep.GuiModeBefore);
            Add(FalloutMainScriptCallerStep.MenuBeforeStore); Add(FalloutMainScriptCallerStep.FirstPredicateBefore);
            Add(FalloutMainScriptCallerStep.FirstBeforeStore); Add(FalloutMainScriptCallerStep.FirstSample);
            Add(FalloutMainScriptCallerStep.FinalPredicateBefore); Add(FalloutMainScriptCallerStep.FinalBeforeStore); Add(FalloutMainScriptCallerStep.ForeignMenu);
            Add(FalloutMainScriptCallerStep.ContextKind);
            if (values.TryGetValue(FalloutMainScriptCallerStep.ContextKind, out var kind) && kind.Integer == 3 &&
                !Bool(FalloutMainScriptCallerStep.ForeignMenu)) Add(FalloutMainScriptCallerStep.KindThreePrelude);
            Add(FalloutMainScriptCallerStep.Player);
            if (value.Source.HasNewVegasChildren) Add(FalloutMainScriptCallerStep.SteamCallbacks);
            if (!(Bool(FalloutMainScriptCallerStep.MenuGateBefore) || Bool(FalloutMainScriptCallerStep.GuiModeBefore)))
                Add(FalloutMainScriptCallerStep.MainHold);
            Add(FalloutMainScriptCallerStep.TimedContexts); Add(FalloutMainScriptCallerStep.MenuGateAfter);
            if (!Bool(FalloutMainScriptCallerStep.MenuGateAfter)) Add(FalloutMainScriptCallerStep.GuiModeAfter);
            Add(FalloutMainScriptCallerStep.MenuAfterStore); Add(FalloutMainScriptCallerStep.FirstPredicateAfter);
            Add(FalloutMainScriptCallerStep.FirstAfterStore); Add(FalloutMainScriptCallerStep.SecondSample);
            Add(FalloutMainScriptCallerStep.FinalPredicateAfter); Add(FalloutMainScriptCallerStep.FinalAfterStore);
        }
        if (!call.Children.Select(child => child.Step).SequenceEqual(expected) ||
            call.Disposition == FalloutMainScriptCallerDisposition.InputSuppressed && !suppressed ||
            call.Disposition == FalloutMainScriptCallerDisposition.ScopeReturned && suppressed ||
            call.Disposition != FalloutMainScriptCallerDisposition.Failed && stopped)
            throw new InvalidDataException("Main call skipped a genuine source child or invented a short-circuit return.");
        foreach (var child in call.Children.Where(child => child.Returned is not null))
        {
            var isBool = child.Step is FalloutMainScriptCallerStep.TabKey or FalloutMainScriptCallerStep.AltKey or
                FalloutMainScriptCallerStep.MenuGateBefore or FalloutMainScriptCallerStep.GuiModeBefore or
                FalloutMainScriptCallerStep.FirstPredicateBefore or FalloutMainScriptCallerStep.FinalPredicateBefore or
                FalloutMainScriptCallerStep.ForeignMenu or FalloutMainScriptCallerStep.MainHold or
                FalloutMainScriptCallerStep.MenuGateAfter or FalloutMainScriptCallerStep.GuiModeAfter or
                FalloutMainScriptCallerStep.FirstPredicateAfter or FalloutMainScriptCallerStep.FinalPredicateAfter or
                FalloutMainScriptCallerStep.MenuBeforeStore or FalloutMainScriptCallerStep.MenuAfterStore or
                FalloutMainScriptCallerStep.FirstBeforeStore or FalloutMainScriptCallerStep.FirstAfterStore or
                FalloutMainScriptCallerStep.FinalBeforeStore or FalloutMainScriptCallerStep.FinalAfterStore;
            if (isBool != (child.Boolean is not null) || (child.Step == FalloutMainScriptCallerStep.ContextKind) != (child.Integer is not null) ||
                (value.Source.HasNewVegasChildren ? child.Integer is < sbyte.MinValue or > sbyte.MaxValue : child.Integer is < byte.MinValue or > byte.MaxValue))
                throw new InvalidDataException("Main predicate return omitted its actual result value.");
        }
        RequireCachedStore(FalloutMainScriptCallerStep.MenuBeforeStore, FalloutMainScriptCallerStep.MenuAfterStore,
            value.CachedInterfaceFields.MenuGate, FalloutMainScriptCallerStep.MenuGateBefore, FalloutMainScriptCallerStep.GuiModeBefore,
            FalloutMainScriptCallerStep.MenuGateAfter, FalloutMainScriptCallerStep.GuiModeAfter);
        RequireCachedStore(FalloutMainScriptCallerStep.FirstBeforeStore, FalloutMainScriptCallerStep.FirstAfterStore,
            value.CachedInterfaceFields.FirstPredicate, FalloutMainScriptCallerStep.FirstPredicateBefore, null, FalloutMainScriptCallerStep.FirstPredicateAfter, null);
        RequireCachedStore(FalloutMainScriptCallerStep.FinalBeforeStore, FalloutMainScriptCallerStep.FinalAfterStore,
            value.CachedInterfaceFields.FinalPredicate, FalloutMainScriptCallerStep.FinalPredicateBefore, null, FalloutMainScriptCallerStep.FinalPredicateAfter, null);
        var lastStore = call.Children.LastOrDefault(child => child.Step is FalloutMainScriptCallerStep.MenuBeforeStore or
            FalloutMainScriptCallerStep.MenuAfterStore or FalloutMainScriptCallerStep.FirstBeforeStore or FalloutMainScriptCallerStep.FirstAfterStore or
            FalloutMainScriptCallerStep.FinalBeforeStore or FalloutMainScriptCallerStep.FinalAfterStore);
        if (lastStore is not null && (lastStore.Returned is null || value.CachedInterfaceFields.Changed != lastStore.Entered + 1))
            throw new InvalidDataException("Main source cached-field store did not preserve its real mutation point.");
        if (value.LastContextTimeWrite is { } current && current.Invocation == call.Invocation &&
            (Bool(FalloutMainScriptCallerStep.MenuGateBefore) || Bool(FalloutMainScriptCallerStep.GuiModeBefore) ||
             Bool(FalloutMainScriptCallerStep.MainHold) || !values.TryGetValue(FalloutMainScriptCallerStep.TimedContexts, out var timed) ||
             current.Changed <= timed.Entered || timed.Returned is { } end && current.Changed >= end))
            throw new InvalidDataException("Main context timer store escaped the real timed child.");
        void RequireCachedStore(FalloutMainScriptCallerStep beforeStore, FalloutMainScriptCallerStep afterStore, bool retained,
            FalloutMainScriptCallerStep beforeQuery, FalloutMainScriptCallerStep? beforeAlternative,
            FalloutMainScriptCallerStep afterQuery, FalloutMainScriptCallerStep? afterAlternative)
        {
            var after = values.TryGetValue(afterStore, out var later);
            if (!after && !values.TryGetValue(beforeStore, out later)) return;
            var query = after ? afterQuery : beforeQuery; var alternative = after ? afterAlternative : beforeAlternative;
            var expected = Bool(query) || alternative is { } other && Bool(other);
            if (later!.Returned is null || later.Boolean != expected || retained != expected)
                throw new InvalidDataException("Main cached source field was not written from its actual returned predicate.");
        }
        bool Bool(FalloutMainScriptCallerStep step) => values.TryGetValue(step, out var found) && found.Boolean == true;
        void Add(FalloutMainScriptCallerStep step)
        {
            if (stopped) return;
            expected.Add(step);
            if (!values.TryGetValue(step, out var found) || found.Returned is null) stopped = true;
        }
    }
}
