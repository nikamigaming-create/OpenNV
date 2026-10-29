using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    internal bool BlocksAmbientConversationRelationship(FalloutFormKey reference, FalloutFormKey target)
    {
        var actor = Actor(reference);
        var partner = Actor(target);
        if (actor.PlayerTeammate || partner.PlayerTeammate)
            throw new NotSupportedException("Ambient teammate conversation requires player-relative hostility admission.");
        if (PerkEntries(reference).Concat(PerkEntries(target)).Any(entry => entry.Entry is 14 or 15))
            throw new NotSupportedException("Ambient conversation requires its disposition/should-attack perk entry owner.");
        bool Hostile(FalloutReferenceInstance from, FalloutReferenceInstance to)
        {
            var threat = FalloutActorThreat.Read(records, from.Base, from.Templates);
            var aggression = ActorValue(from.Reference, "aggression");
            if (aggression != Math.Truncate(aggression) || aggression is < 0 or > 3)
                throw new InvalidDataException("Conversation actor aggression is invalid.");
            return (threat with { Aggression = (byte)aggression }).Hostile(ActorRelation(from.Reference, to.Reference));
        }
        return Hostile(actor, partner) || Hostile(partner, actor);
    }

    internal bool IsRecentConversationPartner(FalloutFormKey reference, FalloutFormKey target, double seconds)
    {
        var actor = Actor(reference);
        var history = (actor.ConversationHistory ?? FalloutConversationHistory.Create(records.NumericSettings))
            .Evaluate(records.NumericSettings, seconds);
        actor.ConversationHistory = history;
        return history.Partners.Contains(target);
    }

    internal void RememberConversationAttempt(FalloutFormKey reference, FalloutFormKey target)
    {
        var actor = Actor(reference);
        var partner = Actor(target);
        var sourceHistory = (actor.ConversationHistory ?? FalloutConversationHistory.Create(records.NumericSettings)).Remember(target);
        var targetHistory = (partner.ConversationHistory ?? FalloutConversationHistory.Create(records.NumericSettings)).Remember(reference);
        actor.ConversationHistory = sourceHistory;
        partner.ConversationHistory = targetHistory;
    }

    // These authoritative state gates precede process-specific admission.
    // Passing them is not permission to start a conversation: current action,
    // participant reservation, history and relationships have separate owners.
    internal bool BlocksAmbientConversation(FalloutFormKey reference)
    {
        var actor = Actor(reference);
        return !IsEnabled(reference) || actor.Deleted || actor.DeletePending ||
            actor.Injury?.Dead == true || actor.KnockedDown || GetSleeping(reference) != 0 ||
            IsInCombat(reference, PlayerInCombat);
    }

    internal bool BlocksAmbientConversationTarget(FalloutFormKey reference)
    {
        if (BlocksAmbientConversation(reference)) return true;
        if (GetSitting(reference) is not (0 or 3)) return true;
        if (CurrentPackage(reference) is not { } current) return false;
        var package = records.GetEffective(current);
        var data = FalloutPackageData.Read(package);
        return data.BlocksAmbientConversation || data.Procedure == 12 &&
            !FalloutSandboxActivities.Read(package).Allows(FalloutSandboxActivity.Dialogue);
    }
}
