using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed partial class FalloutPlayerThreadContextState
{
    private readonly FalloutReferenceFrameJournal<FalloutSourceThreadReferenceFlagsCall> _referenceFrameReceipts = new(FalloutReferenceFrameReceipt.MaximumThreadReads);
    private FalloutSourceThreadReferenceFlagsCall? _lastReferenceFrameCall;
    private string? _referenceFrameFailure;

    internal FalloutSourceThreadReferenceFlagsRead ReadReferenceChangeFlags(FalloutReferencePositionInvocation invocation,
        Guid consumerFactory, Action requireCurrent)
    {
        var call = RunReferenceFlags(invocation.Call, consumerFactory, null, null, requireCurrent, invocation);
        return new(call.Source, call.Process, call.Factory, invocation.Call, consumerFactory,
            call.Thread, call.Changed, 1, call.After);
    }
    // Called only when the real manager retains a structural/failed child. It
    // pins its current getter endpoint; it executes no original getter again.
    internal void RetainCurrentFrameReferenceChangeRead()
    {
        if (_referenceFlagsBusy) { _referenceFlagFault++; throw new InvalidOperationException("Entered thread read cannot be retained."); }
        _referenceFrameReceipts.Seal();
    }
    private void PrepareFrameThreadRead(FalloutReferencePositionInvocation? invocation, bool? enabled, string? boundary)
    {
        if (invocation?.Frame is null) { _referenceFrameReceipts.Seal(); return; }
        if (enabled is not null || boundary is not null || invocation.ReferenceProcess != _process)
            throw new InvalidOperationException("Frame retention cannot replace a current-thread flag writer.");
        _referenceFrameReceipts.Prepare(invocation);
    }
    private void RetainFrameThreadCall(FalloutSourceThreadReferenceFlagsCall call, FalloutReferencePositionInvocation? invocation)
    {
        call = call with { ReferenceInvocation = invocation };
        _lastReferenceFrameCall = call;
        if (invocation?.Frame is { } frame && call.Returned && call.Enabled is null && call.WriterBoundary is null && call.Before == call.After)
            _referenceFrameReceipts.Append(call, frame, call.Entered, call.Changed, 0);
        else { _referenceFrameReceipts.Seal(); _referenceFlagCalls.Add(call); }
    }
    internal static IEnumerable<FalloutReferenceHistoryEvent<FalloutSourceThreadReferenceFlagsCall>> ThreadReferenceFrameHistory(
        FalloutSourceThreadReferenceFlagsSnapshot saved) => FalloutReferenceFrameJournal<FalloutSourceThreadReferenceFlagsCall>.Merge(
            saved.Calls, saved.FrameRuns, call => call.Process, call => call.Entered, call => call.Changed);
    internal static IEnumerable<FalloutSourceThreadReferenceFlagsCall> ThreadReferenceFrameEndpoints(FalloutSourceThreadReferenceFlagsSnapshot saved) =>
        ThreadReferenceFrameHistory(saved).SelectMany(item => item.Call is { } call ? [call] :
            item.Frames!.FirstCalls.Concat(item.Frames.LastCalls).DistinctBy(row => (row.Factory, row.Changed)));

    private static void ValidateThreadReferenceFrames(FalloutReferenceFrameRun<FalloutSourceThreadReferenceFlagsCall> run,
        FalloutSourceThreadReferenceFlagsSnapshot saved, IReadOnlyDictionary<Guid, (Guid Factory, int Ordinal)> publications,
        IDictionary<Guid, int> threads, ref int lastPublication, ref long sequence, uint flags, string? boundary)
    {
        FalloutReferenceFrameJournal<FalloutSourceThreadReferenceFlagsCall>.ValidateShape(run, FalloutReferenceFrameReceipt.MaximumThreadReads,
            call => call.ReferenceInvocation ?? throw new InvalidDataException("Frame thread read lost its actual reference invocation."),
            call => call.Entered, call => call.Changed, _ => 0, (left, right) => left == right);
        if (run.Entered != sequence + 1 || run.Stores != 0 || boundary is not null ||
            !publications.TryGetValue(run.FirstFrame.Parent.Frame.PlayerProcess, out var owner) || owner.Ordinal < lastPublication)
            throw new InvalidDataException("Frame thread getter interval crossed a writer or another current-thread owner.");
        foreach (var call in run.FirstCalls.Concat(run.LastCalls).DistinctBy(row => row.Changed))
        {
            var inv = call.ReferenceInvocation!;
            if (call.Source != saved.Source || call.Process != inv.ReferenceProcess || call.Factory != owner.Factory ||
                call.Invocation != inv.Call || call.ConsumerFactory == Guid.Empty || call.Thread != inv.ThreadId ||
                !call.Returned || call.Failure is not null || call.Enabled is not null || call.WriterBoundary is not null ||
                call.Before != flags || call.After != flags ||
                threads.TryGetValue(call.Process, out var thread) && thread != call.Thread)
                throw new InvalidDataException("Frame thread getter replaced its actual current cells, source caller or retained revision.");
            threads.TryAdd(call.Process, call.Thread);
        }
        sequence = run.Changed; lastPublication = owner.Ordinal;
    }

}
