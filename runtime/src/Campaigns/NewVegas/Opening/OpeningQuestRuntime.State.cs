using System.Buffers.Binary;
using System.Security.Cryptography;
using Godot;
using OpenNV.Runtime.Presentation.CharacterCreation;


using OpenNV.Runtime.Presentation.Ui;
using OpenNV.Runtime.World.Cells;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Actors;

namespace OpenNV.Runtime.Campaigns.NewVegas.Opening;

internal partial class OpeningQuestRuntime
{
    private void ExecuteInfoCommands(
        OpeningDialogueInfo info,
        OpeningDialogueTopic? topic,
        Action completed,
        int generation,
        int index)
    {
        if (generation != _generation)
            return;
        if (index < info.Commands.Count)
        {
            var commands = info.Commands.Select((command, sourceIndex) =>
                new SourceGamebryoResultCommand<OpeningFlowCommand>(
                    sourceIndex,
                    ResultCommandKind(command.Kind),
                    ResultCommandIsTerminal(command),
                    command)).ToArray();
            var execution = GamebryoResultCommandExecutor.Execute(
                commands,
                index,
                command => ApplyInfoResultCommand(
                    command.Value,
                    completed,
                    generation));
            if (execution.Terminal)
                return;
        }

        if (info.NextTopicFormIds.Count > 0)
        {
            ShowTopicChoices(
                info.NextTopicFormIds,
                topic is null
                    ? completed
                    : () => PlayTopic(topic, completed, generation),
                generation);
            return;
        }
        if (!info.Goodbye && topic is not null)
        {
            PlayTopic(topic, completed, generation);
            return;
        }
        CloseModal();
        completed();
    }

    private bool ApplyInfoResultCommand(
        OpeningFlowCommand command,
        Action completed,
        int generation)
    {
        switch (command.Kind)
        {
            case "actorValueDelta":
                ApplyActorValueDelta(command);
                break;
            case "setQuestVariable":
                ApplyQuestVariable(command);
                break;
            case "setDestroyed":
                ApplyDestroyedFromInfo(command);
                break;
            case "additem":
            case "removeitem":
            case "equipitem":
                ApplyInventoryCommand(command);
                break;
            case "playerControls":
                ApplyPlayerControls(command);
                break;
            case "addScriptPackage":
            case "removeScriptPackage":
                ApplyScriptPackage(command);
                break;
            case "imageSpaceModifier":
                ApplyImageSpaceModifier(command);
                break;
            case "referenceEnabled":
                ApplyReferenceEnabled(command);
                break;
            case "actorIntent":
                ApplyActorIntent(command);
                break;
            case "objective":
                ApplyObjective(command);
                break;
            case "startQuest":
            case "stopQuest":
                ApplyQuestLifecycle(command);
                break;
            case "setGlobal":
                ApplyGlobal(command);
                break;
            case "autoDisplayObjectives":
                ApplyAutoDisplayObjectives(command);
                break;
            case "achievement":
                ApplyAchievement(command);
                break;
            case "autosave":
                StoreOpeningCheckpoint();
                break;
            case "setTimer":
                ApplyQuestTimer(command);
                break;
            case "setStage":
                var stageResult = GamebryoDialoguePlayback.RequireStageResult(
                    command.Kind,
                    command.QuestFormId,
                    command.Stage);
                if (stageResult.QuestFormId.Equals(
                    _flow.QuestFormId,
                    StringComparison.OrdinalIgnoreCase))
                {
                    SetStage(stageResult.Stage);
                    return true;
                }
                ApplyQuestStage(command);
                break;
            case "sayTo":
                if (command.TopicEditorId is null)
                    throw new InvalidOperationException("Owned dialogue continuation has no topic.");
                if (IsGuideSpeaker(command))
                    RunWhenGuideReady(
                        () => PlayTopicEditor(command.TopicEditorId, completed, generation),
                        generation);
                else
                    PlayTopicEditor(command.TopicEditorId, completed, generation);
                return true;
            case "deferredStage":
                if (command.Stage is { } deferred && command.Seconds is { } seconds)
                {
                    CloseModal();
                    _timerTargetStage = deferred;
                    _timerRemainingSeconds = seconds;
                    return true;
                }
                throw new InvalidOperationException(
                    "Owned deferred-stage dialogue command is incomplete.");
            default:
                throw new InvalidOperationException(
                    $"Owned opening dialogue command is unsupported: {command.Kind}");
        }
        return true;
    }

    private bool ResultCommandIsTerminal(OpeningFlowCommand command) =>
        command.Kind is "sayTo" or "deferredStage" ||
        command.Kind == "setStage" && command.QuestFormId?.Equals(
            _flow.QuestFormId,
            StringComparison.OrdinalIgnoreCase) == true;

