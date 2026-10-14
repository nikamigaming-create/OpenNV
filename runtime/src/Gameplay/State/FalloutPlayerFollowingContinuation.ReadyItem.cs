using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal static partial class FalloutPlayerFollowingContinuation
{
    private static void ValidateReadyItemQueries(FalloutPlayerFollowingContinuationCall call,
        FalloutPlayerFollowingContinuationSource source, string stack)
    {
        var queries = call.ReadyItemQueries ?? [];
        var selected = call.FailedAt;
        if (queries.Count == 0)
        {
            if (selected is FalloutPlayerFollowingContinuationStep.ReadyItemGlobals or
                FalloutPlayerFollowingContinuationStep.ReadyItemPositive or FalloutPlayerFollowingContinuationStep.ReadyItemTail)
                throw new InvalidDataException("Ready Item continuation omitted its actual source queries.");
            return;
        }
        if (queries.Count > 3 || !call.ScalarSkipped || call.Timer is not { Value: 0 })
            throw new InvalidDataException("Ready Item queries escaped the source timer branch.");
        var originalSource = FalloutOriginalInputSource.Reviewed(source.Controls);
        var first = queries[0] ?? throw new InvalidDataException("Ready Item omitted its first actual query.");
        for (var index = 0; index < queries.Count; ++index)
        {
            var query = queries[index];
            var mode = index switch { 0 => 1, 1 => 0, _ => 2 };
            FalloutOriginalInputValidation.ValidateQuery(originalSource, stack, query);
            if (query.Control != FalloutPlayerFollowingContinuationSource.ReadyItemControl ||
                query.Mode != mode || query.Invocation != new FalloutInputInvocation(call.PlayerProcess,
                    call.Caller.Main, call.Caller.Ordinal, call.Caller.Invocation) ||
                query.Factory != first.Factory || query.Provider != first.Provider || query.Window != first.Window ||
                query.Sample != first.Sample || query.InputRevision < first.InputRevision || query.BufferRevision < first.BufferRevision ||
                index > 0 && (queries[index - 1].Sequence >= query.Sequence || queries[index - 1].Count != 0 ||
                    queries[index - 1].Step != FalloutOriginalInputQueryStep.Returned) ||
                query.BeforeMenuWord != query.AfterMenuWord)
                throw new InvalidDataException("Ready Item changed the actual source invocation, device count or ordered mode prefix.");
            if (query.Step == FalloutOriginalInputQueryStep.Failed)
            {
                if (index != queries.Count - 1 || query.FailedAt is null || string.IsNullOrWhiteSpace(query.Failure) ||
                    selected != FalloutPlayerFollowingContinuationStep.ReadyItem || call.Failure != query.Failure)
                    throw new InvalidDataException("Failed original input query admitted later Ready Item services.");
            }
            else if (query.Step != FalloutOriginalInputQueryStep.Returned || query.FailedAt is not null || query.Failure is not null ||
                query.ControllerSelected != false)
                throw new InvalidDataException("Ready Item query lacks its actual returned source input result.");
        }
        var last = queries[^1];
        if (last.Step == FalloutOriginalInputQueryStep.Failed) return;
        var expected = last.Count > 0
            ? last.Mode == 2 ? FalloutPlayerFollowingContinuationStep.ReadyItemTail : FalloutPlayerFollowingContinuationStep.ReadyItemPositive
            : queries.Count == 3 ? FalloutPlayerFollowingContinuationStep.ReadyItemGlobals : FalloutPlayerFollowingContinuationStep.ReadyItem;
        if (selected != expected || expected == FalloutPlayerFollowingContinuationStep.ReadyItem)
            throw new InvalidDataException("Ready Item skipped a zero-count query or selected the wrong following service.");
    }
}
