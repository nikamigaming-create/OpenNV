using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal static partial class FalloutNativeCampaignSave
{
    private static void ValidateCombatGroupContinuation(FalloutNativeCampaignState state)
    {
        var groups = state.CombatGroups ?? throw new InvalidDataException("Current campaign has no complete combat groups.");
        FalloutCombatGroups.Validate(groups);
        if (groups.OwnerFailure is not null || groups.Failures.Count != 0)
            throw new InvalidDataException("Campaign combat groups retain an unresolved actual source transition.");
        if (groups.Groups.Any(group => group.Members.Count != 0 || group.Targets.Count != 0))
            throw new NotSupportedException("Active campaign combat requires its source controller auxiliary/target-timer continuation.");
        var references = state.References?.ToDictionary(reference => reference.Reference, FalloutFormKeyComparer.Instance) ??
            throw new InvalidDataException("Combat groups have no retained reference owners.");
        foreach (var actor in groups.Actors.Where(actor => !actor.EnginePlayer))
            if (!references.TryGetValue(actor.Reference, out var reference) || reference.Base != actor.Base)
                throw new InvalidDataException("Combat group actor has no exact retained reference/base state.");
        var receipts = groups.CurrentTargets.ToDictionary(receipt => receipt.Actor, FalloutFormKeyComparer.Instance);
        foreach (var reference in references.Values)
        {
            if (reference.Engagement is { } actual && (!receipts.TryGetValue(reference.Reference, out var receipt) || receipt.Target != actual.Target))
                throw new InvalidDataException("Campaign actor engagement has no actual combat-group transition receipt.");
            if (receipts.TryGetValue(reference.Reference, out var observed) && observed.Target != reference.Engagement?.Target)
                throw new InvalidDataException("Campaign group target differs from its current retained actor engagement.");
        }
    }

    private static void ValidateCombatGroupSource(FalloutPluginStack records, FalloutNativeCampaignState state,
        FalloutExperienceHudDeclaration experience)
    {
        ValidateCombatGroupContinuation(state);
        var declaration = FalloutCombatGroupDeclaration.Read(experience);
        var snapshot = state.CombatGroups!;
        var live = records.OwnedSource ?? throw new InvalidDataException("Combat groups have no selected owned source.");
        if (snapshot.Contract != declaration.Contract || snapshot.StackIdentity != live.StackId || snapshot.Player != records.RuntimeFormKey(0x14))
            throw new InvalidDataException("Campaign combat groups differ from their exact executable/stack/player owner.");
        foreach (var actor in snapshot.Actors)
            if (FalloutCombatActorSource.Read(records, declaration, actor.Reference) != actor)
                throw new InvalidDataException("Campaign combat actor differs from its winning record/header/base identity.");
    }
}
