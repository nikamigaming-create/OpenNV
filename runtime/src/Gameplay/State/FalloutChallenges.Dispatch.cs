using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed partial class FalloutChallenges
{
    private FalloutChallengeDispatch Dispatch(uint kind, int amount, ushort value1,
        FalloutStatisticMutation? statistic = null, FalloutFormKey? scripted = null)
    {
        if (_executing.Count >= 64) throw new NotSupportedException("Source challenge recursion exceeds the owned synchronous invocation bound.");
        var frame = new DispatchFrame(checked(++_events), kind, amount, value1, statistic, scripted, _bucketGeneration);
        if (_executing.TryPeek(out var parent)) parent.Children.Add(frame); else _last = frame;
        _executing.Push(frame);
        try
        {
            var rebuild = false;
            for (FalloutChallengeBuckets.Node? node = _buckets.Head(kind); node is not null; node = Next(node))
            {
                var candidate = Read(node);
                if (candidate is not { } form) continue;
                var definition = Definition(form); var state = _state[form];
                if (state.Error is not null) throw new InvalidOperationException("Source challenge retains a refused attempted completion: " + state.Error);
                if (state.Completed && !definition.Recurring || definition.StartDisabled && !state.Unlocked) continue;
                if (kind == 13 && scripted != form) continue;
                if (kind == 11)
                    foreach (var filter in new[] { definition.PrimaryFilter, definition.SecondaryFilter })
                        if (filter is { } sourceFilter) _ = _records.GetEffective(sourceFilter);
                // Event11 passes null forms and zero second/third words. Both
                // authored form filters are therefore genuine no-match arms.
                if (kind == 11 && (definition.PrimaryFilter is not null || definition.SecondaryFilter is not null ||
                    definition.Value1 != value1 || definition.Value2 != 0 || definition.Value3 != 0)) continue;
                var attempt = new AttemptFrame(new(form, definition.Sha256, state.Progress,
                    unchecked(state.Progress + amount), state.RuntimeFlags, FalloutChallengePrefix.ProgressCommitted));
                frame.Attempts.Add(attempt);
                _state[form] = state with { Progress = attempt.Value.After };
                try
                {
                    if (attempt.Value.After >= definition.Threshold) rebuild |= Complete(definition, attempt, frame);
                    else ProgressNotice(definition, attempt);
                    attempt.Value = attempt.Value with { Prefix = FalloutChallengePrefix.Complete };
                }
                catch (Exception failure)
                {
                    var failureType = failure.GetType().FullName ?? failure.GetType().Name;
                    attempt.Value = attempt.Value with { FailureType = failureType, Error = failure.Message };
                    _state[form] = _state[form] with { FailureType = failureType, Error = failure.Message };
                    throw;
                }
            }
            if (rebuild)
            {
                frame.RebuildEntered = true;
                RebuildBuckets();
            }
            frame.Complete = true;
            frame.BucketGenerationAfter = _bucketGeneration;
            return frame.Capture(_events);
        }
        catch (Exception failure)
        {
            frame.FailureType = failure.GetType().FullName ?? failure.GetType().Name;
            frame.Error = failure.Message; throw;
        }
        finally
        {
            frame.ThroughOrdinal = _events; frame.BucketGenerationAfter = _bucketGeneration;
            if (!ReferenceEquals(_executing.Pop(), frame))
                throw new InvalidOperationException("Challenge dispatch retirement lost its actual synchronous parent.");
        }

        FalloutFormKey? Read(FalloutChallengeBuckets.Node node)
        {
            try { var form = FalloutChallengeBuckets.Read(node); Observe(node, "read", form, null); return form; }
            catch (Exception error) { Observe(node, "read-refusal", null, error.Message); throw; }
        }
        FalloutChallengeBuckets.Node? Next(FalloutChallengeBuckets.Node node)
        {
            try { var next = FalloutChallengeBuckets.Next(node); Observe(node, "next", next?.Value, null); return next; }
            catch (Exception error) { Observe(node, "next-refusal", null, error.Message); throw; }
        }
        void Observe(FalloutChallengeBuckets.Node node, string operation, FalloutFormKey? form, string? error) =>
            frame.Traversal.Add(new(node.Event, node.Identity, node.StableHead, node.ConstructedGeneration,
                _bucketGeneration, operation, form, error));
    }
    private bool Complete(FalloutChallengeDefinition definition, AttemptFrame attempt, DispatchFrame frame)
    {
        var source = Source!; var form = definition.Record.FormKey;
        if (definition.Script is { } script)
        {
            attempt.Value = attempt.Value with { Prefix = FalloutChallengePrefix.RewardEntered };
            var invocation = new FalloutChallengeRewardInvocation(this, frame.Ordinal, form, _records.GetEffective(script));
            attempt.Reward = invocation;
            ++_rewardEntry;
            try
            {
                (_rewardExecutor ?? throw new NotSupportedException("source-challenge-immediate-compiled-executor-unbound"))
                    .ExecuteChallengeReward(invocation);
                invocation.RequireCompleted();
                attempt.Value = attempt.Value with { Prefix = FalloutChallengePrefix.RewardReturned };
            }
            catch (Exception failure) { invocation.Fail(failure); throw; }
            finally { --_rewardEntry; }
        }
        if (definition.Type != source.StatisticEvent || definition.Value1 != source.CompletionStatistic)
        {
            attempt.Value = attempt.Value with { Prefix = FalloutChallengePrefix.CompletionStatisticEntered };
            var statistics = _statistics ?? throw new NotSupportedException("source-challenge-completion-statistic-unbound");
            var ordinal = statistics.Operations;
            ++_counterEntry;
            try { statistics.Mod(source.CompletionStatistic, 1, "source-challenge-completion"); }
            finally
            {
                --_counterEntry;
                if (statistics.Operations > ordinal)
                {
                    var actual = statistics.RequireCommittedMutation(checked(ordinal + 1), source.CompletionStatistic,
                        1, "source-challenge-completion");
                    attempt.Value = attempt.Value with { CompletionStatisticOrdinal = actual.Ordinal };
                }
            }
            if (statistics.Operations <= ordinal)
                throw new InvalidDataException("Source challenge completion did not enter the real counter consumer.");
            attempt.Value = attempt.Value with { Prefix = FalloutChallengePrefix.CompletionStatisticReturned };
        }
        Notice(definition, definition.Threshold, completed: true, attempt);
        var current = _state[form];
        var recurring = definition.Recurring && (current.RuntimeFlags & 8u) == 0;
        _state[form] = recurring ? current with
        {
            Progress = unchecked(current.Progress - definition.Threshold),
            RuntimeFlags = current.RuntimeFlags | 4u
        } : current with { RuntimeFlags = current.RuntimeFlags | 2u };
        attempt.Value = attempt.Value with { Prefix = FalloutChallengePrefix.FlagsCommitted };
        return !recurring;
    }
    private void ProgressNotice(FalloutChallengeDefinition definition, AttemptFrame attempt)
    {
        var interval = unchecked((int)definition.Interval);
        if (interval == 0) interval = 100;
        if (attempt.Value.After < 0) return;
        if (interval < 0) throw new NotSupportedException("Source challenge signed interval requires its wrapping subtract-loop owner.");
        if (attempt.Value.After % interval == 0) Notice(definition, attempt.Value.After, completed: false, attempt);
    }
    private void Notice(FalloutChallengeDefinition definition, int count, bool completed, AttemptFrame attempt)
    {
        attempt.Value = attempt.Value with { Prefix = FalloutChallengePrefix.NoticeTestEntered };
        if (definition.Name.Length == 0 || definition.Description.Length == 0 || !Source!.ShowNotices)
        { attempt.Value = attempt.Value with { Prefix = FalloutChallengePrefix.NoticeSkipped }; return; }
        var before = _notifications.Capture().LastOrdinal;
        _notifications.Publish([new(completed ? FalloutHudEventKind.ChallengeCompleted : FalloutHudEventKind.ChallengeProgress,
            definition.Record.FormKey, count)]);
        var after = _notifications.Capture().LastOrdinal;
        if (after != checked(before + 1)) throw new InvalidDataException("Challenge notice lost its actual shared queue ordinal.");
        attempt.Value = attempt.Value with { Prefix = FalloutChallengePrefix.NoticeQueued, NoticeOrdinal = after };
        if (!completed) return;
        attempt.Value = attempt.Value with { Prefix = FalloutChallengePrefix.InterfaceCueEntered };
        PlayInterfaceCompletion(attempt);
    }
}
