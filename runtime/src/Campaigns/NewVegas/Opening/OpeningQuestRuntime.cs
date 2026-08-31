using System.Buffers.Binary;
using System.Security.Cryptography;
using Godot;
using OpenNV.Runtime.Presentation.CharacterCreation;
using OpenNV.Runtime.Presentation.Ui;
using OpenNV.Runtime.World.Actors;

namespace OpenNV.Runtime.Campaigns.NewVegas.Opening;

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
    private const double MillisecondsPerSecond = 1000.0;
    private const string RetainedAccumulationRootTranslation =
        "preserve-hash-bound-owned-clip-root-curve";
    private const string ZeroedAccumulationRootTranslation =
        "owned-world-root-authoritative-zero-local-translation";
    private const string RaceSexSliderPreviousEngineLabel = "<";
    private const string RaceSexSliderNextEngineLabel = ">";
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
    private readonly Dictionary<string, OpeningInventoryState> _inventory =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _equippedItemFormIds =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, OpeningQuestState> _quests =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, OpeningGlobalState> _globals =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, OpeningObjectiveState> _objectives =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, bool> _referenceEnabledStates =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, float> _faceGeometryControlValues =
        new(StringComparer.Ordinal);
    private readonly HashSet<int> _achievements = [];
    private readonly bool[] _playerControls =
        Enumerable.Repeat(true, PlayerControlCount).ToArray();
    private readonly Dictionary<string, ActiveImageSpaceModifier> _activeImageSpaceModifiers =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IReadOnlyList<MeshInstance3D>>
        _guideAnimationObjectSurfaces = new(StringComparer.OrdinalIgnoreCase);

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
    private string _raceFormId = "";
    private string _hairFormId = "";
    private string _eyesFormId = "";
    private int _docReaction;
    private bool _skillDefaultsInitialized;
    private OpeningPlayerPackage? _activePlayerPackage;
    private OpeningPlayerAnimation? _activePlayerAnimation;
    private double _playerAnimationElapsedSeconds;
    private double _packageIdleWaitSeconds;
    private int _playerAnimationSampleIndex;
    private OpeningPlayerAnimation? _lastAppliedPlayerCameraAnimation;
    private float _lastAppliedPlayerCameraTime;
    private int _packageIdleCursor;
    private bool _activeAnimationIsPackageEvent;
    private bool _packageIdleSequenceComplete;
    private CellActorLoader.PlacedActor _guideActor;
    private bool _guideActorResolved;
    private OpeningGuidePackage? _activeGuidePackage;
    private OpeningGuideLocomotionClip? _activeGuideLocomotion;
    private ActorModelSlice.LoadedAnimation? _activeGuideAnimation;
    private ActorModelSlice.LoadedAnimation? _activeGuideIdleAnimation;
    private OpeningGuidePriorityAnimation.LayeredPlayback?
        _guideFurnitureLayeredSeatedAnimation;
    private string? _guideAnimationObjectIdleFormId;
    private OpeningCigaretteSmokePresentation? _guideCigaretteSmokePresentation;
    private Vector3 _guideDestinationCellUnits;
    private IReadOnlyList<Vector3> _guidePathCellUnits = Array.Empty<Vector3>();
    private int _guidePathIndex;
    private OpeningGuideReference? _guideDestinationReference;
    private bool _guideMoving;
    private bool _guidePackageBegan;
    private bool _guideFurnitureOccupied;
    private bool _guideFurnitureExiting;
    private bool _guideFurnitureExitRootMotionApplied;
    private string? _guideFurnitureReferenceFormId;
    private OpeningGuidePackage? _guideFurnitureExitPackage;
    private bool _guideLookAtPlayer;
    private Action? _guideArrivalContinuation;
    private int _guideArrivalGeneration;
    private bool _openingQuestCompleted;
    private bool _autoDisplayObjectives;
    private AcceptanceAppearancePhase _acceptanceAppearancePhase;
    private OpeningPlayerFaceGenPreviewHost? _appearancePreviewHost;
    private OpeningRaceSexMenuHost? _raceSexMenuHost;
    private OpeningRaceSexRenderedDeviceHost? _raceSexRenderedDeviceHost;
    private Action? _raceSexShowSex;
    private Action? _raceSexShowFace;
    private Action? _raceSexShowBody;
    private CharacterBodyProportions _bodyProportions =
        CharacterBodyProportions.Neutral("fnv-custom-live-v1");
    private string _appearancePreviewMode = "3d";
    private bool _appearancePreviewFaceFraming = true;
    private bool _visualCaptureActive;
    private bool _docSpatialAcceptancePassed;

    internal int Stage => _stage;
    internal string PlayerName => _playerName;

    internal async Task<OpeningCampaignState> RunAcceptance(
        string mode,
        string playerName,
        double timeoutSeconds,
        string? captureRoot = null,
        int appearancePresentationHoldFrames = 0)
    {
        var stopAtCheckpoint = mode.Equals("checkpoint", StringComparison.OrdinalIgnoreCase);
        var stopAfterCreator = mode.Equals("creator", StringComparison.OrdinalIgnoreCase);
        var completeAfterResume = mode.Equals("resume", StringComparison.OrdinalIgnoreCase);
        if ((!stopAtCheckpoint && !stopAfterCreator && !completeAfterResume) ||
            string.IsNullOrWhiteSpace(playerName) || timeoutSeconds <= 0.0)
            throw new ArgumentException("Opening acceptance arguments are invalid.");
        var initialState = _loaded.Session.OpeningState;
        if (completeAfterResume && initialState is not { Completed: false })
            throw new InvalidOperationException(
                "Opening resume acceptance requires an incomplete saved opening.");
        var checkpointStage = AuthoredCheckpointStage();
        var proveFirstPlayerAction = completeAfterResume &&
            initialState!.Stage == checkpointStage;
        var startMilliseconds = Time.GetTicksMsec();
        var navigationStage = int.MinValue;
        IReadOnlyList<Vector3> navigationPath = Array.Empty<Vector3>();
        var navigationIndex = 0;
        DesktopKeyBindingConfiguration? movementHeld = null;
        DesktopKeyBindingConfiguration? firstPlayerAction = null;
        Vector3? firstPlayerActionOrigin = null;
        var firstPlayerActionProven = !proveFirstPlayerAction;
        var requireDocSpatialAcceptance = (stopAtCheckpoint || stopAfterCreator) &&
            _stage < OpeningVisualCaptureSession.DocSpatialAcceptanceDeadlineStage;
        var latestDocSpatialTelemetry = "not-yet-observed";
        var appearancePresentationSignature = string.Empty;
        var appearancePresentationFramesRemaining = 0;
        var visualCapture = OpeningVisualCaptureSession.Create(
            this,
            captureRoot,
            mode,
            playerName);
        _visualCaptureActive = visualCapture is not null;
        var activateHeld = false;
        try
        {
            while (true)
            {
                if (activateHeld)
                {
                    Input.ParseInputEvent(DesktopInputMap.CreateEvent(
                        _configuration.Player.DesktopInput.Activate,
                        false));
                    activateHeld = false;
                }
                if (stopAfterCreator &&
                    _stage == AuthoredAppearanceReturnStage() &&
                    _activeModal is null &&
                    _acceptanceAppearancePhase == AcceptanceAppearancePhase.Complete)
                {
                    var creatorState = CaptureState(false);
                    _loaded.Session.StoreOpeningState(creatorState);
                    if (visualCapture is not null)
                    {
                        await visualCapture.CaptureCreatorAccepted(this);
                        VisualProofReportPath = visualCapture.Complete(this, creatorState);
                    }
                    return creatorState;
                }
                var saved = _loaded.Session.OpeningState;
                if (stopAtCheckpoint && saved is { Completed: false })
                {
                    ValidateCheckpointState(saved);
                    if (visualCapture is not null)
                    {
                        await visualCapture.CaptureCheckpointRelease(this);
                        VisualProofReportPath = visualCapture.Complete(this, saved);
                    }
                    return saved;
                }
                if (completeAfterResume && saved is { Completed: true })
                {
                    if (!firstPlayerActionProven)
                        throw new InvalidOperationException(
                            "Opening completed without a configured first player action.");
                    ValidateCompletedState(_flow, saved);
                    if (visualCapture is not null)
                        VisualProofReportPath = visualCapture.Complete(this, saved);
                    return saved;
                }
                var elapsedSeconds =
                    (Time.GetTicksMsec() - startMilliseconds) / MillisecondsPerSecond;
                if (elapsedSeconds > timeoutSeconds)
                    throw new TimeoutException(
                        $"Opening acceptance timed out at stage {_stage} after {elapsedSeconds:F1}s.");

                await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                if (requireDocSpatialAcceptance &&
                    !_docSpatialAcceptancePassed)
                {
                    if (OpeningVisualCaptureSession.TryValidateDocSpatial(
                            this,
                            out var docSpatialTelemetry))
                    {
                        _docSpatialAcceptancePassed = true;
                        latestDocSpatialTelemetry = docSpatialTelemetry;
                        GD.Print(
                            "OPENNV_OPENING_DOC_SPATIAL_PASS " +
                            docSpatialTelemetry);
                    }
                    else if (!string.IsNullOrWhiteSpace(docSpatialTelemetry))
                    {
                        latestDocSpatialTelemetry = docSpatialTelemetry;
                        throw new InvalidOperationException(
                            "Opening deterministic stage-8 source pose intersects the " +
                            $"owned patient-bed collision volume: {docSpatialTelemetry}.");
                    }
                }
                if (requireDocSpatialAcceptance &&
                    !_docSpatialAcceptancePassed &&
                    _stage >= OpeningVisualCaptureSession.DocSpatialAcceptanceDeadlineStage)
                    throw new InvalidOperationException(
                        "Opening source-driven seated pose never cleared the owned patient " +
                        $"bed before stage {_stage}: {latestDocSpatialTelemetry}.");
                if (!firstPlayerActionProven &&
                    firstPlayerAction is not null &&
                    firstPlayerActionOrigin is { } actionOrigin)
                {
                    var progress = HorizontalDistance(
                        actionOrigin,
                        _loaded.Player.GlobalPosition);
                    var minimum = _configuration.Player.DesktopInput.Acceptance
                        .MinimumLocomotionMeters;
                    if (progress >= minimum)
                    {
                        SetAcceptanceMovement(null, ref movementHeld);
                        firstPlayerActionProven = true;
                        GD.Print(
                            $"OPENNV_OPENING_FIRST_PLAYER_ACTION_PASS " +
                            $"fromStage={checkpointStage} observedStage={_stage} " +
                            $"action={firstPlayerAction.Action} " +
                            $"physicalKey={firstPlayerAction.PhysicalKey} " +
                            $"before={actionOrigin} after={_loaded.Player.GlobalPosition} " +
                            $"distanceMeters={progress:F3} minimumMeters={minimum:F3} " +
                            "transport=configured-desktop-input-event " +
                            "movement=owned-navigation");
                        if (visualCapture is not null)
                            await visualCapture.CaptureFirstAction(this, progress, minimum);
                    }
                }
                if (visualCapture is not null &&
                    await visualCapture.ObserveCheckpointState(this))
                    continue;
                if (_activeModal is not null)
                {
                    SetAcceptanceMovement(null, ref movementHeld);
                    if (_raceSexMenuHost is not null &&
                        appearancePresentationHoldFrames > 0)
                    {
                        var signature =
                            $"{_acceptanceAppearancePhase}:" +
                            $"{_raceSexMenuHost.ActiveList}:" +
                            $"{_appearancePreviewMode}:" +
                            $"{_appearancePreviewFaceFraming}";
                        if (!signature.Equals(
                                appearancePresentationSignature,
                                StringComparison.Ordinal))
                        {
                            appearancePresentationSignature = signature;
                            appearancePresentationFramesRemaining =
                                appearancePresentationHoldFrames;
                        }
                        if (appearancePresentationFramesRemaining > 0)
                        {
                            appearancePresentationFramesRemaining--;
                            continue;
                        }
                    }
                    AdvanceAcceptanceModal(
                        playerName,
                        showcaseCreatorControls: appearancePresentationHoldFrames > 0);
                    continue;
                }

                var interaction = _flow.Interactions.SingleOrDefault(value =>
                    value.FromStage == _stage);
                if (interaction is null)
                {
                    SetAcceptanceMovement(null, ref movementHeld);
                    continue;
                }
                if (!_roleNodes.TryGetValue(interaction.TargetRole, out var target))
                    throw new InvalidOperationException(
                        $"Opening acceptance target role is absent: {interaction.TargetRole}");
                var distance = _loaded.Player.GlobalPosition.DistanceTo(
                    target.GlobalPosition);
                if (distance <= _configuration.Player.ActivationDistanceMeters)
                {
                    SetAcceptanceMovement(null, ref movementHeld);
                    if (interaction.Event.Equals("activate", StringComparison.OrdinalIgnoreCase))
                    {
                        Input.ParseInputEvent(DesktopInputMap.CreateEvent(
                            _configuration.Player.DesktopInput.Activate,
                            true));
                        activateHeld = true;
                    }
                    continue;
                }

                if (navigationStage != _stage)
                {
                    navigationStage = _stage;
                    navigationIndex = 0;
                    var startGameUnits = _loaded.CellToGameUnits(
                        _loaded.Root.ToLocal(_loaded.Player.GlobalPosition));
                    navigationPath = _loaded.MainContent.Navigation.FindPath(
                            startGameUnits,
                            _loaded.CellToGameUnits(_loaded.Root.ToLocal(target.GlobalPosition)))
                        .Select(_loaded.GameToWorld)
                        .ToArray();
                    GD.Print(
                        $"OPENNV_OPENING_ACCEPTANCE_PATH stage={_stage} " +
                        $"event={interaction.Event} distance={distance:F3} " +
                        $"waypoints={navigationPath.Count}");
                }
                while (navigationIndex < navigationPath.Count - 1 &&
                    HorizontalDistance(
                        _loaded.Player.GlobalPosition,
                        navigationPath[navigationIndex]) <=
                    _configuration.Player.CapsuleRadiusMeters)
                    navigationIndex++;
                var waypoint = navigationPath[Math.Min(navigationIndex, navigationPath.Count - 1)];
                FaceAcceptanceWaypoint(waypoint);
                var requestedMovement = SelectAcceptanceMovementBinding(waypoint);
                if (proveFirstPlayerAction && firstPlayerAction is null)
                {
                    var currentControls = _playerControls
                        .Select(value => value ? EnabledControlValue : DisabledControlValue)
                        .ToArray();
                    if (!_playerControls[MovementControlIndex] ||
                        _activePlayerPackage is not null ||
                        initialState is null ||
                        !currentControls.SequenceEqual(initialState.PlayerControls))
                        throw new InvalidOperationException(
                            "Opening checkpoint did not cleanly release authored player controls.");
                    firstPlayerAction = requestedMovement;
                    firstPlayerActionOrigin = _loaded.Player.GlobalPosition;
                    GD.Print(
                        $"OPENNV_OPENING_FIRST_PLAYER_ACTION_READY " +
                        $"stage={_stage} action={firstPlayerAction.Action} " +
                        $"physicalKey={firstPlayerAction.PhysicalKey} " +
                        $"controls={string.Join(',', currentControls)} " +
                        $"configuration={_configuration.Sha256} " +
                        "modal=none playerPackage=none");
                    if (visualCapture is not null)
                        await visualCapture.CaptureFirstActionReady(this);
                }
                SetAcceptanceMovement(requestedMovement, ref movementHeld);
            }
        }
        finally
        {
            _visualCaptureActive = false;
            SetAcceptanceMovement(null, ref movementHeld);
            if (activateHeld)
                Input.ParseInputEvent(DesktopInputMap.CreateEvent(
                    _configuration.Player.DesktopInput.Activate,
                    false));
        }
    }

    private void AdvanceAcceptanceModal(
        string playerName,
        bool showcaseCreatorControls)
    {
        if (_activeModal is null || _dialogueVoice.Playing)
            return;
        var lineEdit = Descendants<LineEdit>(_activeModal).FirstOrDefault();
        var buttons = Descendants<Button>(_activeModal)
            .Where(button => !button.Disabled && button.IsVisibleInTree())
            .ToArray();
        if (lineEdit is not null)
        {
            lineEdit.Text = playerName;
            PressAcceptanceButton(buttons.FirstOrDefault(button =>
                button.Text == _flow.Strings["ok"]));
            return;
        }
        if (_raceSexMenuHost is not null)
        {
            var faceGen = _flow.Character.Appearance.FaceGen;
            var previewPolicy = faceGen.ControlSpace.PreviewControl;
            var previewControls = FaceGenPreviewControls(faceGen);
            if (_appearancePreviewHost is null ||
                _raceSexShowSex is null ||
                _raceSexShowFace is null)
                throw new InvalidOperationException(
                    "Owned RaceSexMenu acceptance state is incomplete.");
            void SetGeometryValues(bool edited)
            {
                foreach (var control in previewControls)
                {
                    var value = edited &&
                        control.SettingEntity == previewPolicy.SettingEntity
                            ? previewPolicy.AcceptanceValue
                            : previewPolicy.ResetValue;
                    _faceGeometryControlValues[control.SettingEntity] = value;
                    _appearancePreviewHost.Apply(
                        control.SettingEntity,
                        value);
                }
                _raceSexShowFace();
            }
            bool GeometryValuesEqual(float value) =>
                previewControls.All(control =>
                    _faceGeometryControlValues.TryGetValue(
                        control.SettingEntity,
                        out var actual) &&
                    Mathf.IsEqualApprox(actual, value));
            bool GeometryEditMatches() =>
                previewControls.All(control =>
                    _faceGeometryControlValues.TryGetValue(
                        control.SettingEntity,
                        out var actual) &&
                    Mathf.IsEqualApprox(
                        actual,
                        control.SettingEntity == previewPolicy.SettingEntity
                            ? previewPolicy.AcceptanceValue
                            : previewPolicy.ResetValue));
            if (_acceptanceAppearancePhase == AcceptanceAppearancePhase.InitialSex)
            {
                _raceSexMenuHost.PressNext();
                _acceptanceAppearancePhase = AcceptanceAppearancePhase.SelectRace;
                return;
            }
            if (_acceptanceAppearancePhase == AcceptanceAppearancePhase.SelectRace)
            {
                _raceSexMenuHost.PressNext();
                _acceptanceAppearancePhase = AcceptanceAppearancePhase.SelectHair;
                return;
            }
            if (_acceptanceAppearancePhase == AcceptanceAppearancePhase.SelectHair)
            {
                _raceSexMenuHost.PressNext();
                _acceptanceAppearancePhase = AcceptanceAppearancePhase.SelectEyes;
                return;
            }
            if (_acceptanceAppearancePhase == AcceptanceAppearancePhase.SelectEyes)
            {
                _raceSexMenuHost.PressNext();
                _acceptanceAppearancePhase = AcceptanceAppearancePhase.EditGeometry;
                return;
            }
            if (_acceptanceAppearancePhase == AcceptanceAppearancePhase.EditGeometry)
            {
                SetGeometryValues(edited: true);
                var editedSha256 = CurrentFaceSymmetricGeometrySha256();
                if (!GeometryEditMatches() ||
                    editedSha256.Equals(
                        faceGen.SymmetricGeometrySha256,
                        StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(
                        "Owned FaceGen acceptance sliders did not change source coordinates.");
                _acceptanceAppearancePhase = AcceptanceAppearancePhase.ResetGeometry;
                GD.Print(
                    $"OPENNV_OPENING_ACCEPTANCE_FACEGEN_INPUT " +
                    $"controls={FaceGenControlValuesText(previewControls)} " +
                    $"editedSha256={editedSha256} " +
                    "transport=owned-racesex-slider-state");
                return;
            }
            if (_acceptanceAppearancePhase == AcceptanceAppearancePhase.ResetGeometry)
            {
                SetGeometryValues(edited: false);
                if (!GeometryValuesEqual(previewPolicy.ResetValue) ||
                    !CurrentFaceSymmetricGeometrySha256().Equals(
                        faceGen.SymmetricGeometrySha256,
                        StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(
                        "Owned FaceGen reset did not restore source coordinates.");
                _acceptanceAppearancePhase = AcceptanceAppearancePhase.RestoreGeometryEdit;
                GD.Print(
                    "OPENNV_OPENING_ACCEPTANCE_FACEGEN_RESET " +
                    "transport=owned-racesex-slider-state");
                return;
            }
            if (_acceptanceAppearancePhase == AcceptanceAppearancePhase.RestoreGeometryEdit)
            {
                SetGeometryValues(edited: true);
                _acceptanceAppearancePhase = AcceptanceAppearancePhase.SelectSex;
                return;
            }
            if (_acceptanceAppearancePhase == AcceptanceAppearancePhase.SelectSex &&
                _flow.Character.SexChoices.Count > 1)
            {
                var targetSex = (_sexIndex + 1) % _flow.Character.SexChoices.Count;
                _raceSexShowSex();
                _acceptanceAppearancePhase = AcceptanceAppearancePhase.SelectParts;
                _raceSexMenuHost.ActivateListEntry(
                    _flow.Character.Appearance.SexEngineValues[targetSex]);
                return;
            }
            if (_acceptanceAppearancePhase <= AcceptanceAppearancePhase.SelectParts)
            {
                _raceSexShowFace();
                _acceptanceAppearancePhase = showcaseCreatorControls
                    ? AcceptanceAppearancePhase.ShowcaseFaceNormal
                    : AcceptanceAppearancePhase.Complete;
                GD.Print(
                    $"OPENNV_OPENING_ACCEPTANCE_APPEARANCE_INPUT sex={_sexIndex} " +
                    $"race={_raceFormId} hair={_hairFormId} eyes={_eyesFormId} " +
                    "transport=owned-racesex-active-list");
                return;
            }
            if (_acceptanceAppearancePhase == AcceptanceAppearancePhase.ShowcaseFaceNormal)
            {
                _raceSexRenderedDeviceHost!.ActivateCreatorModeControl("BODY");
                _acceptanceAppearancePhase = AcceptanceAppearancePhase.ShowcaseBodyNormal;
                return;
            }
            if (_acceptanceAppearancePhase == AcceptanceAppearancePhase.ShowcaseBodyNormal)
            {
                _raceSexRenderedDeviceHost!.ActivateCreatorModeControl("PROJECTION");
                _acceptanceAppearancePhase = AcceptanceAppearancePhase.ShowcaseBodyGreen;
                return;
            }
            if (_acceptanceAppearancePhase == AcceptanceAppearancePhase.ShowcaseBodyGreen)
            {
                _raceSexRenderedDeviceHost!.ActivateCreatorModeControl("FACE");
                _acceptanceAppearancePhase = AcceptanceAppearancePhase.ShowcaseFaceGreen;
                return;
            }
            if (_acceptanceAppearancePhase == AcceptanceAppearancePhase.ShowcaseFaceGreen)
            {
                _raceSexRenderedDeviceHost!.ActivateCreatorModeControl("PROJECTION");
                _acceptanceAppearancePhase = AcceptanceAppearancePhase.Complete;
                return;
            }
            if (_acceptanceAppearancePhase == AcceptanceAppearancePhase.Complete)
            {
                _raceSexMenuHost.PressNext();
                return;
            }
        }
        if (_specialValues.Values.Sum() < _flow.Character.SpecialTotalPoints &&
            buttons.FirstOrDefault(button => button.Text == "+") is { } increase)
        {
            PressAcceptanceButton(increase);
            return;
        }
        if (buttons.FirstOrDefault(button =>
                button.Text == _flow.Strings["accept"]) is { } accept)
        {
            PressAcceptanceButton(accept);
            return;
        }
        PressAcceptanceButton(buttons.FirstOrDefault());
    }

    private int AuthoredAppearanceReturnStage()
    {
        var appearanceStages = _flow.Stages.Values
            .Where(program => program.Commands.Any(command =>
                command.Kind == "showMenu" && command.Role == "appearance"))
            .Select(program => program.Stage)
            .ToArray();
        if (appearanceStages.Length != 1 ||
            !_flow.MenuCloseTransitions.TryGetValue(
                appearanceStages[0],
                out var returnStage))
            throw new InvalidOperationException(
                "Owned RaceSexMenu has no unique source stage-to-return transition.");
        return returnStage;
    }

    private static void PressAcceptanceButton(Button? button)
    {
        if (button is null)
            throw new InvalidOperationException(
                "Opening acceptance found no enabled authored menu action.");
        button.EmitSignal(Button.SignalName.Pressed);
    }

    private void FaceAcceptanceWaypoint(Vector3 waypoint)
    {
        var direction = waypoint - _loaded.Player.GlobalPosition;
        direction.Y = 0.0f;
        if (direction.IsZeroApprox())
            return;
        direction = direction.Normalized();
        var desiredYaw = MathF.Atan2(-direction.X, -direction.Z);
        var yawDelta = Mathf.AngleDifference(_loaded.Player.GlobalRotation.Y, desiredYaw);
        Input.MouseMode = Input.MouseModeEnum.Captured;
        Input.ParseInputEvent(new InputEventMouseMotion
        {
            Relative = new Vector2(
                -yawDelta / _configuration.Player.MouseSensitivityRadiansPerPixel,
                0.0f),
        });
    }

    private DesktopKeyBindingConfiguration SelectAcceptanceMovementBinding(Vector3 waypoint)
    {
        var direction = waypoint - _loaded.Player.GlobalPosition;
        direction.Y = 0.0f;
        direction = direction.Normalized();
        var forward = -_loaded.Player.Camera.GlobalBasis.Z;
        var right = _loaded.Player.Camera.GlobalBasis.X;
        forward.Y = 0.0f;
        right.Y = 0.0f;
        forward = forward.Normalized();
        right = right.Normalized();
        var forwardAmount = direction.Dot(forward);
        var rightAmount = direction.Dot(right);
        var input = _configuration.Player.DesktopInput;
        return MathF.Abs(rightAmount) > MathF.Abs(forwardAmount)
            ? rightAmount >= 0.0f ? input.MoveRight : input.MoveLeft
            : forwardAmount >= 0.0f ? input.MoveForward : input.MoveBackward;
    }

    private static void SetAcceptanceMovement(
        DesktopKeyBindingConfiguration? requested,
        ref DesktopKeyBindingConfiguration? held)
    {
        if (requested == held)
            return;
        if (held is not null)
            Input.ParseInputEvent(DesktopInputMap.CreateEvent(held, false));
        held = requested;
        if (held is not null)
            Input.ParseInputEvent(DesktopInputMap.CreateEvent(held, true));
    }

    private static float HorizontalDistance(Vector3 first, Vector3 second)
    {
        var difference = second - first;
        difference.Y = 0.0f;
        return difference.Length();
    }

    private static IEnumerable<T> Descendants<T>(Node node)
        where T : Node
    {
        foreach (var child in node.GetChildren())
        {
            if (child is T match)
                yield return match;
            foreach (var descendant in Descendants<T>(child))
                yield return descendant;
        }
    }

    private void ValidateCheckpointState(OpeningCampaignState state)
    {
        if (state.Stage != AuthoredCheckpointStage() ||
            state.Completed || string.IsNullOrWhiteSpace(state.PlayerName))
            throw new InvalidOperationException(
                "Opening autosave did not preserve the authored checkpoint state.");
    }

    private int AuthoredCheckpointStage()
    {
        var stages = _flow.Stages.Values
            .Where(program => program.Commands.Any(command => command.Kind == "autosave"))
            .Select(program => program.Stage)
            .ToArray();
        if (stages.Length != 1)
            throw new InvalidOperationException(
                "Owned opening flow must have one authored checkpoint stage.");
        return stages[0];
    }

    private static void ValidateCompletedState(
        OpeningNewGameFlow flow,
        OpeningCampaignState state)
    {
        var completionInfos = flow.TopicsByFormId[flow.OutroTopicFormId].Infos
            .Where(info =>
                info.Goodbye &&
                info.Commands.Any(command =>
                    command.Kind == "deferredStage" &&
                    command.Stage == flow.CompletionStage))
            .ToArray();
        var completionControls = completionInfos
            .SelectMany(info => info.Commands)
            .Where(command => command.Kind == "playerControls")
            .ToArray();
        if (completionInfos.Length != 1 || completionControls.Length != 1 ||
            !string.Equals(
                completionControls[0].Operation,
                "enable",
                StringComparison.OrdinalIgnoreCase) ||
            completionControls[0].ControlValues.Count != PlayerControlCount ||
            !state.PlayerControls.SequenceEqual(completionControls[0].ControlValues) ||
            state.Stage != flow.CompletionStage || !state.Completed ||
            string.IsNullOrWhiteSpace(state.PlayerName) ||
            state.SpecialValues.Values.Sum() != flow.Character.SpecialTotalPoints ||
            state.TagSkillFormIds.Count != flow.Character.TagSkillMaximumSelected ||
            state.TraitFormIds.Count > flow.Character.TraitMaximumSelected)
            throw new InvalidOperationException(
                "Opening completion did not preserve the authored final state.");
        var quests = state.Quests.ToDictionary(
            value => value.FormId,
            StringComparer.OrdinalIgnoreCase);
        var globals = state.Globals.ToDictionary(
            value => value.FormId,
            StringComparer.OrdinalIgnoreCase);
        var completionCommands = flow.Stages[flow.CompletionStage].Commands;
        foreach (var command in completionCommands)
        {
            switch (command.Kind)
            {
                case "startQuest" when command.QuestFormId is { } started:
                    if (quests.GetValueOrDefault(started) is not { Running: true, Stopped: false })
                        throw new InvalidOperationException(
                            $"Opening completion did not start quest {started}.");
                    break;
                case "stopQuest" when command.QuestFormId is { } stopped:
                    if (quests.GetValueOrDefault(stopped) is not { Running: false, Stopped: true })
                        throw new InvalidOperationException(
                            $"Opening completion did not stop quest {stopped}.");
                    break;
                case "setStage" when command.QuestFormId is { } quest &&
                    command.Stage is { } stage:
                    if (quests.GetValueOrDefault(quest)?.Stage != stage)
                        throw new InvalidOperationException(
                            $"Opening completion did not advance quest {quest} to {stage}.");
                    break;
                case "setGlobal" when command.GlobalFormId is { } global &&
                    command.NumericValue is { } value:
                    if (globals.GetValueOrDefault(global) is not { } actual ||
                        !Mathf.IsEqualApprox(actual.Value, value))
                        throw new InvalidOperationException(
                            $"Opening completion did not set global {global}.");
                    break;
                case "autoDisplayObjectives" when command.Enabled is { } enabled:
                    if (state.AutoDisplayObjectives != enabled)
                        throw new InvalidOperationException(
                            "Opening completion objective-display state is incorrect.");
                    break;
                case "achievement" when command.Index is { } achievement:
                    if (!state.Achievements.Contains(achievement))
                        throw new InvalidOperationException(
                            $"Opening completion did not retain achievement {achievement}.");
                    break;
            }
        }
    }

    internal void Configure(
        OpeningManifest opening,
        CellSceneLoader.LoadedCell loaded,
        RuntimeConfiguration configuration,
        OpeningCampaignState? restoredState = null)
    {
        _opening = opening;
        _flow = opening.NewGameFlow;
        _raceFormId = _flow.Character.Appearance.DefaultRaceFormId;
        _hairFormId = _flow.Character.Appearance.DefaultHairFormId;
        _eyesFormId = _flow.Character.Appearance.DefaultEyesFormId;
        var faceGen = _flow.Character.Appearance.FaceGen;
        var previewPolicy = faceGen.ControlSpace.PreviewControl;
        foreach (var control in FaceGenPreviewControls(faceGen))
            _faceGeometryControlValues[control.SettingEntity] = previewPolicy.ResetValue;
        if (!CurrentFaceSymmetricGeometrySha256().Equals(
                _flow.Character.Appearance.FaceGen.SymmetricGeometrySha256,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Owned FaceGen default coordinates do not reproduce their source hash.");
        _loaded = loaded;
        _configuration = configuration;
        _loaded.Player.ConfigureOwnedNavigation(
            _loaded.MainContent.Navigation,
            _loaded.Root,
            _loaded.OriginGameUnits);
        _font = OwnedUiTheme.BuildFont(opening.Font);
        Name = "OwnedNewGameFlow";

        _dialogueVoice = new AudioStreamPlayer { Name = "OwnedDialogueVoice" };
        _dialogueVoice.Finished += CompleteDialogueVoice;
        AddChild(_dialogueVoice);

        foreach (var value in _flow.Character.SpecialValues)
            _specialValues[value.FormId] = _flow.Character.SpecialInitial;
        ResolveSceneRoles();
        ResolveGuideActor();
        ResolveGuideAnimationObjects();
        _dialogueFace = new FaceGenMorphController(
            _guideActor.Actor,
            configuration.ActorCompiler.FaceGenAnimation.Lip);
        _loaded.Player.SetExternalActivationHandler(HandleExternalActivation);
        foreach (var activator in _loaded.MainContent.PlacedReferences
                     .Select(reference => reference.Placement)
                     .OfType<ScriptedActivatorInstance>())
            activator.Bind(AuthorizeScriptedActivatorEvent, ApplyScriptedActivatorEvent);

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
        if (restoredState is null)
        {
            _quests.Add(
                _flow.QuestFormId,
                new OpeningQuestState(
                    _flow.QuestFormId,
                    _flow.QuestEditorId,
                    firstStage,
                    true,
                    false));
            SetStage(firstStage);
        }
        else
        {
            RestoreState(restoredState);
            ResumeRestoredCheckpoint(restoredState.Stage);
        }
        GD.Print(
            $"OPENNV_NEW_GAME_FLOW_READY quest={_flow.QuestEditorId} " +
            $"stage={_stage} restored={restoredState is not null} " +
            $"stages={_flow.Stages.Count} topics={_flow.TopicsByFormId.Count}");
    }

    public override void _Process(double delta)
    {
        UpdatePlayerAnimation(delta);
        UpdateImageSpaceModifiers(delta);
        UpdateGuideActor(delta);
        UpdateGuideAnimationObjectLifecycle();
        _guideCigaretteSmokePresentation?.Update(delta);
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

    private void ResolveGuideAnimationObjects()
    {
        var runtimeSurfaces = _guideActor.Actor.Surfaces.Where(surface =>
                surface.Role.StartsWith(
                    "animation-object-",
                    StringComparison.OrdinalIgnoreCase))
            .ToArray();
        foreach (var animationObject in _flow.GuideActorAi.AnimationObjects)
        {
            var surfaces = runtimeSurfaces.Where(surface =>
                    surface.Role.Equals(
                        animationObject.ComponentRole,
                        StringComparison.OrdinalIgnoreCase) &&
                    surface.SourceFormId?.Equals(
                        animationObject.FormId,
                        StringComparison.OrdinalIgnoreCase) == true &&
                    surface.AttachmentNode?.Equals(
                        animationObject.AttachmentNode,
                        StringComparison.Ordinal) == true)
                .Select(surface => surface.Mesh)
                .Distinct()
                .ToArray();
            if (surfaces.Length == 0)
                throw new InvalidOperationException(
                    "Owned guide animation object is absent from its actor: " +
                    animationObject.EditorId);
            if (surfaces.Any(surface => surface.Visible))
                throw new InvalidOperationException(
                    "Owned guide animation object is not default-hidden: " +
                    animationObject.EditorId);
            _guideAnimationObjectSurfaces.Add(animationObject.FormId, surfaces);
        }
        if (runtimeSurfaces.Length !=
            _guideAnimationObjectSurfaces.Values.Sum(value => value.Count))
            throw new InvalidOperationException(
                "Owned guide actor contains undeclared animation-object surfaces.");
        var cigaretteSource = _flow.GuideActorAi.AnimationObjects.Single();
        var cigaretteSurface = _guideActor.Actor.Surfaces.Single(surface =>
            surface.Role.Equals(
                cigaretteSource.ComponentRole,
                StringComparison.OrdinalIgnoreCase) &&
            surface.SourceFormId?.Equals(
                cigaretteSource.FormId,
                StringComparison.OrdinalIgnoreCase) == true);
        _guideCigaretteSmokePresentation =
            OpeningCigaretteSmokePresentation.Create(
                _loaded.Root,
                cigaretteSurface,
                cigaretteSource);
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
        if (TryPreserveInitialFurnitureOccupancy(package))
        {
            _guidePackageBegan = true;
            _guideMoving = false;
            _guidePathCellUnits = Array.Empty<Vector3>();
            _guidePathIndex = 0;
            _activeGuideLocomotion = null;
            PlayGuideFurnitureSeatedLoop();
            return;
        }
        if (_guideFurnitureOccupied)
        {
            BeginGuideFurnitureExit(package);
            return;
        }
        ContinueGuidePackage(package);
    }

    private void ContinueGuidePackage(OpeningGuidePackage package)
    {
        _guidePackageBegan = true;
        if (_guideDestinationReference is not { } destination)
        {
            _guideMoving = false;
            _guidePathCellUnits = Array.Empty<Vector3>();
            _guidePathIndex = 0;
            _activeGuideLocomotion = null;
            PlayGuidePackageIdle(package);
            return;
        }
        _guideDestinationCellUnits = GameplayActorGrounding.ApplyGroundOffset(
            _guideActor,
            _loaded.GameToCellUnits(destination.PositionGameUnits));
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
            .Select(position => GameplayActorGrounding.ApplyGroundOffset(
                _guideActor,
                position))
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

    private bool TryPreserveInitialFurnitureOccupancy(OpeningGuidePackage package)
    {
        if (_guideFurnitureOccupied)
            return package.Location?.FormId.Equals(
                    _guideFurnitureReferenceFormId,
                    StringComparison.OrdinalIgnoreCase) == true;
        var contract = _flow.GuideActorAi.FurnitureOccupancy;
        if (_guidePackageBegan || package.Conditions.Count != 0 ||
            !package.FormId.Equals(
                contract.InitialPackageFormId,
                StringComparison.OrdinalIgnoreCase) ||
            package.Location is not
            {
                TypeName: "nearReference",
                Reference: { } destination,
            } ||
            !destination.FormId.Equals(
                contract.ReferenceFormId,
                StringComparison.OrdinalIgnoreCase))
            return false;
        var sourceReferences = _loaded.MainContent.SourceReferences.Where(value =>
                value.FormId.Equals(
                    destination.FormId,
                    StringComparison.OrdinalIgnoreCase) &&
                value.BaseRecordType.Equals("FURN", StringComparison.Ordinal))
            .ToArray();
        if (sourceReferences.Length == 0)
            return false;
        if (sourceReferences.Length != 1)
            throw new InvalidOperationException(
                "Owned initial furniture package destination is ambiguous: " +
                destination.FormId);
        var source = sourceReferences[0];
        if (!source.FormId.Equals(
                contract.Furniture.ReferenceFormId,
                StringComparison.OrdinalIgnoreCase) ||
            !source.BaseFormId.Equals(
                contract.Furniture.BaseFormId,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Owned initial furniture source differs from the marker contract.");
        var furniture = _loaded.MainContent.PlacedReferences.Where(value =>
                value.FormId.Equals(source.FormId, StringComparison.OrdinalIgnoreCase) &&
                value.BaseFormId.Equals(
                    source.BaseFormId,
                    StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (furniture.Length != 1)
            throw new InvalidOperationException(
                "Owned initial furniture package destination is absent or ambiguous: " +
                destination.FormId);
        var marker = contract.Furniture.Marker;
        var actorRootOffset = marker.OffsetGodotGameUnits -
            marker.ActorPlacementOffset.OffsetGodotGameUnits;
        var markerTransform = furniture[0].Placement.Transform * new Transform3D(
            new Basis(marker.RotationGodot),
            actorRootOffset);
        var actorTransform = markerTransform * new Transform3D(
            new Basis(marker.ActorForwardHeadingDelta.RotationGodot),
            Vector3.Zero);
        if (!markerTransform.Origin.IsFinite() ||
            !markerTransform.Basis.IsFinite() ||
            !actorTransform.Basis.IsFinite())
            throw new InvalidOperationException(
                "Owned initial furniture marker produced a non-finite transform.");
        var actorScale = _guideActor.Placement.Scale;
        _guideActor.Placement.Position = markerTransform.Origin;
        _guideActor.Placement.Basis = actorTransform.Basis
            .Orthonormalized()
            .Scaled(actorScale);
        _loaded.ActorGrounding.RegisterOwnedFurnitureMarkerOccupancy(
            _guideActor,
            furniture[0].Placement,
            markerTransform.Origin);
        _guideFurnitureOccupied = true;
        _guideFurnitureExitRootMotionApplied = false;
        _guideFurnitureReferenceFormId = source.FormId;
        GD.Print(
            $"OPENNV_NEW_GAME_GUIDE_FURNITURE_OCCUPIED " +
            $"package={package.EditorId} reference={source.FormId} " +
            $"base={source.BaseFormId} marker={contract.MarkerId} " +
            $"markerDisposition={contract.MarkerDisposition} " +
            $"markerCell={_guideActor.Placement.Position} " +
            $"markerNifOffset={marker.OffsetNifGameUnits} " +
            $"targetGmstOffset={marker.ActorPlacementOffset.OffsetNifGameUnits} " +
            $"actorRootOffset={actorRootOffset} " +
            $"headingDeltaGmst={marker.ActorForwardHeadingDelta.EditorId} " +
            $"headingDeltaRadians={marker.ActorForwardHeadingDelta.ValueRadians:F7} " +
            $"transform=owned-furniture-nif-marker-minus-gmst-target-offset-and-" +
            $"heading-delta");
        return true;
    }

    private void PlayGuideFurnitureSeatedLoop()
    {
        var furniture = _flow.GuideActorAi.FurnitureOccupancy;
        var animationObject = _flow.GuideActorAi.AnimationObjects.Single(value =>
            value.IdleAnimationFormId.Equals(
                furniture.AnimationObjectIdleFormId,
                StringComparison.OrdinalIgnoreCase));
        var seated = ResolveGuideAnimation(
            furniture.SeatedLoop.LogicalPath,
            furniture.SeatedLoop.Sha256,
            ZeroedAccumulationRootTranslation);
        var smoking = ResolveGuideAnimation(
            animationObject.IdleAnimationLogicalPath,
            animationObject.IdleAnimationSha256,
            ZeroedAccumulationRootTranslation);
        if (smoking.SequenceName != animationObject.IdleAnimationSequenceName ||
            smoking.StartSeconds != animationObject.IdleAnimationStartSeconds ||
            smoking.StopSeconds != animationObject.IdleAnimationStopSeconds ||
            smoking.CycleType != animationObject.IdleAnimationCycleType ||
            !smoking.TransformPrioritiesByNode.OrderBy(value => value.Key)
                .SequenceEqual(
                    animationObject.IdleAnimationTransformPrioritiesByNode
                        .OrderBy(value => value.Key)))
            throw new InvalidOperationException(
                "Owned guide package idle differs from its opening source contract.");
        _guideFurnitureLayeredSeatedAnimation ??=
            OpeningGuidePriorityAnimation.Compose(
                seated,
                smoking,
                animationObject.AttachmentNode);
        var layered = _guideFurnitureLayeredSeatedAnimation;
        layered.Play();
        _activeGuideIdleAnimation = null;
        SetGuideAnimationObjects(furniture.AnimationObjectIdleFormId);
        _activeGuideAnimation = layered.ActiveAnimation;
        GD.Print(
            $"OPENNV_NEW_GAME_GUIDE_FURNITURE_SEATED " +
            $"idle={furniture.SeatedLoop.FormId} " +
            $"sequence={furniture.SeatedLoop.SequenceName} " +
            $"packageIdle={animationObject.IdleAnimationFormId} " +
            $"packageSequence={animationObject.IdleAnimationSequenceName} " +
            $"cigaretteIdle={furniture.AnimationObjectIdleFormId} " +
            "composition=owned-controlled-node-priority");
    }

    private void BeginGuideFurnitureExit(OpeningGuidePackage package)
    {
        var furniture = _flow.GuideActorAi.FurnitureOccupancy;
        if (_guideFurnitureExiting || _guideFurnitureExitRootMotionApplied ||
            _stage != furniture.ReleaseStage ||
            !package.FormId.Equals(
                furniture.ReleasePackageFormId,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Owned guide furniture exit package/stage is unexpected.");
        _guideFurnitureExiting = true;
        _guideFurnitureLayeredSeatedAnimation?.Stop();
        _guideFurnitureExitPackage = package;
        _guideMoving = false;
        _guidePathCellUnits = Array.Empty<Vector3>();
        _guidePathIndex = 0;
        _activeGuideLocomotion = null;
        PlayGuideAnimation(
            furniture.Exit.LogicalPath,
            furniture.Exit.Sha256,
            restart: true,
            idleAnimationFormId: furniture.AnimationObjectIdleFormId,
            loopMode: Animation.LoopModeEnum.None,
            expectedAccumulationRootDisposition:
                RetainedAccumulationRootTranslation);
        GD.Print(
            $"OPENNV_NEW_GAME_GUIDE_FURNITURE_EXIT_BEGIN " +
            $"reference={_guideFurnitureReferenceFormId} " +
            $"idle={furniture.Exit.FormId} sequence={furniture.Exit.SequenceName} " +
            $"nextPackage={package.EditorId}");
    }

    private void FinishGuideFurnitureExit()
    {
        var package = _guideFurnitureExitPackage
            ?? throw new InvalidOperationException(
                "Owned guide furniture exit has no pending package.");
        var furniture = _flow.GuideActorAi.FurnitureOccupancy;
        if (_guideFurnitureExitRootMotionApplied ||
            furniture.Exit.RootMotion is not { } rootMotion)
            throw new InvalidOperationException(
                "Owned guide furniture exit root motion is absent or already applied.");
        var rootBefore = _guideActor.Placement.Position;
        var displacementCell = _guideActor.Placement.Basis
            .Orthonormalized() * rootMotion.DisplacementGodotGameUnits;
        var rootAfter = rootBefore + displacementCell;
        if (!displacementCell.IsFinite() || !rootAfter.IsFinite())
            throw new InvalidOperationException(
                "Owned guide furniture exit produced a non-finite root transform.");
        _guideActor.Placement.Position = rootAfter;
        _guideFurnitureExitRootMotionApplied = true;
        GD.Print(
            $"OPENNV_NEW_GAME_GUIDE_FURNITURE_EXIT_ROOT " +
            $"reference={_guideFurnitureReferenceFormId} " +
            $"sequence={rootMotion.SequenceName} rootBefore={rootBefore} " +
            $"rootAfter={rootAfter} sourceDisplacement=" +
            $"{rootMotion.DisplacementGodotGameUnits} " +
            $"cellDisplacement={displacementCell}");
        GD.Print(
            $"OPENNV_NEW_GAME_GUIDE_FURNITURE_RELEASED " +
            $"reference={_guideFurnitureReferenceFormId} " +
            $"exit={furniture.Exit.FormId} nextPackage={package.EditorId}");
        _guideFurnitureOccupied = false;
        _guideFurnitureExiting = false;
        _guideFurnitureReferenceFormId = null;
        _guideFurnitureExitPackage = null;
        _activeGuideAnimation = null;
        ContinueGuidePackage(package);
    }

    private void UpdateGuideActor(double delta)
    {
        if (!_guideActorResolved)
            return;
        if (_guideFurnitureExiting)
        {
            if (_activeGuideAnimation is not { } exit)
                throw new InvalidOperationException(
                    "Owned guide furniture exit has no active animation.");
            if (exit.Player.IsPlaying() && exit.Player.CurrentAnimation.ToString().Equals(
                    exit.RuntimeName,
                    StringComparison.Ordinal))
                return;
            FinishGuideFurnitureExit();
            return;
        }
        if (_guideFurnitureOccupied)
        {
            if (_activeGuideAnimation is not { } seatedAnimation ||
                !seatedAnimation.Player.IsPlaying() ||
                !seatedAnimation.Player.CurrentAnimation.ToString().Equals(
                    seatedAnimation.RuntimeName,
                    StringComparison.Ordinal))
                PlayGuideFurnitureSeatedLoop();
            return;
        }
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
        var idleFormId = package.IdleAnimationFormIds.FirstOrDefault();
        var path = package.IdleAnimationLogicalPaths.FirstOrDefault()
            ?? _guideActor.IdleAnimationPath;
        PlayGuideAnimation(
            path,
            expectedSha256: null,
            restart: true,
            idleAnimationFormId: idleFormId);
        _activeGuideIdleAnimation = _activeGuideAnimation;
        _activeGuideAnimation = null;
    }

    private void PlayGuideAnimation(
        string logicalPath,
        string? expectedSha256,
        bool restart,
        string? idleAnimationFormId = null,
        Animation.LoopModeEnum? loopMode = null,
        string expectedAccumulationRootDisposition =
            ZeroedAccumulationRootTranslation)
    {
        var animation = ResolveGuideAnimation(
            logicalPath,
            expectedSha256,
            expectedAccumulationRootDisposition);
        if (loopMode is { } requestedLoopMode)
        {
            var resource = animation.Player.GetAnimation(animation.RuntimeName)
                ?? throw new InvalidOperationException(
                    $"Owned guide animation resource is absent: {logicalPath}");
            resource.LoopMode = requestedLoopMode;
        }
        if (restart || !animation.Player.IsPlaying() ||
            !animation.Player.CurrentAnimation.ToString().Equals(
                animation.RuntimeName,
                StringComparison.Ordinal))
        {
            animation.Player.Play(animation.RuntimeName);
            animation.Player.Advance(0.0);
        }
        _activeGuideIdleAnimation = null;
        SetGuideAnimationObjects(idleAnimationFormId);
        _activeGuideAnimation = animation;
    }

    private ActorModelSlice.LoadedAnimation ResolveGuideAnimation(
        string logicalPath,
        string? expectedSha256,
        string expectedAccumulationRootDisposition)
    {
        var expected = ActorModelSlice.NormalizeAnimationPath(logicalPath);
        var matches = _guideActor.Actor.LoadedAnimations.Where(animation =>
                ActorModelSlice.NormalizeAnimationPath(animation.LogicalPath).Equals(
                    expected,
                    StringComparison.OrdinalIgnoreCase) &&
                (expectedSha256 is null || animation.SourceSha256.Equals(
                    expectedSha256,
                    StringComparison.OrdinalIgnoreCase)) &&
                animation.AccumulationRootTranslationDisposition.Equals(
                    expectedAccumulationRootDisposition,
                    StringComparison.Ordinal))
            .ToArray();
        if (matches.Length != 1)
            throw new InvalidOperationException(
                $"Owned guide animation is absent or ambiguous: {logicalPath}");
        return matches[0];
    }

    private void SetGuideAnimationObjects(string? idleAnimationFormId)
    {
        if (string.Equals(
                _guideAnimationObjectIdleFormId,
                idleAnimationFormId,
                StringComparison.OrdinalIgnoreCase))
            return;
        _guideAnimationObjectIdleFormId = idleAnimationFormId;
        foreach (var animationObject in _flow.GuideActorAi.AnimationObjects)
        {
            var visible = idleAnimationFormId is not null &&
                animationObject.IdleAnimationFormId.Equals(
                    idleAnimationFormId,
                    StringComparison.OrdinalIgnoreCase);
            foreach (var surface in _guideAnimationObjectSurfaces[animationObject.FormId])
                surface.Visible = visible;
            _guideCigaretteSmokePresentation?.SetActive(visible);
            GD.Print(
                $"OPENNV_NEW_GAME_ANIMATION_OBJECT form={animationObject.FormId} " +
                $"idle={idleAnimationFormId ?? "none"} " +
                $"editor={animationObject.EditorId} visible={visible} " +
                $"attachment={animationObject.AttachmentNode}");
        }
    }

    private void UpdateGuideAnimationObjectLifecycle()
    {
        if (_activeGuideIdleAnimation is not { } idle ||
            idle.Player.IsPlaying() && idle.Player.CurrentAnimation.ToString().Equals(
                idle.RuntimeName,
                StringComparison.Ordinal))
            return;
        _activeGuideIdleAnimation = null;
        SetGuideAnimationObjects(null);
    }

    public override void _ExitTree()
    {
        if (_guideActorResolved)
            SetGuideAnimationObjects(null);
        _guideCigaretteSmokePresentation?.Root.QueueFree();
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
        if (levelTarget.IsEqualApprox(origin))
            return;
        _guideActor.Placement.LookAt(levelTarget, Vector3.Up);
    }

    private void RunWhenGuideReady(Action continuation, int generation)
    {
        _guideLookAtPlayer = true;
        if (_guideMoving || _guideFurnitureExiting)
        {
            _guideArrivalContinuation = continuation;
            _guideArrivalGeneration = generation;
            return;
        }
        if (!_guideFurnitureOccupied)
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
            if (!_destroyedReferences.Contains(role.ReferenceFormId) ||
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

    private bool AuthorizeScriptedActivatorEvent(ScriptedActivatorEvent source)
    {
        var guard = source.Guard;
        return _objectives.TryGetValue(
            ObjectiveKey(guard.QuestFormId, guard.ObjectiveIndex),
            out var objective) &&
            objective.QuestEditorId.Equals(guard.QuestEditorId, StringComparison.OrdinalIgnoreCase) &&
            objective.State == guard.State && objective.Enabled;
    }

    private void ApplyScriptedActivatorEvent(ScriptedActivatorEvent source)
    {
        foreach (var command in source.Commands)
        {
            if (command.Kind == "setStage" && command.Stage is { } stage)
            {
                if (command.QuestFormId.Equals(_flow.QuestFormId, StringComparison.OrdinalIgnoreCase))
                    SetStage(stage);
                else
                    ApplyQuestStage(command.QuestFormId, command.QuestEditorId, stage, true);
                continue;
            }
            if (command.Kind == "objective" && command.Index is { } index &&
                command.State == "completed" && command.Enabled == true &&
                _objectives.TryGetValue(ObjectiveKey(command.QuestFormId, index), out var objective) &&
                objective.QuestEditorId.Equals(command.QuestEditorId, StringComparison.OrdinalIgnoreCase))
            {
                _objectives[ObjectiveKey(command.QuestFormId, index)] = objective with
                {
                    State = command.State,
                    Enabled = true,
                };
                if (_objective.Text == objective.Text)
                    _objective.Visible = false;
                continue;
            }
            throw new InvalidOperationException("Scripted activator command is not admitted by opening state.");
        }
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
        ApplyQuestStage(
            _flow.QuestFormId,
            _flow.QuestEditorId,
            stage,
            true);
        _timerTargetStage = null;
        _guideArrivalContinuation = null;
        CloseModal();
        ApplyStageControlPolicy();
        EvaluateGuidePackage();
        GD.Print($"OPENNV_NEW_GAME_STAGE quest={_flow.QuestEditorId} stage={stage}");
        ExecuteStageCommand(program, 0, _generation, null);
    }

    private void ResumeRestoredCheckpoint(int stage)
    {
        if (!_flow.Stages.TryGetValue(stage, out var program))
            throw new InvalidOperationException($"Owned New Game stage is absent: {stage}");
        var checkpointStage = AuthoredCheckpointStage();
        var autosaveIndices = program.Commands
            .Select((command, index) => (command, index))
            .Where(value => value.command.Kind == "autosave")
            .Select(value => value.index)
            .ToArray();
        if (stage != checkpointStage || autosaveIndices.Length != 1)
            throw new InvalidOperationException(
                "Saved opening state is not at its unique owned checkpoint command.");

        _generation++;
        _stage = stage;
        _timerTargetStage = null;
        _guideArrivalContinuation = null;
        CloseModal(false);
        ApplyStageControlPolicy();
        EvaluateGuidePackage();
        var resumeCommandIndex = autosaveIndices[0] + 1;
        GD.Print(
            $"OPENNV_NEW_GAME_CHECKPOINT_RESUME quest={_flow.QuestEditorId} " +
            $"stage={stage} resumeCommandIndex={resumeCommandIndex} " +
            "replayedPrefixCommands=0 autosaveReplayed=0 dialogueReplayed=0");
        ExecuteStageCommand(program, resumeCommandIndex, _generation, null);
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
                ApplyQuestTimer(command);
                Next(command.Seconds);
                return;
            case "setQuestVariable":
                ApplyQuestVariable(command);
                Next();
                return;
            case "setStage":
                if (command.QuestFormId?.Equals(
                        _flow.QuestFormId,
                        StringComparison.OrdinalIgnoreCase) == true &&
                    command.Stage is { } nextStage)
                    SetStage(nextStage);
                else
                {
                    ApplyQuestStage(command);
                    Next();
                }
                return;
            case "sayTo":
                if (command.TopicEditorId is null)
                    throw new InvalidOperationException("Owned SayTo command has no topic.");
                if (IsGuideSpeaker(command))
                    RunWhenGuideReady(
                        () => PlayTopicEditor(command.TopicEditorId, () => { }, generation),
                        generation);
                else
                    PlayTopicEditor(command.TopicEditorId, () => { }, generation);
                Next();
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
            case "actorValueDelta":
                ApplyActorValueDelta(command);
                Next();
                return;
            case "startQuest":
            case "stopQuest":
                ApplyQuestLifecycle(command);
                Next();
                return;
            case "setGlobal":
                ApplyGlobal(command);
                Next();
                return;
            case "autoDisplayObjectives":
                ApplyAutoDisplayObjectives(command);
                Next();
                return;
            case "achievement":
                ApplyAchievement(command);
                Next();
                return;
            case "autosave":
                StoreOpeningCheckpoint();
                Next();
                return;
            case "deferredStage":
                if (command.Stage is { } deferred && command.Seconds is { } deferredSeconds)
                {
                    _timerTargetStage = deferred;
                    _timerRemainingSeconds = deferredSeconds;
                    return;
                }
                throw new InvalidOperationException(
                    "Owned deferred-stage command is incomplete.");
            default:
                throw new InvalidOperationException(
                    $"Owned opening stage command is unsupported: {command.Kind}");
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
        if (command.Index is not { } index || command.Enabled is not { } enabled ||
            command.State is null || command.QuestFormId is null ||
            command.QuestEditorId is null ||
            !_flow.Objectives.TryGetValue(index, out var text))
            throw new InvalidOperationException("Owned opening objective command is incomplete.");
        var state = new OpeningObjectiveState(
            command.QuestFormId,
            command.QuestEditorId,
            index,
            command.State,
            enabled,
            text);
        state.Validate();
        _objectives[ObjectiveKey(state.QuestFormId, state.Index)] = state;
        if (enabled && command.State == "displayed" && _autoDisplayObjectives)
        {
            _objective.Text = text;
            _objective.Visible = true;
        }
        else if ((!enabled || command.State == "completed") && _objective.Text == text)
            _objective.Visible = false;
    }

    private void ApplyDestroyed(OpeningFlowCommand command)
    {
        if (command.ReferenceEditorId is null || command.ReferenceFormId is null ||
            command.Destroyed is null)
            throw new InvalidOperationException("Owned destroyed-reference command is incomplete.");
        if (command.Destroyed.Value)
            _destroyedReferences.Add(command.ReferenceFormId);
        else
            _destroyedReferences.Remove(command.ReferenceFormId);
    }

    private void ApplyReferenceEnabled(OpeningFlowCommand command)
    {
        if (command.ReferenceEditorId is null || command.ReferenceFormId is null ||
            command.Enabled is null)
            throw new InvalidOperationException("Owned enabled-reference command is incomplete.");
        _referenceEnabledStates[command.ReferenceFormId] = command.Enabled.Value;
        var loadedNodes = SetReferenceVisibility(
            command.ReferenceFormId,
            command.Enabled.Value,
            command.Enabled.Value);
        GD.Print(
            $"OPENNV_NEW_GAME_REFERENCE reference={command.ReferenceFormId} " +
            $"enabled={command.Enabled.Value} loadedNodes={loadedNodes}");
    }

    private int SetReferenceVisibility(
        string referenceFormId,
        bool enabled,
        bool requireLoaded)
    {
        var nodes = _flow.SceneRoles.Values
            .Where(role => role.ReferenceFormId.Equals(
                referenceFormId,
                StringComparison.OrdinalIgnoreCase))
            .Select(role => _roleNodes.GetValueOrDefault(role.Role))
            .Concat(_loaded.Actors
                .Where(actor => actor.ReferenceFormId.Equals(
                    referenceFormId,
                    StringComparison.OrdinalIgnoreCase))
                .Select(actor => actor.Placement))
            .Concat(_loaded.MainContent.PlacedReferences
                .Where(reference => reference.FormId.Equals(
                    referenceFormId,
                    StringComparison.OrdinalIgnoreCase))
                .Select(reference => reference.Placement))
            .Concat(_loaded.LinkedCells
                .SelectMany(cell => cell.Content.PlacedReferences)
                .Where(reference => reference.FormId.Equals(
                    referenceFormId,
                    StringComparison.OrdinalIgnoreCase))
                .Select(reference => reference.Placement))
            .Where(node => node is not null)
            .Cast<Node3D>()
            .Distinct()
            .ToArray();
        if (requireLoaded && nodes.Length == 0)
            throw new InvalidOperationException(
                $"Owned enabled reference is absent from the loaded world: {referenceFormId}");
        foreach (var node in nodes)
            node.Visible = enabled;
        return nodes.Length;
    }

    private void ApplyActorIntent(OpeningFlowCommand command)
    {
        if (command.ReferenceEditorId is not { } reference ||
            command.ReferenceFormId is not { } referenceForm ||
            command.Operation is null ||
            !_flow.SceneRoles.TryGetValue(_flow.GuideActorAi.Role, out var role) ||
            !role.EditorId.Equals(reference, StringComparison.OrdinalIgnoreCase) ||
            !role.ReferenceFormId.Equals(referenceForm, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Owned opening actor intent target is unsupported.");
        if (command.Operation.Equals("look", StringComparison.OrdinalIgnoreCase))
        {
            _guideLookAtPlayer = true;
            if (!_guideMoving && !_guideFurnitureOccupied && !_guideFurnitureExiting)
                FaceGuideToward(_loaded.Player.GlobalPosition);
        }
        else if (command.Operation.Equals("stoplook", StringComparison.OrdinalIgnoreCase))
        {
            _guideLookAtPlayer = false;
            if (!_guideMoving && !_guideFurnitureOccupied && !_guideFurnitureExiting &&
                _guideDestinationReference is { } destination)
                _guideActor.Placement.Basis = new Basis(destination.RotationGodot);
        }
        else if (command.Operation.Equals("evp", StringComparison.OrdinalIgnoreCase) ||
            command.Operation.Equals("resetai", StringComparison.OrdinalIgnoreCase))
            EvaluateGuidePackage(force: true);
        else
            throw new InvalidOperationException(
                $"Owned opening actor intent operation is unsupported: {command.Operation}");
        GD.Print(
            $"OPENNV_NEW_GAME_ACTOR_INTENT reference={command.ReferenceEditorId} " +
            $"operation={command.Operation} target={command.TargetEditorId}");
    }

    private void ApplyIdle(OpeningFlowCommand command)
    {
        if (command.ReferenceEditorId is null || command.ReferenceFormId is null ||
            command.IdleEditorId is null ||
            command.IdleFormId is null || command.IdleRecordType != "IDLE" ||
            command.AnimationLogicalPath is null)
            throw new InvalidOperationException("Owned opening idle command is incomplete.");
        var actors = _loaded.Actors.Where(value =>
            _flow.SceneRoles.Values.Any(role =>
                role.EditorId.Equals(command.ReferenceEditorId, StringComparison.OrdinalIgnoreCase) &&
                role.ReferenceFormId.Equals(
                    command.ReferenceFormId,
                    StringComparison.OrdinalIgnoreCase) &&
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
        if (actor.ReferenceFormId.Equals(
            _flow.GuideActorAi.ReferenceFormId,
            StringComparison.OrdinalIgnoreCase))
        {
            PlayGuideAnimation(
                command.AnimationLogicalPath,
                expectedSha256: null,
                restart: true,
                idleAnimationFormId: command.IdleFormId);
            _activeGuideIdleAnimation = _activeGuideAnimation;
            _activeGuideAnimation = null;
        }
        else
        {
            animation.Player.Play(animation.RuntimeName);
            animation.Player.Advance(0.0);
        }
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
        _lastAppliedPlayerCameraAnimation = animation;
        _lastAppliedPlayerCameraTime = time;
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
        if (command.ItemEditorId is null || command.ItemFormId is null ||
            command.ItemRecordType is null)
            throw new InvalidOperationException("Owned opening inventory command is incomplete.");
        var count = command.Count ?? 1;
        if (count <= 0)
            throw new InvalidOperationException("Owned opening inventory count is invalid.");
        if (command.Kind == "removeitem")
        {
            var remaining = _inventory.GetValueOrDefault(command.ItemFormId)?.Count - count ?? 0;
            if (remaining > 0)
                _inventory[command.ItemFormId] = new OpeningInventoryState(
                    command.ItemFormId,
                    command.ItemEditorId,
                    command.ItemRecordType,
                    remaining);
            else
            {
                _inventory.Remove(command.ItemFormId);
                _equippedItemFormIds.Remove(command.ItemFormId);
            }
            return;
        }
        if (command.Kind == "additem")
        {
            var current = _inventory.GetValueOrDefault(command.ItemFormId)?.Count ?? 0;
            _inventory[command.ItemFormId] = new OpeningInventoryState(
                command.ItemFormId,
                command.ItemEditorId,
                command.ItemRecordType,
                current + count);
            return;
        }
        if (command.Kind == "equipitem")
        {
            if (!_inventory.ContainsKey(command.ItemFormId))
                throw new InvalidOperationException(
                    $"Owned opening equip item is absent from inventory: {command.ItemFormId}");
            _equippedItemFormIds.Add(command.ItemFormId);
            return;
        }
        throw new InvalidOperationException(
            $"Owned opening inventory operation is unsupported: {command.Kind}");
    }

    private void ShowNameMenu(Action completed)
    {
        var content = OpenPanel(MenuRect("name"), "name");
        var prompt = NewLabel(_flow.Strings["namePrompt"]);
        prompt.HorizontalAlignment = HorizontalAlignment.Center;
        content.AddChild(prompt);
        var input = new LineEdit
        {
            Name = "OwnedPlayerNameInput",
            Text = _playerName,
            PlaceholderText = _flow.Strings["namePrompt"],
            Alignment = HorizontalAlignment.Center,
            CustomMinimumSize = new Vector2(
                0.0f,
                _opening.Font.LineHeightPixels * 2.0f),
        };
        ApplyTextTheme(input);
        var inputStyle = OwnedUiTheme.HighlightedStyle(
            _opening.MainMenuColor,
            _opening.Style);
        input.AddThemeStyleboxOverride("normal", inputStyle);
        input.AddThemeStyleboxOverride("focus", inputStyle);
        content.AddChild(input);
        var accept = NewButton(_flow.Strings["ok"]);
        content.AddChild(accept);
        void Submit()
        {
            var value = input.Text.Trim();
            if (string.IsNullOrWhiteSpace(value))
                return;
            _playerName = value;
            GD.Print($"OPENNV_NEW_GAME_NAME_CONFIRMED name={_playerName}");
            CloseModal();
            completed();
        }
        accept.Pressed += Submit;
        input.TextSubmitted += _ => Submit();
        Callable.From(input.GrabFocus).CallDeferred();
        GD.Print("OPENNV_NEW_GAME_NAME_INPUT_READY visible=true focus=deferred");
    }

    private void ShowAppearanceMenu(Action completed)
    {
        var appearance = _flow.Character.Appearance;
        var faceGen = appearance.FaceGen;
        var previewPolicy = faceGen.ControlSpace.PreviewControl;
        var appearanceMenu = _flow.Menus["appearance"];
        if (appearanceMenu.RaceSexMenuTiles is not { } source ||
            appearanceMenu.Rect != source.Background.Rect ||
            !_flow.Strings.TryGetValue(
                source.Navigation.Back.LabelRole,
                out var backLabel) ||
            !_flow.Strings.TryGetValue(
                source.Navigation.Next.LabelRole,
                out var nextLabel) ||
            backLabel != source.Navigation.Back.Label ||
            nextLabel != source.Navigation.Next.Label)
            throw new InvalidOperationException(
                "Owned RaceSexMenu labels or tile contract are incomplete.");
        if (appearanceMenu.RenderedDevice is not { } renderedDevice)
            throw new InvalidOperationException(
                "Owned RaceSex rendered-device contract is absent.");
        var modalRoot = OpenModalRoot("appearance");
        _raceSexRenderedDeviceHost = new OpeningRaceSexRenderedDeviceHost(
            renderedDevice,
            modalRoot,
            _flow.ReferenceCanvasSize,
            _configuration,
            _loaded.MainContent.Lighting,
            _loaded.UnitsToMeters);
        var root = _raceSexRenderedDeviceHost.CreateMenuPresentationHost(
            source.Background.Rect);
        _raceSexMenuHost = new OpeningRaceSexMenuHost(
            source,
            _opening.MainMenuColor,
            _opening.Style,
            root,
            RaceSexSliderPreviousEngineLabel,
            RaceSexSliderNextEngineLabel,
            _raceSexRenderedDeviceHost.SetActiveList);
        _raceSexMenuHost.FaceGrabHost();
        var preview = _raceSexRenderedDeviceHost.CreateFacePresentationHost();
        var previewControls = FaceGenPreviewControls(faceGen);
        OpeningPlayerFaceGenPreviewHost? previewHost = null;
        OpeningPlayerFaceGenPreview? selectedPreviewState = null;

        void RefreshPreview() => previewHost?.SetPreviewState(
            _bodyProportions,
            _appearancePreviewFaceFraming,
            greenProjection: _appearancePreviewMode == "2d");

        void UpdateControlValue(
            OpeningNativeFaceGenGeometryControl control,
            float value)
        {
            if (!float.IsFinite(value) ||
                value < previewPolicy.Minimum ||
                value > previewPolicy.Maximum)
                throw new InvalidOperationException(
                    "FaceGen RaceSexMenu UI value is invalid.");
            var uiValue = Mathf.IsEqualApprox(
                value,
                previewPolicy.ResetValue)
                ? previewPolicy.ResetValue
                : value;
            var morphWeight = uiValue * previewPolicy.MorphWeightScale;
            _faceGeometryControlValues[control.SettingEntity] = uiValue;
            previewHost?.Apply(control.SettingEntity, uiValue);
            RefreshPreview();
            GD.Print(
                $"OPENNV_NEW_GAME_FACEGEN_CONTROL name={control.SettingEntity} " +
                $"axisSha256={control.AxisSha256} uiValue={uiValue:R} " +
                $"morphWeight={morphWeight:R} " +
                $"semantics={previewPolicy.Semantics}");
        }

        void RenderPreview(OpeningAppearanceSex sex)
        {
            if (previewHost is not null)
            {
                var disposed = previewHost.DisposeOwnedTree();
                GD.Print(
                    "OPENNV_NEW_GAME_FACEGEN_PREVIEW_DISPOSED " +
                    $"control={disposed.ControlInstanceId} " +
                    $"viewport={disposed.ViewportInstanceId} " +
                    $"actor={disposed.ActorInstanceId} " +
                    $"disposition={disposed.Disposition}");
            }
            foreach (var child in preview.GetChildren())
                child.Free();
            previewHost = null;
            _appearancePreviewHost = null;
            var engineSex = appearance.SexEngineValues[_sexIndex];
            var selectedPreview = faceGen.PreviewHead.Previews.SingleOrDefault(value =>
                engineSex == value.Sex &&
                _raceFormId.Equals(value.RaceFormId, StringComparison.OrdinalIgnoreCase) &&
                _hairFormId.Equals(value.HairFormId, StringComparison.OrdinalIgnoreCase) &&
                _eyesFormId.Equals(value.EyesFormId, StringComparison.OrdinalIgnoreCase));
            if (selectedPreview is null)
                throw new InvalidOperationException(
                    "Owned RaceSexMenu has no exact full-body preview artifact for " +
                    $"sex={engineSex} race={_raceFormId} hair={_hairFormId} " +
                    $"eyes={_eyesFormId}.");
            selectedPreviewState = selectedPreview;
            previewHost = OpeningPlayerFaceGenPreviewHost.Load(
                selectedPreview,
                previewControls,
                previewPolicy,
                preview,
                _configuration,
                _loaded.MainContent.Lighting,
                _loaded.UnitsToMeters,
                source.FaceGrab.Rect.Size,
                renderedDevice);
            foreach (var control in previewControls)
                previewHost.Apply(
                    control.SettingEntity,
                    _faceGeometryControlValues[control.SettingEntity]);
            RefreshPreview();
            _appearancePreviewHost = previewHost;
            GD.Print(
                $"OPENNV_NEW_GAME_FACEGEN_PREVIEW_READY " +
                $"player={selectedPreview.PlayerFormId} " +
                $"race={selectedPreview.RaceFormId} " +
                $"sex={selectedPreview.Sex} " +
                $"hair={selectedPreview.HairFormId} " +
                $"eyes={selectedPreview.EyesFormId} " +
                $"boundControls={previewHost.BoundControlCount} " +
                $"boundSurfaces={previewHost.BoundSurfaceCount} " +
                $"availableControls={selectedPreview.GeometryControlCount}");
        }

        OpeningAppearanceRace CurrentRace() =>
            appearance.Races.Single(value => value.FormId.Equals(
                _raceFormId,
                StringComparison.OrdinalIgnoreCase));

        OpeningAppearanceSex CurrentSex() =>
            CurrentRace().Sex[appearance.SexEngineValues[_sexIndex]];

        void Accept()
        {
            if (selectedPreviewState is not { FullBody: true } selectedPreview ||
                previewHost is null ||
                selectedPreview.BodyComponentRoles is not { Count: > 0 } bodyRoles)
                throw new InvalidOperationException(
                    "Owned RaceSexMenu full-body preview state is incomplete at accept.");
            GD.Print(
                $"OPENNV_NEW_GAME_APPEARANCE_CONFIRMED " +
                $"sex={appearance.SexEngineValues[_sexIndex]} " +
                $"race={_raceFormId} hair={_hairFormId} eyes={_eyesFormId} " +
                $"faceGeometry={faceGen.SymmetricGeometrySha256} " +
                $"editedFaceGeometry={CurrentFaceSymmetricGeometrySha256()} " +
                $"controls={FaceGenControlValuesText(previewControls)} " +
                $"previewStatus={selectedPreview.Status} " +
                $"previewRuntime={selectedPreview.RuntimeDisposition} " +
                $"fullBody={selectedPreview.FullBody} " +
                $"previewMode={_appearancePreviewMode} " +
                $"bodyProportions={_bodyProportions} " +
                $"bodyRoles={string.Join(',', bodyRoles)} " +
                $"boundFaceGenSurfaces={previewHost.BoundSurfaceCount} " +
                $"boundFaceGenControls={previewHost.BoundControlCount}");
            CloseModal();
            completed();
        }

        Action showSex = null!;
        Action showRace = null!;
        Action showHair = null!;
        Action showEyes = null!;
        Action showFace = null!;
        Action showBody = null!;

        showSex = () => _raceSexMenuHost!.ShowList(
            "sex",
            [
                new OpeningRaceSexListEntry(
                    "sex-header",
                    _flow.Character.SexTitle,
                    false,
                    false,
                    () => { }),
                .. _flow.Character.SexChoices.Select((label, index) =>
                {
                    var choiceIndex = index;
                    return new OpeningRaceSexListEntry(
                        appearance.SexEngineValues[index],
                        label,
                        _sexIndex == index,
                        true,
                        () =>
                        {
                            _sexIndex = choiceIndex;
                            ResolveCurrentAppearanceForSex(resetToRaceDefaults: false);
                            RenderPreview(CurrentSex());
                            showSex();
                        });
                }),
            ],
            null,
            showRace);
        showRace = () => _raceSexMenuHost!.ShowList(
            "race",
            appearance.Races.Select(race =>
            {
                var selectedRace = race;
                return new OpeningRaceSexListEntry(
                    race.FormId,
                    race.Label,
                    race.FormId.Equals(_raceFormId, StringComparison.OrdinalIgnoreCase),
                    true,
                    () =>
                    {
                        _raceFormId = selectedRace.FormId;
                        var sex = CurrentSex();
                        _hairFormId = sex.DefaultHairFormId;
                        _eyesFormId = sex.DefaultEyesFormId;
                        RenderPreview(sex);
                        showRace();
                    });
            }).ToArray(),
            showSex,
            showHair);
        showHair = () =>
        {
            var sex = CurrentSex();
            _raceSexMenuHost!.ShowList(
                "hair",
                sex.HairOptions.Select(option =>
                {
                    var selectedOption = option;
                    return new OpeningRaceSexListEntry(
                        option.FormId,
                        option.Label,
                        option.FormId.Equals(
                            _hairFormId,
                            StringComparison.OrdinalIgnoreCase),
                        true,
                        () =>
                        {
                            _hairFormId = selectedOption.FormId;
                            RenderPreview(CurrentSex());
                            showHair();
                        });
                }).ToArray(),
                showRace,
                showEyes);
        };
        showEyes = () =>
        {
            var sex = CurrentSex();
            _raceSexMenuHost!.ShowList(
                "eyes",
                sex.EyeOptions.Select(option =>
                {
                    var selectedOption = option;
                    return new OpeningRaceSexListEntry(
                        option.FormId,
                        option.Label,
                        option.FormId.Equals(
                            _eyesFormId,
                            StringComparison.OrdinalIgnoreCase),
                        true,
                        () =>
                        {
                            _eyesFormId = selectedOption.FormId;
                            RenderPreview(CurrentSex());
                            showEyes();
                        });
                }).ToArray(),
                showHair,
                showFace);
        };
        showFace = () =>
        {
            _appearancePreviewFaceFraming = true;
            RefreshPreview();
            _raceSexMenuHost!.ShowSliders(
                "faceGeometry",
                previewControls.Select(control =>
                {
                    var selectedControl = control;
                    return new OpeningRaceSexSliderEntry(
                        control.SettingEntity,
                        control.SourceLabel,
                        _faceGeometryControlValues[control.SettingEntity],
                        previewPolicy.Minimum,
                        previewPolicy.Maximum,
                        previewPolicy.Step,
                        previewPolicy.Jump,
                        value => value.ToString(
                            "+0;-0;0",
                            System.Globalization.CultureInfo.InvariantCulture),
                        value =>
                        {
                            UpdateControlValue(selectedControl, value);
                            showFace();
                        });
                }).ToArray(),
                showEyes,
                Accept);
        };
        showBody = () =>
        {
            _appearancePreviewFaceFraming = false;
            RefreshPreview();
            _raceSexMenuHost!.ShowSliders(
                "body",
                new[]
                {
                    (Key: "height", Label: "Height"),
                    (Key: "chest", Label: "Chest"),
                    (Key: "shoulders", Label: "Shoulders"),
                    (Key: "waist", Label: "Waist"),
                    (Key: "arms", Label: "Arms"),
                    (Key: "thighs", Label: "Thighs"),
                    (Key: "calves", Label: "Calves"),
                }.Select(row =>
                {
                    var selected = row;
                    return new OpeningRaceSexSliderEntry(
                        selected.Key,
                        selected.Label,
                        _bodyProportions.Value(selected.Key),
                        CharacterBodyProportions.Minimum,
                        CharacterBodyProportions.Maximum,
                        CharacterBodyProportions.Step,
                        CharacterBodyProportions.Jump,
                        value => $"{Mathf.RoundToInt(value * 100.0f)}%",
                        value =>
                        {
                            _bodyProportions = _bodyProportions.With(selected.Key, value);
                            RefreshPreview();
                            showBody();
                        });
                }).ToArray(),
                showFace,
                Accept);
        };
        _raceSexShowSex = showSex;
        _raceSexShowFace = showFace;
        _raceSexShowBody = showBody;
        _raceSexRenderedDeviceHost.ConfigureCharacterControls(
            source.Font,
            showSex,
            showRace,
            () =>
            {
                _appearancePreviewFaceFraming = true;
                RefreshPreview();
                showFace();
                _raceSexRenderedDeviceHost.SetCreatorModeState(
                    "FACE",
                    bodyEnabled: false,
                    projectionEnabled: _appearancePreviewMode == "2d",
                    faceEnabled: true);
            },
            showHair,
            () =>
            {
                _appearancePreviewFaceFraming = true;
                RefreshPreview();
                showFace();
                _raceSexRenderedDeviceHost.SetCreatorModeState(
                    "FACE",
                    bodyEnabled: false,
                    projectionEnabled: _appearancePreviewMode == "2d",
                    faceEnabled: true);
            },
            () =>
            {
                _appearancePreviewFaceFraming = false;
                RefreshPreview();
                showBody();
                _raceSexRenderedDeviceHost.SetCreatorModeState(
                    "BODY",
                    bodyEnabled: true,
                    projectionEnabled: _appearancePreviewMode == "2d",
                    faceEnabled: false);
            },
            () =>
            {
                _appearancePreviewMode = _appearancePreviewMode == "3d"
                    ? "2d"
                    : "3d";
                RefreshPreview();
                _raceSexRenderedDeviceHost.SetCreatorModeState(
                    _appearancePreviewFaceFraming ? "FACE" : "BODY",
                    !_appearancePreviewFaceFraming,
                    projectionEnabled: _appearancePreviewMode == "2d",
                    faceEnabled: _appearancePreviewFaceFraming);
            });
        RenderPreview(CurrentSex());
        _raceSexRenderedDeviceHost.SetCreatorModeState(
            "FACE",
            bodyEnabled: false,
            projectionEnabled: _appearancePreviewMode == "2d",
            faceEnabled: true);
        showSex();
        GD.Print(
            "OPENNV_NEW_GAME_APPEARANCE_LAYOUT_STATIC_PASS " +
            $"canvas={_flow.ReferenceCanvasSize} faceGrab={source.FaceGrab.Rect} " +
            $"facePresentation={_raceSexRenderedDeviceHost.FacePresentationRect} " +
            $"menuPresentation={_raceSexRenderedDeviceHost.MenuPresentationRect} " +
            $"sourcePanel={source.Background.Rect} " +
            $"listRow={source.ListItem.Rect} sliderRow={source.Slider.Rect} " +
            $"scrollUp={source.Scroll.Up.Rect} scrollDown={source.Scroll.Down.Rect} " +
            "activeList=sex oneActiveList=True " +
            "authority=hashed-owned-racesex-menu-tiles-v1");
    }

    private void ResolveCurrentAppearanceForSex(bool resetToRaceDefaults)
    {
        var appearance = _flow.Character.Appearance;
        var engineSex = appearance.SexEngineValues[_sexIndex];
        var race = appearance.Races.SingleOrDefault(value => value.FormId.Equals(
            _raceFormId,
            StringComparison.OrdinalIgnoreCase)) ?? appearance.Races.Single(value =>
            value.FormId.Equals(
                appearance.DefaultRaceFormId,
                StringComparison.OrdinalIgnoreCase));
        _raceFormId = race.FormId;
        var sex = race.Sex[engineSex];
        if (resetToRaceDefaults || !sex.HairOptions.Any(value => value.FormId.Equals(
                _hairFormId,
                StringComparison.OrdinalIgnoreCase)))
            _hairFormId = sex.DefaultHairFormId;
        if (resetToRaceDefaults || !sex.EyeOptions.Any(value => value.FormId.Equals(
                _eyesFormId,
                StringComparison.OrdinalIgnoreCase)))
            _eyesFormId = sex.DefaultEyesFormId;
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
                Texture = OwnedUiTheme.LoadTexture(value.IconPath),
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
            return;
        }
        var command = info.Commands[index];
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
                if (command.QuestFormId?.Equals(
                    _flow.QuestFormId,
                    StringComparison.OrdinalIgnoreCase) == true &&
                    command.Stage is { } nextStage)
                {
                    SetStage(nextStage);
                    return;
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
                return;
            case "deferredStage":
                if (command.Stage is { } deferred && command.Seconds is { } seconds)
                {
                    CloseModal();
                    _timerTargetStage = deferred;
                    _timerRemainingSeconds = seconds;
                    return;
                }
                throw new InvalidOperationException(
                    "Owned deferred-stage dialogue command is incomplete.");
            default:
                throw new InvalidOperationException(
                    $"Owned opening dialogue command is unsupported: {command.Kind}");
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
