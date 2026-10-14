using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutSourceReferencePositionState
{
    private readonly FalloutReferenceFrameJournal<FalloutReferencePositionCall> _frameReceipts = new(FalloutReferenceFrameReceipt.MaximumPositionCalls);
    private string? _failure;

    private void PrepareFramePosition(FalloutReferencePositionInvocation invocation, string kind)
    {
        if (invocation.Frame is { } frame && (kind switch
        {
            "base-reference-position" => frame.Operation != FalloutReferenceFrameOperation.BasePosition,
            "Actor-reference-position" => frame.Operation != FalloutReferenceFrameOperation.CorrectPosition,
            _ => true
        })) throw new InvalidOperationException("Frame retention cannot replace an ordinary reference command writer.");
        _frameReceipts.Prepare(invocation);
    }
    private void RetainPositionCall(FalloutReferencePositionCall call)
    {
        if (call.Invocation.Frame is { } frame && call.Returned && IsFramePosition(call))
            _frameReceipts.Append(call, frame, call.Entered, call.Changed, call.Stores.Count);
        else { _frameReceipts.Seal(); _calls.Add(call); }
    }
    private static bool IsFramePosition(FalloutReferencePositionCall call) =>
        call.Kind is "base-reference-position" or "Actor-reference-position" && call.Stores.Count == 3 &&
        call.Invocation.Frame?.Operation == (call.Kind == "base-reference-position" ?
            FalloutReferenceFrameOperation.BasePosition : FalloutReferenceFrameOperation.CorrectPosition) &&
        call.Stores.Select(row => row.Step).SequenceEqual(new[] { FalloutReferencePositionStep.PositionX,
            FalloutReferencePositionStep.PositionY, FalloutReferencePositionStep.PositionZ }) &&
        call.Worldspace is null && call.InvalidatedLane is null && call.Prefix is null && call.WriterBoundary is null &&
        call.Change is { Returned: true, Mask: FalloutReferencePositionSource.PositionChangeMask, Force: false } child &&
        child.Invocation == call.Invocation && child.Source == call.Source &&
        call.After == call.Before with { Position = call.After.Position } &&
        call.Flags is not null && call.Persistent is not null &&
        (call.Persistent == false ? call.BaseKind is null : call.BaseKind is { } kind && !FalloutReferencePositionSource.UsesWorldspacePositionList(kind));

    internal static IEnumerable<FalloutReferenceHistoryEvent<FalloutReferencePositionCall>> PositionHistory(
        FalloutReferencePositionSnapshot saved) => FalloutReferenceFrameJournal<FalloutReferencePositionCall>.Merge(
            saved.Calls, saved.FrameRuns, call => call.Invocation.ReferenceProcess, call => call.Entered, call => call.Changed);

    internal static IEnumerable<FalloutReferencePositionCall> PositionEndpoints(FalloutReferencePositionSnapshot saved) =>
        PositionHistory(saved).SelectMany(item => item.Call is { } call ? [call] :
            item.Frames!.FirstCalls.Concat(item.Frames.LastCalls).DistinctBy(row => (row.Factory, row.Changed)));

    private static FalloutReferenceFrameRun<FalloutReferencePositionCall> ClonePositionFrames(
        FalloutReferenceFrameRun<FalloutReferencePositionCall> run) => run with
        {
            FirstCalls = Array.AsReadOnly(run.FirstCalls.Select(CloneCall).ToArray()),
            LastCalls = Array.AsReadOnly(run.LastCalls.Select(CloneCall).ToArray()),
            EffectCall = run.EffectCall is { } call ? CloneCall(call) : null
        };
    private static bool SameFramePosition(FalloutReferencePositionCall left, FalloutReferencePositionCall right)
    {
        var empty = Array.Empty<FalloutReferencePositionStore>();
        return (left with { Stores = empty, Change = null }) == (right with { Stores = empty, Change = null }) &&
            left.Stores.SequenceEqual(right.Stores) && left.Change is { } a && right.Change is { } b &&
            FalloutSourceReferenceChangeManager.SameFrameChange(a, b);
    }

    private static void ValidatePositionFrames(FalloutReferenceFrameRun<FalloutReferencePositionCall> run,
        FalloutReferencePositionSnapshot saved,
        IReadOnlyDictionary<Guid, (int Ordinal, FalloutReferencePositionPublication Value)> publications,
        IDictionary<Guid, int> threads, ref int lastPublication, ref long sequence,
        ref FalloutReferencePositionValues values, IDictionary<FalloutReferencePositionLane, string> writers)
    {
        FalloutReferenceFrameJournal<FalloutReferencePositionCall>.ValidateShape(run, FalloutReferenceFrameReceipt.MaximumPositionCalls, call => call.Invocation,
            call => call.Entered, call => call.Changed, call => call.Stores.Count, SameFramePosition);
        if (run.Entered != sequence + 1 || run.Stores != checked(3 * run.Calls) ||
            run.FirstCalls[0].Before != values || writers.ContainsKey(FalloutReferencePositionLane.PositionMode) ||
            run.LastCalls[0].Before != values with { Position = run.LastCalls[0].Before.Position })
            throw new InvalidDataException("Movement frame history changed an independent reference lane or lost its starting revision.");
        var owner = publications[run.FirstFrame.Parent.Frame.PlayerProcess];
        if (owner.Ordinal < lastPublication) throw new InvalidDataException("Frame receipt crossed a retired reference publication.");
        foreach (var call in run.FirstCalls.Concat(run.LastCalls).Append(run.EffectCall!).DistinctBy(row => row.Changed))
        {
            var inv = call.Invocation; inv.ValidatePackageCaller();
            if (call.Source != saved.Source.Identity || call.Factory != owner.Value.Factory || inv.Actor != saved.Actor ||
                !FalloutReferenceFrameJournal<FalloutReferencePositionCall>.SameOwner(inv.Frame!, run.FirstFrame) ||
                inv.ReferenceConstructor != owner.Value.Constructor || call.Step != FalloutReferencePositionStep.Returned ||
                !call.Returned || call.Failure is not null || !IsFramePosition(call) ||
                threads.TryGetValue(inv.ReferenceProcess, out var thread) && thread != inv.ThreadId)
                throw new InvalidDataException("Frame endpoint lost its original returned setter, scope or mask-two child.");
            var before = call.Before; var revision = call.Entered;
            foreach (var store in call.Stores)
            {
                if (store.Changed != ++revision || store.Bits is null || store.Form is not null || store.Present is not null)
                    throw new InvalidDataException("Frame endpoint changed original paired XYZ revisions.");
                before = Apply(before, store);
            }
            if (call.Changed != revision + 1 || before != call.After)
                throw new InvalidDataException("Frame endpoint values disagree with its real committed stores.");
            threads.TryAdd(inv.ReferenceProcess, inv.ThreadId);
        }
        values = run.LastCalls[^1].After; sequence = run.Changed;
        writers.Remove(FalloutReferencePositionLane.Position); lastPublication = owner.Ordinal;
    }
}
