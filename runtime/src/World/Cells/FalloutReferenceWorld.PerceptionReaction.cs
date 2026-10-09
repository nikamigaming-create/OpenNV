using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    private Func<FalloutFormKey, FalloutFormKey, bool, FalloutPerceptionPerkBoolean>? _perceptionPerk15;
    private Action<FalloutFormKey>? _perceptionPairPerkPreparation;

    internal void BindPerceptionPerkOwners(Action<FalloutFormKey>? pairPreparation,
        Func<FalloutFormKey, FalloutFormKey, bool, FalloutPerceptionPerkBoolean>? entry15)
    {
        if (_perceptionPairPerkPreparation is not null || _perceptionPerk15 is not null)
            throw new InvalidOperationException("Perception perk consumers already have a living owner.");
        _perceptionPairPerkPreparation = pairPreparation; _perceptionPerk15 = entry15;
    }
    internal void PreparePerceptionPairPerks(FalloutFormKey actor)
    {
        if (_perceptionDeclaration?.Scalar == FalloutDetectionScalarKind.NewVegas && actor != _enginePlayer && !Actor(actor).PlayerTeammate)
        {
            // The original ordinary-NPC virtual getter returns null at both
            // pair preparation entry points, independently of source PRKR.
            return;
        }
        (_perceptionPairPerkPreparation ?? throw new NotSupportedException("Selected player/teammate pair preparation has no original perk owner."))(actor);
    }
    internal FalloutCombatGroupPredicate ReadPerceptionReaction(FalloutFormKey member, FalloutFormKey target)
    {
        if (member == target) return new(false, "source-same-actor-reaction");
        if (member == _enginePlayer) return new(null, "source-player-directed-reaction-owner-unowned");
        var instance = Actor(member);
        var aggression = ActorValue(member, "aggression");
        if (!float.IsFinite(aggression) || aggression != MathF.Truncate(aggression) || aggression is < 0 or > 3)
            throw new InvalidDataException("Current aggression does not fit the original reaction byte.");
        // Do not require a later unowned input before an original direct arm.
        if (aggression == 3) return new(true, "source-current-aggression-frenzied-direct-return");
        var teammate = instance.PlayerTeammate;
        var targetTeammate = target != _enginePlayer && Actor(target).PlayerTeammate;
        var special = teammate || target == _enginePlayer || targetTeammate ? (bool?)null : false;
        byte? targetAggression = null;
        if (target != _enginePlayer)
        {
            var current = ActorValue(target, "aggression");
            if (!float.IsFinite(current) || current != MathF.Truncate(current) || current is < 0 or > 3)
                throw new InvalidDataException("Target aggression does not fit its original reaction byte.");
            targetAggression = (byte)current;
        }
        return FalloutPerceptionReaction.GroupTarget(new((byte)aggression, targetAggression, ActorRelation(member, target), teammate,
            target == _enginePlayer, targetTeammate, null, special, "source-winning-current-actor-faction-reaction"),
            initial => _perceptionPerk15?.Invoke(member, target, initial) ??
                new(null, "source-reaction-perk-entry15-function-and-parameter-consumer-unowned"));
    }
}
