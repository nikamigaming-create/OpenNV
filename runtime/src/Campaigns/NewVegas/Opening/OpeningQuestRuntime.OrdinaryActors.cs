using Godot;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Campaigns.NewVegas.Opening;

internal partial class OpeningQuestRuntime
{
    private readonly Dictionary<string, OpeningGuidePackage> _ordinaryActorPackages =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, GamebryoPackageTravel> _ordinaryActorTravel =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ActorAnimationPlayback> _ordinaryActorLocomotion =
        new(StringComparer.OrdinalIgnoreCase);

    private void EvaluateOrdinaryActorPackages()
    {
        foreach (var actor in _flow.OrdinaryActors)
        {
            var placed = _loaded.Actors.Single(value => value.ReferenceFormId.Equals(
                actor.ReferenceFormId, StringComparison.OrdinalIgnoreCase));
            var candidates = actor.PackagePriority.Select(formId => actor.Packages[formId])
                .Select(package => new GamebryoPackageCandidate<OpeningGuidePackage>(
                    package.FormId,
                    package.Conditions.Select(PackageCondition).ToArray(),
                    OrdinaryPackageTarget(package, placed),
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
                    PackageQuestVariables())
                {
                    CompletedObjectives = _objectives.Values
                        .Where(value => value.State.Equals(
                            "completed", StringComparison.Ordinal))
                        .Select(value => GamebryoPackageSelector.ObjectiveKey(
                            value.QuestFormId, checked((uint)value.Index)))
                        .ToHashSet(StringComparer.OrdinalIgnoreCase),
                },
                requireMatch: false);
            if (selected is null)
            {
                _ordinaryActorPackages.Remove(actor.ReferenceFormId);
                continue;
            }
            var changed = !_ordinaryActorPackages.TryGetValue(
                actor.ReferenceFormId, out var previous) ||
                !previous.FormId.Equals(selected.FormId, StringComparison.OrdinalIgnoreCase);
            _ordinaryActorPackages[actor.ReferenceFormId] = selected.Value;
            placed.Placement.SetMeta("opennv_package_form_id", selected.FormId);
            placed.Placement.SetMeta("opennv_package_target_form_id",
                selected.Target.ReferenceFormId ?? "");
            placed.Placement.SetMeta("opennv_package_target_kind", selected.Target.Kind);
            if (changed && selected.Target.Placement is not null)
                BeginOrdinaryActorTravel(actor, placed, selected);
        }
    }

    private IReadOnlyDictionary<string, double> PackageQuestVariables()
    {
        var values = _questVariables.ToDictionary(
            value => value.Key,
            value => (double)value.Value,
            StringComparer.OrdinalIgnoreCase);
        foreach (var quest in _flow.OrdinaryQuests.Values)
            foreach (var variable in quest.Variables)
            {
                var named = QuestVariableKey(quest.FormId, variable.Value);
                if (_questVariables.TryGetValue(named, out var value))
                    values[GamebryoPackageSelector.VariableKey(
                        quest.FormId, variable.Key)] = value;
            }
        return values;
    }

    private GamebryoPackageTarget OrdinaryPackageTarget(
        OpeningGuidePackage package,
        CellActorLoader.PlacedActor actor)
    {
        if (package.Target is { TypeName: "reference" } target)
            return new GamebryoPackageTarget(
                "actorReference", target.FormId, null);
        if (package.Location is not
            {
                TypeName: "nearReference",
                Reference: { } reference,
            })
            return PackageTargetWithoutPlacement(package);
        return new GamebryoPackageTarget(
            package.Location.TypeName,
            reference.FormId,
            GamebryoPackagePlacement.FromCellReference(
                package.Location.TypeName,
                reference.FormId,
                _loaded.GameToCellUnits(reference.PositionGameUnits),
                reference.RotationGodot,
                actor.Placement.Scale));
    }

    private void BeginOrdinaryActorTravel(
        OpeningOrdinaryActor actor,
        CellActorLoader.PlacedActor placed,
        GamebryoPackageCandidate<OpeningGuidePackage> selected)
    {
        var target = selected.Target.Placement ?? throw new InvalidOperationException(
            "Owned ordinary package travel has no source placement.");
        var grounded = GameplayActorGrounding.ApplyGroundOffset(
            placed, target.SourceTransform.Origin);
        target = new SourcePackagePlacement(
            target.Kind,
            target.TargetFormId,
            GamebryoPackagePlacement.AdjustSupportHeight(
                target.SourceTransform,
                grounded.Y - target.SourceTransform.Origin.Y));
        var path = _loaded.MainContent.Navigation.FindPath(
                _loaded.CellToGameUnits(placed.Placement.Position),
                _loaded.CellToGameUnits(target.SourceTransform.Origin))
            .Select(_loaded.GameToCellUnits)
            .Select(position => GameplayActorGrounding.ApplyGroundOffset(placed, position))
            .ToArray();
        if (path.Length == 0)
            throw new InvalidOperationException(
                "Owned ordinary package navigation returned no waypoints.");
        var clip = selected.Value.AlwaysRun
            ? _flow.GuideActorAi.Locomotion.Run
            : _flow.GuideActorAi.Locomotion.Walk;
        _ordinaryActorTravel[actor.ReferenceFormId] = GamebryoPackageTravel.Start(
            selected.FormId,
            target,
            placed.Placement.Transform,
            path,
            clip.RootMotion.SpeedGameUnitsPerSecond,
            GamebryoPackageTravel.ExactArrivalToleranceCellUnits);
        _ordinaryActorLocomotion[actor.ReferenceFormId] = ActorAnimationPlayback.Start(
            placed.Actor,
            new SourceActorAnimation(
                clip.LogicalPath,
                clip.Sha256,
                clip.RootMotion.SequenceName,
                clip.RootMotion.StartSeconds,
                clip.RootMotion.StopSeconds,
                clip.RootMotion.CycleType,
                ZeroedAccumulationRootTranslation));
    }

    private void UpdateOrdinaryActorTravel(double delta)
    {
        foreach (var actor in _flow.OrdinaryActors)
        {
            if (!_ordinaryActorTravel.TryGetValue(actor.ReferenceFormId, out var travel))
                continue;
            var placed = _loaded.Actors.Single(value => value.ReferenceFormId.Equals(
                actor.ReferenceFormId, StringComparison.OrdinalIgnoreCase));
            _ordinaryActorLocomotion[actor.ReferenceFormId].Advance(delta);
            var arrived = travel.Advance(delta);
            travel.Publish(placed.Placement);
            _loaded.Session.StoreOpeningState(CaptureState(true));
            if (!arrived)
                continue;
            _ordinaryActorLocomotion[actor.ReferenceFormId].Stop();
            _ordinaryActorLocomotion.Remove(actor.ReferenceFormId);
            _ordinaryActorTravel.Remove(actor.ReferenceFormId);
            var transition = actor.ArrivalTransitions.SingleOrDefault(value =>
                value.PackageFormId.Equals(
                    _ordinaryActorPackages[actor.ReferenceFormId].FormId,
                    StringComparison.OrdinalIgnoreCase) &&
                _quests[value.QuestFormId].Stage == value.FromStage);
            if (transition is null)
                continue;
            ApplyQuestStage(
                transition.QuestFormId,
                _flow.OrdinaryQuests[transition.QuestFormId].EditorId,
                transition.ToStage,
                running: true);
            _loaded.Session.StoreOpeningState(CaptureState(true));
            EvaluateOrdinaryActorPackages();
        }
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