    private static GamebryoResultCommandKind ResultCommandKind(string kind) => kind switch
    {
        "actorValueDelta" => GamebryoResultCommandKind.ActorValueDelta,
        "setQuestVariable" => GamebryoResultCommandKind.SetQuestVariable,
        "setDestroyed" => GamebryoResultCommandKind.SetDestroyed,
        "additem" => GamebryoResultCommandKind.AddItem,
        "removeitem" => GamebryoResultCommandKind.RemoveItem,
        "equipitem" => GamebryoResultCommandKind.EquipItem,
        "playerControls" => GamebryoResultCommandKind.PlayerControls,
        "addScriptPackage" => GamebryoResultCommandKind.AddScriptPackage,
        "removeScriptPackage" => GamebryoResultCommandKind.RemoveScriptPackage,
        "imageSpaceModifier" => GamebryoResultCommandKind.ImageSpaceModifier,
        "referenceEnabled" => GamebryoResultCommandKind.ReferenceEnabled,
        "actorIntent" => GamebryoResultCommandKind.ActorIntent,
        "objective" => GamebryoResultCommandKind.Objective,
        "startQuest" => GamebryoResultCommandKind.StartQuest,
        "stopQuest" => GamebryoResultCommandKind.StopQuest,
        "setGlobal" => GamebryoResultCommandKind.SetGlobal,
        "autoDisplayObjectives" => GamebryoResultCommandKind.AutoDisplayObjectives,
        "achievement" => GamebryoResultCommandKind.Achievement,
        "autosave" => GamebryoResultCommandKind.Autosave,
        "setTimer" => GamebryoResultCommandKind.SetTimer,
        "setStage" => GamebryoResultCommandKind.SetStage,
        "sayTo" => GamebryoResultCommandKind.SayTo,
        "deferredStage" => GamebryoResultCommandKind.DeferredStage,
        _ => throw new InvalidOperationException(
            $"Owned opening dialogue command is unsupported: {kind}"),
    };

    private void ShowTopicChoices(
        IReadOnlyList<string> topicFormIds,
        Action completed,
        int generation)
    {
        var content = OpenPanel(MenuRect("tagSkills"));
        foreach (var formId in topicFormIds)
        {
            if (!_flow.TopicsByFormId.TryGetValue(formId, out var topic))
                throw new InvalidOperationException($"Owned dialogue choice is absent: {formId}");
            var button = NewButton(topic.Prompt);
            button.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            button.Alignment = HorizontalAlignment.Left;
            button.Pressed += () => PlayTopic(topic, completed, generation);
            content.AddChild(button);
        }
    }

    private bool EvaluateCondition(OpeningDialogueCondition condition)
    {
        var actual = condition.Function switch
        {
            GetIsSexConditionFunction => _sexIndex ==
                Convert.ToInt32(condition.Parameter1, FormIdRadix) ? 1.0f : 0.0f,
            GetIsIdConditionFunction => _flow.SceneRoles.Values.Any(value =>
                value.BaseFormId.Equals(
                    condition.Parameter1,
                    StringComparison.OrdinalIgnoreCase)) ? 1.0f : 0.0f,
            GetQuestVariableConditionFunction => _docReaction,
            _ => throw new InvalidOperationException(
                $"Owned dialogue condition function is unsupported: {condition.Function}"),
        };
        return CompareCondition(
            condition.OperatorFlags,
            actual,
            condition.ComparisonValue);
    }

    private static bool CompareCondition(
        int operatorFlags,
        float actual,
        float comparisonValue) =>
        (operatorFlags & ConditionOperatorMask) switch
        {
            ConditionEqual => Mathf.IsEqualApprox(actual, comparisonValue),
            ConditionNotEqual => !Mathf.IsEqualApprox(actual, comparisonValue),
            ConditionGreater => actual > comparisonValue,
            ConditionGreaterOrEqual => actual >= comparisonValue,
            ConditionLess => actual < comparisonValue,
            ConditionLessOrEqual => actual <= comparisonValue,
            _ => throw new InvalidOperationException(
                $"Owned condition comparison is unsupported: {operatorFlags}"),
        };

    private void ApplyActorValueDelta(OpeningFlowCommand command)
    {
        if (command.OwnerEditorId is null || command.OwnerFormId is null ||
            command.ValueName is null || command.Delta is not { } delta)
            throw new InvalidOperationException("Owned actor-value command is incomplete.");
        _psychologyScores[command.ValueName] =
            _psychologyScores.GetValueOrDefault(command.ValueName) + delta;
    }

    private void ApplyQuestVariable(OpeningFlowCommand command)
    {
        if (command.QuestEditorId is null || command.QuestFormId is null ||
            command.ValueName is null || command.NumericValue is not { } value)
            throw new InvalidOperationException("Owned quest-variable command is incomplete.");
        _questVariables[QuestVariableKey(command.QuestFormId, command.ValueName)] = value;
    }

    private void ApplyQuestTimer(OpeningFlowCommand command)
    {
        if (command.QuestEditorId is null || command.QuestFormId is null ||
            command.Seconds is not { } seconds || seconds < 0.0f)
            throw new InvalidOperationException("Owned quest-timer command is incomplete.");
        _questVariables[QuestVariableKey(command.QuestFormId, "fTimer")] = seconds;
    }

