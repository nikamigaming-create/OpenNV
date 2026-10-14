using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal enum FalloutReferenceFrameOperation { Borrow, AngleReset, CorrectPosition, BasePosition }
internal sealed record FalloutReferenceFrameReceipt(FalloutPlayerActorMovementInvocation Parent,
    FalloutReferenceFrameOperation Operation)
{
    internal const int MaximumPositionCalls = 2, MaximumChangeCalls = 3,
        MaximumThreadReads = 3, MaximumFileReads = 2;
    internal void Validate(FalloutReferencePositionInvocation invocation)
    {
        var frame = Parent.Frame;
        if (Parent.Call == Guid.Empty || frame.Caller.Main == Guid.Empty || frame.Caller.Invocation == Guid.Empty ||
            frame.Caller.Ordinal < 1 || frame.Prelude == Guid.Empty || !Enum.IsDefined(Operation) ||
            invocation.Ingress != FalloutReferencePositionIngress.ActorPosition || invocation.PackageCommand is not null ||
            invocation.Actor != frame.Common.Actor || invocation.ReferenceProcess != frame.PlayerProcess ||
            invocation.ReferenceConstructor != frame.PlayerConstructor || invocation.CommonProcess != frame.Common.Process ||
            invocation.Epoch != frame.Common.Epoch || invocation.ThreadId != frame.ThreadId)
            throw new InvalidDataException("Reference frame receipt lost its actual entered Actor movement caller.");
    }
}

// Receipt retention only. The reference fields, masks and getter effects remain
// in their existing owners. A run grows in counters, never in per-frame arrays.
internal sealed record FalloutReferenceFrameRun<T>(FalloutReferenceFrameReceipt FirstFrame,
    FalloutReferenceFrameReceipt LastFrame, long Frames, long Calls, long Stores, long Entered, long Changed,
    IReadOnlyList<T> FirstCalls, IReadOnlyList<T> LastCalls, T? EffectCall = default);
internal sealed record FalloutReferenceHistoryEvent<T>(T? Call, FalloutReferenceFrameRun<T>? Frames,
    Guid Process, long Entered, long Changed) where T : class;

internal sealed class FalloutReferenceFrameJournal<T>(int maximumCallsPerFrame, bool uniqueOperations = true) where T : class
{
    private readonly List<FalloutReferenceFrameRun<T>> _runs = [];
    private bool _extend;
    private FalloutReferenceFrameReceipt? _lastFrame;
    private int _lastFrameCalls;
    private uint _lastFrameOperations;
    internal IReadOnlyList<FalloutReferenceFrameRun<T>> Runs => _runs;

    internal void Restore(IReadOnlyList<FalloutReferenceFrameRun<T>>? saved)
    {
        if (_runs.Count != 0) throw new InvalidOperationException("Frame receipt restoration cannot replay.");
        _runs.AddRange(saved ?? []);
        if (_runs.Count != 0) { _lastFrame = _runs[^1].LastFrame; _lastFrameCalls = _runs[^1].LastCalls.Count; }
        _extend = false;
    }
    internal void Seal() => _extend = false;

    internal void Prepare(FalloutReferencePositionInvocation invocation)
    {
        if (invocation.Frame is not { } frame) { Seal(); return; }
        frame.Validate(invocation);
        var sameOwner = _lastFrame is not null && SameOwner(_lastFrame, frame);
        var sameFrame = sameOwner && _lastFrame!.Parent == frame.Parent;
        var operation = 1u << (int)frame.Operation;
        if (sameOwner && (_lastFrame!.Parent.Frame.Caller.Main != frame.Parent.Frame.Caller.Main ||
                frame.Parent.Frame.Caller.Ordinal < _lastFrame.Parent.Frame.Caller.Ordinal ||
                frame.Parent.Frame.Caller.Ordinal == _lastFrame.Parent.Frame.Caller.Ordinal && !sameFrame) ||
            sameFrame && (_lastFrameCalls >= maximumCallsPerFrame || uniqueOperations && (_lastFrameOperations & operation) != 0))
            throw new InvalidOperationException("Frame receipts replayed a caller or exceeded its actual bounded children.");
        _lastFrameCalls = sameFrame ? _lastFrameCalls + 1 : 1;
        _lastFrameOperations = sameFrame ? _lastFrameOperations | operation : operation;
        _lastFrame = frame;
    }

    internal void Append(T call, FalloutReferenceFrameReceipt frame, long entered, long changed, int stores)
    {
        if (entered < 1 || changed != checked(entered + stores + 1) || stores < 0)
            throw new InvalidDataException("Frame receipt altered the actual entered/store/completion revisions.");
        var previous = _extend && _runs.Count != 0 ? _runs[^1] : null;
        var sameOwner = previous is not null && SameOwner(previous.LastFrame, frame);
        if (!sameOwner || previous!.Changed != entered - 1)
        {
            _runs.Add(new(frame, frame, 1, 1, stores, entered, changed, [call], [call], stores == 0 ? null : call));
            _extend = true;
            return;
        }
        var sameFrame = previous.LastFrame.Parent == frame.Parent;
        if (sameFrame && previous.LastCalls.Count >= maximumCallsPerFrame ||
            !sameFrame && (previous.LastFrame.Parent.Frame.Caller.Main != frame.Parent.Frame.Caller.Main ||
                frame.Parent.Frame.Caller.Ordinal <= previous.LastFrame.Parent.Frame.Caller.Ordinal))
            throw new InvalidOperationException("Frame receipts replayed a caller or exceeded the selected bounded children.");
        T[] last = sameFrame ? previous.LastCalls.Append(call).ToArray() : [call];
        IReadOnlyList<T> first = previous.FirstFrame.Parent == frame.Parent ? previous.FirstCalls.Append(call).ToArray() : previous.FirstCalls;
        _runs[^1] = previous with
        {
            LastFrame = frame,
            Frames = checked(previous.Frames + (sameFrame ? 0 : 1)),
            Calls = checked(previous.Calls + 1),
            Stores = checked(previous.Stores + stores),
            Changed = changed,
            FirstCalls = first,
            LastCalls = last,
            EffectCall = stores == 0 ? previous.EffectCall : call
        };
    }

