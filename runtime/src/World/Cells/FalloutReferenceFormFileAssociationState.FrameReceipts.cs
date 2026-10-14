namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceFormFileAssociationState
{
    private readonly FalloutReferenceFrameJournal<FalloutReferenceFormFileCall> _frameReceipts = new(FalloutReferenceFrameReceipt.MaximumFileReads);
    private FalloutReferenceFormFileCall? _lastFileCall;
    private string? _failure;

    private void PrepareFrameFile(FalloutReferencePositionInvocation invocation, string? writer)
    {
        if (invocation.Frame is not null && writer is not null)
            throw new InvalidOperationException("Frame retention cannot replace an original association writer.");
        _frameReceipts.Prepare(invocation);
    }
    private void RetainFileCall(FalloutReferenceFormFileCall call)
    {
        _lastFileCall = call;
        if (call.Invocation.Frame is { } frame && call.Returned && call.Writer is null && call.Read is not null)
            _frameReceipts.Append(call, frame, call.Entered, call.Changed, 0);
        else { _frameReceipts.Seal(); _calls.Add(call); }
    }
    internal static IEnumerable<FalloutReferenceHistoryEvent<FalloutReferenceFormFileCall>> FileHistory(FalloutReferenceFormFileSnapshot saved) =>
        FalloutReferenceFrameJournal<FalloutReferenceFormFileCall>.Merge(saved.Calls, saved.FrameRuns,
            call => call.Invocation.ReferenceProcess, call => call.Entered, call => call.Changed);
    internal static IEnumerable<FalloutReferenceFormFileCall> FileEndpoints(FalloutReferenceFormFileSnapshot saved) =>
        FileHistory(saved).SelectMany(item => item.Call is { } call ? [call] :
            item.Frames!.FirstCalls.Concat(item.Frames.LastCalls).DistinctBy(row => row.Changed));

    private static void ValidateFileFrames(FalloutReferenceFrameRun<FalloutReferenceFormFileCall> run,
        FalloutReferenceFormFileSnapshot saved, IReadOnlyDictionary<Guid, FalloutReferencePositionPublication> owners,
        IDictionary<Guid, int> threads, ref int ordinal, ref long sequence, string? writer)
    {
        FalloutReferenceFrameJournal<FalloutReferenceFormFileCall>.ValidateShape(run, FalloutReferenceFrameReceipt.MaximumFileReads, call => call.Invocation,
            call => call.Entered, call => call.Changed, _ => 0, (left, right) => left == right);
        if (run.Entered != sequence + 1 || run.Stores != 0 || writer is not null ||
            !owners.TryGetValue(run.FirstFrame.Parent.Frame.PlayerProcess, out var owner))
            throw new InvalidDataException("Frame file getter interval crossed an association writer or another field owner.");
        var currentOrdinal = saved.Publications.Select((publication, index) => (publication.Process, index))
            .Single(value => value.Process == owner.Process).index;
        if (currentOrdinal < ordinal) throw new InvalidDataException("Frame getter crossed a retired association constructor.");
        foreach (var call in run.FirstCalls.Concat(run.LastCalls).DistinctBy(row => row.Changed))
        {
            var inv = call.Invocation; inv.ValidatePackageCaller(); var read = call.Read;
            if (!call.Returned || call.Failure is not null || call.Writer is not null || inv.Actor != saved.Actor ||
                inv.ReferenceConstructor != owner.Constructor || read is null || read.Source != saved.Source.Identity ||
                read.Stack != saved.Stack || read.Process != owner.Process || read.Factory != owner.Factory || read.Constructor != owner.Constructor ||
                read.Actor != saved.Actor || read.Sequence != call.Entered || read.Count != 0 || read.File is not null ||
                threads.TryGetValue(owner.Process, out var thread) && thread != inv.ThreadId)
                throw new InvalidDataException("Frame file endpoint substituted an owning plugin or changed its actual returned getter.");
            threads.TryAdd(owner.Process, inv.ThreadId);
        }
        sequence = run.Changed; ordinal = currentOrdinal;
    }
}