    private void ApplyQuestStage(OpeningFlowCommand command)
    {
        if (command.QuestFormId is null || command.QuestEditorId is null ||
            command.Stage is not { } stage)
            throw new InvalidOperationException("Owned quest-stage command is incomplete.");
        ApplyQuestStage(command.QuestFormId, command.QuestEditorId, stage, true);
    }

    private void ApplyQuestStage(
        string formId,
        string editorId,
        int stage,
        bool running)
    {
        if (stage < 0)
            throw new InvalidOperationException("Owned quest stage is invalid.");
        var existing = _quests.GetValueOrDefault(formId);
        _quests[formId] = new OpeningQuestState(
            formId,
            editorId,
            stage,
            running || existing?.Running == true,
            false);
    }

    private void ApplyQuestLifecycle(OpeningFlowCommand command)
    {
        if (command.QuestFormId is null || command.QuestEditorId is null)
            throw new InvalidOperationException("Owned quest-lifecycle command is incomplete.");
        var existing = _quests.GetValueOrDefault(command.QuestFormId);
        var starting = command.Kind == "startQuest";
        var stopping = command.Kind == "stopQuest";
        if (!starting && !stopping)
            throw new InvalidOperationException(
                $"Owned quest-lifecycle operation is unsupported: {command.Kind}");
        _quests[command.QuestFormId] = new OpeningQuestState(
            command.QuestFormId,
            command.QuestEditorId,
            existing?.Stage ?? 0,
            starting,
            stopping);
    }

    private void ApplyGlobal(OpeningFlowCommand command)
    {
        if (command.GlobalFormId is null || command.GlobalEditorId is null ||
            command.NumericValue is not { } value || !float.IsFinite(value))
            throw new InvalidOperationException("Owned global command is incomplete.");
        _globals[command.GlobalFormId] = new OpeningGlobalState(
            command.GlobalFormId,
            command.GlobalEditorId,
            value);
    }

    private void ApplyAutoDisplayObjectives(OpeningFlowCommand command)
    {
        if (command.Enabled is not { } enabled)
            throw new InvalidOperationException(
                "Owned auto-display-objectives command is incomplete.");
        _autoDisplayObjectives = enabled;
        if (!enabled)
            _objective.Visible = false;
    }

    private void ApplyAchievement(OpeningFlowCommand command)
    {
        if (command.Index is not { } index || index < 0)
            throw new InvalidOperationException("Owned achievement command is incomplete.");
        _achievements.Add(index);
    }

    private void StoreOpeningCheckpoint()
    {
        AlignPlayerToOwnedNavigation();
        var state = CaptureState(false);
        _loaded.Session.StoreOpeningState(state);
        GD.Print(
            $"OPENNV_NEW_GAME_AUTOSAVE stage={state.Stage} " +
            $"quests={state.Quests.Count} inventory={state.Inventory.Count}");
    }

    private void AlignPlayerToOwnedNavigation()
    {
        var interaction = _flow.Interactions.SingleOrDefault(value =>
            value.FromStage == _stage);
        if (interaction is null ||
            !_roleNodes.TryGetValue(interaction.TargetRole, out var target))
            throw new InvalidOperationException(
                "Owned opening autosave stage has no resolved gameplay interaction.");
        var currentGameUnits = _loaded.CellToGameUnits(
            _loaded.Root.ToLocal(_loaded.Player.GlobalPosition));
        var targetGameUnits = _loaded.CellToGameUnits(
            _loaded.Root.ToLocal(target.GlobalPosition));
        var candidates = new[]
            {
                _loaded.MainContent.Navigation.FindNearestPoint(currentGameUnits),
            }
            .Concat(_loaded.MainContent.Navigation.FindPath(
                currentGameUnits,
                targetGameUnits))
            .Distinct()
            .ToArray();
        var candidateIndex = -1;
        for (var index = 0; index < candidates.Length; index++)
        {
            if (!PlayerNavigationDepartureIsClear(candidates, index))
                continue;
            candidateIndex = index;
            break;
        }
        if (candidateIndex < 0)
            throw new InvalidOperationException(
                "Owned opening navigation has no collision-free player handoff point.");
        var alignedWorld = PlayerNavigationCandidateWorld(candidates[candidateIndex]);
        var before = _loaded.Player.GlobalPosition;
        _loaded.Player.GlobalPosition = alignedWorld;
        _loaded.Player.Velocity = Vector3.Zero;
        GD.Print(
            $"OPENNV_NEW_GAME_PLAYER_NAV_HANDOFF stage={_stage} " +
            $"event={interaction.Event} before={before} after={alignedWorld} " +
            $"candidate={candidateIndex + 1}/{candidates.Length} " +
            $"source=owned-navmesh-first-collision-free-capsule");
    }

