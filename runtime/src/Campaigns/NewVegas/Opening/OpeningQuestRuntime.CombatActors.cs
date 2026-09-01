using Godot;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;
using OpenNV.Runtime.World.Interactions;

namespace OpenNV.Runtime.Campaigns.NewVegas.Opening;

internal partial class OpeningQuestRuntime
{
    private readonly Dictionary<string, GamebryoCreatureAnimationPlayback>
        _combatActorAnimations = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, GamebryoCreatureCombatAi> _combatActorAi =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CombatNavigationState> _combatNavigation =
        new(StringComparer.OrdinalIgnoreCase);

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
            var actorRadiusGameUnits = Math.Max(
                actor.Actor.Bounds.Size.X,
                actor.Actor.Bounds.Size.Z) /
                _loaded.UnitsToMeters * BoundsToHalfExtents;
            var playerRadiusGameUnits =
                _configuration.Player.CapsuleRadiusMeters / _loaded.UnitsToMeters;
            var contract = GamebryoCreatureCombatAi.Contract(
                _combatActorAnimations[target.ReferenceFormId],
                actorRadiusGameUnits + playerRadiusGameUnits,
                target.AttackDamage);
            _combatActorAi.Add(
                target.ReferenceFormId,
                GamebryoCreatureCombatAi.Start(contract));
            actor.Placement.SetMeta(
                "opennv_melee_contact_range_game_units",
                contract.ContactRangeGameUnits);
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
                _combatActorAi[target.ReferenceFormId].Interrupt();
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
        _combatActorAi[target.ReferenceFormId].Kill();
        CombatActor(target).Placement.SetMeta(
            "opennv_death_presentation",
            "source-havok-ragdoll-not-kf");
    }

    private void UpdateCombatActors(double delta)
    {
        if (_loaded.Session.PlayerHitPoints is not > 0)
            return;
        foreach (var encounter in _flow.CombatEncounters)
        {
            var quest = _quests[encounter.QuestFormId];
            if (quest.Stage < encounter.MinimumCombatStage)
                continue;
            foreach (var target in encounter.Targets)
            {
                if (_combatHealthByReferenceFormId[target.ReferenceFormId] == 0 ||
                    _referenceEnabledStates.TryGetValue(
                        target.ReferenceFormId, out var enabled) && !enabled)
                    continue;
                var actor = CombatActor(target);
                if (_combatActorAnimations[target.ReferenceFormId].Role ==
                    GamebryoCreatureAnimationPlayback.HitRole)
                    continue;
                var content = AllLoadedContent().Single(value =>
                    ReferenceEquals(actor.Placement, value.Root) ||
                    value.Root.IsAncestorOf(actor.Placement));
                var playerCellPosition = content.Root.ToLocal(
                    _loaded.Player.GlobalPosition);
                var currentGamePosition = content.WorldToGame(
                    actor.Placement.GlobalPosition);
                var targetGamePosition = content.WorldToGame(
                    _loaded.Player.GlobalPosition);
                var horizontalDistance = new Vector2(
                    playerCellPosition.X - actor.Placement.Position.X,
                    playerCellPosition.Z - actor.Placement.Position.Z).Length();
                var contract = GamebryoCreatureCombatAi.Contract(
                    _combatActorAnimations[target.ReferenceFormId],
                    Math.Max(actor.Actor.Bounds.Size.X, actor.Actor.Bounds.Size.Z) /
                        _loaded.UnitsToMeters * BoundsToHalfExtents +
                        _configuration.Player.CapsuleRadiusMeters /
                        _loaded.UnitsToMeters,
                    target.AttackDamage);
                if (!_combatNavigation.TryGetValue(
                        target.ReferenceFormId, out var navigation))
                {
                    navigation = new CombatNavigationState(
                        content.Navigation.FindPath(
                                currentGamePosition,
                                targetGamePosition)
                            .Select(content.GameToWorld)
                            .Select(content.Root.ToLocal)
                            .ToArray(),
                        0);
                }
                while (navigation.Index < navigation.Path.Count &&
                    new Vector2(
                        navigation.Path[navigation.Index].X - actor.Placement.Position.X,
                        navigation.Path[navigation.Index].Z - actor.Placement.Position.Z)
                        .IsZeroApprox())
                    navigation = navigation with { Index = navigation.Index + 1 };
                if (navigation.Index >= navigation.Path.Count &&
                    horizontalDistance > contract.ContactRangeGameUnits)
                {
                    navigation = new CombatNavigationState(
                        content.Navigation.FindPath(
                                currentGamePosition,
                                targetGamePosition)
                            .Select(content.GameToWorld)
                            .Select(content.Root.ToLocal)
                            .ToArray(),
                        0);
                }
                _combatNavigation[target.ReferenceFormId] = navigation;
                if (navigation.Path.Count == 0)
                    continue;
                var nextWaypoint = navigation.Index < navigation.Path.Count
                    ? navigation.Path[navigation.Index]
                    : playerCellPosition;
                var ai = _combatActorAi[target.ReferenceFormId];
                var priorPhase = ai.State.Phase;
                var step = ai.Advance(
                    delta,
                    actor.Placement.Transform,
                    playerCellPosition,
                    nextWaypoint);
                actor.Placement.Transform = navigation.Index < navigation.Path.Count
                    ? GamebryoPackagePlacement.AtSupportHeight(
                        step.Transform,
                        navigation.Path[navigation.Index].Y)
                    : step.Transform;
                GamebryoActorCollision.Synchronize(actor.Placement);
                var animation = _combatActorAnimations[target.ReferenceFormId];
                if (step.BeganMelee)
                    animation.Play(GamebryoCreatureAnimationPlayback.MeleeRole);
                else if (step.BeganLocomotion &&
                    animation.Role != GamebryoCreatureAnimationPlayback.LocomotionRole)
                    animation.Play(GamebryoCreatureAnimationPlayback.LocomotionRole);
                else if (priorPhase == GamebryoCreatureCombatPhase.Melee &&
                    step.State.Phase == GamebryoCreatureCombatPhase.Chase)
                    animation.Play(GamebryoCreatureAnimationPlayback.LocomotionRole);
                if (step.Damage > 0 && GameplaySession.WorldLineIsClear(
                        actor.Placement,
                        GamebryoActorCollision.Center(actor.Placement),
                        _loaded.Player.GlobalPosition,
                        _loaded.Player.CollisionMask))
                {
                    var hitPoints = _loaded.Session.ApplySourceDamage(step.Damage);
                    actor.Placement.SetMeta("opennv_last_melee_damage", step.Damage);
                    actor.Placement.SetMeta("opennv_player_hit_points", hitPoints);
                    if (hitPoints == 0)
                    {
                        _loaded.Session.StoreOpeningState(CaptureState(true));
                        return;
                    }
                }
            }
        }
        foreach (var animation in _combatActorAnimations.Values)
            animation.Advance(delta);
        _loaded.Session.StoreOpeningState(CaptureState(true));
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

    private sealed record CombatNavigationState(
        IReadOnlyList<Vector3> Path,
        int Index);
}