    internal static bool SameOwner(FalloutReferenceFrameReceipt left, FalloutReferenceFrameReceipt right) =>
        left.Parent.Frame.PlayerProcess == right.Parent.Frame.PlayerProcess &&
        left.Parent.Frame.PlayerConstructor == right.Parent.Frame.PlayerConstructor &&
        left.Parent.Frame.Common.Actor == right.Parent.Frame.Common.Actor &&
        left.Parent.Frame.Common.Process == right.Parent.Frame.Common.Process &&
        left.Parent.Frame.Common.Epoch == right.Parent.Frame.Common.Epoch &&
        left.Parent.Frame.ThreadId == right.Parent.Frame.ThreadId;

    internal static void ValidateShape(FalloutReferenceFrameRun<T> run, int maximumCalls,
        Func<T, FalloutReferencePositionInvocation> invocation,
        Func<T, long> entered, Func<T, long> changed, Func<T, int> stores, Func<T, T, bool> same)
    {
        if (run.Frames < 1 || run.Calls < run.Frames || run.Calls > checked(run.Frames * maximumCalls) ||
            run.Stores < 0 || run.Entered < 1 || run.Changed != checked(run.Entered - 1 + 2 * run.Calls + run.Stores) ||
            run.FirstCalls is not { Count: > 0 } || run.LastCalls is not { Count: > 0 } ||
            run.FirstCalls.Count > maximumCalls || run.LastCalls.Count > maximumCalls ||
            !SameOwner(run.FirstFrame, run.LastFrame) ||
            run.FirstFrame.Parent.Frame.Caller.Main != run.LastFrame.Parent.Frame.Caller.Main ||
            run.LastFrame.Parent.Frame.Caller.Ordinal < run.FirstFrame.Parent.Frame.Caller.Ordinal ||
            run.Frames > checked(run.LastFrame.Parent.Frame.Caller.Ordinal - run.FirstFrame.Parent.Frame.Caller.Ordinal + 1) ||
            run.Frames == 1 && run.FirstFrame.Parent != run.LastFrame.Parent ||
            run.Frames > 1 && run.FirstFrame.Parent == run.LastFrame.Parent)
            throw new InvalidDataException("Frame run altered source caller bounds or revision/count algebra.");
        Check(run.FirstCalls, run.FirstFrame); Check(run.LastCalls, run.LastFrame);
        if (entered(run.FirstCalls[0]) != run.Entered || changed(run.LastCalls[^1]) != run.Changed ||
            run.Frames == 1 && (run.FirstCalls.Count != run.LastCalls.Count ||
                run.FirstCalls.Where((call, index) => !same(call, run.LastCalls[index])).Any()) ||
            run.Stores == 0 && run.EffectCall is not null || run.Stores != 0 && (run.EffectCall is null ||
                entered(run.EffectCall) < run.Entered || changed(run.EffectCall) > run.Changed || stores(run.EffectCall) < 1))
            throw new InvalidDataException("Frame run omitted its original first/last completed child batch.");
        void Check(IReadOnlyList<T> rows, FalloutReferenceFrameReceipt frame)
        {
            long previous = entered(rows[0]) - 1;
            var operations = new HashSet<FalloutReferenceFrameOperation>();
            foreach (var row in rows)
            {
                var caller = invocation(row); caller.Frame?.Validate(caller);
                if (caller.Frame?.Parent != frame.Parent || !operations.Add(caller.Frame.Operation) || entered(row) != previous + 1 ||
                    changed(row) != checked(entered(row) + stores(row) + 1))
                    throw new InvalidDataException("Frame endpoint replaced an actual child or its revision order.");
                previous = changed(row);
            }
        }
    }

    internal static IEnumerable<FalloutReferenceHistoryEvent<T>> Merge(IReadOnlyList<T> calls,
        IReadOnlyList<FalloutReferenceFrameRun<T>>? frames, Func<T, Guid> process,
        Func<T, long> entered, Func<T, long> changed)
    {
        var runs = frames ?? []; var callIndex = 0; var runIndex = 0; long end = 0;
        while (callIndex < calls.Count || runIndex < runs.Count)
        {
            var useCall = runIndex == runs.Count || callIndex < calls.Count && entered(calls[callIndex]) < runs[runIndex].Entered;
            FalloutReferenceHistoryEvent<T> next;
            if (useCall)
            {
                var call = calls[callIndex++]; next = new(call, null, process(call), entered(call), changed(call));
            }
            else
            {
                var run = runs[runIndex++]; next = new(null, run, run.FirstFrame.Parent.Frame.PlayerProcess, run.Entered, run.Changed);
            }
            if (next.Entered != end + 1 || next.Changed <= next.Entered)
                throw new InvalidDataException("Sparse reference history lost or overlapped an actual revision interval.");
            end = next.Changed; yield return next;
        }
    }
}
