using Godot;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Campaigns.NewVegas.Opening;

internal partial class OpeningQuestRuntime
{
    private bool HandleHitscanHit(GamebryoHitscanHit hit)
    {
        foreach (var targetSet in _flow.HitTargetSets)
        {
            var target = targetSet.Targets.SingleOrDefault(value =>
            {
                var placed = FindPlacedReference(value.ReferenceFormId);
                return placed is not null && MatchesTarget(hit.Collider, placed);
            });
            if (target is null)
                continue;
            if (!_quests.TryGetValue(targetSet.QuestFormId, out var quest) ||
                !quest.Running || quest.Stopped ||
                _destroyedReferences.Contains(target.ReferenceFormId) ||
                hit.WeaponAnimationType is not { } animationType ||
                animationType <= targetSet.WeaponAnimationTypeMinimumExclusive ||
                animationType >= targetSet.WeaponAnimationTypeMaximumExclusive ||
                hit.WeaponFormId.Equals(
                    targetSet.ExcludedWeaponFormId,
                    StringComparison.OrdinalIgnoreCase))
                return false;

            _destroyedReferences.Add(target.ReferenceFormId);
            GamebryoReferenceEnableRuntime.Apply(
                FindPlacedReference(target.ReferenceFormId) ??
                throw new InvalidOperationException(
                    "Owned hit target is absent from the loaded world."),
                false);
            var counterKey = QuestVariableKey(
                targetSet.QuestFormId,
                targetSet.QuestVariableName);
            var count = checked((int)_questVariables.GetValueOrDefault(counterKey) + 1);
            _questVariables[counterKey] = count;
            var tutorial = _quests.GetValueOrDefault(targetSet.TutorialQuestFormId)
                ?? throw new InvalidOperationException(
                    "Owned hit-target tutorial quest is absent.");
            ApplyQuestStage(
                tutorial.FormId,
                tutorial.EditorId,
                targetSet.TutorialStage,
                true);
            if (count >= targetSet.Threshold)
                CompleteHitTargetSet(targetSet);
            PlayHitReaction(targetSet);
            _loaded.Session.StoreOpeningState(CaptureState(true));
            return true;
        }
        return HandleCombatActorHit(hit);
    }

    private Node3D? FindPlacedReference(string referenceFormId) =>
        _loaded.MainContent.PlacedReferences
            .Concat(_loaded.LinkedCells.SelectMany(value => value.Content.PlacedReferences))
            .SingleOrDefault(value => value.FormId.Equals(
                referenceFormId,
                StringComparison.OrdinalIgnoreCase))
            ?.Placement;

    private void CompleteHitTargetSet(OpeningHitTargetSet targetSet)
    {
        var quest = _flow.OrdinaryQuests[targetSet.QuestFormId];
        var text = quest.Objectives[targetSet.ObjectiveIndex];
        _objectives[ObjectiveKey(targetSet.QuestFormId, targetSet.ObjectiveIndex)] =
            new OpeningObjectiveState(
                targetSet.QuestFormId,
                quest.EditorId,
                targetSet.ObjectiveIndex,
                "completed",
                true,
                text);
        if (_objective.Text == text)
            _objective.Visible = false;
        var speaker = _loaded.Actors.Single(value => value.ReferenceFormId.Equals(
            targetSet.SpeakerReferenceFormId,
            StringComparison.OrdinalIgnoreCase));
        speaker.Placement.SetMeta("opennv_actor_intent", "evp");
        speaker.Placement.SetMeta("opennv_actor_intent_source", targetSet.ScriptEditorId);
        EvaluateOrdinaryActorPackages();
    }

    private void PlayHitReaction(OpeningHitTargetSet targetSet)
    {
        if (_activeModal is not null || _dialogueVoice.Playing)
            return;
        _generation++;
        var generation = _generation;
        PlayTopicForm(
            targetSet.ReactionTopicFormId,
            () =>
            {
                if (generation != _generation)
                    return;
                _loaded.Session.StoreOpeningState(CaptureState(true));
            },
            generation);
    }
}
