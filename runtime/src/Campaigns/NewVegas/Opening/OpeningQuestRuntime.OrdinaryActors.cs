using Godot;
using OpenNV.Runtime.World.Actors;

namespace OpenNV.Runtime.Campaigns.NewVegas.Opening;

internal partial class OpeningQuestRuntime
{
    private readonly Dictionary<string, OpeningGuidePackage> _ordinaryActorPackages =
        new(StringComparer.OrdinalIgnoreCase);

    private void EvaluateOrdinaryActorPackages()
    {
        foreach (var actor in _flow.OrdinaryActors)
        {
            var candidates = actor.PackagePriority.Select(formId => actor.Packages[formId])
                .Select(package => new GamebryoPackageCandidate<OpeningGuidePackage>(
                    package.FormId,
                    package.Conditions.Select(PackageCondition).ToArray(),
                    OrdinaryPackageTarget(package),
                    null,
                    package))
                .ToArray();
            var selected = GamebryoPackageSelector.SelectFirst(
                candidates,
                new GamebryoPackageState(
                    _quests.Values.ToDictionary(
                        quest => quest.FormId,
                        quest => quest.Stage,
                        StringComparer.OrdinalIgnoreCase),
                    _quests.Values.Where(quest => quest.Stopped)
                        .Select(quest => quest.FormId)
                        .ToHashSet(StringComparer.OrdinalIgnoreCase),
                    _questVariables.ToDictionary(
                        value => value.Key,
                        value => (double)value.Value,
                        StringComparer.OrdinalIgnoreCase)),
                requireMatch: false);
            if (selected is null)
            {
                _ordinaryActorPackages.Remove(actor.ReferenceFormId);
                continue;
            }
            _ordinaryActorPackages[actor.ReferenceFormId] = selected.Value;
            var placement = _loaded.Actors.Single(value =>
                value.ReferenceFormId.Equals(
                    actor.ReferenceFormId, StringComparison.OrdinalIgnoreCase)).Placement;
            placement.SetMeta("opennv_package_form_id", selected.FormId);
            placement.SetMeta("opennv_package_target_form_id",
                selected.Target.ReferenceFormId ?? "");
            placement.SetMeta("opennv_package_target_kind", selected.Target.Kind);
        }
    }

    private static GamebryoPackageTarget OrdinaryPackageTarget(
        OpeningGuidePackage package)
    {
        if (package.Target is { TypeName: "reference" } target)
            return new GamebryoPackageTarget(
                "actorReference", target.FormId, null);
        return PackageTargetWithoutPlacement(package);
    }

    private static GamebryoPackageTarget PackageTargetWithoutPlacement(
        OpeningGuidePackage package) => package.Location is null
            ? GamebryoPackageTarget.None
            : new GamebryoPackageTarget(
                package.Location.TypeName,
                package.Location.Reference?.FormId,
                null);

    private bool HandleOrdinaryActorActivation(Node? collider)
    {
        foreach (var actor in _flow.OrdinaryActors)
        {
            if (!_roleNodes.TryGetValue(actor.Role, out var target) ||
                !MatchesTarget(collider, target) &&
                _loaded.Player.GlobalPosition.DistanceTo(target.GlobalPosition) >
                    _configuration.Player.ActivationDistanceMeters)
                continue;
            var questFormIds = actor.Topics.Values
                .SelectMany(topic => topic.Infos)
                .SelectMany(info => info.Commands)
                .Where(command => command.Kind == "setStage" &&
                    command.QuestFormId is not null)
                .Select(command => command.QuestFormId!)
                .Where(_flow.OrdinaryQuests.ContainsKey)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (questFormIds.Length != 1 ||
                !_quests.TryGetValue(questFormIds[0], out var quest) ||
                quest.Stage != _flow.OrdinaryQuests[questFormIds[0]].EntryStage ||
                !_ordinaryActorPackages.ContainsKey(actor.ReferenceFormId))
                return false;
            var topic = actor.Topics[actor.ActivationTopicFormId];
            _generation++;
            var generation = _generation;
            ShowTopicChoices(
                [topic.FormId],
                () =>
                {
                    if (generation != _generation)
                        return;
                    _loaded.Session.StoreOpeningState(CaptureState(true));
                    EvaluateOrdinaryActorPackages();
                },
                generation);
            return true;
        }
        return false;
    }

    private void ApplyOrdinaryActorIntent(OpeningFlowCommand command)
    {
        if (command.ReferenceFormId is null || command.ReferenceEditorId is null ||
            command.Operation is null)
            throw new InvalidOperationException(
                "Owned ordinary actor intent command is incomplete.");
        var actor = _loaded.Actors.SingleOrDefault(value =>
            value.ReferenceFormId.Equals(
                command.ReferenceFormId, StringComparison.OrdinalIgnoreCase));
        if (actor == default)
            throw new InvalidOperationException(
                "Owned ordinary actor intent target is absent from the loaded world.");
        actor.Placement.SetMeta("opennv_actor_intent", command.Operation);
        actor.Placement.SetMeta("opennv_actor_intent_source", command.ReferenceEditorId);
    }
}