    private bool PlayerNavigationDepartureIsClear(
        IReadOnlyList<Vector3> candidates,
        int index)
    {
        if (!PlayerNavigationCandidateIsClear(candidates[index]))
            return false;
        if (index >= candidates.Count - 1)
            return true;
        var current = candidates[index];
        var next = candidates[index + 1];
        var distanceMeters = PlayerNavigationCandidateWorld(current).DistanceTo(
            PlayerNavigationCandidateWorld(next));
        var samples = Math.Max(
            1,
            Mathf.CeilToInt(distanceMeters / _configuration.Player.CapsuleRadiusMeters));
        for (var sample = 1; sample <= samples; sample++)
        {
            var amount = (float)sample / samples;
            if (!PlayerNavigationCandidateIsClear(current.Lerp(next, amount)))
                return false;
        }
        return true;
    }

    private bool PlayerNavigationCandidateIsClear(Vector3 candidateGameUnits)
    {
        var query = new PhysicsShapeQueryParameters3D
        {
            Shape = new CapsuleShape3D
            {
                Radius = _configuration.Player.CapsuleRadiusMeters,
                Height = _configuration.Player.CapsuleHeightMeters,
            },
            Transform = new Transform3D(
                _loaded.Player.GlobalBasis.Orthonormalized(),
                PlayerNavigationCandidateWorld(candidateGameUnits)),
            CollisionMask = _configuration.Player.CollisionMask,
            CollideWithAreas = false,
            CollideWithBodies = true,
            Exclude = new Godot.Collections.Array<Rid> { _loaded.Player.GetRid() },
        };
        return _loaded.Player.GetWorld3D().DirectSpaceState.IntersectShape(query, 1).Count == 0;
    }

    private Vector3 PlayerNavigationCandidateWorld(Vector3 candidateGameUnits) =>
        _loaded.GameToWorld(candidateGameUnits) +
        Vector3.Up * (
            _configuration.Player.SpawnCenterHeightMeters + _loaded.Player.SafeMargin);

