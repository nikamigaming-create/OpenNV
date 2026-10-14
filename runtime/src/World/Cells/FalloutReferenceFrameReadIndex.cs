using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

// Constructed once per aggregate validation. Interior lookups are logarithmic
// in retained command/cold intervals, never in the number of original frames.
internal sealed class FalloutReferenceFrameReadIndex
{
    private readonly Dictionary<(Guid Process, Guid Factory), FalloutReferenceFrameRun<FalloutSourceThreadReferenceFlagsCall>[]> _runs;
    internal FalloutReferenceFrameReadIndex(FalloutSourceThreadReferenceFlagsSnapshot saved) =>
        _runs = (saved.FrameRuns ?? []).GroupBy(run => (run.FirstCalls[0].Process, run.FirstCalls[0].Factory))
            .ToDictionary(group => group.Key, group => group.OrderBy(run => run.Entered).ToArray());

    internal bool Contains(FalloutSourceThreadReferenceFlagsRead read, FalloutReferencePositionInvocation invocation)
    {
        if (invocation.Frame is not { } frame || !_runs.TryGetValue((read.Process, read.Factory), out var runs)) return false;
        var low = 0; var high = runs.Length - 1;
        while (low <= high)
        {
            var middle = low + (high - low) / 2; var run = runs[middle];
            if (read.Sequence <= run.Entered) { high = middle - 1; continue; }
            if (read.Sequence > run.Changed) { low = middle + 1; continue; }
            if ((read.Sequence - run.Entered) % 2 != 1 ||
                !FalloutReferenceFrameJournal<FalloutSourceThreadReferenceFlagsCall>.SameOwner(frame, run.FirstFrame) ||
                frame.Parent.Frame.Caller.Main != run.FirstFrame.Parent.Frame.Caller.Main ||
                frame.Parent.Frame.Caller.Ordinal < run.FirstFrame.Parent.Frame.Caller.Ordinal ||
                frame.Parent.Frame.Caller.Ordinal > run.LastFrame.Parent.Frame.Caller.Ordinal) return false;
            var actual = run.FirstCalls.Concat(run.LastCalls).FirstOrDefault(call => call.Changed == read.Sequence);
            if (actual is not null) return read == Read(actual) && actual.ReferenceInvocation == invocation;
            var basis = run.FirstCalls[0];
            return read.Source == basis.Source && read.Process == basis.Process && read.Factory == basis.Factory &&
                read.ConsumerFactory == basis.ConsumerFactory && read.Thread == basis.Thread && read.Flags == basis.After &&
                read.Initialized == 1 && read.Invocation == invocation.Call;
        }
        return false;
        static FalloutSourceThreadReferenceFlagsRead Read(FalloutSourceThreadReferenceFlagsCall call) =>
            new(call.Source, call.Process, call.Factory, call.Invocation, call.ConsumerFactory, call.Thread, call.Changed, 1, call.After);
    }
}
