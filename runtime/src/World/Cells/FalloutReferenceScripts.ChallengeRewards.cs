using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceScripts
{
    private readonly HashSet<FalloutChallengeRewardInvocation> _challengeRewards = [];
    internal bool OwnsChallengeCampaign(FalloutPluginStack source, FalloutChallenges owner) =>
        ReferenceEquals(records, source) && world.CampaignChallengesConfigured &&
        ReferenceEquals(world.Challenges, owner) && ReferenceEquals(host.Challenges, owner);
    internal void BindCampaignChallengeRewards()
    {
        if (!world.CampaignChallengesConfigured || world.Challenges.Source is null) return;
        world.Challenges.BindRewardExecutor(this);
    }

    internal void ExecuteChallengeReward(FalloutChallengeRewardInvocation reward)
    {
        if (!OwnsChallengeCampaign(records, reward.Owner) || !_challengeRewards.Add(reward))
            throw new InvalidOperationException("Immediate challenge executor lacks its current campaign completion owner.");
        try
        {
            reward.Begin(records);
            var program = reward.Program;
            if (program.CodeBytes == 0) { reward.AuthoredEmpty(); return; }
            if (program.Events.Count == 0)
                throw new NotSupportedException("Nonempty immediate challenge SCDA has no owned event extent.");
            for (var ordinal = 0; ordinal < program.Events.Count; ++ordinal)
            {
                var cursor = reward.EnterEvent(ordinal);
                var block = program.Events[ordinal];
                FalloutScriptManualSaveRequests.Entered? entered = null;
                Exception? retainedFailure = null;
                try
                {
                    if (!world.RequireChallengeImmediateGameMode()) { reward.Filtered(); continue; }
                    foreach (var _ in CompiledSteps(reward.Target, program, block, 0,
                        observeInvocation: actual => { entered = actual; reward.ObserveEntered(actual); },
                        cursor: cursor, canContinue: () => true, localAuthority: reward)) { }
                    if (entered is null || !cursor.State.Completed)
                        throw new InvalidOperationException("Immediate challenge event has no completed actual shared invocation.");
                    reward.Retired(world.ScriptManualSaves, Receipt("completed", null));
                }
                catch (Exception failure)
                {
                    retainedFailure = failure;
                    var receipt = Receipt(entered is null ? "admission-refusal" : "closed-failure", failure.Message);
                    // Retire validates the exact VM lease, not a caller's claim
                    // that a matching-looking script or event has completed.
                    try { reward.Retired(world.ScriptManualSaves, receipt); }
                    catch (Exception retirement)
                    {
                        retainedFailure = new AggregateException(failure, retirement); throw retainedFailure;
                    }
                    throw new FalloutCompiledInvocationFailure(receipt, failure);
                }
                finally { reward.ExitEvent(retainedFailure); }

                FalloutCompiledSliceReceipt Receipt(string disposition, string? failure) =>
                    new(reward.Target, program.Source.FormKey, program.Scope.RecordSha256, program.Scope.ScopeSha256,
                        program.ProgramSha256, FalloutCompiledSliceReceipt.EventScope(program, ordinal), ordinal,
                        block.Event, block.Begin, block.End, entered?.Session ?? Guid.Empty, entered?.Invocation ?? 0,
                        0, cursor.State with { Branches = cursor.State.Branches.ToArray() }, disposition, failure);
            }
            reward.Completed();
        }
        finally { _challengeRewards.Remove(reward); }
    }
}