    private OpeningCampaignState CaptureState(bool completed) => new(
        OpeningCampaignState.ExpectedSchema,
        _flow.QuestFormId,
        _flow.QuestEditorId,
        _stage,
        completed,
        _playerName,
        _sexIndex,
        new OpeningCharacterAppearanceState(
            _raceFormId,
            _hairFormId,
            _eyesFormId,
            CurrentFaceSymmetricGeometrySha256(),
            _flow.Character.Appearance.FaceGen.AsymmetricGeometrySha256,
            _flow.Character.Appearance.FaceGen.SymmetricTextureSha256,
            _faceGeometryControlValues
                .OrderBy(value => value.Key, StringComparer.Ordinal)
                .ToDictionary(
                    value => value.Key,
                    value => value.Value,
                    StringComparer.Ordinal),
            _bodyProportions,
            _appearancePreviewMode),
        _docReaction,
        _specialValues
            .OrderBy(value => value.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(value => value.Key, value => value.Value, StringComparer.OrdinalIgnoreCase),
        _tagSkills.Order(StringComparer.OrdinalIgnoreCase).ToArray(),
        _traits.Order(StringComparer.OrdinalIgnoreCase).ToArray(),
        _psychologyScores
            .OrderBy(value => value.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(value => value.Key, value => value.Value, StringComparer.OrdinalIgnoreCase),
        _questVariables
            .OrderBy(value => value.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(value => value.Key, value => value.Value, StringComparer.OrdinalIgnoreCase),
        _quests.Values.OrderBy(value => value.FormId, StringComparer.OrdinalIgnoreCase).ToArray(),
        _globals.Values.OrderBy(value => value.FormId, StringComparer.OrdinalIgnoreCase).ToArray(),
        _objectives.Values
            .OrderBy(value => value.QuestFormId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(value => value.Index)
            .ToArray(),
        _autoDisplayObjectives,
        _achievements.Order().ToArray(),
        _inventory.Values.OrderBy(value => value.FormId, StringComparer.OrdinalIgnoreCase).ToArray(),
        _equippedItemFormIds.Order(StringComparer.OrdinalIgnoreCase).ToArray(),
        _destroyedReferences.Order(StringComparer.OrdinalIgnoreCase).ToArray(),
        _referenceEnabledStates
            .OrderBy(value => value.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(value => value.Key, value => value.Value, StringComparer.OrdinalIgnoreCase),
        _playerControls.Select(value => value ? EnabledControlValue : DisabledControlValue).ToArray(),
        OpeningTransformState.Capture(_loaded.Player),
        OpeningTransformState.Capture(_guideActor.Placement));

    internal static bool MatchesFlow(
        OpeningNewGameFlow flow,
        OpeningCampaignState state)
    {
        try
        {
            ValidateStateForFlow(flow, state);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    internal static bool GameplayUiEnabled(OpeningCampaignState state) =>
        state.PlayerControls.Count == PlayerControlCount &&
        state.PlayerControls[PipBoyControlIndex] == EnabledControlValue;

    internal static void ApplyPlayerControlPolicy(
        CellPlayer player,
        IReadOnlyList<int> playerControls,
        bool saveEnabled)
    {
        if (playerControls.Count != PlayerControlCount ||
            playerControls.Any(value => value is not DisabledControlValue and not EnabledControlValue))
            throw new InvalidOperationException("Owned player-control state is invalid.");
        bool Enabled(int index) => playerControls[index] == EnabledControlValue;
        player.SetControlPolicy(
            Enabled(MovementControlIndex),
            Enabled(LookingControlIndex),
            Enabled(MovementControlIndex) && Enabled(RolloverTextControlIndex),
            Enabled(FightingControlIndex),
            saveEnabled);
    }

    private static void ValidateStateForFlow(
        OpeningNewGameFlow flow,
        OpeningCampaignState state)
    {
        state.Validate();
        var expectedSpecial = flow.Character.SpecialValues
            .Select(value => value.FormId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var expectedSkills = flow.Character.SkillValues
            .Select(value => value.FormId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var expectedTraits = flow.Character.TraitValues
            .Select(value => value.FormId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var specialTotal = state.SpecialValues.Values.Sum();
        var initialSpecialTotal =
            flow.Character.SpecialInitial * flow.Character.SpecialValues.Count;
        var primaryQuest = state.Quests.SingleOrDefault(value =>
            value.FormId.Equals(flow.QuestFormId, StringComparison.OrdinalIgnoreCase));
        var validStage = state.Completed
            ? state.Stage == flow.CompletionStage
            : state.Stage != flow.CompletionStage && flow.Stages.ContainsKey(state.Stage);
        if (!state.QuestFormId.Equals(flow.QuestFormId, StringComparison.OrdinalIgnoreCase) ||
            !state.QuestEditorId.Equals(flow.QuestEditorId, StringComparison.OrdinalIgnoreCase) ||
            !validStage ||
            primaryQuest is null ||
            !primaryQuest.EditorId.Equals(flow.QuestEditorId, StringComparison.OrdinalIgnoreCase) ||
            primaryQuest.Stage != state.Stage ||
            state.Completed && (!primaryQuest.Stopped || primaryQuest.Running) ||
            !state.Completed && primaryQuest.Stopped ||
            string.IsNullOrWhiteSpace(state.PlayerName) ||
            state.SexIndex >= flow.Character.SexChoices.Count ||
            !AppearanceMatchesFlow(flow, state.SexIndex, state.Appearance) ||
            !state.SpecialValues.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase)
                .SetEquals(expectedSpecial) ||
            state.SpecialValues.Values.Any(value =>
                value < flow.Character.SpecialMinimum ||
                value > flow.Character.SpecialMaximum) ||
            specialTotal != initialSpecialTotal &&
                specialTotal != flow.Character.SpecialTotalPoints ||
            !state.TagSkillFormIds.All(expectedSkills.Contains) ||
            state.TagSkillFormIds.Count > flow.Character.TagSkillMaximumSelected ||
            !state.TraitFormIds.All(expectedTraits.Contains) ||
            state.TraitFormIds.Count > flow.Character.TraitMaximumSelected ||
            state.PlayerControls.Count != PlayerControlCount)
            throw new InvalidOperationException(
                "Saved opening state does not match the owned New Game flow.");
        if (state.Completed)
            ValidateCompletedState(flow, state);
    }

    private static bool AppearanceMatchesFlow(
        OpeningNewGameFlow flow,
        int sexIndex,
        OpeningCharacterAppearanceState state)
    {
        if (sexIndex < 0 || sexIndex >= flow.Character.Appearance.SexEngineValues.Count)
            return false;
        var race = flow.Character.Appearance.Races.SingleOrDefault(value =>
            value.FormId.Equals(
                state.RaceFormId,
                StringComparison.OrdinalIgnoreCase));
        if (race is null || !race.Sex.TryGetValue(
                flow.Character.Appearance.SexEngineValues[sexIndex],
                out var sex))
            return false;
        return sex.HairOptions.Any(value => value.FormId.Equals(
                state.HairFormId,
                StringComparison.OrdinalIgnoreCase)) &&
            sex.EyeOptions.Any(value => value.FormId.Equals(
                state.EyesFormId,
                StringComparison.OrdinalIgnoreCase)) &&
            state.FaceSymmetricGeometrySha256.Equals(
                FaceSymmetricGeometrySha256(
                    flow.Character.Appearance.FaceGen,
                    state.FaceGeometryControlValues),
                StringComparison.OrdinalIgnoreCase) &&
            state.FaceAsymmetricGeometrySha256.Equals(
                flow.Character.Appearance.FaceGen.AsymmetricGeometrySha256,
                StringComparison.OrdinalIgnoreCase) &&
            state.FaceSymmetricTextureSha256.Equals(
                flow.Character.Appearance.FaceGen.SymmetricTextureSha256,
                StringComparison.OrdinalIgnoreCase);
    }

    private string CurrentFaceSymmetricGeometrySha256() =>
        FaceSymmetricGeometrySha256(
            _flow.Character.Appearance.FaceGen,
            _faceGeometryControlValues);

    private string FaceGenControlValuesText(
        IReadOnlyList<OpeningNativeFaceGenGeometryControl> controls) =>
        string.Join(
            ",",
            controls.Select(control =>
                $"{control.SettingEntity}:" +
                $"{_faceGeometryControlValues[control.SettingEntity]:F4}"));

    private static IReadOnlyList<OpeningNativeFaceGenGeometryControl>
        FaceGenPreviewControls(OpeningAppearanceFaceGen faceGen)
    {
        var selected = faceGen.ControlSpace.NativeGeometryControls.ToArray();
        if (selected.Length == 0 ||
            selected.Select(control => control.SettingEntity)
                .Distinct(StringComparer.Ordinal).Count() != selected.Length)
            throw new InvalidOperationException(
                "Owned FaceGen native geometry control inventory is incomplete.");
        var configured = faceGen.ControlSpace.PreviewControl;
        var configuredMatches = selected.Where(control =>
            control.ControlIndex == configured.ControlIndex &&
            control.SettingEntity == configured.SettingEntity &&
            control.AxisSha256 == configured.AxisSha256).ToArray();
        if (configuredMatches.Length != 1)
            throw new InvalidOperationException(
                "Owned FaceGen configured proof coordinate is not a native control.");
        foreach (var control in selected)
        {
            var source = faceGen.ControlSpace.SymmetricGeometryControls.SingleOrDefault(
                value => value.Index == control.ControlIndex);
            if (source is null ||
                source.SourceLabel != control.SourceLabel ||
                source.AxisSha256 != control.AxisSha256 ||
                source.Axis.Count != faceGen.SymmetricGeometryValues.Count)
                throw new InvalidOperationException(
                    $"Owned FaceGen native control axis differs: {control.SettingEntity}.");
        }
        return selected;
    }

    private static string FaceSymmetricGeometrySha256(
        OpeningAppearanceFaceGen faceGen,
        IReadOnlyDictionary<string, float> values)
    {
        var policy = faceGen.ControlSpace.PreviewControl;
        var controls = FaceGenPreviewControls(faceGen);
        if (values.Count != controls.Count ||
            controls.Any(control =>
                !values.TryGetValue(control.SettingEntity, out var value) ||
                !float.IsFinite(value) ||
                value < policy.Minimum ||
                value > policy.Maximum))
            throw new InvalidOperationException(
                "Saved RaceSexMenu FaceGen UI coordinates are invalid.");
        var sourceControls = controls.Select(control =>
            faceGen.ControlSpace.SymmetricGeometryControls.Single(source =>
                source.Index == control.ControlIndex)).ToArray();

        var payload = new byte[faceGen.SymmetricGeometryValues.Count * sizeof(float)];
        for (var index = 0; index < faceGen.SymmetricGeometryValues.Count; index++)
        {
            var coordinate = faceGen.SymmetricGeometryValues[index];
            for (var controlIndex = 0; controlIndex < controls.Count; controlIndex++)
            {
                var value = values[controls[controlIndex].SettingEntity];
                if (value == policy.ResetValue)
                    continue;
                coordinate += value * policy.MorphWeightScale *
                    sourceControls[controlIndex].Axis[index];
            }
            if (!float.IsFinite(coordinate))
                throw new InvalidOperationException(
                    "Edited FaceGen geometry coordinate is non-finite.");
            BinaryPrimitives.WriteSingleLittleEndian(
                payload.AsSpan(index * sizeof(float), sizeof(float)),
                coordinate);
        }
        return Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
    }

    private void RestoreState(OpeningCampaignState state)
    {
        ValidateStateForFlow(_flow, state);

        _playerName = state.PlayerName;
        _sexIndex = state.SexIndex;
        _raceFormId = state.Appearance.RaceFormId;
        _hairFormId = state.Appearance.HairFormId;
        _eyesFormId = state.Appearance.EyesFormId;
        _bodyProportions = state.Appearance.BodyProportions;
        _appearancePreviewMode = state.Appearance.PreviewMode;
        Replace(
            _faceGeometryControlValues,
            state.Appearance.FaceGeometryControlValues);
        _docReaction = state.DocReaction;
        Replace(_specialValues, state.SpecialValues);
        Replace(_tagSkills, state.TagSkillFormIds);
        Replace(_traits, state.TraitFormIds);
        Replace(_psychologyScores, state.PsychologyScores);
        Replace(_questVariables, state.QuestVariables);
        Replace(_quests, state.Quests, value => value.FormId);
        Replace(_globals, state.Globals, value => value.FormId);
        Replace(_objectives, state.Objectives, value => ObjectiveKey(value.QuestFormId, value.Index));
        Replace(_achievements, state.Achievements);
        Replace(_inventory, state.Inventory, value => value.FormId);
        Replace(_equippedItemFormIds, state.EquippedItemFormIds);
        Replace(_destroyedReferences, state.DestroyedReferenceFormIds);
        Replace(_referenceEnabledStates, state.ReferenceEnabledStates);
        _autoDisplayObjectives = state.AutoDisplayObjectives;
        _skillDefaultsInitialized = _tagSkills.Count > 0;
        for (var index = 0; index < PlayerControlCount; index++)
            _playerControls[index] = state.PlayerControls[index] == EnabledControlValue;
        state.PlayerTransform.Apply(_loaded.Player);
        state.GuideTransform.Apply(_guideActor.Placement);
        foreach (var reference in _referenceEnabledStates)
            SetReferenceVisibility(reference.Key, reference.Value, false);
    }

    private static void Replace<T>(HashSet<T> target, IEnumerable<T> source)
    {
        target.Clear();
        target.UnionWith(source);
    }

    private static void Replace<T>(
        Dictionary<string, T> target,
        IReadOnlyDictionary<string, T> source)
    {
        target.Clear();
        foreach (var value in source)
            target.Add(value.Key, value.Value);
    }

    private static void Replace<T>(
        Dictionary<string, T> target,
        IEnumerable<T> source,
        Func<T, string> key)
    {
        target.Clear();
        foreach (var value in source)
            target.Add(key(value), value);
    }

    private static string QuestVariableKey(string questFormId, string variable) =>
        $"{questFormId}.{variable}";

    private static string ObjectiveKey(string questFormId, int index) =>
        $"{questFormId}.{index}";

    private void ApplyDestroyedFromInfo(OpeningFlowCommand command)
    {
        if (command.ReferenceEditorId is null || command.ReferenceFormId is null ||
            command.Destroyed is null)
            throw new InvalidOperationException("Owned dialogue destroyed-reference command is incomplete.");
        if (command.Destroyed.Value)
            _destroyedReferences.Add(command.ReferenceFormId);
        else
            _destroyedReferences.Remove(command.ReferenceFormId);
    }

    private void CompleteOpening()
    {
        _openingQuestCompleted = true;
        EvaluateGuidePackage();
        _objective.Visible = false;
        CloseModal();
        ApplyStageControlPolicy();
        var state = CaptureState(true);
        _loaded.Session.StoreOpeningState(state);
        _loaded.Player.SetExternalActivationHandler(null);
        _loaded.Player.ClearOwnedNavigation();
        _viewport.MouseFilter = Control.MouseFilterEnum.Ignore;
        _viewport.Visible = false;
        SetProcess(false);
        GD.Print(
            $"OPENNV_NEW_GAME_OPEN_WORLD_READY quest={_flow.QuestEditorId} " +
            $"stage={_stage} name={_playerName} inventory={_inventory.Count} " +
            $"quests={state.Quests.Count} achievements={state.Achievements.Count}");
    }

    private VBoxContainer OpenPanel(Rect2 rect, string? menuRole = null)
    {
        var root = OpenModalRoot(menuRole);
        var panel = new Panel
        {
            Position = rect.Position,
            Size = rect.Size,
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        panel.AddThemeStyleboxOverride(
            "panel",
            OwnedUiTheme.HighlightedStyle(_opening.MainMenuColor, _opening.Style));
        if (menuRole is not null &&
            _flow.Menus.TryGetValue(menuRole, out var menu) &&
            menu.Background is { } background)
        {
            var backgroundTexture = new TextureRect
            {
                Name = $"Owned{menu.MenuName}Background",
                Texture = OwnedUiTheme.LoadTexture(background.Path),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.Scale,
                Position = rect.Position,
                Size = rect.Size,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            root.AddChild(backgroundTexture);
        }
        root.AddChild(panel);
        var margins = new MarginContainer();
        margins.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        margins.AddThemeConstantOverride(
            "margin_left",
            Mathf.RoundToInt(_opening.Style.HorizontalPaddingPixels));
        margins.AddThemeConstantOverride(
            "margin_right",
            Mathf.RoundToInt(_opening.Style.HorizontalPaddingPixels));
        margins.AddThemeConstantOverride(
            "margin_top",
            Mathf.RoundToInt(_opening.Style.VerticalPaddingPixels));
        margins.AddThemeConstantOverride(
            "margin_bottom",
            Mathf.RoundToInt(_opening.Style.VerticalPaddingPixels));
        panel.AddChild(margins);
        var content = new VBoxContainer();
        content.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        content.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        margins.AddChild(content);
        return content;
    }

    private Control OpenOwnedTilePanel(
        OwnedGamebryoTileLayout layout,
        string menuRole)
    {
        var root = OpenModalRoot(menuRole);
        var panel = new Panel { MouseFilter = Control.MouseFilterEnum.Stop };
        OwnedGamebryoTileRuntime.ApplyAbsolute(panel, layout);
        panel.AddThemeStyleboxOverride(
            "panel",
            OwnedUiTheme.HighlightedStyle(_opening.MainMenuColor, _opening.Style));
        if (!_flow.Menus.TryGetValue(menuRole, out var menu) ||
            menu.Background is not { } background)
            throw new InvalidOperationException(
                $"Owned tile panel background is unavailable: {menuRole}");
        var backgroundTexture = new TextureRect
        {
            Name = $"Owned{menu.MenuName}Background",
            Texture = OwnedUiTheme.LoadTexture(background.Path),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        backgroundTexture.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        panel.AddChild(backgroundTexture);
        root.AddChild(panel);
        return panel;
    }

    private Control OpenModalRoot(string? menuRole = null)
    {
        CloseModal(false);
        var root = new Control
        {
            Name = "OwnedMenu",
            Position = Vector2.Zero,
            Size = _flow.ReferenceCanvasSize,
            ZIndex = 1,
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        _canvas.AddChild(root);
        _activeModal = root;
        _loaded.Player.SetControlPolicy(false, false, false, false, false);
        Input.MouseMode = Input.MouseModeEnum.Visible;
        return root;
    }

    private Rect2 MenuRect(string role, string? alternateRole = null)
    {
        if (_flow.Menus.TryGetValue(role, out var menu) && menu.Rect is { } rect)
            return rect;
        if (alternateRole is not null &&
            _flow.Menus.TryGetValue(alternateRole, out var alternate) &&
            alternate.Rect is { } alternateRect)
            return alternateRect;
        var authored = _flow.Menus["name"].Rect;
        return authored ?? new Rect2(Vector2.Zero, _flow.ReferenceCanvasSize);
    }

    private Label NewLabel(string text)
    {
        var label = new Label { Text = text };
        ApplyTextTheme(label);
        return label;
    }

    private Button NewButton(string text)
    {
        var button = new Button
        {
            Text = text,
            FocusMode = Control.FocusModeEnum.All,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand,
        };
        OwnedUiTheme.ApplyButton(
            button,
            _font,
            _opening.MainMenuColor,
            _opening.Style);
        return button;
    }

    private void ApplyTextTheme(Control control)
    {
        control.AddThemeFontOverride("font", _font);
        control.AddThemeFontSizeOverride(
            "font_size",
            Mathf.RoundToInt(_opening.Font.LineHeightPixels));
        control.AddThemeColorOverride(
            "font_color",
            OwnedUiTheme.Brightness(
                _opening.MainMenuColor,
                _opening.Style.TextBrightness));
    }

    private void CloseModal(bool restoreControls = true)
    {
        StopDialogueVoice();
        if (_appearancePreviewHost is not null)
        {
            var disposed = _appearancePreviewHost.DisposeOwnedTree();
            GD.Print(
                "OPENNV_NEW_GAME_FACEGEN_PREVIEW_DISPOSED " +
                $"control={disposed.ControlInstanceId} " +
                $"viewport={disposed.ViewportInstanceId} " +
                $"actor={disposed.ActorInstanceId} " +
                $"disposition={disposed.Disposition}");
            _appearancePreviewHost = null;
        }
        _raceSexMenuHost = null;
        _raceSexRenderedDeviceHost = null;
        _raceSexShowSex = null;
        _raceSexShowFace = null;
        if (_activeModal is not null)
        {
            _activeModal.Visible = false;
            _activeModal.QueueFree();
            _activeModal = null;
        }
        if (restoreControls)
            ApplyStageControlPolicy();
    }

    private void ApplyStageControlPolicy()
    {
        if (_activeModal is not null)
        {
            _loaded.Session.SetGameplayUiVisible(false);
            _loaded.Player.SetControlPolicy(false, false, false, false, false);
            return;
        }
        _loaded.Session.SetGameplayUiVisible(_playerControls[PipBoyControlIndex]);
        ApplyPlayerControlPolicy(
            _loaded.Player,
            _playerControls
                .Select(value => value ? EnabledControlValue : DisabledControlValue)
                .ToArray(),
            _stage == _flow.CompletionStage);
        if (!_loaded.Player.UsesXr && DisplayServer.GetName() != "headless")
            Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    private void ScaleReferenceCanvas()
    {
        if (_canvas is null || _viewport is null ||
            _flow.ReferenceCanvasSize.X <= 0.0f ||
            _flow.ReferenceCanvasSize.Y <= 0.0f)
            return;
        var viewportSize = _viewport.Size;
        var scale = Mathf.Min(
            viewportSize.X / _flow.ReferenceCanvasSize.X,
            viewportSize.Y / _flow.ReferenceCanvasSize.Y);
        _canvas.Scale = Vector2.One * scale;
        _canvas.Position =
            (viewportSize - _flow.ReferenceCanvasSize * scale) * OwnedUiTheme.CenteringFactor;
    }

    private sealed class ActiveImageSpaceModifier(OpeningImageSpaceModifier modifier)
    {
        internal OpeningImageSpaceModifier Modifier { get; } = modifier;
        internal double ElapsedSeconds { get; set; }
    }

    private enum AcceptanceAppearancePhase
    {
        InitialSex,
        SelectRace,
        SelectHair,
        SelectEyes,
        EditGeometry,
        ResetGeometry,
        RestoreGeometryEdit,
        SelectSex,
        SelectParts,
        ShowcaseFaceNormal,
        ShowcaseBodyNormal,
        ShowcaseBodyGreen,
        ShowcaseFaceGreen,
        Complete,
    }
}
