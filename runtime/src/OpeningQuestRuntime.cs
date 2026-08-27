using Godot;

namespace OpenNV.Runtime;

internal partial class OpeningQuestRuntime : CanvasLayer
{
    private const int GetIsSexConditionFunction = 70;
    private const int GetIsIdConditionFunction = 72;
    private const int GetQuestVariableConditionFunction = 79;
    private const int ConditionOperatorMask = 0xe0;
    private const int ConditionEqual = 0x00;
    private const int ConditionNotEqual = 0x20;
    private const int ConditionGreater = 0x40;
    private const int ConditionGreaterOrEqual = 0x60;
    private const int ConditionLess = 0x80;
    private const int ConditionLessOrEqual = 0xa0;
    private const int FormIdRadix = 16;

    private readonly Dictionary<string, Node3D> _roleNodes =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _topicCursors =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _saidOnce = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _destroyedReferences =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, float> _questVariables =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _psychologyScores =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _specialValues =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _tagSkills = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _traits = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _inventory =
        new(StringComparer.OrdinalIgnoreCase);

    private OpeningManifest _opening = null!;
    private OpeningNewGameFlow _flow = null!;
    private CellSceneLoader.LoadedCell _loaded;
    private RuntimeConfiguration _configuration = null!;
    private FontFile _font = null!;
    private Control _viewport = null!;
    private Control _canvas = null!;
    private Control? _activeModal;
    private Label _objective = null!;
    private int _stage;
    private int _generation;
    private int? _timerTargetStage;
    private double _timerRemainingSeconds;
    private string _playerName = "";
    private int _sexIndex;
    private int _docReaction;
    private bool _skillDefaultsInitialized;

    internal int Stage => _stage;
    internal string PlayerName => _playerName;

