using Godot;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Campaigns.NewVegas.Opening;

internal partial class OpeningQuestRuntime
{
    private const float BoundsToHalfExtents = 0.5f;

    private readonly Dictionary<string, OpeningGuidePackage> _ordinaryActorPackages =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, GamebryoPackageTravel> _ordinaryActorTravel =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ActorAnimationPlayback> _ordinaryActorLocomotion =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _completedOrdinaryPackages =
        new(StringComparer.OrdinalIgnoreCase);
    private bool _ordinaryAutomaticDialogueActive;

    private void EvaluateOrdinaryActorPackages()
    {
        foreach (var actor in _flow.OrdinaryActors)
        {
            var placed = _loaded.Actors.Single(value => value.ReferenceFormId.Equals(
                actor.ReferenceFormId, StringComparison.OrdinalIgnoreCase));
            var candidates = actor.PackagePriority
                .Where(formId => !_completedOrdinaryPackages.Contains(formId))
                .Select(formId => actor.Packages[formId])
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
            placed.Placement.SetMeta(
                "opennv_package_dialogue_target_form_id",
                selected.Value.Target?.FormId ?? "");
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
        if (package.Location is not
            {
                TypeName: "nearReference",
                Reference: { } reference,
            })
        {
            if (package.Target is { TypeName: "reference" } target)
                return new GamebryoPackageTarget(
                    "actorReference", target.FormId, null);
            return PackageTargetWithoutPlacement(package);
        }
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
        var reference = selected.Value.Location?.Reference ??
            throw new InvalidOperationException(
                "Owned ordinary package travel has no source reference.");
        var sourceContent = ContentOwningActor(placed);
        var targetContent = ContentOwningReference(reference);
        placed.Placement.Reparent(targetContent.Root, true);
        var target = GamebryoPackagePlacement.FromCellReference(
            selected.Target.Kind,
            reference.FormId,
            targetContent.Root.ToLocal(targetContent.GameToWorld(
                reference.PositionGameUnits)),
            reference.RotationGodot,
            placed.Placement.Scale);
        var grounded = GameplayActorGrounding.ApplyGroundOffset(
            placed, target.SourceTransform.Origin);
        target = new SourcePackagePlacement(
            target.Kind,
            target.TargetFormId,
            GamebryoPackagePlacement.AdjustSupportHeight(
                target.SourceTransform,
                grounded.Y - target.SourceTransform.Origin.Y));
        var path = OrdinaryActorTravelPath(
            placed,
            sourceContent,
            targetContent,
            target.SourceTransform.Origin);
        if (path.Length == 0 && placed.Placement.Position.DistanceTo(
                target.SourceTransform.Origin) <=
            GamebryoPackageTravel.ExactArrivalToleranceCellUnits)
            path = [target.SourceTransform.Origin];
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

    private Vector3[] OrdinaryActorTravelPath(
        CellActorLoader.PlacedActor placed,
        CellContentLoader.LoadedContent source,
        CellContentLoader.LoadedContent target,
        Vector3 targetPosition)
    {
        IEnumerable<Vector3> WorldPath(
            CellContentLoader.LoadedContent content,
            Vector3 from,
            Vector3 to) => content.Navigation.FindPath(
                content.WorldToGame(from),
                content.WorldToGame(to)).Select(content.GameToWorld);

        var targetWorld = target.Root.ToGlobal(targetPosition);
        IEnumerable<Vector3> worldPath;
        if (source.FormId.Equals(target.FormId, StringComparison.OrdinalIgnoreCase))
            worldPath = WorldPath(source, placed.Placement.GlobalPosition, targetWorld);
        else
        {
            var links = _loaded.PortalLinks.Where(link =>
                    link.FromCellFormId.Equals(source.FormId, StringComparison.OrdinalIgnoreCase) &&
                    link.ToCellFormId.Equals(target.FormId, StringComparison.OrdinalIgnoreCase) ||
                    link.ToCellFormId.Equals(source.FormId, StringComparison.OrdinalIgnoreCase) &&
                    link.FromCellFormId.Equals(target.FormId, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (links.Length != 1)
                throw new InvalidOperationException(
                    "Owned ordinary package cross-CELL path is absent or ambiguous.");
            var link = links[0];
            var sourceFrame = link.FromCellFormId.Equals(
                    source.FormId, StringComparison.OrdinalIgnoreCase)
                ? link.FromFrame
                : link.ToFrame;
            var targetFrame = link.FromCellFormId.Equals(
                    target.FormId, StringComparison.OrdinalIgnoreCase)
                ? link.FromFrame
                : link.ToFrame;
            var sourcePortal = (sourceFrame.From + sourceFrame.To) * BoundsToHalfExtents;
            var targetPortal = (targetFrame.From + targetFrame.To) * BoundsToHalfExtents;
            worldPath = WorldPath(source, placed.Placement.GlobalPosition, sourcePortal)
                .Concat(WorldPath(target, targetPortal, targetWorld));
        }
        return worldPath
            .Select(world => target.Root.ToLocal(world))
            .Select(position => GameplayActorGrounding.ApplyGroundOffset(placed, position))
            .ToArray();
    }

    private CellContentLoader.LoadedContent ContentOwningActor(
        CellActorLoader.PlacedActor actor) => AllLoadedContent().Single(content =>
            ReferenceEquals(actor.Placement, content.Root) ||
            content.Root.IsAncestorOf(actor.Placement));

    private CellContentLoader.LoadedContent ContentOwningReference(
        OpeningGuideReference reference)
    {
        if (reference.CellFormId is null)
            throw new InvalidOperationException(
                "Owned package reference has no source CELL identity: " +
                reference.FormId);
        return AllLoadedContent().Single(content =>
            content.SourceCellFormIds.Contains(reference.CellFormId));
    }

    private IEnumerable<CellContentLoader.LoadedContent> AllLoadedContent() =>
        new[] { _loaded.MainContent }
            .Concat(_loaded.LinkedCells.Select(value => value.Content));

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
            var completedPackage = _ordinaryActorPackages[actor.ReferenceFormId];
            if (completedPackage.EventCommands.TryGetValue(
                    "end", out var endCommands) && endCommands.Count > 0)
            {
                ExecuteOrdinaryCommands(endCommands);
                _loaded.Session.StoreOpeningState(CaptureState(true));
                EvaluateOrdinaryActorPackages();
                continue;
            }
            var packageDialogue = actor.AutomaticPackageDialogues.SingleOrDefault(value =>
                value.PackageFormId.Equals(
                    completedPackage.FormId,
                    StringComparison.OrdinalIgnoreCase));
            if (packageDialogue is not null)
            {
                BeginAutomaticPackageDialogue(actor, packageDialogue);
                continue;
            }
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

    private void BeginAutomaticPackageDialogue(
        OpeningOrdinaryActor actor,
        OpeningOrdinaryPackageDialogue dialogue)
    {
        if (_ordinaryAutomaticDialogueActive || _activeModal is not null ||
            _dialogueVoice.Playing)
            throw new InvalidOperationException(
                "Owned automatic package dialogue overlapped active dialogue.");
        _ordinaryAutomaticDialogueActive = true;
        _generation++;
        var generation = _generation;
        // A new package-owned GREETING is a new retail dialogue selection pass.
        // INFO say-once state persists, but an earlier conversation's ordered
        // cursor must not suppress newly eligible stage-conditioned INFO rows.
        _topicCursors.Remove(dialogue.GreetingTopicFormId);
        PlayTopicForm(
            dialogue.GreetingTopicFormId,
            () =>
            {
                if (generation != _generation)
                    return;
                _ordinaryAutomaticDialogueActive = false;
                _completedOrdinaryPackages.Add(dialogue.PackageFormId);
                _loaded.Session.StoreOpeningState(CaptureState(true));
                EvaluateOrdinaryActorPackages();
            },
            generation);
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
                    _completedOrdinaryPackages.Add(
                        _ordinaryActorPackages[actor.ReferenceFormId].FormId);
                    _loaded.Session.StoreOpeningState(CaptureState(true));
                    EvaluateOrdinaryActorPackages();
                },
                generation);
            return true;
        }
        return false;
    }

    private void EvaluateOrdinaryDialogueTriggers()
    {
        if (_ordinaryAutomaticDialogueActive || _activeModal is not null ||
            _dialogueVoice.Playing)
            return;
        foreach (var actor in _flow.OrdinaryActors)
            foreach (var trigger in actor.AutomaticDialogueTriggers)
            {
                if (!_quests.TryGetValue(trigger.QuestFormId, out var quest) ||
                    quest.Stopped ||
                    _objectives.TryGetValue(
                        ObjectiveKey(trigger.QuestFormId, trigger.ObjectiveIndex),
                        out var objective) && objective.Enabled ||
                    _ordinaryActorTravel.ContainsKey(actor.ReferenceFormId) ||
                    !_ordinaryActorPackages.ContainsKey(actor.ReferenceFormId))
                    continue;
                var placed = _loaded.Actors.Single(value => value.ReferenceFormId.Equals(
                    actor.ReferenceFormId, StringComparison.OrdinalIgnoreCase));
                var sourceTransform = new Transform3D(
                    new Basis(trigger.RotationGodot),
                    _loaded.GameToCellUnits(trigger.PositionGameUnits));
                var playerLocal = sourceTransform.AffineInverse() *
                    _loaded.Root.ToLocal(_loaded.Player.GlobalPosition);
                var actorLocal = sourceTransform.AffineInverse() *
                    _loaded.Root.ToLocal(placed.Placement.GlobalPosition);
                var halfBounds = new Vector3(
                    trigger.BoundsGameUnits.X,
                    trigger.BoundsGameUnits.Z,
                    trigger.BoundsGameUnits.Y) * BoundsToHalfExtents;
                if (Mathf.Abs(playerLocal.X) > halfBounds.X ||
                    Mathf.Abs(playerLocal.Y) > halfBounds.Y ||
                    Mathf.Abs(playerLocal.Z) > halfBounds.Z ||
                    Mathf.Abs(actorLocal.X) > halfBounds.X ||
                    Mathf.Abs(actorLocal.Y) > halfBounds.Y ||
                    Mathf.Abs(actorLocal.Z) > halfBounds.Z)
                    continue;
                _ordinaryAutomaticDialogueActive = true;
                _generation++;
                var generation = _generation;
                PlayTopicForm(
                    trigger.TopicFormId,
                    () =>
                    {
                        if (generation != _generation)
                            return;
                        _ordinaryAutomaticDialogueActive = false;
                        _loaded.Session.StoreOpeningState(CaptureState(true));
                        EvaluateOrdinaryActorPackages();
                    },
                    generation);
                return;
            }
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
