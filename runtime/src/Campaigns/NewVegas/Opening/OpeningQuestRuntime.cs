using System.Buffers.Binary;
using System.Security.Cryptography;
using Godot;
using OpenNV.Runtime.Presentation.CharacterCreation;

using OpenNV.Runtime.SceneGraph;


using OpenNV.Runtime.InputSystem;
using OpenNV.Runtime.Presentation.Ui;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;
using OpenNV.Runtime.World.Interactions;
using OpenNV.Runtime.Gameplay.State;

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
    private OpeningEquippedWeaponState? _equippedWeaponState;
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
    private readonly Dictionary<string, float> _faceTextureControlValues =
        new(StringComparer.Ordinal);
    private float? _faceAgeRawValue;
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
    private FaceGenMorphController _dialogueFace = null!;
    private readonly Dictionary<string, FaceGenMorphController> _ordinaryDialogueFaces =
        new(StringComparer.OrdinalIgnoreCase);
    private GamebryoDialoguePlayback _dialoguePlayback = null!;
    private string? _activeDialogueInfoFormId;
    private int _activeDialogueResponseIndex;
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
    private SourceActorAnimation? _activeGuidePackageAnimation;
    private GamebryoPackageTarget _activeGuidePackageTarget =
        GamebryoPackageTarget.None;
    private OpeningGuideLocomotionClip? _activeGuideLocomotion;
    private ActorModelSlice.LoadedAnimation? _activeGuideAnimation;
    private ActorModelSlice.LoadedAnimation? _activeGuideIdleAnimation;
    private OpeningGuidePriorityAnimation.LayeredPlayback?
        _guideFurnitureLayeredSeatedAnimation;
    private GamebryoFurnitureSession? _guideFurnitureSession;
    private string? _guideAnimationObjectIdleFormId;
    private OpeningCigaretteSmokePresentation? _guideCigaretteSmokePresentation;
    private GamebryoPackageTravel? _guidePackageTravel;
    private bool _restoringGuidePackage;
    private OpeningGuidePackageState? _restoredGuidePackageState;
    private OpeningGuideReference? _guideDestinationReference;
    private bool _guideMoving;
    private bool _guidePackageBegan;
    private bool _guideFurnitureOccupied;
    private bool _guideFurnitureExiting;
    private string? _guideFurnitureReferenceFormId;
    private OpeningGuidePackage? _guideFurnitureExitPackage;
    private bool _guideLookAtPlayer;
    private Action? _guideArrivalContinuation;
    private int _guideArrivalGeneration;
    private bool _openingQuestCompleted;
    private bool _autoDisplayObjectives;
    private AcceptanceAppearancePhase _acceptanceAppearancePhase;
    private OwnedGamebryoFaceGenPreviewHost? _appearancePreviewHost;
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
        var lineEdit = NodeTraversal.Descendants<LineEdit>(_activeModal).FirstOrDefault();
        var buttons = NodeTraversal.Descendants<Button>(_activeModal)
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
        foreach (var settingEntity in faceGen.PreviewHead.TextureControlNames)
            _faceTextureControlValues[settingEntity] = previewPolicy.ResetValue;
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
        AddChild(_dialogueVoice);

        foreach (var value in _flow.Character.SpecialValues)
            _specialValues[value.FormId] = _flow.Character.SpecialInitial;
        ResolveSceneRoles();
        ResolveGuideActor();
        ResolveGuideAnimationObjects();
        _dialogueFace = new FaceGenMorphController(
            _guideActor.Actor,
            configuration.ActorCompiler.FaceGenAnimation.Lip);
        foreach (var actor in _flow.OrdinaryActors)
        {
            var placed = _loaded.Actors.Single(value =>
                value.ReferenceFormId.Equals(
                    actor.ReferenceFormId, StringComparison.OrdinalIgnoreCase) &&
                value.BaseFormId.Equals(
                    actor.BaseFormId, StringComparison.OrdinalIgnoreCase));
            _ordinaryDialogueFaces.Add(
                actor.Role,
                new FaceGenMorphController(
                    placed.Actor,
                    configuration.ActorCompiler.FaceGenAnimation.Lip));
        }
        _dialoguePlayback = new GamebryoDialoguePlayback(
            _dialogueVoice,
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
            if (restoredState.Completed)
            {
                _openingQuestCompleted = true;
                _stage = restoredState.Stage;
                _viewport.Visible = false;
                _viewport.MouseFilter = Control.MouseFilterEnum.Ignore;
                EvaluateOrdinaryActorPackages();
            }
            else
                ResumeRestoredCheckpoint(restoredState.Stage);
        }
        GD.Print(
            $"OPENNV_NEW_GAME_FLOW_READY quest={_flow.QuestEditorId} " +
            $"stage={_stage} restored={restoredState is not null} " +
            $"stages={_flow.Stages.Count} topics={_flow.TopicsByFormId.Count}");
    }

    public override void _Process(double delta)
    {
        if (_openingQuestCompleted)
        {
            UpdateDialogueVoice();
            UpdateOrdinaryActorTravel(delta);
            EvaluateOrdinaryDialogueTriggers();
            return;
        }
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
        _restoringGuidePackage = true;
        try
        {
            if (_restoredGuidePackageState is null)
                throw new InvalidOperationException(
                    "Saved opening checkpoint has no guide package continuation state.");
            EvaluateGuidePackage();
            if (_restoredGuidePackageState is not null)
                throw new InvalidOperationException(
                    "Saved opening guide package continuation was not consumed.");
        }
        finally
        {
            _restoringGuidePackage = false;
        }
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

        void Next(float? updatedTimer = null) => ExecuteStageCommand(
            program,
            index + 1,
            generation,
            updatedTimer ?? timerSeconds);
        var commands = program.Commands.Select((command, sourceIndex) =>
            new SourceGamebryoStageCommand<OpeningFlowCommand>(
                sourceIndex,
                StageCommandKind(command.Kind),
                command)).ToArray();
        GamebryoStageCommandExecutor.ExecuteOne(commands, index, sourceCommand =>
        {
            var command = sourceCommand.Value;
            switch (command.Kind)
            {
                case "setTimer":
                    ApplyQuestTimer(command);
                    Next(command.Seconds);
                    return true;
                case "setQuestVariable":
                    ApplyQuestVariable(command);
                    Next();
                    return true;
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
                    return true;
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
                    return true;
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
                    return true;
                case "objective":
                    ApplyObjective(command);
                    Next();
                    return true;
                case "setDestroyed":
                    ApplyDestroyed(command);
                    Next();
                    return true;
                case "playIdle":
                    ApplyIdle(command);
                    Next();
                    return true;
                case "playerControls":
                    ApplyPlayerControls(command);
                    Next();
                    return true;
                case "addScriptPackage":
                case "removeScriptPackage":
                    ApplyScriptPackage(command);
                    Next();
                    return true;
                case "imageSpaceModifier":
                    ApplyImageSpaceModifier(command);
                    Next();
                    return true;
                case "additem":
                case "removeitem":
                case "equipitem":
                    ApplyInventoryCommand(command);
                    Next();
                    return true;
                case "referenceEnabled":
                    ApplyReferenceEnabled(command);
                    Next();
                    return true;
                case "actorIntent":
                    ApplyActorIntent(command);
                    Next();
                    return true;
                case "actorValueDelta":
                    ApplyActorValueDelta(command);
                    Next();
                    return true;
                case "startQuest":
                case "stopQuest":
                    ApplyQuestLifecycle(command);
                    Next();
                    return true;
                case "setGlobal":
                    ApplyGlobal(command);
                    Next();
                    return true;
                case "autoDisplayObjectives":
                    ApplyAutoDisplayObjectives(command);
                    Next();
                    return true;
                case "achievement":
                    ApplyAchievement(command);
                    Next();
                    return true;
                case "autosave":
                    StoreOpeningCheckpoint();
                    Next();
                    return true;
                case "deferredStage":
                    if (command.Stage is { } deferred && command.Seconds is { } deferredSeconds)
                    {
                        _timerTargetStage = deferred;
                        _timerRemainingSeconds = deferredSeconds;
                        return true;
                    }
                    throw new InvalidOperationException(
                        "Owned deferred-stage command is incomplete.");
                default:
                    throw new InvalidOperationException(
                        $"Owned opening stage command is unsupported: {command.Kind}");
            }
        });
    }

    private static GamebryoStageCommandKind StageCommandKind(string kind) => kind switch
    {
        "setTimer" => GamebryoStageCommandKind.SetTimer,
        "setQuestVariable" => GamebryoStageCommandKind.SetQuestVariable,
        "setStage" => GamebryoStageCommandKind.SetStage,
        "sayTo" => GamebryoStageCommandKind.Dialogue,
        "showMenu" => GamebryoStageCommandKind.ShowMenu,
        "objective" => GamebryoStageCommandKind.Objective,
        "setDestroyed" => GamebryoStageCommandKind.SetDestroyed,
        "playIdle" => GamebryoStageCommandKind.PlayIdle,
        "playerControls" => GamebryoStageCommandKind.PlayerControls,
        "addScriptPackage" => GamebryoStageCommandKind.AddScriptPackage,
        "removeScriptPackage" => GamebryoStageCommandKind.RemoveScriptPackage,
        "imageSpaceModifier" => GamebryoStageCommandKind.ImageSpaceModifier,
        "additem" => GamebryoStageCommandKind.AddItem,
        "removeitem" => GamebryoStageCommandKind.RemoveItem,
        "equipitem" => GamebryoStageCommandKind.EquipItem,
        "referenceEnabled" => GamebryoStageCommandKind.ReferenceEnabled,
        "actorIntent" => GamebryoStageCommandKind.ActorIntent,
        "actorValueDelta" => GamebryoStageCommandKind.ActorValueDelta,
        "startQuest" => GamebryoStageCommandKind.StartQuest,
        "stopQuest" => GamebryoStageCommandKind.StopQuest,
        "setGlobal" => GamebryoStageCommandKind.SetGlobal,
        "autoDisplayObjectives" => GamebryoStageCommandKind.AutoDisplayObjectives,
        "achievement" => GamebryoStageCommandKind.Achievement,
        "autosave" => GamebryoStageCommandKind.Autosave,
        "deferredStage" => GamebryoStageCommandKind.DeferredStage,
        _ => throw new InvalidOperationException(
            $"Owned opening stage command is unsupported: {kind}"),
    };

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
            command.QuestEditorId is null)
            throw new InvalidOperationException("Owned opening objective command is incomplete.");
        var objectives = command.QuestFormId.Equals(
            _flow.QuestFormId,
            StringComparison.OrdinalIgnoreCase)
            ? _flow.Objectives
            : _flow.OrdinaryQuests.TryGetValue(command.QuestFormId, out var ordinary)
                ? ordinary.Objectives
                : throw new InvalidOperationException(
                    "Owned objective quest is absent from the compiled flow.");
        if (!objectives.TryGetValue(index, out var text))
            throw new InvalidOperationException("Owned opening objective text is absent.");
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
            command.Enabled.Value && command.EnableParentChildFormIds.Count == 0);
        foreach (var childFormId in command.EnableParentChildFormIds)
        {
            _referenceEnabledStates[childFormId] = command.Enabled.Value;
            loadedNodes += SetReferenceVisibility(
                childFormId,
                command.Enabled.Value,
                false);
        }
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
            if (command.ItemRecordType == "WEAP")
            {
                if (command.Weapon is null || command.Weapon.Damage <= 0 ||
                    command.Weapon.ClipSize <= 0)
                    throw new InvalidOperationException(
                        "Owned equipped weapon source contract is incomplete.");
                _equippedWeaponState = new OpeningEquippedWeaponState(
                    command.ItemFormId,
                    command.Weapon.AmmoFormId,
                    command.Weapon.Damage,
                    command.Weapon.ClipSize,
                    command.Weapon.ClipSize);
            }
            return;
        }
        throw new InvalidOperationException(
            $"Owned opening inventory operation is unsupported: {command.Kind}");
    }


}
