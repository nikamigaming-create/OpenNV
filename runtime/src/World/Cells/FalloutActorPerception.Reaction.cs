using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal sealed record FalloutPerceptionReactionInput(byte? Aggression, byte? TargetAggression, uint? Relation, bool? PlayerTeammate,
    bool? TargetIsPlayer, bool? TargetPlayerTeammate, bool? PlayerHostilePredicate,
    bool? SpecialTeammateBranch, string Owner);
internal sealed record FalloutPerceptionPerkBoolean(bool? Result, string Owner);

internal static class FalloutPerceptionReaction
{
    // Mode one is the original CombatManager member/target predicate. It is
    // not FalloutActorThreat.Initiates, and confidence is not this consumer.
    internal static FalloutCombatGroupPredicate GroupTarget(FalloutPerceptionReactionInput input,
        Func<bool, FalloutPerceptionPerkBoolean> entry15)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(input.Owner);
        var aggression = input.Aggression ?? throw new NotSupportedException("Reaction has no current aggression owner.");
        if (aggression > 3) throw new InvalidDataException("Source aggression is outside its byte domain.");
        // Both compilers return here before all reaction and entry-15 work.
        if (aggression == 3) return new(true, input.Owner + "/frenzied-direct-return");
        if (input.SpecialTeammateBranch is not { } special)
            return new(null, input.Owner + "/source-teammate-player-hostility-branch-unowned");
        if (special)
        {
            if (input.PlayerHostilePredicate is null)
                return new(null, input.Owner + "/source-player-hostility-predicate-unowned");
            // The full teammate arm also reads an independent derived actor
            // value before its return. A faction relation does not supply it.
            return new(null, input.Owner + "/source-teammate-derived-value-election-unowned");
        }
        var relation = input.Relation ?? throw new NotSupportedException("Reaction has no current directed faction owner.");
        if (relation > 3) throw new InvalidDataException("Reaction has an invalid directed faction result.");
        var targetAggression = input.TargetAggression ?? throw new NotSupportedException("Reaction has no target aggression owner.");
        if (targetAggression > 3) throw new InvalidDataException("Target aggression is outside its source byte domain.");
        var initial = targetAggression == 3 || relation is not (2 or 3);
        // Unlike the member Aggression=3 arm, ordinary false also reaches
        // entry 15. An active function can change it before publication.
        var modified = entry15(initial);
        if (string.IsNullOrWhiteSpace(modified.Owner)) throw new InvalidDataException("Reaction lost its entry-15 owner.");
        return new(modified.Result, input.Owner + "/" + modified.Owner);
    }
}
