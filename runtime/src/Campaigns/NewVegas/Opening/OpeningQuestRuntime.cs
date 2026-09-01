using System.Buffers.Binary;
using System.Security.Cryptography;
using Godot;
using OpenNV.Runtime.Presentation.CharacterCreation;

using OpenNV.Runtime.SceneGraph;


using OpenNV.Runtime.InputSystem;
using OpenNV.Runtime.Diagnostics.Acceptance;
using OpenNV.Runtime.Presentation.Ui;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;
using OpenNV.Runtime.World.Interactions;
using OpenNV.Runtime.World.Portals;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Campaigns.NewVegas.Opening;

internal partial class OpeningQuestRuntime : CanvasLayer
{
    private const int GetIsSexConditionFunction = 70;
    private const int GetIsIdConditionFunction = 72;
    private const int GetQuestVariableConditionFunction = 79;
    private const int GetStageConditionFunction = 58;
    private const int GetStageDoneConditionFunction = 420;
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
    private readonly Dictionary<string, float> _referenceVariables =
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
    private readonly Dictionary<string, int> _combatHealthByReferenceFormId =
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
    private ActorAnimationPlayback? _guideLocomotionPlayback;
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
        var completeAtFirstEncounter = mode is "route-stage50" or "route-stage50-resume";
        var resumeToFirstEncounter = mode == "route-stage50-resume";
        if ((!stopAtCheckpoint && !stopAfterCreator && !completeAfterResume &&
                !completeAtFirstEncounter) ||
            string.IsNullOrWhiteSpace(playerName) || timeoutSeconds <= 0.0)
            throw new ArgumentException("Opening acceptance arguments are invalid.");
        var initialState = _loaded.Session.OpeningState;
        if (resumeToFirstEncounter && initialState is null)
            throw new InvalidOperationException(
                "FNV route resume acceptance requires saved campaign state.");
        if (completeAfterResume && initialState is not { Completed: false })
            throw new InvalidOperationException(
                "Opening resume acceptance requires an incomplete saved opening.");
        var checkpointStage = AuthoredCheckpointStage();
        var proveFirstPlayerAction = completeAfterResume &&
            initialState!.Stage == checkpointStage;
        var startMilliseconds = Time.GetTicksMsec();
        var navigationKey = string.Empty;
        var navigationEvent = string.Empty;
        Vector3? navigationTarget = null;
        IReadOnlyList<Vector3> navigationPath = Array.Empty<Vector3>();
        var navigationIndex = 0;
        var obstacleRecoveryFailures = new Dictionary<string, int>(
            StringComparer.Ordinal);
        var rejectedFiringDestinations = new HashSet<Vector3>();
        var lastRouteProgressSecond = -1;
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
        _loaded.Player.SetSyntheticMouseMotionPolicy(true);
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
                if (completeAtFirstEncounter && saved is { Completed: true } &&
                    FirstEncounterCompleted())
                {
                    _loaded.Session.StoreOpeningState(CaptureState(true));
                    GD.Print(
                        "OPENNV_FNV_ROUTE_STAGE50_PASS " +
                        $"activeCell={_loaded.Session.ActiveCellFormId} " +
                        $"destroyedTargets={_destroyedReferences.Count} " +
                        $"combatDeaths={_combatHealthByReferenceFormId.Values.Count(value => value == 0)}");
                    return CaptureState(true);
                }
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
                var routeProgressSecond = (int)elapsedSeconds;
                if (routeProgressSecond != lastRouteProgressSecond &&
                    routeProgressSecond % 5 == 0)
                {
                    lastRouteProgressSecond = routeProgressSecond;
                    GD.Print(
                        $"OPENNV_OPENING_ACCEPTANCE_PROGRESS stage={_stage} " +
                        $"cell={_loaded.Session.ActiveCellFormId} " +
                        $"player={_loaded.Player.GlobalPosition} " +
                        $"movement={_loaded.Player.MovementEnabled} " +
                        $"look={_loaded.Player.LookEnabled} " +
                        $"activation={_loaded.Player.ActivationEnabled} " +
                        $"pipBoy={_loaded.Session.IsPipBoyOpen} " +
                        $"movementAction={movementHeld?.Action ?? "none"} " +
                        $"movementPressed={movementHeld is not null && Input.IsActionPressed(movementHeld.Action)} " +
                        $"movementState={_loaded.Player.LastMovementState} " +
                        $"movementCollision={_loaded.Player.LastMovementCollision} " +
                        $"navigationIndex={navigationIndex}/{navigationPath.Count}");
                }
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
                if (movementHeld is not null &&
                    _loaded.Player.LastBlockingCollider is not null &&
                    Ancestor<DoorInstance>(_loaded.Player.LastBlockingCollider) is null &&
                    navigationPath.Count > 0)
                {
                    SetAcceptanceMovement(null, ref movementHeld);
                    var recoveryWaypoint = navigationPath[
                        Math.Min(navigationIndex, navigationPath.Count - 1)];
                    var recoveryOrigin = _loaded.Player.GlobalPosition;
                    var recoveryDistance = HorizontalDistance(
                        recoveryOrigin,
                        recoveryWaypoint);
                    var blockerPath = _loaded.Player.LastBlockingCollider.GetPath()
                        .ToString();
                    var recovered = await CellRouteTravelAcceptance.RecoverAroundObstacle(
                        this,
                        _loaded.Player,
                        recoveryWaypoint,
                        _configuration.Player.DesktopInput,
                        _configuration,
                        _configuration.Player.CapsuleRadiusMeters,
                        requireDirectSweep: false);
                    if (!recovered && navigationIndex + 1 < navigationPath.Count)
                    {
                        var lookaheadWaypoint = navigationPath[navigationIndex + 1];
                        recovered = await CellRouteTravelAcceptance.RecoverAroundObstacle(
                            this,
                            _loaded.Player,
                            lookaheadWaypoint,
                            _configuration.Player.DesktopInput,
                            _configuration,
                            _configuration.Player.CapsuleRadiusMeters,
                            requireDirectSweep: false);
                        if (recovered)
                            navigationIndex++;
                    }
                    var progress = recoveryDistance - HorizontalDistance(
                        _loaded.Player.GlobalPosition,
                        recoveryWaypoint);
                    var recoveryKey = $"{navigationKey}:{blockerPath}";
                    if (recovered)
                    {
                        obstacleRecoveryFailures.Remove(recoveryKey);
                        continue;
                    }
                    if (progress < _configuration.Player.CapsuleRadiusMeters)
                    {
                        var failures = obstacleRecoveryFailures.GetValueOrDefault(
                            recoveryKey) + 1;
                        obstacleRecoveryFailures[recoveryKey] = failures;
                        if (failures >= CellRouteTravelAcceptance
                                .MaximumOwnedNavigationReplans)
                        {
                            if (navigationEvent.Equals("fire", StringComparison.OrdinalIgnoreCase) &&
                                navigationPath.Count > 0)
                            {
                                rejectedFiringDestinations.Add(navigationPath[^1]);
                                obstacleRecoveryFailures.Remove(recoveryKey);
                            }
                            else
                                throw new InvalidOperationException(
                                    "Configured route could not clear authored CELL collision " +
                                    $"after {failures} owned navigation replans: {blockerPath}; " +
                                    $"step={_loaded.Player.LastStepAttempt}; " +
                                    $"movement={_loaded.Player.LastMovementState}");
                        }
                    }
                    navigationKey = string.Empty;
                    navigationTarget = null;
                    navigationPath = Array.Empty<Vector3>();
                    navigationIndex = 0;
                    continue;
                }
                if (AcceptanceOpenBlockingDoor() is { } openBlockingDoor &&
                    navigationPath.Count > 0)
                {
                    SetAcceptanceMovement(null, ref movementHeld);
                    var recoveryWaypoint = navigationPath[
                        Math.Min(navigationIndex, navigationPath.Count - 1)];
                    var recoveryOrigin = _loaded.Player.GlobalPosition;
                    var recovered = await CellRouteTravelAcceptance.RecoverAroundObstacle(
                            this,
                            _loaded.Player,
                            recoveryWaypoint,
                            _configuration.Player.DesktopInput,
                            _configuration,
                            _configuration.Player.CapsuleRadiusMeters,
                            requireDirectSweep: false);
                    if (!recovered && HorizontalDistance(
                            recoveryOrigin,
                            _loaded.Player.GlobalPosition) <
                        _configuration.Player.CapsuleRadiusMeters)
                        throw new InvalidOperationException(
                            "Configured route could not clear the opened source door: " +
                            openBlockingDoor.ReferenceFormId);
                    navigationKey = string.Empty;
                    navigationTarget = null;
                    navigationPath = Array.Empty<Vector3>();
                    navigationIndex = 0;
                    continue;
                }
                if (AcceptanceBlockingDoor() is { } blockingDoor)
                {
                    SetAcceptanceMovement(null, ref movementHeld);
                    FlatControlsAcceptance.ApplyMouseLook(
                        _loaded.Player,
                        blockingDoor.Position,
                        _configuration.Player);
                    var settleFrames =
                        _configuration.Player.DesktopInput.Acceptance.SettleFrames;
                    for (var settle = 0; settle < settleFrames; settle++)
                        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                    Input.ParseInputEvent(DesktopInputMap.CreateEvent(
                        _configuration.Player.DesktopInput.Activate,
                        true));
                    await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                    Input.ParseInputEvent(DesktopInputMap.CreateEvent(
                        _configuration.Player.DesktopInput.Activate,
                        false));
                    await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                    for (var settle = 0; settle < settleFrames; settle++)
                        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                    if (!blockingDoor.Door.IsOpen)
                        throw new InvalidOperationException(
                            "Configured route activation did not open its source door: " +
                            blockingDoor.Door.ReferenceFormId);
                    await blockingDoor.Door.WaitForArticulation();
                    if (blockingDoor.Door.HasSourceArticulation &&
                        !blockingDoor.Door.SourceOpenTerminalApplied)
                        throw new InvalidOperationException(
                            "Configured route source door did not reach its open terminal: " +
                            blockingDoor.Door.ReferenceFormId);
                    if (navigationPath.Count > 0)
                    {
                        var recoveryWaypoint = navigationPath[
                            Math.Min(navigationIndex, navigationPath.Count - 1)];
                        var recoveryOrigin = _loaded.Player.GlobalPosition;
                        var recovered = await CellRouteTravelAcceptance.RecoverAroundObstacle(
                                this,
                                _loaded.Player,
                                recoveryWaypoint,
                                _configuration.Player.DesktopInput,
                                _configuration,
                                _configuration.Player.CapsuleRadiusMeters,
                                requireDirectSweep: false);
                        if (!recovered && HorizontalDistance(
                                recoveryOrigin,
                                _loaded.Player.GlobalPosition) <
                            _configuration.Player.CapsuleRadiusMeters)
                            throw new InvalidOperationException(
                                "Configured route could not clear the opened source door: " +
                                blockingDoor.Door.ReferenceFormId);
                        navigationKey = string.Empty;
                        navigationTarget = null;
                        navigationPath = Array.Empty<Vector3>();
                        navigationIndex = 0;
                    }
                    GD.Print(
                        "OPENNV_OPENING_ACCEPTANCE_BLOCKING_DOOR_OPEN " +
                        $"form={blockingDoor.Door.ReferenceFormId} " +
                        $"cell={_loaded.Session.ActiveCellFormId}");
                    continue;
                }
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
                        showcaseCreatorControls: appearancePresentationHoldFrames > 0,
                        exerciseSourceSelections: !completeAtFirstEncounter);
                    continue;
                }

                var routeAction = completeAtFirstEncounter && saved is { Completed: true }
                    ? FirstEncounterAction()
                    : null;
                var combatActive = _flow.CombatEncounters.Any(encounter =>
                    _quests.TryGetValue(encounter.QuestFormId, out var quest) &&
                    quest.Stage >= encounter.MinimumCombatStage &&
                    encounter.Targets.Any(target =>
                        _combatHealthByReferenceFormId[target.ReferenceFormId] > 0));
                if (combatActive && _loaded.Session.AmmoInMagazine == 0 &&
                    _loaded.Session.ReserveAmmo > 0)
                {
                    SetAcceptanceMovement(null, ref movementHeld);
                    Input.ParseInputEvent(DesktopInputMap.CreateEvent(
                        _configuration.Player.DesktopInput.Reload,
                        true));
                    await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                    Input.ParseInputEvent(DesktopInputMap.CreateEvent(
                        _configuration.Player.DesktopInput.Reload,
                        false));
                    await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                    continue;
                }
                var interaction = routeAction is null
                    ? _flow.Interactions.SingleOrDefault(value => value.FromStage == _stage)
                    : null;
                if (interaction is null && routeAction is null)
                {
                    SetAcceptanceMovement(null, ref movementHeld);
                    continue;
                }
                Node3D target;
                string eventName;
                string currentNavigationKey;
                if (routeAction is not null)
                {
                    target = routeAction.Target;
                    eventName = routeAction.Event;
                    currentNavigationKey = routeAction.Key;
                }
                else
                {
                    if (!_roleNodes.TryGetValue(interaction!.TargetRole, out target!))
                        throw new InvalidOperationException(
                            $"Opening acceptance target role is absent: {interaction.TargetRole}");
                    eventName = interaction.Event;
                    currentNavigationKey =
                        $"opening:{_stage}:{interaction.Event}:{interaction.TargetRole}";
                }
                var interactionPosition = routeAction?.AimTarget ?? target.GlobalPosition;
                var distanceOrigin = eventName.Equals(
                        "activate",
                        StringComparison.OrdinalIgnoreCase)
                    ? _loaded.Player.Camera.GlobalPosition
                    : _loaded.Player.GlobalPosition;
                var distance = distanceOrigin.DistanceTo(interactionPosition);
                var fireReady = false;
                if (eventName.Equals("fire", StringComparison.OrdinalIgnoreCase) &&
                    distance <= _configuration.Player.FireRayDistanceMeters)
                {
                    FlatControlsAcceptance.ApplyMouseLook(
                        _loaded.Player,
                        interactionPosition,
                        _configuration.Player);
                    await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                    fireReady = _loaded.Session.CanHitscanReach(
                        _loaded.Player.Camera,
                        _loaded.Player.CollisionMask,
                        target);
                }
                if (distance <= _configuration.Player.ActivationDistanceMeters || fireReady)
                {
                    SetAcceptanceMovement(null, ref movementHeld);
                    if (fireReady)
                    {
                        var settleFrames =
                            _configuration.Player.DesktopInput.Acceptance.SettleFrames;
                        for (var settle = 0; settle < settleFrames; settle++)
                        {
                            var collision = target.GetChildren()
                                .OfType<GamebryoActorCollision>()
                                .SingleOrDefault();
                            interactionPosition = collision?.GlobalPosition ??
                                routeAction?.AimTarget ?? target.GlobalPosition;
                            FlatControlsAcceptance.ApplyMouseLook(
                                _loaded.Player,
                                interactionPosition,
                                _configuration.Player);
                            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                            if (!_loaded.Session.CanHitscanReach(
                                    _loaded.Player.Camera,
                                    _loaded.Player.CollisionMask,
                                    target))
                            {
                                fireReady = false;
                                break;
                            }
                        }
                        if (!fireReady)
                            continue;
                    }
                    if (eventName.Equals("activate", StringComparison.OrdinalIgnoreCase))
                    {
                        var settleFrames =
                            _configuration.Player.DesktopInput.Acceptance.SettleFrames;
                        FlatControlsAcceptance.ApplyMouseLook(
                            _loaded.Player,
                            interactionPosition,
                            _configuration.Player);
                        for (var settle = 0;
                             settle < settleFrames;
                             settle++)
                            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                        Input.ParseInputEvent(DesktopInputMap.CreateEvent(
                            _configuration.Player.DesktopInput.Activate,
                            true));
                        activateHeld = true;
                        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                        Input.ParseInputEvent(DesktopInputMap.CreateEvent(
                            _configuration.Player.DesktopInput.Activate,
                            false));
                        activateHeld = false;
                        if (!navigationKey.Equals(
                                currentNavigationKey,
                                StringComparison.Ordinal))
                        {
                            navigationKey = currentNavigationKey;
                            var activationOffset = interactionPosition -
                                _loaded.Player.Camera.GlobalPosition;
                            var facingDot = -_loaded.Player.Camera.GlobalBasis.Z.Normalized()
                                .Dot(activationOffset.Normalized());
                            GD.Print(
                                $"OPENNV_OPENING_ACCEPTANCE_ACTIVATION " +
                                $"key={currentNavigationKey} " +
                                $"player={_loaded.Player.GlobalPosition} " +
                                $"camera={_loaded.Player.Camera.GlobalPosition} " +
                                $"aim={interactionPosition} " +
                                $"distance={activationOffset.Length():F3} " +
                                $"facingDot={facingDot:F6} " +
                                $"collider={_loaded.Player.LastActivationCollider} " +
                                $"door={_loaded.Player.LastActivationDoorFormId}");
                        }
                    }
                    else if (eventName.Equals("fire", StringComparison.OrdinalIgnoreCase))
                    {
                        Input.ParseInputEvent(DesktopInputMap.CreateEvent(
                            _configuration.Player.DesktopInput.Fire,
                            true));
                        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                        Input.ParseInputEvent(DesktopInputMap.CreateEvent(
                            _configuration.Player.DesktopInput.Fire,
                            false));
                    }
                    continue;
                }

                var movingTargetChanged = eventName.Equals(
                        "follow", StringComparison.OrdinalIgnoreCase) &&
                    navigationPath.Count > 0 &&
                    navigationIndex >= navigationPath.Count - 1 &&
                    navigationTarget is { } previousNavigationTarget &&
                    HorizontalDistance(previousNavigationTarget, interactionPosition) >
                        _configuration.Player.CapsuleRadiusMeters;
                if (!navigationKey.Equals(currentNavigationKey, StringComparison.Ordinal) ||
                    movingTargetChanged)
                {
                    navigationKey = currentNavigationKey;
                    navigationEvent = eventName;
                    navigationTarget = interactionPosition;
                    navigationIndex = 0;
                    var content = ActiveAcceptanceContent();
                    var playerFoot = _loaded.Player.GlobalPosition - Vector3.Up *
                        _configuration.Player.SpawnCenterHeightMeters;
                    navigationPath = eventName.Equals(
                            "fire",
                            StringComparison.OrdinalIgnoreCase)
                        ? FindAcceptanceFiringPath(
                            content,
                            playerFoot,
                            interactionPosition,
                            target,
                            rejectedFiringDestinations)
                        : content.Navigation.FindPath(
                                content.WorldToGame(playerFoot),
                                content.WorldToGame(interactionPosition))
                            .Select(content.GameToWorld)
                            .ToArray();
                    if (navigationPath.Count == 0)
                    {
                        if (eventName.Equals("fire", StringComparison.OrdinalIgnoreCase))
                        {
                            SetAcceptanceMovement(null, ref movementHeld);
                            continue;
                        }
                        throw new InvalidOperationException(
                            $"Owned route target has no authored navigation path: {currentNavigationKey}");
                    }
                    GD.Print(
                        $"OPENNV_OPENING_ACCEPTANCE_PATH key={currentNavigationKey} " +
                        $"event={eventName} distance={distance:F3} " +
                        $"waypoints={navigationPath.Count}");
                }
                if (navigationPath.Count == 0)
                {
                    SetAcceptanceMovement(null, ref movementHeld);
                    continue;
                }
                while (navigationIndex < navigationPath.Count - 1 &&
                    HorizontalDistance(
                        _loaded.Player.GlobalPosition,
                        navigationPath[navigationIndex]) <=
                    _configuration.Player.CapsuleRadiusMeters)
                    navigationIndex++;
                var waypoint = navigationPath[Math.Min(navigationIndex, navigationPath.Count - 1)];
                if (navigationIndex == navigationPath.Count - 1 &&
                    HorizontalDistance(_loaded.Player.GlobalPosition, waypoint) <=
                        _configuration.Player.CapsuleRadiusMeters &&
                    HorizontalDistance(_loaded.Player.GlobalPosition, interactionPosition) >
                        _configuration.Player.ActivationDistanceMeters)
                {
                    SetAcceptanceMovement(null, ref movementHeld);
                    var recovered = await CellRouteTravelAcceptance.RecoverAroundObstacle(
                        this,
                        _loaded.Player,
                        interactionPosition,
                        _configuration.Player.DesktopInput,
                        _configuration,
                        _configuration.Player.CapsuleRadiusMeters,
                        requireDirectSweep: false);
                    var recoveryKey = $"{currentNavigationKey}:projected-endpoint";
                    if (!recovered)
                    {
                        var failures = obstacleRecoveryFailures.GetValueOrDefault(
                            recoveryKey) + 1;
                        obstacleRecoveryFailures[recoveryKey] = failures;
                        if (failures >= CellRouteTravelAcceptance
                                .MaximumOwnedNavigationReplans)
                            throw new InvalidOperationException(
                                "Configured route could not continue from its authored " +
                                "NAVM projection over owned CELL collision after " +
                                $"{failures} bounded recoveries: {currentNavigationKey}.");
                    }
                    else
                    {
                        obstacleRecoveryFailures.Remove(recoveryKey);
                    }
                    navigationKey = string.Empty;
                    navigationTarget = null;
                    navigationPath = Array.Empty<Vector3>();
                    navigationIndex = 0;
                    continue;
                }
                FaceAcceptanceWaypoint(waypoint);
                // FaceAcceptanceWaypoint publishes the exact yaw through the configured
                // mouse input.  That input is consumed on the next physics frame, so
                // selecting a movement direction from the current camera basis here can
                // alternate forward/backward at a boundary.  Movement is relative to the
                // requested facing, and therefore always uses the configured forward
                // binding.
                var requestedMovement = _configuration.Player.DesktopInput.MoveForward;
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
            _loaded.Player.SetSyntheticMouseMotionPolicy(false);
            SetAcceptanceMovement(null, ref movementHeld);
            if (activateHeld)
                Input.ParseInputEvent(DesktopInputMap.CreateEvent(
                    _configuration.Player.DesktopInput.Activate,
                    false));
        }
    }

    private bool FirstEncounterCompleted()
    {
        if (_flow.CombatEncounters.Count == 0)
            throw new InvalidOperationException(
                "Owned FNV route has no first combat encounter.");
        var completionStages = _flow.CombatEncounters
            .Select(value => (value.QuestFormId, value.CompletionStage))
            .Distinct().ToArray();
        if (completionStages.Length != 1)
            throw new InvalidOperationException(
                "Owned FNV first-encounter completion is ambiguous.");
        var completion = completionStages[0];
        return _quests.TryGetValue(completion.QuestFormId, out var quest) &&
            quest.Stage >= completion.CompletionStage;
    }

    private OpeningRouteAcceptanceAction FirstEncounterAction()
    {
        var actor = _flow.OrdinaryActors.Single();
        var placedActor = _loaded.Actors.Single(value =>
            value.ReferenceFormId.Equals(
                actor.ReferenceFormId, StringComparison.OrdinalIgnoreCase));
        string? packageTargetCellFormId = null;
        if (_ordinaryActorPackages.TryGetValue(
                actor.ReferenceFormId, out var selectedPackage) &&
            selectedPackage.Location?.Reference is { } packageTarget)
        {
            packageTargetCellFormId = ContentOwningReference(packageTarget).FormId;
            if (!_loaded.Session.ActiveCellFormId.Equals(
                    packageTargetCellFormId, StringComparison.OrdinalIgnoreCase))
                return PortalActionToward(packageTargetCellFormId);
        }
        var actorCellFormId = packageTargetCellFormId ??
            ContentOwningActor(placedActor).FormId;
        if (!_loaded.Session.ActiveCellFormId.Equals(
                actorCellFormId,
                StringComparison.OrdinalIgnoreCase))
            return PortalActionToward(actorCellFormId);

        foreach (var encounter in _flow.CombatEncounters)
        {
            if (!_quests.TryGetValue(encounter.QuestFormId, out var quest) ||
                quest.Stage < encounter.MinimumCombatStage)
                continue;
            foreach (var target in encounter.Targets)
            {
                if (_combatHealthByReferenceFormId[target.ReferenceFormId] == 0 ||
                    !_referenceEnabledStates.GetValueOrDefault(
                        target.ReferenceFormId, true))
                    continue;
                var combatActor = CombatActor(target);
                GamebryoActorCollision.Synchronize(combatActor.Placement);
                return new OpeningRouteAcceptanceAction(
                    combatActor.Placement,
                    "fire",
                    $"combat:{target.ReferenceFormId}",
                    combatActor.Placement.GlobalTransform *
                        combatActor.LocalBounds.GetCenter());
            }
        }

        foreach (var targetSet in _flow.HitTargetSets)
        {
            foreach (var target in targetSet.Targets)
            {
                if (_destroyedReferences.Contains(target.ReferenceFormId) ||
                    !_referenceEnabledStates.GetValueOrDefault(target.ReferenceFormId))
                    continue;
                var placed = FindPlacedReference(target.ReferenceFormId) ??
                    throw new InvalidOperationException(
                        "Owned enabled shooting target is absent.");
                return new OpeningRouteAcceptanceAction(
                    placed,
                    "fire",
                    $"target:{target.ReferenceFormId}");
            }
        }

        var ordinaryQuest = _flow.OrdinaryQuests.Values.Single();
        _quests.TryGetValue(ordinaryQuest.FormId, out var questState);
        var activate = questState?.Stage == ordinaryQuest.EntryStage;
        return new OpeningRouteAcceptanceAction(
            placedActor.Placement,
            activate ? "activate" : "follow",
            $"actor:{actor.ReferenceFormId}:{questState?.Stage ?? -1}");
    }

    private OpeningRouteAcceptanceAction PortalActionToward(string targetCellFormId)
    {
        var activeCellFormId = _loaded.Session.ActiveCellFormId;
        var candidates = new List<(
            string Next,
            DoorInstance Door,
            CellSceneLoader.DoorRay Frame)>();
        foreach (var link in _loaded.PortalLinks)
        {
            if (link.FromCellFormId.Equals(
                    activeCellFormId,
                    StringComparison.OrdinalIgnoreCase))
                candidates.Add((link.ToCellFormId, link.FromDoor, link.FromFrame));
            else if (link.ToCellFormId.Equals(
                    activeCellFormId,
                    StringComparison.OrdinalIgnoreCase))
                candidates.Add((link.FromCellFormId, link.ToDoor, link.ToFrame));
        }
        candidates = candidates.Where(value =>
            CellRouteExists(value.Next, targetCellFormId, activeCellFormId)).ToList();
        if (candidates.Count != 1)
            throw new InvalidOperationException(
                "Owned route portal path is absent or ambiguous: " +
                $"{activeCellFormId} -> {targetCellFormId}");
        var selected = candidates[0];
        return new OpeningRouteAcceptanceAction(
            selected.Door,
            "activate",
            $"portal:{selected.Door.ReferenceFormId}",
            (selected.Frame.From + selected.Frame.To) / 2.0f);
    }

    private bool CellRouteExists(
        string startCellFormId,
        string targetCellFormId,
        string excludedCellFormId)
    {
        var visited = new HashSet<string>(
            [excludedCellFormId],
            StringComparer.OrdinalIgnoreCase);
        var pending = new Queue<string>();
        pending.Enqueue(startCellFormId);
        while (pending.TryDequeue(out var cellFormId))
        {
            if (!visited.Add(cellFormId))
                continue;
            if (cellFormId.Equals(targetCellFormId, StringComparison.OrdinalIgnoreCase))
                return true;
            foreach (var link in _loaded.PortalLinks)
            {
                if (link.FromCellFormId.Equals(
                        cellFormId,
                        StringComparison.OrdinalIgnoreCase))
                    pending.Enqueue(link.ToCellFormId);
                else if (link.ToCellFormId.Equals(
                             cellFormId,
                             StringComparison.OrdinalIgnoreCase))
                    pending.Enqueue(link.FromCellFormId);
            }
        }
        return false;
    }

    private CellContentLoader.LoadedContent ActiveAcceptanceContent()
    {
        if (_loaded.Session.ActiveCellFormId.Equals(
                _loaded.FormId, StringComparison.OrdinalIgnoreCase))
            return _loaded.MainContent;
        return _loaded.LinkedCells.Single(value => value.Content.FormId.Equals(
            _loaded.Session.ActiveCellFormId,
            StringComparison.OrdinalIgnoreCase)).Content;
    }

    private sealed record OpeningRouteAcceptanceAction(
        Node3D Target,
        string Event,
        string Key,
        Vector3? AimTarget = null);

    private void AdvanceAcceptanceModal(
        string playerName,
        bool showcaseCreatorControls,
        bool exerciseSourceSelections)
    {
        if (_activeModal is null || _dialoguePlayback.ActiveLine is not null)
            return;
        if (!_activeModal.HasMeta("opennv_acceptance_inspected"))
        {
            _activeModal.SetMeta("opennv_acceptance_inspected", true);
            var allButtons = NodeTraversal.Descendants<Button>(_activeModal).ToArray();
            GD.Print(
                $"OPENNV_OPENING_ACCEPTANCE_MODAL buttons={allButtons.Length} " +
                $"enabled={allButtons.Count(button => !button.Disabled)} " +
                $"visible={allButtons.Count(button => button.IsVisibleInTree())}");
        }
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
                exerciseSourceSelections &&
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
        PressAcceptanceButton(
            buttons.FirstOrDefault(button => button.HasFocus()) ??
            buttons.FirstOrDefault());
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

    private (DoorInstance Door, Vector3 Position)? AcceptanceBlockingDoor()
    {
        var door = Ancestor<DoorInstance>(_loaded.Player.LastBlockingCollider);
        if (door is null || door.IsOpen || door.Destination is not null ||
            door.LinkedDoor is not null ||
            _loaded.Player.LastBlockingPosition is not { } blockingPosition)
            return null;
        var content = ActiveAcceptanceContent();
        if (!content.Doors.TryGetValue(door.ReferenceFormId, out var activeDoor) ||
            !ReferenceEquals(activeDoor, door))
            throw new InvalidOperationException(
                "Configured route was blocked by a door outside the active CELL.");
        return _loaded.Player.Camera.GlobalPosition.DistanceTo(blockingPosition) <=
            _configuration.Player.ActivationDistanceMeters
                ? (door, blockingPosition)
                : null;
    }

    private DoorInstance? AcceptanceOpenBlockingDoor()
    {
        var door = Ancestor<DoorInstance>(_loaded.Player.LastBlockingCollider);
        if (door is null || !door.IsOpen || door.Destination is not null ||
            door.LinkedDoor is not null)
            return null;
        var content = ActiveAcceptanceContent();
        if (!content.Doors.TryGetValue(door.ReferenceFormId, out var activeDoor) ||
            !ReferenceEquals(activeDoor, door))
            throw new InvalidOperationException(
                "Configured route was blocked by an open door outside the active CELL.");
        return door;
    }

    private static T? Ancestor<T>(Node? node)
        where T : Node
    {
        while (node is not null)
        {
            if (node is T typed)
                return typed;
            node = node.GetParent();
        }
        return null;
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
        _loaded.Session.SetHitscanHitHandler(HandleHitscanHit);
        InitializeCombatActors();
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
                ApplyStageControlPolicy();
                _loaded.Player.ClearOwnedNavigation();
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
            UpdateDialogueVoice(delta);
            UpdateOrdinaryActorTravel(delta);
            UpdateCombatActors(delta);
            EvaluateOrdinaryDialogueTriggers();
            return;
        }
        UpdatePlayerAnimation(delta);
        UpdateImageSpaceModifiers(delta);
        UpdateGuideActor(delta);
        UpdateGuideAnimationObjectLifecycle();
        _guideCigaretteSmokePresentation?.Update(delta);
        UpdateDialogueVoice(delta);
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
                case "setReferenceVariable":
                    ApplyReferenceVariable(command);
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
        "setReferenceVariable" => GamebryoStageCommandKind.SetReferenceVariable,
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

}