    internal void Configure(
        OpeningManifest opening,
        CellSceneLoader.LoadedCell loaded,
        RuntimeConfiguration configuration)
    {
        _opening = opening;
        _flow = opening.NewGameFlow;
        _loaded = loaded;
        _configuration = configuration;
        _font = OpeningUiTheme.BuildFont(opening.Font);
        Name = "OwnedNewGameFlow";

        foreach (var value in _flow.Character.SpecialValues)
            _specialValues[value.FormId] = _flow.Character.SpecialInitial;
        ResolveSceneRoles();
        _loaded.Player.SetExternalActivationHandler(HandleExternalActivation);

        _viewport = new Control { Name = "OpeningFlowViewport" };
        _viewport.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _viewport.Resized += ScaleReferenceCanvas;
        AddChild(_viewport);
        _canvas = new Control
        {
            Name = "RetailFlowCanvas",
            Size = _flow.ReferenceCanvasSize,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _viewport.AddChild(_canvas);
        _objective = new Label
        {
            Name = "QuestObjective",
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        ApplyTextTheme(_objective);
        _canvas.AddChild(_objective);
        ScaleReferenceCanvas();
        Callable.From(ScaleReferenceCanvas).CallDeferred();

        var firstStage = _flow.Stages.Keys.Min();
        SetStage(firstStage);
        GD.Print(
            $"OPENNV_NEW_GAME_FLOW_READY quest={_flow.QuestEditorId} " +
            $"stage={firstStage} stages={_flow.Stages.Count} topics={_flow.TopicsByFormId.Count}");
    }

    public override void _Process(double delta)
    {
        if (_activeModal is not null)
            return;
        if (_timerTargetStage is { } timerTarget)
        {
            _timerRemainingSeconds -= delta;
            if (_timerRemainingSeconds <= 0.0)
            {
                _timerTargetStage = null;
                SetStage(timerTarget);
                return;
            }
        }
        var proximity = _flow.Interactions.SingleOrDefault(value =>
            value.FromStage == _stage &&
            value.Event.Equals("proximity", StringComparison.OrdinalIgnoreCase));
        if (proximity is null || !_roleNodes.TryGetValue(proximity.TargetRole, out var target))
            return;
        if (_loaded.Player.GlobalPosition.DistanceTo(target.GlobalPosition) <=
            _configuration.Player.ActivationDistanceMeters)
            SetStage(proximity.ToStage);
    }

    private void ResolveSceneRoles()
    {
        foreach (var role in _flow.SceneRoles.Values)
        {
            Node3D? node = role.RecordType switch
            {
                "ACHR" or "ACRE" => _loaded.Actors
                    .FirstOrDefault(value => value.ReferenceFormId.Equals(
                        role.ReferenceFormId,
                        StringComparison.OrdinalIgnoreCase))
                    .Placement,
                _ when _loaded.MainContent.PlacedReferences.FirstOrDefault(value =>
                    value.FormId.Equals(
                        role.ReferenceFormId,
                        StringComparison.OrdinalIgnoreCase)) is { } reference =>
                    reference.Placement,
                _ when _loaded.MainContent.Doors.TryGetValue(
                    role.ReferenceFormId,
                    out var door) => door,
                _ => null,
            };
            if (node is null)
                throw new InvalidOperationException(
                    $"Owned opening scene role is absent from its CELL: {role.Role}");
            _roleNodes.Add(role.Role, node);
        }
    }

    private bool HandleExternalActivation(Node? collider)
    {
        foreach (var role in _flow.SceneRoles.Values)
        {
            if (!_destroyedReferences.Contains(role.EditorId) ||
                !_roleNodes.TryGetValue(role.Role, out var destroyed) ||
                !MatchesTarget(collider, destroyed))
                continue;
            GD.Print($"OPENNV_NEW_GAME_ACTIVATE_BLOCKED reference={role.ReferenceFormId}");
            return true;
        }
        var interaction = _flow.Interactions.SingleOrDefault(value =>
            value.FromStage == _stage &&
            value.Event.Equals("activate", StringComparison.OrdinalIgnoreCase));
        if (interaction is null || !_roleNodes.TryGetValue(interaction.TargetRole, out var target))
            return false;
        if (!MatchesTarget(collider, target) &&
            _loaded.Player.GlobalPosition.DistanceTo(target.GlobalPosition) >
            _configuration.Player.ActivationDistanceMeters)
            return false;
        if (interaction.Menu?.Role == "special")
        {
            ShowSpecialMenu(() => SetStage(interaction.ToStage));
            return true;
        }
        SetStage(interaction.ToStage);
        return true;
    }

    private static bool MatchesTarget(Node? collider, Node3D target) =>
        collider is not null &&
        (collider == target || target.IsAncestorOf(collider) || collider.IsAncestorOf(target));

    private void SetStage(int stage)
    {
        if (!_flow.Stages.TryGetValue(stage, out var program))
            throw new InvalidOperationException($"Owned New Game stage is absent: {stage}");
        _generation++;
        _stage = stage;
        _timerTargetStage = null;
        CloseModal();
        ApplyStageControlPolicy();
        GD.Print($"OPENNV_NEW_GAME_STAGE quest={_flow.QuestEditorId} stage={stage}");
        ExecuteStageCommand(program, 0, _generation, null);
    }

    private void ExecuteStageCommand(
        OpeningStageProgram program,
        int index,
        int generation,
        float? timerSeconds)
    {
        if (generation != _generation)
            return;
        if (index >= program.Commands.Count)
        {
            if (timerSeconds is { } seconds &&
                _flow.TimerTransitions.TryGetValue(_stage, out var transition))
            {
                if (seconds <= 0.0f)
                    Callable.From(() => SetStage(transition.ToStage)).CallDeferred();
                else
                {
                    _timerRemainingSeconds = seconds;
                    _timerTargetStage = transition.ToStage;
                }
                return;
            }
            if (_stage == _flow.PsychologyStartStage)
            {
                PlayInfo(
                    _flow.PsychologyRootInfo,
                    null,
                    () => { },
                    generation);
                return;
            }
            if (_stage == _flow.OutroStartStage)
            {
                PlayTopicForm(_flow.OutroTopicFormId, () => { }, generation);
                return;
            }
            if (_stage == _flow.CompletionStage)
                CompleteOpening();
            return;
        }

        var command = program.Commands[index];
        void Next(float? updatedTimer = null) => ExecuteStageCommand(
            program,
            index + 1,
            generation,
            updatedTimer ?? timerSeconds);
        switch (command.Kind)
        {
            case "setTimer":
                Next(command.Seconds);
                return;
            case "setQuestVariable":
                if (command.QuestEditorId is not null && command.ValueName is not null &&
                    command.NumericValue is { } numeric)
                    _questVariables[$"{command.QuestEditorId}.{command.ValueName}"] = numeric;
                Next();
                return;
            case "setStage":
                if (command.QuestEditorId?.Equals(
                    _flow.QuestEditorId,
                    StringComparison.OrdinalIgnoreCase) == true &&
                    command.Stage is { } nextStage)
                    SetStage(nextStage);
                else
                    Next();
                return;
            case "sayTo":
                if (command.TopicEditorId is null)
                    throw new InvalidOperationException("Owned SayTo command has no topic.");
                PlayTopicEditor(command.TopicEditorId, () => Next(), generation);
                return;
            case "showMenu":
                ShowMenu(command, () =>
                {
                    if (generation != _generation)
                        return;
                    if (command.Role == "appearance" &&
                        _flow.MenuCloseTransitions.TryGetValue(_stage, out var nextStage))
                        SetStage(nextStage);
                    else
                        Next();
                });
                return;
            case "objective":
                ApplyObjective(command);
                Next();
                return;
            case "setDestroyed":
                ApplyDestroyed(command);
                Next();
                return;
            case "playIdle":
                ApplyIdle(command);
                Next();
                return;
            default:
                Next();
                return;
        }
    }

    private void ShowMenu(OpeningFlowCommand command, Action completed)
    {
        switch (command.Role)
        {
            case "name":
                ShowNameMenu(completed);
                return;
            case "appearance":
                ShowAppearanceMenu(completed);
                return;
            case "tagSkills":
                ShowSkillMenu(completed);
                return;
            case "traits":
                ShowTraitMenu(completed);
                return;
            case "special":
                ShowSpecialMenu(completed);
                return;
            default:
                throw new InvalidOperationException(
                    $"Owned opening menu role is unsupported: {command.Role}");
        }
    }

    private void ApplyObjective(OpeningFlowCommand command)
    {
        if (command.Index is not { } index || command.Enabled != true ||
            !_flow.Objectives.TryGetValue(index, out var text))
            return;
        if (command.State == "displayed")
        {
            _objective.Text = text;
            _objective.Visible = true;
        }
        else if (command.State == "completed" && _objective.Text == text)
            _objective.Visible = false;
    }

    private void ApplyDestroyed(OpeningFlowCommand command)
    {
        if (command.ReferenceEditorId is null || command.Destroyed is null)
            return;
        if (command.Destroyed.Value)
            _destroyedReferences.Add(command.ReferenceEditorId);
        else
            _destroyedReferences.Remove(command.ReferenceEditorId);
    }

    private void ApplyIdle(OpeningFlowCommand command)
    {
        if (command.ReferenceEditorId is null || command.IdleEditorId is null ||
            command.AnimationLogicalPath is null)
            return;
        var actors = _loaded.Actors.Where(value =>
            _flow.SceneRoles.Values.Any(role =>
                role.EditorId.Equals(command.ReferenceEditorId, StringComparison.OrdinalIgnoreCase) &&
                role.ReferenceFormId.Equals(
                    value.ReferenceFormId,
                    StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        if (actors.Length != 1)
            throw new InvalidOperationException(
                $"Owned opening idle actor is ambiguous: {command.ReferenceEditorId}");
        var actor = actors[0];
        var expected = ActorModelSlice.NormalizeAnimationPath(command.AnimationLogicalPath);
        var animations = actor.Actor.LoadedAnimations.Where(animation =>
                ActorModelSlice.NormalizeAnimationPath(animation.LogicalPath).Equals(
                    expected,
                    StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (animations.Length != 1)
            throw new InvalidOperationException(
                $"Owned opening idle animation is absent from the actor: {command.AnimationLogicalPath}");
        var animation = animations[0];
        animation.Player.Play(animation.RuntimeName);
        animation.Player.Advance(0.0);
        GD.Print(
            $"OPENNV_NEW_GAME_IDLE source={command.ReferenceEditorId} " +
            $"authored={command.IdleEditorId} runtime={animation.RuntimeName}");
    }

    private void ShowNameMenu(Action completed)
    {
        var content = OpenPanel(MenuRect("name"));
        var prompt = NewLabel(_flow.Strings["namePrompt"]);
        prompt.HorizontalAlignment = HorizontalAlignment.Center;
        content.AddChild(prompt);
        var input = new LineEdit { Text = _playerName };
        ApplyTextTheme(input);
        content.AddChild(input);
        var accept = NewButton(_flow.Strings["ok"]);
        content.AddChild(accept);
        void Submit()
        {
            var value = input.Text.Trim();
            if (string.IsNullOrWhiteSpace(value))
                return;
            _playerName = value;
            CloseModal();
            completed();
        }
        accept.Pressed += Submit;
        input.TextSubmitted += _ => Submit();
        Callable.From(input.GrabFocus).CallDeferred();
    }

    private void ShowAppearanceMenu(Action completed)
    {
        var content = OpenPanel(MenuRect("appearance"));
        var title = NewLabel(_flow.Character.SexTitle);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        content.AddChild(title);
        for (var index = 0; index < _flow.Character.SexChoices.Count; index++)
        {
            var choiceIndex = index;
            var button = NewButton(_flow.Character.SexChoices[index]);
            button.ToggleMode = true;
            button.ButtonPressed = _sexIndex == choiceIndex;
            button.Pressed += () =>
            {
                _sexIndex = choiceIndex;
                ShowAppearanceMenu(completed);
            };
            content.AddChild(button);
        }
        var accept = NewButton(_flow.Strings["accept"]);
        accept.Pressed += () =>
        {
            CloseModal();
            completed();
        };
        content.AddChild(accept);
    }

    private void ShowSpecialMenu(Action completed)
    {
        var content = OpenPanel(MenuRect("special", "tagSkills"));
        var title = NewLabel(_flow.SceneRoles["vigorTester"].DisplayName);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        content.AddChild(title);
        foreach (var value in _flow.Character.SpecialValues)
        {
            var row = new HBoxContainer();
            row.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            content.AddChild(row);
            var label = NewLabel(value.Name);
            label.TooltipText = value.Description;
            label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            row.AddChild(label);
            var decrease = NewButton("−");
            decrease.Disabled = _specialValues[value.FormId] <= _flow.Character.SpecialMinimum;
            decrease.Pressed += () =>
            {
                _specialValues[value.FormId]--;
                ShowSpecialMenu(completed);
            };
            row.AddChild(decrease);
            var current = NewLabel(_specialValues[value.FormId].ToString());
            current.HorizontalAlignment = HorizontalAlignment.Center;
            row.AddChild(current);
            var increase = NewButton("+");
            increase.Disabled =
                _specialValues[value.FormId] >= _flow.Character.SpecialMaximum ||
                _specialValues.Values.Sum() >= _flow.Character.SpecialTotalPoints;
            increase.Pressed += () =>
            {
                _specialValues[value.FormId]++;
                ShowSpecialMenu(completed);
            };
            row.AddChild(increase);
        }
        var remaining = _flow.Character.SpecialTotalPoints - _specialValues.Values.Sum();
        var counter = NewLabel(remaining.ToString());
        counter.HorizontalAlignment = HorizontalAlignment.Center;
        content.AddChild(counter);
        var footer = new HBoxContainer();
        footer.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        content.AddChild(footer);
        var reset = NewButton(_flow.Strings["reset"]);
        reset.Pressed += () =>
        {
            foreach (var value in _flow.Character.SpecialValues)
                _specialValues[value.FormId] = _flow.Character.SpecialInitial;
            ShowSpecialMenu(completed);
        };
        footer.AddChild(reset);
        var accept = NewButton(_flow.Strings["accept"]);
        accept.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        accept.Disabled = remaining != 0;
        accept.Pressed += () =>
        {
            _docReaction = CalculateDocReaction();
            CloseModal();
            completed();
        };
        footer.AddChild(accept);
    }

    private int CalculateDocReaction()
    {
        var rule = _flow.Character.DocReaction;
        OpeningDocReactionValue? extreme = null;
        var maximumDeviation = float.MinValue;
        var low = false;
        foreach (var value in rule.Values)
        {
            var current = _specialValues[value.FormId];
            var deviation = MathF.Abs(current - rule.AverageValue);
            if (deviation <= maximumDeviation)
                continue;
            maximumDeviation = deviation;
            low = current < rule.AverageValue;
            extreme = value;
        }
        if (extreme is null ||
            maximumDeviation < (low
                ? rule.LowDeviationThreshold
                : rule.HighDeviationThreshold))
            return rule.DefaultReaction;
        return low ? extreme.LowReaction : extreme.HighReaction;
    }

    private void ShowSkillMenu(Action completed)
    {
        if (!_skillDefaultsInitialized)
        {
            foreach (var value in _flow.Character.SkillValues
                .OrderByDescending(value => _psychologyScores.GetValueOrDefault(value.SourceName))
                .Take(_flow.Character.TagSkillMaximumSelected))
                _tagSkills.Add(value.FormId);
            _skillDefaultsInitialized = true;
        }
        ShowSelectionMenu(
            "tagSkills",
            _flow.Character.SkillValues,
            _tagSkills,
            _flow.Character.TagSkillMaximumSelected,
            true,
            completed,
            () => _tagSkills.Clear());
    }

    private void ShowTraitMenu(Action completed) => ShowSelectionMenu(
        "traits",
        _flow.Character.TraitValues,
        _traits,
        _flow.Character.TraitMaximumSelected,
        false,
        completed,
        () => _traits.Clear());

    private void ShowSelectionMenu(
        string role,
        IReadOnlyList<OpeningCharacterValue> values,
        HashSet<string> selected,
        int maximum,
        bool requireMaximum,
        Action completed,
        Action resetSelection)
    {
        var content = OpenPanel(MenuRect(role));
        var title = NewLabel(_flow.Menus[role].MenuName);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        content.AddChild(title);
        var columns = new HBoxContainer();
        columns.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        columns.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        content.AddChild(columns);
        var scroll = new ScrollContainer();
        scroll.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        scroll.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        columns.AddChild(scroll);
        var list = new VBoxContainer();
        list.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        scroll.AddChild(list);
        var details = new VBoxContainer();
        details.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        columns.AddChild(details);
        var selectedValue = values.FirstOrDefault(value => selected.Contains(value.FormId)) ??
            values.First();
        AddCharacterDetails(details, selectedValue);
        foreach (var value in values)
        {
            var button = NewButton(value.Name);
            button.ToggleMode = true;
            button.ButtonPressed = selected.Contains(value.FormId);
            button.TooltipText = value.Description;
            button.Disabled = !button.ButtonPressed && selected.Count >= maximum;
            button.Pressed += () =>
            {
                if (!selected.Remove(value.FormId) && selected.Count < maximum)
                    selected.Add(value.FormId);
                ShowSelectionMenu(
                    role,
                    values,
                    selected,
                    maximum,
                    requireMaximum,
                    completed,
                    resetSelection);
            };
            button.MouseEntered += () =>
            {
                foreach (var child in details.GetChildren())
                    child.QueueFree();
                AddCharacterDetails(details, value);
            };
            list.AddChild(button);
        }
        var count = NewLabel($"{selected.Count}/{maximum}");
        count.HorizontalAlignment = HorizontalAlignment.Center;
        content.AddChild(count);
        var footer = new HBoxContainer();
        footer.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        content.AddChild(footer);
        var reset = NewButton(_flow.Strings["reset"]);
        reset.Pressed += () =>
        {
            resetSelection();
            ShowSelectionMenu(
                role,
                values,
                selected,
                maximum,
                requireMaximum,
                completed,
                resetSelection);
        };
        footer.AddChild(reset);
        var accept = NewButton(_flow.Strings["accept"]);
        accept.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        accept.Disabled = requireMaximum && selected.Count != maximum;
        accept.Pressed += () =>
        {
            CloseModal();
            completed();
        };
        footer.AddChild(accept);
    }

    private void AddCharacterDetails(Node parent, OpeningCharacterValue value)
    {
        if (value.IconPath is not null)
        {
            parent.AddChild(new TextureRect
            {
                Texture = OpeningUiTheme.LoadTexture(value.IconPath),
                ExpandMode = TextureRect.ExpandModeEnum.KeepSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            });
        }
        var name = NewLabel(value.Name);
        name.HorizontalAlignment = HorizontalAlignment.Center;
        parent.AddChild(name);
        var description = NewLabel(value.Description);
        description.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        description.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        parent.AddChild(description);
    }

    private void PlayTopicEditor(string editorId, Action completed, int generation)
    {
        if (!_flow.TopicsByEditorId.TryGetValue(editorId, out var topic))
            throw new InvalidOperationException($"Owned dialogue topic is absent: {editorId}");
        PlayTopic(topic, completed, generation);
    }

    private void PlayTopicForm(string formId, Action completed, int generation)
    {
        if (!_flow.TopicsByFormId.TryGetValue(formId, out var topic))
            throw new InvalidOperationException($"Owned dialogue topic is absent: {formId}");
        PlayTopic(topic, completed, generation);
    }

    private void PlayTopic(OpeningDialogueTopic topic, Action completed, int generation)
    {
        var cursor = _topicCursors.GetValueOrDefault(topic.FormId);
        OpeningDialogueInfo? selected = null;
        while (cursor < topic.Infos.Count)
        {
            var candidate = topic.Infos[cursor++];
            if (candidate.SayOnce && _saidOnce.Contains(candidate.FormId))
                continue;
            if (!candidate.Conditions.All(EvaluateCondition))
                continue;
            selected = candidate;
            break;
        }
        _topicCursors[topic.FormId] = cursor;
        if (selected is null)
        {
            CloseModal();
            completed();
            return;
        }
        if (selected.SayOnce)
            _saidOnce.Add(selected.FormId);
        PlayInfo(selected, topic, completed, generation);
    }

    private void PlayInfo(
        OpeningDialogueInfo info,
        OpeningDialogueTopic? topic,
        Action completed,
        int generation,
        int lineIndex = 0)
    {
        if (generation != _generation)
            return;
        if (lineIndex >= info.Lines.Count)
        {
            ExecuteInfoCommands(info, topic, completed, generation, 0);
            return;
        }
        var content = OpenPanel(MenuRect("name"));
        var guide = NewLabel(_flow.SceneRoles["guideActor"].DisplayName);
        guide.HorizontalAlignment = HorizontalAlignment.Right;
        content.AddChild(guide);
        var line = NewButton(info.Lines[lineIndex]);
        line.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        line.Alignment = HorizontalAlignment.Left;
        line.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        line.Pressed += () => PlayInfo(
            info,
            topic,
            completed,
            generation,
            lineIndex + 1);
        content.AddChild(line);
        Callable.From(line.GrabFocus).CallDeferred();
    }

    private void ExecuteInfoCommands(
        OpeningDialogueInfo info,
        OpeningDialogueTopic? topic,
        Action completed,
        int generation,
        int index)
    {
        if (generation != _generation)
            return;
        if (index >= info.Commands.Count)
        {
            if (info.NextTopicFormIds.Count > 0)
            {
                ShowTopicChoices(info.NextTopicFormIds, completed, generation);
                return;
            }
            if (!info.Goodbye && topic is not null)
            {
                PlayTopic(topic, completed, generation);
                return;
            }
            CloseModal();
            completed();
            return;
        }
        var command = info.Commands[index];
        switch (command.Kind)
        {
            case "actorValueDelta":
                if (command.ValueName is not null && command.Delta is { } delta)
                    _psychologyScores[command.ValueName] =
                        _psychologyScores.GetValueOrDefault(command.ValueName) + delta;
                break;
            case "setQuestVariable":
                if (command.QuestEditorId is not null && command.ValueName is not null &&
                    command.NumericValue is { } numeric)
                    _questVariables[$"{command.QuestEditorId}.{command.ValueName}"] = numeric;
                break;
            case "setDestroyed":
                ApplyDestroyedFromInfo(command);
                break;
            case "additem":
                if (command.ItemEditorId is not null)
                    _inventory[command.ItemEditorId] =
                        _inventory.GetValueOrDefault(command.ItemEditorId) + (command.Count ?? 1);
                break;
            case "setStage":
                if (command.QuestEditorId?.Equals(
                    _flow.QuestEditorId,
                    StringComparison.OrdinalIgnoreCase) == true &&
                    command.Stage is { } nextStage)
                {
                    SetStage(nextStage);
                    return;
                }
                break;
            case "sayTo":
                if (command.TopicEditorId is null)
                    throw new InvalidOperationException("Owned dialogue continuation has no topic.");
                PlayTopicEditor(command.TopicEditorId, completed, generation);
                return;
            case "deferredStage":
                if (command.Stage is { } deferred && command.Seconds is { } seconds)
                {
                    CloseModal();
                    _timerTargetStage = deferred;
                    _timerRemainingSeconds = seconds;
                    return;
                }
                break;
        }
        ExecuteInfoCommands(info, topic, completed, generation, index + 1);
    }

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
        return (condition.OperatorFlags & ConditionOperatorMask) switch
        {
            ConditionEqual => Mathf.IsEqualApprox(actual, condition.ComparisonValue),
            ConditionNotEqual => !Mathf.IsEqualApprox(actual, condition.ComparisonValue),
            ConditionGreater => actual > condition.ComparisonValue,
            ConditionGreaterOrEqual => actual >= condition.ComparisonValue,
            ConditionLess => actual < condition.ComparisonValue,
            ConditionLessOrEqual => actual <= condition.ComparisonValue,
            _ => throw new InvalidOperationException(
                $"Owned dialogue comparison is unsupported: {condition.OperatorFlags}"),
        };
    }

    private void ApplyDestroyedFromInfo(OpeningFlowCommand command)
    {
        if (command.ReferenceEditorId is null || command.Destroyed is null)
            return;
        if (command.Destroyed.Value)
            _destroyedReferences.Add(command.ReferenceEditorId);
        else
            _destroyedReferences.Remove(command.ReferenceEditorId);
    }

    private void CompleteOpening()
    {
        _objective.Visible = false;
        CloseModal();
        _loaded.Player.SetControlPolicy(true, true, true, true, true);
        GD.Print(
            $"OPENNV_NEW_GAME_OPEN_WORLD_READY quest={_flow.QuestEditorId} " +
            $"stage={_stage} name={_playerName} inventory={_inventory.Count}");
    }

    private VBoxContainer OpenPanel(Rect2 rect)
    {
        CloseModal(false);
        var root = new Control
        {
            Name = "OwnedMenu",
            Position = Vector2.Zero,
            Size = _flow.ReferenceCanvasSize,
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        _canvas.AddChild(root);
        _activeModal = root;
        var panel = new Panel
        {
            Position = rect.Position,
            Size = rect.Size,
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        panel.AddThemeStyleboxOverride("panel", OpeningUiTheme.HighlightedStyle(_opening));
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
        _loaded.Player.SetControlPolicy(false, false, false, false, false);
        Input.MouseMode = Input.MouseModeEnum.Visible;
        return content;
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
        OpeningUiTheme.ApplyButton(button, _font, _opening);
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
            OpeningUiTheme.Brightness(
                _opening.MainMenuColor,
                _opening.Style.TextBrightness));
    }

    private void CloseModal(bool restoreControls = true)
    {
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
            _loaded.Player.SetControlPolicy(false, false, false, false, false);
            return;
        }
        if (_stage == _flow.CompletionStage)
        {
            _loaded.Player.SetControlPolicy(true, true, true, true, true);
            return;
        }
        var interaction = _flow.Interactions.FirstOrDefault(value => value.FromStage == _stage);
        _loaded.Player.SetControlPolicy(
            interaction is not null,
            true,
            interaction?.Event.Equals("activate", StringComparison.OrdinalIgnoreCase) == true,
            false,
            false);
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
            (viewportSize - _flow.ReferenceCanvasSize * scale) * OpeningUiTheme.CenteringFactor;
    }
}
