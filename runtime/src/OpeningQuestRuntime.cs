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
    private const int MovementControlIndex = 0;
    private const int PipBoyControlIndex = 1;
    private const int FightingControlIndex = 2;
    private const int PointOfViewControlIndex = 3;
    private const int LookingControlIndex = 4;
    private const int RolloverTextControlIndex = 5;
    private const int SneakingControlIndex = 6;
    private const int PlayerControlCount = 7;
    private const int DisabledControlValue = 0;
    private const int EnabledControlValue = 1;
    private const float TransparentAlpha = 0.0f;

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
    private readonly bool[] _playerControls =
        Enumerable.Repeat(true, PlayerControlCount).ToArray();
    private readonly Dictionary<string, ActiveImageSpaceModifier> _activeImageSpaceModifiers =
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
    private ColorRect _imageSpaceFade = null!;
    private AudioStreamPlayer _dialogueVoice = null!;
    private Action? _dialogueVoiceCompletion;
    private FaceGenMorphController _dialogueFace = null!;
    private FaceGenLipAnimation? _activeDialogueLip;
    private string? _activeDialogueInfoFormId;
    private int _activeDialogueResponseIndex;
    private bool _dialogueLipSampleLogged;
    private int _dialoguePlaybackGeneration;
    private int _stage;
    private int _generation;
    private int? _timerTargetStage;
    private double _timerRemainingSeconds;
    private string _playerName = "";
    private int _sexIndex;
    private int _docReaction;
    private bool _skillDefaultsInitialized;
    private OpeningPlayerPackage? _activePlayerPackage;
    private OpeningPlayerAnimation? _activePlayerAnimation;
    private double _playerAnimationElapsedSeconds;
    private double _packageIdleWaitSeconds;
    private int _playerAnimationSampleIndex;
    private int _packageIdleCursor;
    private bool _activeAnimationIsPackageEvent;
    private bool _packageIdleSequenceComplete;
    private CellActorLoader.PlacedActor _guideActor;
    private bool _guideActorResolved;
    private OpeningGuidePackage? _activeGuidePackage;
    private OpeningGuideLocomotionClip? _activeGuideLocomotion;
    private ActorModelSlice.LoadedAnimation? _activeGuideAnimation;
    private Vector3 _guideDestinationCellUnits;
    private IReadOnlyList<Vector3> _guidePathCellUnits = Array.Empty<Vector3>();
    private int _guidePathIndex;
    private OpeningGuideReference? _guideDestinationReference;
    private bool _guideMoving;
    private bool _guideLookAtPlayer;
    private Action? _guideArrivalContinuation;
    private int _guideArrivalGeneration;
    private bool _openingQuestCompleted;

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

        _dialogueVoice = new AudioStreamPlayer { Name = "OwnedDialogueVoice" };
        _dialogueVoice.Finished += CompleteDialogueVoice;
        AddChild(_dialogueVoice);

        foreach (var value in _flow.Character.SpecialValues)
            _specialValues[value.FormId] = _flow.Character.SpecialInitial;
        ResolveSceneRoles();
        ResolveGuideActor();
        _dialogueFace = new FaceGenMorphController(
            _guideActor.Actor,
            configuration.ActorCompiler.FaceGenAnimation.Lip);
        _loaded.Player.SetExternalActivationHandler(HandleExternalActivation);

        _viewport = new Control { Name = "OpeningFlowViewport" };
        _viewport.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _viewport.Resized += ScaleReferenceCanvas;
        AddChild(_viewport);
        _imageSpaceFade = new ColorRect
        {
            Name = "OwnedImageSpaceFade",
            Color = Colors.Transparent,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _imageSpaceFade.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _viewport.AddChild(_imageSpaceFade);
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
        UpdatePlayerAnimation(delta);
        UpdateImageSpaceModifiers(delta);
        UpdateGuideActor(delta);
        UpdateDialogueVoice();
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

    private void ResolveGuideActor()
    {
        var matches = _loaded.Actors.Where(value =>
                value.ReferenceFormId.Equals(
                    _flow.GuideActorAi.ReferenceFormId,
                    StringComparison.OrdinalIgnoreCase) &&
                value.BaseFormId.Equals(
                    _flow.GuideActorAi.BaseFormId,
                    StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (matches.Length != 1 ||
            !_roleNodes.TryGetValue(_flow.GuideActorAi.Role, out var roleNode) ||
            roleNode != matches[0].Placement)
            throw new InvalidOperationException(
                "Owned opening guide actor is absent or ambiguous in its CELL.");
        _guideActor = matches[0];
        _guideActorResolved = true;
    }

    private void EvaluateGuidePackage(bool force = false)
    {
        if (!_guideActorResolved)
            return;
        var package = _flow.GuideActorAi.PackagePriority
            .Select(formId => _flow.GuideActorAi.Packages[formId])
            .FirstOrDefault(value => value.Conditions.All(EvaluateGuideCondition))
            ?? throw new InvalidOperationException(
                "Owned opening guide has no eligible AI package.");
        if (!force && _activeGuidePackage?.FormId.Equals(
                package.FormId,
                StringComparison.OrdinalIgnoreCase) == true)
            return;
        _activeGuidePackage = package;
        _guideLookAtPlayer = false;
        BeginGuidePackage(package);
        GD.Print(
            $"OPENNV_NEW_GAME_GUIDE_PACKAGE form={package.FormId} " +
            $"editor={package.EditorId} type={package.PackageTypeName} " +
            $"alwaysRun={package.AlwaysRun}");
    }

    private bool EvaluateGuideCondition(OpeningGuideCondition condition)
    {
        float actual;
        if (condition.FunctionName.Equals("getStage", StringComparison.OrdinalIgnoreCase))
        {
            actual = condition.Parameter1.Equals(
                _flow.GuideActorAi.QuestFormId,
                StringComparison.OrdinalIgnoreCase)
                ? _stage
                : 0.0f;
        }
        else if (condition.FunctionName.Equals(
            "getQuestCompleted",
            StringComparison.OrdinalIgnoreCase))
        {
            actual = condition.Parameter1.Equals(
                    _flow.GuideActorAi.QuestFormId,
                    StringComparison.OrdinalIgnoreCase) &&
                _openingQuestCompleted
                    ? 1.0f
                    : 0.0f;
        }
        else if (condition.FunctionName.Equals(
            "getQuestVariable",
            StringComparison.OrdinalIgnoreCase))
        {
            actual = _questVariables.GetValueOrDefault(
                $"{condition.Parameter1}.{condition.Parameter2}");
        }
        else
        {
            throw new InvalidOperationException(
                $"Owned guide condition function is unsupported: {condition.FunctionName}");
        }
        return CompareCondition(
            condition.OperatorFlags,
            actual,
            condition.ComparisonValue);
    }

    private void BeginGuidePackage(OpeningGuidePackage package)
    {
        _guideArrivalContinuation = null;
        _guideDestinationReference = package.Location?.Reference;
        if (_guideDestinationReference is not { } destination)
        {
            _guideMoving = false;
            _guidePathCellUnits = Array.Empty<Vector3>();
            _guidePathIndex = 0;
            _activeGuideLocomotion = null;
            PlayGuidePackageIdle(package);
            return;
        }
        _guideDestinationCellUnits = _loaded.GameToCellUnits(
            destination.PositionGameUnits);
        _activeGuideLocomotion = package.AlwaysRun
            ? _flow.GuideActorAi.Locomotion.Run
            : _flow.GuideActorAi.Locomotion.Walk;
        if (_guideActor.Placement.Position == _guideDestinationCellUnits)
        {
            _guidePathCellUnits = Array.Empty<Vector3>();
            _guidePathIndex = 0;
            _guideMoving = false;
            FinishGuideTravel();
            return;
        }
        _guidePathCellUnits = _loaded.MainContent.Navigation.FindPath(
                _loaded.CellToGameUnits(_guideActor.Placement.Position),
                destination.PositionGameUnits)
            .Select(_loaded.GameToCellUnits)
            .ToArray();
        if (_guidePathCellUnits.Count == 0)
            throw new InvalidOperationException(
                "Owned opening guide navigation returned no waypoints.");
        GD.Print(
            $"OPENNV_NEW_GAME_GUIDE_PATH package={package.EditorId} " +
            $"navmeshes={_loaded.MainContent.Navigation.NavMeshes} " +
            $"vertices={_loaded.MainContent.Navigation.Vertices} " +
            $"triangles={_loaded.MainContent.Navigation.Triangles} " +
            $"waypoints={_guidePathCellUnits.Count}");
        _guidePathIndex = 0;
        _guideDestinationCellUnits = _guidePathCellUnits[_guidePathIndex];
        _guideMoving = true;
        PlayGuideAnimation(
            _activeGuideLocomotion.LogicalPath,
            _activeGuideLocomotion.Sha256,
            restart: true);
    }

    private void UpdateGuideActor(double delta)
    {
        if (!_guideActorResolved)
            return;
        if (!_guideMoving)
        {
            if (_guideLookAtPlayer)
                FaceGuideToward(_loaded.Player.GlobalPosition);
            return;
        }
        if (_activeGuideLocomotion is not { } locomotion)
            throw new InvalidOperationException(
                "Owned opening guide is moving without locomotion data.");
        if (_activeGuideAnimation is not { } animation || !animation.Player.IsPlaying())
            PlayGuideAnimation(
                locomotion.LogicalPath,
                locomotion.Sha256,
                restart: true);
        var travelRemaining =
            locomotion.RootMotion.SpeedGameUnitsPerSecond * (float)delta;
        while (_guideMoving)
        {
            var current = _guideActor.Placement.Position;
            var offset = _guideDestinationCellUnits - current;
            var distance = offset.Length();
            if (travelRemaining < distance)
            {
                _guideActor.Placement.Position =
                    current + offset / distance * travelRemaining;
                FaceGuideTowardCellPosition(_guideDestinationCellUnits);
                return;
            }
            _guideActor.Placement.Position = _guideDestinationCellUnits;
            travelRemaining -= distance;
            _guidePathIndex++;
            if (_guidePathIndex >= _guidePathCellUnits.Count)
            {
                FinishGuideTravel();
                return;
            }
            _guideDestinationCellUnits = _guidePathCellUnits[_guidePathIndex];
            FaceGuideTowardCellPosition(_guideDestinationCellUnits);
        }
    }

    private void FinishGuideTravel()
    {
        _guideMoving = false;
        _activeGuideLocomotion = null;
        if (_guideDestinationReference is { } destination)
            _guideActor.Placement.Basis = new Basis(destination.RotationGodot);
        if (_guideLookAtPlayer)
            FaceGuideToward(_loaded.Player.GlobalPosition);
        if (_activeGuidePackage is { } package)
            PlayGuidePackageIdle(package);
        GD.Print(
            $"OPENNV_NEW_GAME_GUIDE_ARRIVED package={_activeGuidePackage?.EditorId} " +
            $"position={_guideActor.Placement.Position}");
        if (_guideArrivalContinuation is not { } continuation)
            return;
        var generation = _guideArrivalGeneration;
        _guideArrivalContinuation = null;
        Callable.From(() =>
        {
            if (generation == _generation)
                continuation();
        }).CallDeferred();
    }

    private void PlayGuidePackageIdle(OpeningGuidePackage package)
    {
        var path = package.IdleAnimationLogicalPaths.FirstOrDefault()
            ?? _guideActor.IdleAnimationPath;
        PlayGuideAnimation(path, expectedSha256: null, restart: true);
        _activeGuideAnimation = null;
    }

    private void PlayGuideAnimation(
        string logicalPath,
        string? expectedSha256,
        bool restart)
    {
        var expected = ActorModelSlice.NormalizeAnimationPath(logicalPath);
        var matches = _guideActor.Actor.LoadedAnimations.Where(animation =>
                ActorModelSlice.NormalizeAnimationPath(animation.LogicalPath).Equals(
                    expected,
                    StringComparison.OrdinalIgnoreCase) &&
                (expectedSha256 is null || animation.SourceSha256.Equals(
                    expectedSha256,
                    StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        if (matches.Length != 1)
            throw new InvalidOperationException(
                $"Owned guide animation is absent or ambiguous: {logicalPath}");
        var animation = matches[0];
        if (restart || !animation.Player.IsPlaying() ||
            !animation.Player.CurrentAnimation.ToString().Equals(
                animation.RuntimeName,
                StringComparison.Ordinal))
        {
            animation.Player.Play(animation.RuntimeName);
            animation.Player.Advance(0.0);
        }
        _activeGuideAnimation = animation;
    }

    private void FaceGuideTowardCellPosition(Vector3 target)
    {
        var current = _guideActor.Placement.Position;
        FaceGuideToward(_loaded.Root.ToGlobal(
            new Vector3(target.X, current.Y, target.Z)));
    }

    private void FaceGuideToward(Vector3 globalTarget)
    {
        var origin = _guideActor.Placement.GlobalPosition;
        var levelTarget = new Vector3(globalTarget.X, origin.Y, globalTarget.Z);
        if (origin != levelTarget)
            _guideActor.Placement.LookAt(levelTarget, Vector3.Up);
    }

    private void RunWhenGuideReady(Action continuation, int generation)
    {
        _guideLookAtPlayer = true;
        if (_guideMoving)
        {
            _guideArrivalContinuation = continuation;
            _guideArrivalGeneration = generation;
            return;
        }
        FaceGuideToward(_loaded.Player.GlobalPosition);
        continuation();
    }

    private bool IsGuideSpeaker(OpeningFlowCommand command) =>
        command.SpeakerEditorId is { } speaker &&
        _flow.SceneRoles.TryGetValue(_flow.GuideActorAi.Role, out var role) &&
        role.EditorId.Equals(speaker, StringComparison.OrdinalIgnoreCase);

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
        _guideArrivalContinuation = null;
        CloseModal();
        ApplyStageControlPolicy();
        EvaluateGuidePackage();
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
                RunWhenGuideReady(
                    () => PlayTopicForm(_flow.OutroTopicFormId, () => { }, generation),
                    generation);
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
                if (IsGuideSpeaker(command))
                    RunWhenGuideReady(
                        () => PlayTopicEditor(command.TopicEditorId, () => Next(), generation),
                        generation);
                else
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
            case "playerControls":
                ApplyPlayerControls(command);
                Next();
                return;
            case "addScriptPackage":
            case "removeScriptPackage":
                ApplyScriptPackage(command);
                Next();
                return;
            case "imageSpaceModifier":
                ApplyImageSpaceModifier(command);
                Next();
                return;
            case "additem":
            case "removeitem":
            case "equipitem":
                ApplyInventoryCommand(command);
                Next();
                return;
            case "referenceEnabled":
                ApplyReferenceEnabled(command);
                Next();
                return;
            case "actorIntent":
                ApplyActorIntent(command);
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

    private void ApplyReferenceEnabled(OpeningFlowCommand command)
    {
        if (command.ReferenceEditorId is null || command.Enabled is null)
            return;
        foreach (var role in _flow.SceneRoles.Values.Where(role =>
            role.EditorId.Equals(
                command.ReferenceEditorId,
                StringComparison.OrdinalIgnoreCase)))
        {
            if (_roleNodes.TryGetValue(role.Role, out var node))
                node.Visible = command.Enabled.Value;
        }
        GD.Print(
            $"OPENNV_NEW_GAME_REFERENCE reference={command.ReferenceEditorId} " +
            $"enabled={command.Enabled.Value}");
    }

    private void ApplyActorIntent(OpeningFlowCommand command)
    {
        if (command.ReferenceEditorId is { } reference &&
            _flow.SceneRoles.TryGetValue(_flow.GuideActorAi.Role, out var role) &&
            role.EditorId.Equals(reference, StringComparison.OrdinalIgnoreCase))
        {
            if (command.Operation?.Equals("look", StringComparison.OrdinalIgnoreCase) == true)
            {
                _guideLookAtPlayer = true;
                if (!_guideMoving)
                    FaceGuideToward(_loaded.Player.GlobalPosition);
            }
            else if (command.Operation?.Equals(
                "stoplook",
                StringComparison.OrdinalIgnoreCase) == true)
            {
                _guideLookAtPlayer = false;
                if (!_guideMoving && _guideDestinationReference is { } destination)
                    _guideActor.Placement.Basis = new Basis(destination.RotationGodot);
            }
            else if (command.Operation?.Equals(
                    "evp",
                    StringComparison.OrdinalIgnoreCase) == true ||
                command.Operation?.Equals(
                    "resetai",
                    StringComparison.OrdinalIgnoreCase) == true)
            {
                EvaluateGuidePackage(force: true);
            }
        }
        GD.Print(
            $"OPENNV_NEW_GAME_ACTOR_INTENT reference={command.ReferenceEditorId} " +
            $"operation={command.Operation} target={command.TargetEditorId}");
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
        if (actor.ReferenceFormId.Equals(
            _flow.GuideActorAi.ReferenceFormId,
            StringComparison.OrdinalIgnoreCase))
            _activeGuideAnimation = null;
        GD.Print(
            $"OPENNV_NEW_GAME_IDLE source={command.ReferenceEditorId} " +
            $"authored={command.IdleEditorId} runtime={animation.RuntimeName}");
    }

    private void ApplyPlayerControls(OpeningFlowCommand command)
    {
        if (command.Operation is null || command.ControlValues.Count > PlayerControlCount ||
            command.ControlValues.Any(value =>
                value is not DisabledControlValue and not EnabledControlValue))
            throw new InvalidOperationException("Owned player-control command is invalid.");
        var enabled = command.Operation.Equals("enable", StringComparison.OrdinalIgnoreCase);
        var disabled = command.Operation.Equals("disable", StringComparison.OrdinalIgnoreCase);
        if (!enabled && !disabled)
            throw new InvalidOperationException(
                $"Owned player-control operation is unsupported: {command.Operation}");
        for (var index = 0; index < command.ControlValues.Count; index++)
        {
            if (command.ControlValues[index] == EnabledControlValue)
                _playerControls[index] = enabled;
        }
        ApplyStageControlPolicy();
        GD.Print(
            $"OPENNV_NEW_GAME_CONTROLS operation={command.Operation} " +
            $"movement={_playerControls[MovementControlIndex]} " +
            $"pipboy={_playerControls[PipBoyControlIndex]} " +
            $"fighting={_playerControls[FightingControlIndex]} " +
            $"pov={_playerControls[PointOfViewControlIndex]} " +
            $"looking={_playerControls[LookingControlIndex]} " +
            $"rollover={_playerControls[RolloverTextControlIndex]} " +
            $"sneaking={_playerControls[SneakingControlIndex]}");
    }

    private void ApplyScriptPackage(OpeningFlowCommand command)
    {
        if (command.Kind == "removeScriptPackage")
        {
            _activePlayerPackage = null;
            _activePlayerAnimation = null;
            _packageIdleWaitSeconds = 0.0;
            GD.Print("OPENNV_NEW_GAME_PLAYER_PACKAGE operation=remove");
            return;
        }
        if (command.PackageEditorId is null ||
            !_flow.PlayerAnimation.Packages.TryGetValue(
                command.PackageEditorId,
                out var package))
            throw new InvalidOperationException(
                $"Owned player package is absent: {command.PackageEditorId}");
        var eventName = _activePlayerPackage?.EditorId.Equals(
            package.EditorId,
            StringComparison.OrdinalIgnoreCase) == true
            ? "change"
            : "begin";
        _activePlayerPackage = package;
        _packageIdleCursor = 0;
        _packageIdleSequenceComplete = false;
        _packageIdleWaitSeconds = 0.0;
        if (package.EventAnimationFormIds.TryGetValue(eventName, out var formId) &&
            formId is not null)
        {
            var idleIndex = package.IdleAnimationFormIds
                .Select((value, index) => (value, index))
                .FirstOrDefault(value => value.value.Equals(
                    formId,
                    StringComparison.OrdinalIgnoreCase));
            if (idleIndex.value is not null)
                _packageIdleCursor = idleIndex.index + 1;
            StartPlayerAnimation(formId, true);
        }
        else
            StartNextPackageIdle();
        GD.Print(
            $"OPENNV_NEW_GAME_PLAYER_PACKAGE operation=add " +
            $"package={package.EditorId} event={eventName}");
    }

    private void StartNextPackageIdle()
    {
        var package = _activePlayerPackage;
        if (package is null || package.IdleAnimationFormIds.Count == 0 ||
            _packageIdleSequenceComplete)
            return;
        if (!package.RunInSequence && package.IdleAnimationFormIds.Count > 1)
            throw new InvalidOperationException(
                "Owned opening package random idle selection requires a retail RNG state.");
        if (_packageIdleCursor >= package.IdleAnimationFormIds.Count)
            _packageIdleCursor = 0;
        var formId = package.IdleAnimationFormIds[_packageIdleCursor++];
        StartPlayerAnimation(formId, false);
    }

    private void StartPlayerAnimation(string formId, bool packageEvent)
    {
        if (!_flow.PlayerAnimation.Animations.TryGetValue(formId, out var animation))
            throw new InvalidOperationException(
                $"Owned player animation is absent: {formId}");
        _activePlayerAnimation = animation;
        _activeAnimationIsPackageEvent = packageEvent;
        _playerAnimationElapsedSeconds = 0.0;
        _playerAnimationSampleIndex = 0;
        ApplyPlayerAnimationSample(animation.Track.StartSeconds);
        GD.Print(
            $"OPENNV_NEW_GAME_PLAYER_ANIMATION form={animation.FormId} " +
            $"authored={animation.EditorId} seconds={animation.Track.StopSeconds:F6}");
    }

    private void UpdatePlayerAnimation(double delta)
    {
        if (_activePlayerAnimation is null)
        {
            if (_activePlayerPackage is null || _packageIdleWaitSeconds <= 0.0)
                return;
            _packageIdleWaitSeconds -= delta;
            if (_packageIdleWaitSeconds <= 0.0)
                StartNextPackageIdle();
            return;
        }
        _playerAnimationElapsedSeconds += delta;
        var track = _activePlayerAnimation.Track;
        var time = MathF.Min(
            track.StopSeconds,
            track.StartSeconds + (float)_playerAnimationElapsedSeconds);
        ApplyPlayerAnimationSample(time);
        if (time < track.StopSeconds)
            return;

        _activePlayerAnimation = null;
        var package = _activePlayerPackage;
        if (package is null)
            return;
        if (_activeAnimationIsPackageEvent)
        {
            _activeAnimationIsPackageEvent = false;
            StartNextPackageIdle();
            return;
        }
        if (package.RunInSequence && _packageIdleCursor < package.IdleAnimationFormIds.Count)
        {
            StartNextPackageIdle();
            return;
        }
        if (package.DoOnce)
        {
            _packageIdleSequenceComplete = true;
            return;
        }
        _packageIdleCursor = 0;
        _packageIdleWaitSeconds = package.IdleTimerSeconds;
        if (_packageIdleWaitSeconds <= 0.0)
            StartNextPackageIdle();
    }

    private void ApplyPlayerAnimationSample(float time)
    {
        var animation = _activePlayerAnimation ??
            throw new InvalidOperationException("Owned player animation is not active.");
        var track = animation.Track;
        while (_playerAnimationSampleIndex + 1 < track.Samples.Count &&
            track.Samples[_playerAnimationSampleIndex + 1].TimeSeconds <= time)
            _playerAnimationSampleIndex++;
        var first = track.Samples[_playerAnimationSampleIndex];
        var second = track.Samples[Math.Min(
            _playerAnimationSampleIndex + 1,
            track.Samples.Count - 1)];
        var amount = second.TimeSeconds <= first.TimeSeconds
            ? 0.0f
            : (time - first.TimeSeconds) / (second.TimeSeconds - first.TimeSeconds);
        var translation = first.TranslationGodotGameUnits.Lerp(
            second.TranslationGodotGameUnits,
            amount);
        var rotation = first.Rotation.Slerp(second.Rotation, amount).Normalized();
        var parentTransform = Transform3D.Identity;
        foreach (var parent in track.ParentChain)
        {
            parentTransform *= new Transform3D(
                new Basis(parent.Rotation).Scaled(parent.Scale),
                parent.TranslationGodotGameUnits * _loaded.UnitsToMeters);
        }
        var result = parentTransform * new Transform3D(
            new Basis(rotation),
            translation * _loaded.UnitsToMeters);
        _loaded.Player.ApplyAuthoredCameraTransform(
            new Transform3D(result.Basis.Orthonormalized(), result.Origin));
    }

    private void ApplyImageSpaceModifier(OpeningFlowCommand command)
    {
        if (command.ModifierEditorId is null || command.Operation is null ||
            !_flow.ImageSpaceModifiers.TryGetValue(
                command.ModifierEditorId,
                out var modifier))
            throw new InvalidOperationException(
                $"Owned image-space modifier is absent: {command.ModifierEditorId}");
        if (command.Operation.Equals("remove", StringComparison.OrdinalIgnoreCase))
            _activeImageSpaceModifiers.Remove(modifier.EditorId);
        else if (command.Operation.Equals("apply", StringComparison.OrdinalIgnoreCase))
            _activeImageSpaceModifiers[modifier.EditorId] =
                new ActiveImageSpaceModifier(modifier);
        else
            throw new InvalidOperationException(
                $"Owned image-space operation is unsupported: {command.Operation}");
        UpdateImageSpaceFade();
        GD.Print(
            $"OPENNV_NEW_GAME_IMAGE_SPACE operation={command.Operation} " +
            $"modifier={modifier.EditorId} crossFade={command.CrossFade == true}");
    }

    private void UpdateImageSpaceModifiers(double delta)
    {
        foreach (var active in _activeImageSpaceModifiers.Values)
            active.ElapsedSeconds += delta;
        foreach (var editorId in _activeImageSpaceModifiers
            .Where(value => value.Value.ElapsedSeconds >= value.Value.Modifier.DurationSeconds)
            .Select(value => value.Key)
            .ToArray())
            _activeImageSpaceModifiers.Remove(editorId);
        UpdateImageSpaceFade();
    }

    private void UpdateImageSpaceFade()
    {
        var colorNumerator = Vector3.Zero;
        var colorWeight = 0.0f;
        var strongestAlpha = TransparentAlpha;
        foreach (var active in _activeImageSpaceModifiers.Values)
        {
            var modifier = active.Modifier;
            var normalizedTime = modifier.DurationSeconds <= 0.0f
                ? 1.0f
                : Mathf.Clamp(
                    (float)(active.ElapsedSeconds / modifier.DurationSeconds),
                    0.0f,
                    1.0f);
            var fade = EvaluateFade(modifier.Fade, normalizedTime);
            var weight = MathF.Max(TransparentAlpha, fade.A);
            colorNumerator += new Vector3(fade.R, fade.G, fade.B) * weight;
            colorWeight += weight;
            strongestAlpha = MathF.Max(strongestAlpha, weight);
        }
        _imageSpaceFade.Color = colorWeight <= TransparentAlpha
            ? Colors.Transparent
            : new Color(
                colorNumerator.X / colorWeight,
                colorNumerator.Y / colorWeight,
                colorNumerator.Z / colorWeight,
                strongestAlpha);
    }

    private static Color EvaluateFade(
        IReadOnlyList<OpeningImageSpaceFadeKey> keys,
        float time)
    {
        if (keys.Count == 0)
            return Colors.Transparent;
        if (time <= keys[0].Time)
            return keys[0].Color;
        if (time >= keys[^1].Time)
            return keys[^1].Color;
        foreach (var pair in keys.Zip(keys.Skip(1)))
        {
            if (time < pair.First.Time || time > pair.Second.Time)
                continue;
            var amount = (time - pair.First.Time) / (pair.Second.Time - pair.First.Time);
            return pair.First.Color.Lerp(pair.Second.Color, amount);
        }
        throw new InvalidOperationException("Owned image-space fade interval is absent.");
    }

    private void ApplyInventoryCommand(OpeningFlowCommand command)
    {
        if (command.ItemEditorId is null)
            return;
        var count = command.Count ?? 1;
        if (command.Kind == "removeitem")
        {
            var remaining = _inventory.GetValueOrDefault(command.ItemEditorId) - count;
            if (remaining > 0)
                _inventory[command.ItemEditorId] = remaining;
            else
                _inventory.Remove(command.ItemEditorId);
            return;
        }
        if (command.Kind == "additem")
            _inventory[command.ItemEditorId] =
                _inventory.GetValueOrDefault(command.ItemEditorId) + count;
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
        if (lineIndex >= info.Responses.Count)
        {
            ExecuteInfoCommands(info, topic, completed, generation, 0);
            return;
        }
        var response = info.Responses[lineIndex];
        var content = OpenPanel(MenuRect("name"));
        var guide = NewLabel(
            _flow.SceneRoles[_flow.DialogueVoice.SpeakerRole].DisplayName);
        guide.HorizontalAlignment = HorizontalAlignment.Right;
        content.AddChild(guide);
        var line = NewButton(response.Text);
        line.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        line.Alignment = HorizontalAlignment.Left;
        line.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        line.Pressed += CompleteDialogueVoice;
        content.AddChild(line);
        StartDialogueVoice(
            response,
            info.FormId,
            generation,
            () => PlayInfo(
                info,
                topic,
                completed,
                generation,
                lineIndex + 1));
        Callable.From(line.GrabFocus).CallDeferred();
    }

    private void StartDialogueVoice(
        OpeningDialogueResponse response,
        string infoFormId,
        int flowGeneration,
        Action completed)
    {
        StopDialogueVoice();
        var stream = AudioStreamOggVorbis.LoadFromFile(response.Voice.SourcePath)
            ?? throw new InvalidOperationException(
                $"Owned dialogue voice could not be decoded: {response.Voice.LogicalPath}");
        var durationSeconds = stream.GetLength();
        if (!double.IsFinite(durationSeconds) || durationSeconds <= 0.0)
            throw new InvalidOperationException(
                $"Owned dialogue voice has no duration: {response.Voice.LogicalPath}");
        var lip = FaceGenLipAnimation.Load(
            response.Lip.SourcePath,
            _configuration.ActorCompiler.FaceGenAnimation.Lip);
        var playbackGeneration = ++_dialoguePlaybackGeneration;
        _dialogueVoice.Stream = stream;
        _activeDialogueLip = lip;
        _activeDialogueInfoFormId = infoFormId;
        _activeDialogueResponseIndex = response.Index;
        _dialogueLipSampleLogged = false;
        _dialogueVoiceCompletion = () =>
        {
            if (playbackGeneration != _dialoguePlaybackGeneration ||
                flowGeneration != _generation)
                return;
            StopDialogueVoice();
            completed();
        };
        _dialogueVoice.Play();
        GD.Print(
            $"OPENNV_NEW_GAME_DIALOGUE_VOICE info={infoFormId} " +
            $"line={response.Index} duration={durationSeconds:F3} " +
            $"voice={response.Voice.LogicalPath} lip={response.Lip.LogicalPath}");
        GD.Print(
            $"OPENNV_NEW_GAME_DIALOGUE_LIP_LOADED info={infoFormId} " +
            $"line={response.Index} frames={lip.FrameCount} startFrame={lip.StartFrame} " +
            $"metadata=0x{lip.MetadataWord:x8}");
    }

    private void UpdateDialogueVoice()
    {
        if (_dialogueVoiceCompletion is null ||
            _activeDialogueLip is null ||
            !_dialogueVoice.Playing)
            return;
        var seconds = _dialogueVoice.GetPlaybackPosition();
        var dominant = _dialogueFace.Apply(_activeDialogueLip, seconds);
        if (!_dialogueLipSampleLogged && dominant.Value != 0.0f)
        {
            _dialogueLipSampleLogged = true;
            GD.Print(
                $"OPENNV_NEW_GAME_DIALOGUE_LIP_SAMPLE info={_activeDialogueInfoFormId} " +
                $"line={_activeDialogueResponseIndex} seconds={seconds:F3} " +
                $"target={dominant.Target} value={dominant.Value:F6}");
        }
    }

    private void CompleteDialogueVoice()
    {
        var completed = _dialogueVoiceCompletion;
        _dialogueVoiceCompletion = null;
        completed?.Invoke();
    }

    private void StopDialogueVoice()
    {
        _dialogueVoiceCompletion = null;
        _dialogueFace?.Clear();
        _activeDialogueLip = null;
        _activeDialogueInfoFormId = null;
        _activeDialogueResponseIndex = 0;
        _dialogueLipSampleLogged = false;
        _dialoguePlaybackGeneration++;
        if (_dialogueVoice is not null && _dialogueVoice.Playing)
            _dialogueVoice.Stop();
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
                if (IsGuideSpeaker(command))
                    RunWhenGuideReady(
                        () => PlayTopicEditor(command.TopicEditorId, completed, generation),
                        generation);
                else
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
        _openingQuestCompleted = true;
        EvaluateGuidePackage();
        _objective.Visible = false;
        CloseModal();
        ApplyStageControlPolicy();
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
        StopDialogueVoice();
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
        _loaded.Player.SetControlPolicy(
            _playerControls[MovementControlIndex],
            _playerControls[LookingControlIndex],
            _playerControls[MovementControlIndex] &&
                _playerControls[RolloverTextControlIndex],
            _playerControls[FightingControlIndex],
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
            (viewportSize - _flow.ReferenceCanvasSize * scale) * OpeningUiTheme.CenteringFactor;
    }

    private sealed class ActiveImageSpaceModifier(OpeningImageSpaceModifier modifier)
    {
        internal OpeningImageSpaceModifier Modifier { get; } = modifier;
        internal double ElapsedSeconds { get; set; }
    }
}
