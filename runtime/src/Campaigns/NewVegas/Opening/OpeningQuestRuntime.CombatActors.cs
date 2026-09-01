using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;
using OpenNV.Runtime.World.Interactions;

namespace OpenNV.Runtime.Campaigns.NewVegas.Opening;

internal partial class OpeningQuestRuntime
{
    private readonly Dictionary<string, GamebryoCreatureAnimationPlayback>
        _combatActorAnimations = new(StringComparer.OrdinalIgnoreCase);

    private void InitializeCombatActors()
    {
        foreach (var target in _flow.CombatEncounters.SelectMany(value => value.Targets))
        {
            var actor = CombatActor(target);
            if (!_combatHealthByReferenceFormId.TryAdd(
                    target.ReferenceFormId, target.MaximumHealth))
                throw new InvalidOperationException(
                    "Owned combat actor is duplicated across encounters.");
            actor.Placement.SetMeta(
                "opennv_ai_package_form_ids",
                string.Join(",", target.PackageFormIds));
            actor.Placement.SetMeta("opennv_attack_damage", target.AttackDamage);
            actor.Placement.SetMeta("opennv_maximum_health", target.MaximumHealth);
            _combatActorAnimations.Add(
                target.ReferenceFormId,
                GamebryoCreatureAnimationPlayback.Start(actor.Actor));
        }
        PublishCombatActors();
    }

    private bool HandleCombatActorHit(GamebryoHitscanHit hit)
    {
        foreach (var encounter in _flow.CombatEncounters)
            foreach (var target in encounter.Targets)
            {
                var actor = CombatActor(target);
                if (!MatchesTarget(hit.Collider, actor.Placement))
                    continue;
                if (_referenceEnabledStates.TryGetValue(
                        target.ReferenceFormId, out var enabled) && !enabled)
                    return false;
                if (_equippedWeaponState is not { AmmoFormId: { } ammunitionFormId } weapon ||
                    !weapon.WeaponFormId.Equals(
                        hit.WeaponFormId, StringComparison.OrdinalIgnoreCase))
                    return false;
                var currentHealth = _combatHealthByReferenceFormId[target.ReferenceFormId];
                var outcome = GamebryoRangedCombat.ApplyHit(
                    new GamebryoRangedAttack(
                        weapon.WeaponFormId,
                        ammunitionFormId,
                        weapon.Damage),
                    hit.WeaponFormId,
                    new GamebryoCombatantState(
                        target.ReferenceFormId,
                        target.MaximumHealth,
                        currentHealth,
                        currentHealth == 0));
                _combatHealthByReferenceFormId[target.ReferenceFormId] =
                    outcome.Target.CurrentHealth;
                _combatActorAnimations[target.ReferenceFormId].Play(
                    GamebryoCreatureAnimationPlayback.HitRole);
                PublishCombatActor(target);
                if (outcome.Died)
                    ExecuteCombatDeath(encounter, target);
                _loaded.Session.StoreOpeningState(CaptureState(true));
                return true;
            }
        return false;
    }

    private void ExecuteCombatDeath(
        OpeningCombatEncounter encounter,
        OpeningCombatTarget target)
    {
        var quest = _quests.GetValueOrDefault(encounter.QuestFormId) ??
            throw new InvalidOperationException(
                "Owned combat death quest is absent from runtime state.");
        var counterKey = QuestVariableKey(
            encounter.QuestFormId,
            encounter.QuestVariableName);
        var count = checked((int)_questVariables.GetValueOrDefault(counterKey));
        var objective = _objectives.GetValueOrDefault(
            ObjectiveKey(encounter.QuestFormId, encounter.ObjectiveIndex));
        var displayed = objective is { Enabled: true };
        var outcome = GamebryoDeathCounter.Advance(
            new GamebryoDeathCounterContract(
                encounter.CounterIncrement,
                encounter.Threshold,
                encounter.MinimumCombatStage,
                encounter.CompletionStage),
            new GamebryoDeathCounterState(
                count,
                quest.Stage,
                displayed,
                objective is { Enabled: true, State: "completed" }));
        _questVariables[counterKey] = outcome.Count;
        foreach (var stage in outcome.Stages)
        {
            ApplyQuestStage(
                encounter.QuestFormId,
                quest.EditorId,
                stage,
                true);
        }
        if (outcome.ResetAi)
        {
            var resetActor = _loaded.Actors.Single(value =>
                value.ReferenceFormId.Equals(
                    encounter.ResetActorReferenceFormId,
                    StringComparison.OrdinalIgnoreCase));
            resetActor.Placement.SetMeta("opennv_actor_intent", "resetai");
            resetActor.Placement.SetMeta(
                "opennv_actor_intent_source",
                encounter.DeathScriptEditorId);
            EvaluateOrdinaryActorPackages();
        }
        CombatActor(target).Placement.SetMeta(
            "opennv_death_script_form_id",
            encounter.DeathScriptFormId);
        _combatActorAnimations[target.ReferenceFormId].Stop();
        CombatActor(target).Placement.SetMeta(
            "opennv_death_presentation",
            "source-havok-ragdoll-not-kf");
    }

    private void UpdateCombatActorAnimations(double delta)
    {
        foreach (var animation in _combatActorAnimations.Values)
            animation.Advance(delta);
    }

    private void PublishCombatActors()
    {
        foreach (var target in _flow.CombatEncounters.SelectMany(value => value.Targets))
            PublishCombatActor(target);
    }

    private void PublishCombatActor(OpeningCombatTarget target)
    {
        var health = _combatHealthByReferenceFormId[target.ReferenceFormId];
        var actor = CombatActor(target);
        actor.Placement.SetMeta("opennv_current_health", health);
        actor.Placement.SetMeta("opennv_dead", health == 0);
        if (health == 0)
            _combatActorAnimations[target.ReferenceFormId].Stop();
    }

    private CellActorLoader.PlacedActor CombatActor(OpeningCombatTarget target)
    {
        var matches = _loaded.Actors.Where(value =>
            value.ReferenceFormId.Equals(
                target.ReferenceFormId, StringComparison.OrdinalIgnoreCase) &&
            value.BaseFormId.Equals(
                target.BaseFormId, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length != 1)
            throw new InvalidOperationException(
                "Owned combat actor identity is absent or ambiguous.");
        return matches[0];
    }
}
