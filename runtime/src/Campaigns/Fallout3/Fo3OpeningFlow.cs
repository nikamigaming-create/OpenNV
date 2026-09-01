using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using OpenNV.Runtime.Campaigns.NewVegas.Opening;
using OpenNV.Runtime.Presentation.CharacterCreation;
using OpenNV.Runtime.Presentation.Ui;


using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Campaigns.Fallout3;

internal partial class Fo3OpeningFlow : CanvasLayer
{
    private Fo3OwnedProfile _profile = null!;
    private string _savePath = "";
    private Node3D _worldHost = null!;
    private Fo3Vault101BirthPresentationContract? _birthPresentation;
    private Fo3Cg00RetailStage10Contract? _retailCg00Stage10Contract;
    private Fo3TtwCg00Stage10PresentationContract? _ttwCg00Stage10PresentationContract;
    private Fo3TtwCg00Stage10SurfaceContract? _ttwCg00Stage10SurfaceContract;
    private ColorRect _background = null!;
    private PanelContainer _panel = null!;
    private VBoxContainer _content = null!;
    private AudioStreamPlayer _music = null!;
    private Control? _introLayer;
    private VideoStreamPlayer? _video;
    private Fo3SexChoice? _selectedSex;
    private Node3D? _vaultPreviewHost;
    private Control? _vaultPreviewOverlay;
    private AudioStreamPlayer? _vaultDialogueVoice;
    private FaceGenMorphController? _cg01DadFace;
    private FaceGenLipAnimation? _activeCg01DadLip;
    private string? _activeCg01DadInfoFormId;
    private bool _cg01DadLipSampleLogged;
    private int _cg01DadLipCueSamples;
    private readonly List<string> _cg01DadPublishedSpeakerIdleInfoFormIds = [];
    private AudioStreamPlayer? _vaultEffectSound;
    private ColorRect? _vaultStage90Fade;
    private Fo3Stage90ImageSpaceModifier? _activeStage90ImageSpaceModifier;
    private double _stage90ImageSpaceElapsedSeconds;
    private Fo3Vault101BirthSceneCoverage? _vaultBirthCoverage;
    private Fo3Stage100RuntimeContext? _stage100Runtime;
    private double _stage100TimerRemainingSeconds;
    private RuntimeConfiguration _runtimeConfiguration = null!;
    private OpeningManifest? _characterReflectron;
    private OpeningRaceSexRenderedDeviceHost? _reflectron;
    private string? _appearanceProofMode;
    private string? _appearanceProofReportPath;
    private string? _appearanceProofCaptureRoot;
    private bool _characterVideo;
    private Control? _creatorLayer;
    private LineEdit? _activeNameInput;
    private Action? _activeAppearanceShowFace;
    private HSlider? _activeFaceControlSlider;
    private Fo3AppearanceSelection? _activeAppearanceSelection;
    private OwnedGamebryoFaceGenPreviewHost? _activeFacePreview;
    private bool _introCompleted;
    private Fo3OwnedVideoMode _ownedVideoMode;
    private Fo3Cg01Stage0State? _activeCg01MovieState;
    private Fo3Cg01RuntimeContext? _activeCg01MovieContext;
    private Fo3Cg01ToddlerWorldRuntime? _cg01ToddlerWorld;
    private Fo3SpecialBookMenuRuntime? _cg01SpecialBookMenu;
    private Action<double>? _cg01Stage50TimerTick;
    private Action<double>? _cg01DadPackageTravelTick;
    private Action<double>? _cg01Stage90TimerTick;
    private Action? _cg02IntroBegin;
    private Action<double>? _cg02IntroTimerTick;
    private Action<double>? _cg02CakePackageTick;
    private Action<double>? _cg02ButchTimerTick;
    private Action<double>? _cg02ButchPackageTick;
    private readonly Dictionary<string, CellActorLoader.PlacedActor> _cg02IntroActors =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly List<GamebryoDialoguePlayback> _cg02IntroDialogue = [];
    private readonly Dictionary<string, ActorAnimationPlayback> _cg02IntroAnimations =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly List<AudioStreamPlayer> _cg02IntroSounds = [];
    private string? _cg01ProofMode;
    private string? _cg01ProofReportPath;
    private string? _cg01ProofCapturePath;
    private bool _cg01ProofMovieEscapeSkipped;
    private bool _ownedVideoFrameNonblank;
    private bool _ownedVideoEverVisible;
    private bool _ownedVideoCleared;
    private CellReferenceLedger.Geometry? _cg01DadDialogueGeometry;
    private bool _cg01ProofCaptureCompleted;
    private string? _cg01ProofCaptureSha256;
    private string? _cg01ProofCaptureInfoFormId;
    private string? _cg01ProofCaptureSpeakerIdleFormId;
    private int _cg01ProofCaptureWidth;
    private int _cg01ProofCaptureHeight;
    private int _cg01ProofCaptureRgbSpan;

    internal void Configure(
        Fo3OwnedProfile profile,
        string savePath,
        Node3D worldHost,
        RuntimeConfiguration runtimeConfiguration,
        Fo3Vault101BirthPresentationContract? birthPresentation,
        string? appearanceProofMode = null,
        string? appearanceProofReportPath = null,
        string? appearanceProofCaptureRoot = null,
        string? cg01ProofMode = null,
        string? cg01ProofReportPath = null,
        string? cg01ProofCapturePath = null,
        Fo3Cg00RetailStage10Contract? retailCg00Stage10Contract = null,
        Fo3TtwCg00Stage10PresentationContract? ttwCg00Stage10PresentationContract = null,
        Fo3TtwCg00Stage10SurfaceContract? ttwCg00Stage10SurfaceContract = null,
        OpeningManifest? characterReflectron = null,
        bool characterVideo = false)
    {
        _profile = profile;
        _savePath = System.IO.Path.GetFullPath(savePath);
        _worldHost = worldHost;
        _runtimeConfiguration = runtimeConfiguration;
        _birthPresentation = birthPresentation;
        _retailCg00Stage10Contract = retailCg00Stage10Contract;
        _ttwCg00Stage10PresentationContract = ttwCg00Stage10PresentationContract;
        _ttwCg00Stage10SurfaceContract = ttwCg00Stage10SurfaceContract;
        _characterReflectron = characterReflectron;
        if ((_ttwCg00Stage10PresentationContract is null) !=
            (_ttwCg00Stage10SurfaceContract is null))
            throw new InvalidOperationException(
                "Fallout 3 TTW stage-10 presentation and surface contracts must be paired.");
        if (_retailCg00Stage10Contract is not null &&
            _ttwCg00Stage10PresentationContract is not null)
            throw new InvalidOperationException(
                "Fallout 3 stage-10 proof cannot mix standalone and TTW observations.");
        if (_birthPresentation is not null &&
            (!_birthPresentation.EntryReferenceFormId.Equals(
                 _profile.Section4Transition.LocationReferenceFormId,
                 StringComparison.OrdinalIgnoreCase) ||
             !_birthPresentation.CellFormId.Equals(
                 _profile.BirthSlice.CellFormId,
                 StringComparison.OrdinalIgnoreCase) ||
             !_birthPresentation.DadActor.ReferenceFormId.Equals(
                 _profile.Stage100Transition.DisabledDad.FormId,
                 StringComparison.OrdinalIgnoreCase) ||
             _birthPresentation.Cg01DadActors.Count !=
                 _profile.Stage65Appearance.SelectionResults.Count ||
             _birthPresentation.Cg01DadActors.Values.Any(value =>
                 !value.Actor.ReferenceFormId.Equals(
                     _profile.Cg01Stage0Transition.Dad.FormId,
                     StringComparison.OrdinalIgnoreCase) ||
                 !value.Actor.BaseFormId.Equals(
                     _profile.Cg01Stage0Transition.Dad.BaseFormId,
                     StringComparison.OrdinalIgnoreCase) ||
                 !value.Actor.StartMarkerReferenceFormId.Equals(
                     _profile.Cg01Stage0Transition.DadStartMarker.FormId,
                     StringComparison.OrdinalIgnoreCase))))
            throw new InvalidOperationException(
                "Fallout 3 stage-62 package or stage-100 Dad does not join the owned Vault 101 scene.");
        _appearanceProofMode = appearanceProofMode;
        _appearanceProofReportPath = appearanceProofReportPath;
        _appearanceProofCaptureRoot = appearanceProofCaptureRoot;
        _characterVideo = characterVideo;
        _cg01ProofMode = cg01ProofMode;
        _cg01ProofReportPath = cg01ProofReportPath;
        _cg01ProofCapturePath = cg01ProofCapturePath;
        Name = "Fallout3FrontEnd";
        Layer = Fo3OpeningFlowNumericContracts.UiLayer;
    }

    public override void _Ready()
    {
        BuildShell();
        if (_characterVideo)
        {
            RunCharacterGenerationVideo();
            return;
        }
        if (_cg01ProofMode is not null)
        {
            RunCg01Proof();
            return;
        }
        if (_appearanceProofMode is not null)
        {
            if (_appearanceProofMode is "early-apply" or "early-restore" or
                "early-presentation" or "stage10-presentation")
            {
                try
                {
                    RunCg00EarlyProof();
                }
                catch (Exception exception)
                {
                    GD.PushError($"OPENNV_FO3_CG00_EARLY_PRESENTATION_FAIL {exception}");
                    GetTree().Quit(Fo3OpeningFlowNumericContracts.ProofFailureExitCode);
                }
                return;
            }
            RunAppearanceProof();
            return;
        }
        StartMenuMusic();
        ShowMainMenu();
        GD.Print(
            $"OPENNV_FO3_BIRTH_CONTRACT_READY profile={_profile.ProfileId} " +
            $"schema={Fo3BirthSliceContract.ExpectedSchema} cell={_profile.BirthSlice.CellFormId} " +
            $"playerSpawn={_profile.BirthSlice.PlayerSpawnReferenceFormId} " +
            $"doctor={_profile.BirthSlice.DoctorActorReferenceFormId} " +
            $"references={_profile.BirthSlice.ReferenceCount} " +
            $"models={_profile.BirthSlice.CellModelResourceCount} rendered=0 interactive=0");
        GD.Print(
            $"OPENNV_FO3_FRONTEND_READY profile={_profile.ProfileId} " +
            $"quest={_profile.QuestEditorId} form={_profile.QuestFormId} " +
            $"intro=owned-transcode escapeSkip=1 sexChoices={_profile.SexChoices.Count} " +
            $"nameStage={_profile.NameStage} appearanceStage={_profile.AppearanceStage}");
    }

    public override void _Process(double delta)
    {
        EnforceOwnedPresentationShell();
        UpdateOwnedVideoSurface();
        UpdateCg00EarlyBirth(delta);
        UpdateCg00EarlyProof();
        UpdateCg01DadLip();
        _cg01Stage50TimerTick?.Invoke(delta);
        _cg01DadPackageTravelTick?.Invoke(delta);
        _cg01Stage90TimerTick?.Invoke(delta);
        _cg02IntroTimerTick?.Invoke(delta);
        _cg02CakePackageTick?.Invoke(delta);
        _cg02ButchTimerTick?.Invoke(delta);
        _cg02ButchPackageTick?.Invoke(delta);
        foreach (var dialogue in _cg02IntroDialogue)
            dialogue.Update();
        foreach (var animation in _cg02IntroAnimations.Values)
            animation.Advance(delta);
        if (_vaultStage90Fade is not null && _activeStage90ImageSpaceModifier is not null)
        {
            _stage90ImageSpaceElapsedSeconds += delta;
            var modifier = _activeStage90ImageSpaceModifier;
            var normalizedTime = modifier.DurationSeconds <= 0.0f
                ? 1.0f
                : Mathf.Clamp(
                    (float)(_stage90ImageSpaceElapsedSeconds / modifier.DurationSeconds),
                    0.0f,
                    1.0f);
            _vaultStage90Fade.Color = EvaluateStage90Fade(modifier.Fade, normalizedTime);
            if (normalizedTime >= 1.0f)
            {
                _vaultStage90Fade.QueueFree();
                _vaultStage90Fade = null;
                _activeStage90ImageSpaceModifier = null;
                _stage90ImageSpaceElapsedSeconds = 0.0;
            }
        }

        if (_stage100Runtime is null)
            return;
        _stage100TimerRemainingSeconds = Math.Max(
            0.0,
            _stage100TimerRemainingSeconds - delta);
        if (_stage100TimerRemainingSeconds > 0.0)
            return;
        var context = _stage100Runtime;
        _stage100Runtime = null;
        CompleteStage90Timer(context);
    }

    public override void _Input(InputEvent @event)
    {
        if (!IsEscapePressed(@event))
            return;
        if (_video is not null)
        {
            GetViewport().SetInputAsHandled();
            CompleteOwnedVideo(true);
            return;
        }
        if (_vaultPreviewHost is not null)
        {
            GetViewport().SetInputAsHandled();
            ExitVault101Preview();
            return;
        }
    }

    private void ShowVault101BirthRoomBeforeStage65(
        string playerName,
        Fo3SexChoice sex,
        Fo3AppearanceSelection selection,
        bool persistPackage)
    {
        var contract = _birthPresentation ?? throw new InvalidOperationException(
            "Fallout 3 pre-stage-65 Vault room has no owned presentation contract.");
        if (_vaultPreviewHost is not null)
        {
            if (_vaultBirthCoverage is null)
                throw new InvalidOperationException(
                    "Fallout 3 creator Vault backdrop coverage is absent.");
            ClearContent();
            _background.Visible = false;
            _panel.Visible = false;
            var resumedPackage = _profile.Section4Transition.Activate();
            if (persistPackage)
                PersistSection4Package(playerName, sex, selection, resumedPackage);
            GD.Print(
                $"OPENNV_FO3_CG00_CREATOR_CONFIRMED_VAULT_READY profile={_profile.ProfileId} " +
                $"stage={_profile.Appearance.AcceptedStage} package={resumedPackage.FormId} " +
                $"location={resumedPackage.LocationReferenceFormId} playerGeometry=" +
                $"{selection.Sex.FaceGen.SymmetricGeometrySha256} " +
                $"doctorVisible={_vaultBirthCoverage.DoctorActor.Placement.Visible} " +
                $"dadVisible={_vaultBirthCoverage.DadActor.Placement.Visible} " +
                "stage65Triggered=0 sourceMarkerPending=1");
            return;
        }
        ClearContent();
        var baselineSelection = _profile.Appearance.ResolveSelection(
            sex.EngineSex,
            selection.Race.FormId,
            selection.Race.ChildRaceFormId,
            selection.Hair.FormId,
            selection.Eyes.FormId);
        var baselineStage65 = _profile.Stage65Appearance.Apply(
            sex.EngineSex,
            baselineSelection.Race.FormId,
            baselineSelection.Sex.FaceGen);
        var previewHost = new Node3D { Name = "FO3_VAULT101_BIRTH_ROOM_PRE_STAGE65" };
        _worldHost.AddChild(previewHost);
        Fo3Vault101BirthSceneCoverage coverage;
        try
        {
            var hiddenFutureDad = contract.Cg01DadActorFor(
                baselineSelection.Race.FormId,
                sex.EngineSex,
                baselineStage65);
            coverage = Fo3Vault101BirthScene.Build(previewHost, contract, hiddenFutureDad);
        }
        catch
        {
            previewHost.QueueFree();
            throw;
        }
        _vaultPreviewHost = previewHost;
        _vaultBirthCoverage = coverage;
        _background.Visible = false;
        _panel.Visible = false;
        var package = _profile.Section4Transition.Activate();
        if (persistPackage)
            PersistSection4Package(playerName, sex, selection, package);
        GD.Print(
            $"OPENNV_FO3_CG00_CREATOR_CONFIRMED_VAULT_READY profile={_profile.ProfileId} " +
            $"stage={_profile.Appearance.AcceptedStage} package={package.FormId} " +
            $"location={package.LocationReferenceFormId} playerGeometry=" +
            $"{selection.Sex.FaceGen.SymmetricGeometrySha256} " +
            $"doctorVisible={coverage.DoctorActor.Placement.Visible} " +
            $"dadVisible={coverage.DadActor.Placement.Visible} " +
            "stage65Triggered=0 sourceMarkerPending=1");
    }

    private void ShowAppearanceAccepted(
        string playerName,
        Fo3SexChoice sex,
        Fo3AppearanceSelection selection)
    {
        ClearContent();
        _content.AddChild(Label(
            $"{_profile.QuestEditorId}  •  STAGE {_profile.Appearance.AcceptedStage}",
            Fo3OpeningFlowNumericContracts.TitleFontPixels));
        _content.AddChild(Label(
            $"{playerName}  •  {sex.Label}  •  {selection.Race.Label}",
            Fo3OpeningFlowNumericContracts.BodyFontPixels));
        _content.AddChild(Label(
            $"HAIR: {selection.Hair.Label}  •  EYES: {selection.Eyes.Label}",
            Fo3OpeningFlowNumericContracts.BodyFontPixels));
        _content.AddChild(Label(
            $"NEXT OWNED COMMAND: {_profile.Appearance.AcceptedStageCommand}",
            Fo3OpeningFlowNumericContracts.BodyFontPixels));
        _content.AddChild(Label(
            _birthPresentation is null
                ? "The owned CG00 appearance choice is saved at stage 62. The Section 4 " +
                    "package and later stage contracts are compiled, but normal progression stops " +
                    "until the authored package/dialogue triggers execute in the Vault 101 world."
                : "The owned CG00 appearance choice is saved at stage 62. Its next authored " +
                    "package targets the exact Vault 101 player-start marker. The bounded preview " +
                    "shows that owned room only; it does not execute the package or dialogue.",
            Fo3OpeningFlowNumericContracts.BodyFontPixels));
        if (_birthPresentation is not null)
        {
            var enter = Button("ENTER OWNED VAULT 101 BIRTH ROOM");
            enter.Pressed += () => ShowVault101BirthRoom(playerName, sex, selection);
            _content.AddChild(enter);
        }
        var menu = Button("MAIN MENU");
        menu.Pressed += () =>
        {
            StartMenuMusicAfterStop();
            ShowMainMenu();
        };
        _content.AddChild(menu);
        GD.Print(
            $"OPENNV_FO3_CG00_APPEARANCE_ACCEPTED profile={_profile.ProfileId} " +
            $"stage={_profile.Appearance.AcceptedStage} race={selection.Race.FormId} " +
            $"hair={selection.Hair.FormId} eyes={selection.Eyes.FormId} " +
            $"next={_profile.Appearance.AcceptedStageCommand} packageRuntimeReady=0");
    }

    private void ShowVault101BirthRoom(
        string playerName,
        Fo3SexChoice sex,
        Fo3AppearanceSelection selection,
        Fo3Stage65AppearanceState? resumedStage65 = null,
        Fo3Stage80State? resumedStage80 = null,
        Fo3Stage85State? resumedStage85 = null,
        Fo3Stage90State? resumedStage90 = null,
        Fo3Stage100State? resumedStage100 = null,
        Fo3Cg01Stage0State? resumedCg01 = null,
        Fo3Cg01Stage10State? resumedCg01Stage10 = null,
        Fo3Cg01Stage12State? resumedCg01Stage12 = null,
        Fo3Cg01ToddlerWorldState? resumedCg01ToddlerWorld = null,
        Fo3Cg01Stage14State? resumedCg01Stage14 = null,
        Fo3Cg01Stage20State? resumedCg01Stage20 = null)
    {
        var contract = _birthPresentation ?? throw new InvalidOperationException(
            "Fallout 3 Vault 101 birth room has no owned presentation contract.");
        var transition = _profile.Section4Transition;
        if (transition.SourceStage != _profile.Appearance.AcceptedStage ||
            !transition.LocationReferenceFormId.Equals(
                contract.EntryReferenceFormId,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Fallout 3 Vault 101 birth room does not join the stage-62 package location.");
        var resumedCg00ActorPackages = CaptureCg00ActorPackageStates();
        if (_vaultPreviewHost is not null)
        {
            _vaultPreviewHost.QueueFree();
            _vaultPreviewHost = null;
            _vaultBirthCoverage = null;
        }
        ClearContent();

        Fo3PlayerPackageRuntimeActivation? activation = null;
        var stage65 = resumedStage65;
        if (stage65 is null)
        {
            activation = transition.ActivateAtOwnedMarker(
                contract.EntryReferenceFormId,
                _profile.Appearance.AcceptedStage,
                targetStageDone: false);
            stage65 = _profile.Stage65Appearance.Apply(
                sex.EngineSex,
                selection.Race.FormId,
                selection.Sex.FaceGen);
            if (stage65.Stage != activation.TriggeredStage)
                throw new InvalidOperationException(
                    "Fallout 3 player-package trigger differs from the stage-65 result.");
        }
        if ((resumedStage80 is not null &&
                resumedStage80.Stage != _profile.Stage80Transition.Stage) ||
            (resumedStage85 is not null &&
                (resumedStage80 is null ||
                 resumedStage85.Stage != _profile.Stage85Transition.Stage)) ||
            (resumedStage90 is not null &&
                (resumedStage85 is null ||
                 resumedStage90.Stage != _profile.Stage90Transition.Stage)) ||
            (resumedStage100 is not null &&
                (resumedStage90 is null ||
                 resumedStage100.Stage != _profile.Stage100Transition.Stage)) ||
            (resumedCg01 is not null && resumedStage100 is null) ||
            (resumedCg01Stage10 is not null && resumedCg01 is null) ||
            (resumedCg01Stage12 is not null && resumedCg01Stage10 is null) ||
            (resumedCg01ToddlerWorld is not null && resumedCg01Stage12 is null) ||
            (resumedCg01Stage14 is not null && resumedCg01ToddlerWorld is null) ||
            (resumedCg01Stage20 is not null && resumedCg01Stage14 is null))
            throw new InvalidOperationException(
                "Fallout 3 resumed birth-room stage chain is incomplete.");

        var previewHost = new Node3D { Name = "FO3_VAULT101_BIRTH_ROOM" };
        _worldHost.AddChild(previewHost);
        Fo3Vault101BirthSceneCoverage coverage;
        try
        {
            var cg01DadAppearance = contract.Cg01DadActorFor(
                selection.Race.FormId,
                sex.EngineSex,
                stage65 ?? throw new InvalidOperationException(
                    "Fallout 3 stage-65 appearance state is absent."));
            coverage = Fo3Vault101BirthScene.Build(
                previewHost,
                contract,
                cg01DadAppearance);
        }
        catch
        {
            previewHost.QueueFree();
            throw;
        }
        _vaultPreviewHost = previewHost;
        _vaultBirthCoverage = coverage;
        RestoreCg00ActorPackageStates(resumedCg00ActorPackages);
        _background.Visible = false;
        _panel.Visible = false;

        if (activation is not null)
            PersistStage65Appearance(
                playerName,
                sex,
                selection,
                activation.Package,
                stage65,
                activation);
        if (resumedStage80 is null)
        {
            if (_appearanceProofMode is not "early-apply" and not "early-restore")
            {
                var subtitle = AddVaultDialogueOverlay();
                var branch = _profile.Stage80Transition.DialogueFor(sex.EngineSex);
                Callable.From(() => PlayVaultDialogue(
                    branch,
                    subtitle,
                    playerName,
                    sex,
                    selection,
                    stage65)).CallDeferred();
            }
        }
        else if (resumedStage85 is null)
        {
            var stage85 = _profile.Stage85Transition.Apply(resumedStage80);
            PersistStage85Transition(
                playerName,
                sex,
                selection,
                transition.Activate(),
                stage65,
                resumedStage80,
                stage85);
            PrintStage85Applied(stage85, resumed: true);
            resumedStage85 = stage85;
        }
        if (resumedStage80 is not null && resumedStage90 is null)
            BeginStage85ProgressionDialogue(
                playerName,
                sex,
                selection,
                stage65,
                resumedStage80,
                resumedStage85!);
        if (resumedStage100 is not null)
        {
            ApplyStage100Presentation(resumedStage100);
            if (resumedCg01 is not null)
            {
                var cg01Context = new Fo3Cg01RuntimeContext(
                    playerName,
                    sex,
                    selection,
                    transition.Activate(),
                    stage65,
                    resumedStage80!,
                    resumedStage85!,
                    resumedStage90!,
                    resumedStage100);
                ApplyCg01Stage5Presentation(resumedCg01, stage65);
                if (resumedCg01Stage12 is not null)
                    BeginCg01ToddlerWorld(
                        resumedCg01,
                        cg01Context,
                        resumedCg01Stage10!,
                        resumedCg01ToddlerWorld,
                        acceptanceProof: false,
                        restoredStage14: resumedCg01Stage14,
                        restoredStage20: resumedCg01Stage20);
                else if (resumedCg01Stage10 is not null)
                    BeginCg01ToddlerWorld(
                        resumedCg01,
                        cg01Context,
                        resumedCg01Stage10,
                        restored: null,
                        acceptanceProof: false);
                else
                    BeginCg01DadDialogue(resumedCg01, cg01Context, resumed: true);
            }
            else
                ApplyCg01AfterStage100(new Fo3Cg01RuntimeContext(
                    playerName,
                    sex,
                    selection,
                    transition.Activate(),
                    stage65,
                    resumedStage80!,
                    resumedStage85!,
                    resumedStage90!,
                    resumedStage100));
        }
        else if (resumedStage90 is not null)
            StartStage100Timer(new Fo3Stage100RuntimeContext(
                playerName,
                sex,
                selection,
                transition.Activate(),
                stage65,
                resumedStage80!,
                resumedStage85!,
                resumedStage90));
        var activeStage = resumedCg01Stage12?.ActiveStage ??
            resumedCg01Stage10?.ActiveStage ?? resumedCg01?.ActiveStage ??
            resumedStage100?.Stage ??
            resumedStage90?.Stage ?? resumedStage85?.Stage ??
            resumedStage80?.Stage ?? stage65.Stage;
        GD.Print(
            $"OPENNV_FO3_CG00_VAULT101_BIRTH_ROOM_READY profile={_profile.ProfileId} " +
            $"stage={activeStage} package={transition.PackageFormId} " +
            $"entry={contract.EntryReferenceFormId} cell={contract.CellFormId} " +
            $"references={coverage.PlacedReferences} actors=3 " +
            $"doctor={coverage.DoctorActor.ReferenceFormId} " +
            $"dad={coverage.DadActor.ReferenceFormId} " +
            $"cg01Dad={coverage.Cg01DadActor.ReferenceFormId} " +
            $"resumed={(resumedStage65 is null ? 0 : 1)} " +
            $"packageActive={(resumedStage100 is null ? 1 : 0)} " +
            $"trigger={transition.NextCommand} playerIdleExecuted=0 " +
            $"dialoguePlaybackReady={(resumedStage80 is null ? 1 : 0)} retailTiming=0 " +
            $"stage80Applied={(resumedStage80 is null ? 0 : 1)} " +
            $"stage85Applied={(resumedStage85 is null ? 0 : 1)} " +
            $"stage90Applied={(resumedStage90 is null ? 0 : 1)} " +
            $"stage100Applied={(resumedStage100 is null ? 0 : 1)} " +
            $"cg01Stage0Applied={(resumedCg01 is null ? 0 : 1)} " +
            $"cg01Stage10Applied={(resumedCg01Stage10 is null ? 0 : 1)} " +
            $"cg01MovieReplayed={(resumedCg01 is null ? "n/a" : "0")} " +
            $"dadEnabled={(resumedStage100 is null ? 1 : 0)}");
    }

    private Button AddVaultDialogueOverlay(
        string nodeName = "FO3_STAGE65_VAULT101_DIALOGUE")
    {
        var fonts = OwnedGamebryoTileRuntime.RequireDialogueFonts(
            _profile.DialogueMenu,
            _profile.UiFonts);
        var overlay = new OwnedGamebryoDialogueMenuRuntime(
            _profile.DialogueMenu,
            _profile.InterfaceColor,
            _profile.MenuBackgroundAlpha,
            OwnedUiTheme.BuildFont(fonts.SpeakerName),
            OwnedUiTheme.BuildFont(fonts.Body))
        {
            Name = nodeName,
        };
        AddChild(overlay);
        overlay.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var subtitle = overlay.SpeakerTextControl;
        _vaultPreviewOverlay = overlay;
        return subtitle;
    }

    private static void ShowVaultDialogue(Button subtitle, string speaker, string text)
    {
        if (subtitle.GetParent() is not OwnedGamebryoDialogueMenuRuntime menu)
            throw new InvalidOperationException(
                "Fallout 3 DialogueMenu runtime owner is unavailable.");
        menu.ShowLine(speaker, text, () => { });
    }

    private static void HideVaultDialogue(Button subtitle)
    {
        if (subtitle.GetParent() is not OwnedGamebryoDialogueMenuRuntime menu)
            throw new InvalidOperationException(
                "Fallout 3 DialogueMenu runtime owner is unavailable.");
        menu.HideMenu();
    }

    private void PlayVaultDialogue(
        Fo3Stage80DialogueBranch branch,
        Button subtitle,
        string playerName,
        Fo3SexChoice sex,
        Fo3AppearanceSelection selection,
        Fo3Stage65AppearanceState stage65)
    {
        _vaultDialogueVoice?.Stop();
        _vaultDialogueVoice?.QueueFree();
        var stream = AudioStreamOggVorbis.LoadFromFile(branch.Response.Voice.SourcePath)
            ?? throw new InvalidOperationException(
                $"Fallout 3 owned Dad voice could not be decoded: " +
                branch.Response.Voice.LogicalPath);
        var durationSeconds = stream.GetLength();
        if (!double.IsFinite(durationSeconds) || durationSeconds <= 0.0)
            throw new InvalidOperationException("Fallout 3 owned Dad voice has no duration.");
        _music.Stop();
        _vaultDialogueVoice = new AudioStreamPlayer
        {
            Name = "FO3_CG00_OWNED_DAD_DIALOGUE",
            Stream = stream,
        };
        _vaultDialogueVoice.Finished += () => CompleteStage65Dialogue(
            subtitle,
            playerName,
            sex,
            selection,
            stage65);
        AddChild(_vaultDialogueVoice);
        ShowVaultDialogue(
            subtitle,
            _vaultBirthCoverage?.DadActor.Actor.Name ??
                throw new InvalidOperationException("Fallout 3 Dad actor is unavailable."),
            branch.Response.Text);
        _vaultDialogueVoice.Play();
        GD.Print(
            $"OPENNV_FO3_CG00_DAD_CUE_STARTED stage=65 info={branch.InfoFormId} " +
            $"response={branch.Response.Index} duration={durationSeconds:F3} " +
            $"voice={branch.Response.Voice.LogicalPath} " +
            $"lip={branch.Response.Lip.LogicalPath} sourceTriggerAdvance=1 explicitUiAdvance=0 " +
            "dadRendered=1 lipPlayback=0 retailTiming=0 stage80Applied=0");
    }

    private void CompleteStage65Dialogue(
        Button subtitle,
        string playerName,
        Fo3SexChoice sex,
        Fo3AppearanceSelection selection,
        Fo3Stage65AppearanceState stage65)
    {
        HideVaultDialogue(subtitle);
        _vaultPreviewOverlay?.QueueFree();
        _vaultPreviewOverlay = null;
        _vaultDialogueVoice?.QueueFree();
        _vaultDialogueVoice = null;
        var package = _profile.Section4Transition.Activate();
        var stage80 = _profile.Stage80Transition.Apply(sex.EngineSex, stage65);
        PersistStage80Transition(playerName, sex, selection, package, stage65, stage80);
        GD.Print(
            $"OPENNV_FO3_CG00_STAGE80_APPLIED_NORMAL stage={stage80.Stage} " +
            $"info={stage80.AppliedInfoFormId} commands={stage80.AppliedCommandCount} " +
            $"package={stage80.AddedPlayerPackage.FormId} " +
            $"variables={stage80.ScriptVariables.Count} " +
            $"evaluated={stage80.EvaluatedPackageReferences.Count} " +
            $"enabled={stage80.EnabledReferences.Count} cueFinished=1 playerIdleExecuted=0");
        var stage85 = _profile.Stage85Transition.Apply(stage80);
        PersistStage85Transition(
            playerName,
            sex,
            selection,
            package,
            stage65,
            stage80,
            stage85);
        PrintStage85Applied(stage85, resumed: false);
        BeginStage85ProgressionDialogue(
            playerName,
            sex,
            selection,
            stage65,
            stage80,
            stage85);
    }

    private static void PrintStage85Applied(Fo3Stage85State stage85, bool resumed) =>
        GD.Print(
            $"OPENNV_FO3_CG00_STAGE85_APPLIED_NORMAL stage={stage85.Stage} " +
            $"info={stage85.AppliedInfoFormId} commands={stage85.AppliedCommandCount} " +
            $"resumed={(resumed ? 1 : 0)} infoConditionsEvaluated=1 " +
            "dialoguePlayback=0 playerIdleExecuted=0 retailTiming=0");

    private void BeginStage85ProgressionDialogue(
        string playerName,
        Fo3SexChoice sex,
        Fo3AppearanceSelection selection,
        Fo3Stage65AppearanceState stage65,
        Fo3Stage80State stage80,
        Fo3Stage85State stage85)
    {
        var dialogue = _profile.Stage90Transition.Dialogue;
        var subtitle = AddVaultDialogueOverlay("FO3_STAGE85_VAULT101_DIALOGUE");
        var stream = AudioStreamOggVorbis.LoadFromFile(dialogue.Response.Voice.SourcePath)
            ?? throw new InvalidOperationException(
                "Fallout 3 owned post-stage-85 Dad voice could not be decoded: " +
                dialogue.Response.Voice.LogicalPath);
        var durationSeconds = stream.GetLength();
        if (!double.IsFinite(durationSeconds) || durationSeconds <= 0.0)
            throw new InvalidOperationException(
                "Fallout 3 owned post-stage-85 Dad voice has no duration.");
        _vaultDialogueVoice?.Stop();
        _vaultDialogueVoice?.QueueFree();
        _vaultDialogueVoice = new AudioStreamPlayer
        {
            Name = "FO3_CG00_OWNED_DAD_STAGE90_DIALOGUE",
            Stream = stream,
        };
        _vaultDialogueVoice.Finished += () => CompleteStage85ProgressionDialogue(
            subtitle,
            playerName,
            sex,
            selection,
            stage65,
            stage80,
            stage85);
        AddChild(_vaultDialogueVoice);
        ShowVaultDialogue(
            subtitle,
            _vaultBirthCoverage?.DadActor.Actor.Name ??
                throw new InvalidOperationException("Fallout 3 Dad actor is unavailable."),
            dialogue.Response.Text);
        _vaultDialogueVoice.Play();
        GD.Print(
            $"OPENNV_FO3_CG00_STAGE90_CUE_STARTED stage=85 info={dialogue.InfoFormId} " +
            $"response={dialogue.Response.Index} duration={durationSeconds:F3} " +
            $"voice={dialogue.Response.Voice.LogicalPath} " +
            $"lip={dialogue.Response.Lip.LogicalPath} continuationMarker=1 " +
            "sourceTriggerAdvance=1 explicitUiAdvance=0 packageAi=0 " +
            "lipPlayback=0 retailTiming=0 stage90Applied=0");
    }

    private void CompleteStage85ProgressionDialogue(
        Button subtitle,
        string playerName,
        Fo3SexChoice sex,
        Fo3AppearanceSelection selection,
        Fo3Stage65AppearanceState stage65,
        Fo3Stage80State stage80,
        Fo3Stage85State stage85)
    {
        HideVaultDialogue(subtitle);
        _vaultPreviewOverlay?.QueueFree();
        _vaultPreviewOverlay = null;
        _vaultDialogueVoice?.QueueFree();
        _vaultDialogueVoice = null;
        var stage90 = _profile.Stage90Transition.Apply(stage85);
        StartStage90ImageSpace(stage90.ImageSpaceModifier);
        StartStage90Sound(stage90.Sound);
        PersistStage90Transition(
            playerName,
            sex,
            selection,
            _profile.Section4Transition.Activate(),
            stage65,
            stage80,
            stage85,
            stage90);
        StartStage100Timer(new Fo3Stage100RuntimeContext(
            playerName,
            sex,
            selection,
            _profile.Section4Transition.Activate(),
            stage65,
            stage80,
            stage85,
            stage90));
        GD.Print(
            $"OPENNV_FO3_CG00_STAGE90_APPLIED_NORMAL stage={stage90.Stage} " +
            $"info={stage90.AppliedInfoFormId} commands={stage90.AppliedCommandCount} " +
            $"timer={stage90.QuestVariables.Single(value => value.Name == "timer").Value:F1} " +
            $"runTimer={stage90.QuestVariables.Single(value => value.Name == "runTimer").Value:F0} " +
            $"imad={stage90.ImageSpaceModifier.FormId} imadFade=1 imadOtherChannels=0 " +
            $"sound={stage90.Sound.FormId} soundStarted=1 timerAdvancing=1 " +
            "playerIdleExecuted=0 packageAi=0 retailTiming=0 stage100Applied=0");
    }

    private void StartStage100Timer(Fo3Stage100RuntimeContext context)
    {
        if (_stage100Runtime is not null || !context.Stage90.TimerAdvancing)
            throw new InvalidOperationException("Fallout 3 stage-100 timer is already active.");
        var timer = context.Stage90.QuestVariables.Single(value => value.Name == "timer");
        if (timer.Value != _profile.Stage100Transition.TimerInitialSeconds)
            throw new InvalidOperationException("Fallout 3 stage-100 timer start differs.");
        _stage100Runtime = context;
        _stage100TimerRemainingSeconds = timer.Value;
        GD.Print(
            $"OPENNV_FO3_CG00_STAGE100_TIMER_STARTED sourceStage={context.Stage90.Stage} " +
            $"seconds={_stage100TimerRemainingSeconds:F1} decrement=GetSecondsPassed " +
            "debugJump=0 retailTiming=0");
    }

    private void CompleteStage90Timer(Fo3Stage100RuntimeContext context)
    {
        var stage100 = _profile.Stage100Transition.Apply(
            context.Stage90,
            _stage100TimerRemainingSeconds);
        ApplyStage100Presentation(stage100);
        PersistStage100Transition(
            context.PlayerName,
            context.Sex,
            context.Selection,
            context.Section4Package,
            context.Stage65,
            context.Stage80,
            context.Stage85,
            context.Stage90,
            stage100);
        ApplyCg01AfterStage100(
            new Fo3Cg01RuntimeContext(
                context.PlayerName,
                context.Sex,
                context.Selection,
                context.Section4Package,
                context.Stage65,
                context.Stage80,
                context.Stage85,
                context.Stage90,
                stage100));
        GD.Print(
            $"OPENNV_FO3_CG00_STAGE100_APPLIED_NORMAL stage={stage100.Stage} " +
            $"commandsApplied={stage100.AppliedCommandCount} " +
            $"commandsAccounted={stage100.AccountedCommandCount} packageActive=0 " +
            $"dad={stage100.DisabledDad.FormId} dadEnabled=0 cg00Running=0 " +
            $"playerYoung=1 nextQuest={stage100.NextBoundary.QuestFormId} " +
            $"nextStage={stage100.NextBoundary.Stage} nextApplied=1 " +
            $"nextContract={stage100.NextBoundary.TransitionContract.Sha256}");
    }

    private void StartStage90ImageSpace(Fo3Stage90ImageSpaceModifier modifier)
    {
        _vaultStage90Fade?.QueueFree();
        _activeStage90ImageSpaceModifier = modifier;
        _stage90ImageSpaceElapsedSeconds = 0.0;
        _vaultStage90Fade = new ColorRect
        {
            Name = "FO3_CG00_STAGE90_OWNED_FADE",
            Color = EvaluateStage90Fade(modifier.Fade, 0.0f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _vaultStage90Fade.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(_vaultStage90Fade);
    }

    private void StartStage90Sound(Fo3Stage90Sound sound)
    {
        _vaultEffectSound?.Stop();
        _vaultEffectSound?.QueueFree();
        var stream = AudioStreamWav.LoadFromFile(sound.Asset.SourcePath)
            ?? throw new InvalidOperationException(
                $"Fallout 3 owned stage-90 sound could not be decoded: " +
                sound.Asset.LogicalPath);
        _vaultEffectSound = new AudioStreamPlayer
        {
            Name = "FO3_CG00_STAGE90_OWNED_SOUND",
            Stream = stream,
        };
        AddChild(_vaultEffectSound);
        _vaultEffectSound.Play();
    }

    private static Color EvaluateStage90Fade(
        IReadOnlyList<Fo3Stage90FadeKey> keys,
        float normalizedTime)
    {
        if (normalizedTime <= keys[0].Time)
            return keys[0].Color;
        if (normalizedTime >= keys[^1].Time)
            return keys[^1].Color;
        for (var index = 1; index < keys.Count; index++)
        {
            var right = keys[index];
            if (normalizedTime > right.Time)
                continue;
            var left = keys[index - 1];
            var width = right.Time - left.Time;
            var weight = width <= 0.0f
                ? 1.0f
                : (normalizedTime - left.Time) / width;
            return left.Color.Lerp(right.Color, weight);
        }
        throw new InvalidOperationException("Fallout 3 stage-90 fade curve is incomplete.");
    }

    private void ExitVault101Preview()
    {
        _vaultDialogueVoice?.Stop();
        _vaultDialogueVoice?.QueueFree();
        _vaultDialogueVoice = null;
        _vaultEffectSound?.Stop();
        _vaultEffectSound?.QueueFree();
        _vaultEffectSound = null;
        _vaultStage90Fade?.QueueFree();
        _vaultStage90Fade = null;
        _activeStage90ImageSpaceModifier = null;
        _stage90ImageSpaceElapsedSeconds = 0.0;
        _stage100Runtime = null;
        _stage100TimerRemainingSeconds = 0.0;
        _vaultBirthCoverage = null;
        _vaultPreviewOverlay?.QueueFree();
        _vaultPreviewOverlay = null;
        _vaultPreviewHost?.QueueFree();
        _vaultPreviewHost = null;
        _background.Visible = true;
        _panel.Visible = true;
        StartMenuMusicAfterStop();
        ShowMainMenu();
    }

    private void ShowSection4PackageActive(
        string playerName,
        Fo3SexChoice sex,
        Fo3AppearanceSelection selection)
    {
        var activation = _profile.Section4Transition.ActivateAtOwnedMarker(
            _profile.Section4Transition.LocationReferenceFormId,
            _profile.Appearance.AcceptedStage,
            targetStageDone: false);
        var package = activation.Package;
        ClearContent();
        _content.AddChild(Label(
            $"{_profile.QuestEditorId}  •  STAGE {_profile.Appearance.AcceptedStage}",
            Fo3OpeningFlowNumericContracts.TitleFontPixels));
        _content.AddChild(Label(
            $"{playerName}  •  {sex.Label}  •  {selection.Race.Label}",
            Fo3OpeningFlowNumericContracts.BodyFontPixels));
        _content.AddChild(Label(
            $"ACTIVE PLAYER PACKAGE: {package.EditorId} ({package.FormId})",
            Fo3OpeningFlowNumericContracts.BodyFontPixels));
        _content.AddChild(Label(
            $"OWNED LOCATION REFERENCE: {package.LocationReferenceFormId}",
            Fo3OpeningFlowNumericContracts.BodyFontPixels));
        _content.AddChild(Label(
            $"NEXT OWNED COMMAND: {package.NextCommand}",
            Fo3OpeningFlowNumericContracts.BodyFontPixels));
        _content.AddChild(Label(
            $"Stage {package.NextStage} applies every owned MatchRace and MatchFaceGeometry " +
            "command to the four source-resolved Dad references.",
            Fo3OpeningFlowNumericContracts.BodyFontPixels));
        var apply = Button($"APPLY STAGE {package.NextStage}");
        apply.Pressed += () =>
        {
            var state = _profile.Stage65Appearance.Apply(
                sex.EngineSex,
                selection.Race.FormId,
                selection.Sex.FaceGen);
            PersistStage65Appearance(
                playerName,
                sex,
                selection,
                package,
                state,
                activation);
            ShowStage65AppearanceApplied(playerName, sex, selection, state);
        };
        _content.AddChild(apply);
        var menu = Button("MAIN MENU");
        menu.Pressed += () =>
        {
            StartMenuMusicAfterStop();
            ShowMainMenu();
        };
        _content.AddChild(menu);
        GD.Print(
            $"OPENNV_FO3_CG00_SECTION4_ACTIVE profile={_profile.ProfileId} " +
            $"stage={_profile.Appearance.AcceptedStage} package={package.FormId} " +
            $"location={package.LocationReferenceFormId} nextStage={package.NextStage} " +
            "advanced=0 stage65ContractReady=1");
    }

    private void ShowStage65AppearanceApplied(
        string playerName,
        Fo3SexChoice sex,
        Fo3AppearanceSelection selection,
        Fo3Stage65AppearanceState state)
    {
        ClearContent();
        _content.AddChild(Label(
            $"{_profile.QuestEditorId}  •  STAGE {state.Stage}",
            Fo3OpeningFlowNumericContracts.TitleFontPixels));
        _content.AddChild(Label(
            $"{playerName}  •  {sex.Label}  •  {selection.Race.Label}",
            Fo3OpeningFlowNumericContracts.BodyFontPixels));
        _content.AddChild(Label(
            $"{state.AppliedCommandCount} OWNED COMMANDS APPLIED  •  " +
            $"{state.Parents.Count} PARENT APPEARANCES RESOLVED",
            Fo3OpeningFlowNumericContracts.BodyFontPixels));
        _content.AddChild(Label(
            "The selected player race and FaceGen remain authoritative. Each Dad now uses that " +
            "race, its default face texture, and the owned percentage geometry match.",
            Fo3OpeningFlowNumericContracts.BodyFontPixels));
        _content.AddChild(Label(
            $"NEXT BOUNDARY: {state.NextBoundary}",
            Fo3OpeningFlowNumericContracts.BodyFontPixels));
        _content.AddChild(Label(
            "The owned post-stage-65 INFO conditions select one sex-specific result. " +
            "Its source-bound cue plays in the bounded Vault preview; this state screen " +
            "applies only the exact stage result.",
            Fo3OpeningFlowNumericContracts.BodyFontPixels));
        var apply = Button($"APPLY OWNED INFO RESULT  •  STAGE {_profile.Stage80Transition.Stage}");
        apply.Pressed += () =>
        {
            var stage80 = _profile.Stage80Transition.Apply(sex.EngineSex, state);
            var package = _profile.Section4Transition.Activate();
            PersistStage80Transition(playerName, sex, selection, package, state, stage80);
            ShowStage80Applied(playerName, sex, selection, state, stage80);
        };
        _content.AddChild(apply);
        var menu = Button("MAIN MENU");
        menu.Pressed += () =>
        {
            StartMenuMusicAfterStop();
            ShowMainMenu();
        };
        _content.AddChild(menu);
        GD.Print(
            $"OPENNV_FO3_CG00_STAGE65_APPLIED profile={_profile.ProfileId} " +
            $"stage={state.Stage} commands={state.AppliedCommandCount} " +
            $"parents={state.Parents.Count} playerRace={selection.Race.FormId} " +
            $"nextBoundary={state.NextBoundary}");
    }

    private void ShowStage80Applied(
        string playerName,
        Fo3SexChoice sex,
        Fo3AppearanceSelection selection,
        Fo3Stage65AppearanceState stage65,
        Fo3Stage80State state)
    {
        ClearContent();
        _content.AddChild(Label(
            $"{_profile.QuestEditorId}  •  STAGE {state.Stage}",
            Fo3OpeningFlowNumericContracts.TitleFontPixels));
        _content.AddChild(Label(
            $"{playerName}  •  {sex.Label}  •  {selection.Race.Label}",
            Fo3OpeningFlowNumericContracts.BodyFontPixels));
        _content.AddChild(Label(
            $"OWNED INFO RESULT: {state.AppliedInfoFormId}",
            Fo3OpeningFlowNumericContracts.BodyFontPixels));
        _content.AddChild(Label(
            $"{state.AppliedCommandCount} OWNED COMMANDS APPLIED  •  " +
            $"ADDED PACKAGE {state.AddedPlayerPackage.EditorId} " +
            $"({state.AddedPlayerPackage.FormId})",
            Fo3OpeningFlowNumericContracts.BodyFontPixels));
        _content.AddChild(Label(
            $"SCRIPT VARIABLES: {state.ScriptVariables.Count}  •  " +
            $"PACKAGE REEVALUATIONS: {state.EvaluatedPackageReferences.Count}  •  " +
            $"ENABLED REFERENCES: {state.EnabledReferences.Count}",
            Fo3OpeningFlowNumericContracts.BodyFontPixels));
        _content.AddChild(Label(
            "This is authoritative CG00 state only. Vault 101 world placement, actors, " +
            "animation, and dialogue are not rendered by this transition.",
            Fo3OpeningFlowNumericContracts.BodyFontPixels));
        _content.AddChild(Label(
            $"NEXT BOUNDARY: {state.NextBoundary}",
            Fo3OpeningFlowNumericContracts.BodyFontPixels));
        _content.AddChild(Label(
            "The next owned INFO result advances CG00 to an authored stage with no executable " +
            "stage commands. Dialogue playback remains outside this slice.",
            Fo3OpeningFlowNumericContracts.BodyFontPixels));
        var apply = Button($"APPLY OWNED INFO RESULT  •  STAGE {_profile.Stage85Transition.Stage}");
        apply.Pressed += () =>
        {
            var stage85 = _profile.Stage85Transition.Apply(state);
            var package = _profile.Section4Transition.Activate();
            PersistStage85Transition(
                playerName,
                sex,
                selection,
                package,
                stage65,
                state,
                stage85);
            ShowStage85Applied(playerName, sex, selection, stage85);
        };
        _content.AddChild(apply);
        var menu = Button("MAIN MENU");
        menu.Pressed += () =>
        {
            StartMenuMusicAfterStop();
            ShowMainMenu();
        };
        _content.AddChild(menu);
        GD.Print(
            $"OPENNV_FO3_CG00_STAGE80_APPLIED profile={_profile.ProfileId} " +
            $"sourceStage={stage65.Stage} stage={state.Stage} info={state.AppliedInfoFormId} " +
            $"commands={state.AppliedCommandCount} package={state.AddedPlayerPackage.FormId} " +
            $"variables={state.ScriptVariables.Count} evp={state.EvaluatedPackageReferences.Count} " +
            $"enabled={state.EnabledReferences.Count} dialoguePlayback=0 worldRendered=0 " +
            $"nextBoundary={state.NextBoundary}");
    }

    private void ShowStage85Applied(
        string playerName,
        Fo3SexChoice sex,
        Fo3AppearanceSelection selection,
        Fo3Stage85State state)
    {
        ClearContent();
        _content.AddChild(Label(
            $"{_profile.QuestEditorId}  •  STAGE {state.Stage}",
            Fo3OpeningFlowNumericContracts.TitleFontPixels));
        _content.AddChild(Label(
            $"{playerName}  •  {sex.Label}  •  {selection.Race.Label}",
            Fo3OpeningFlowNumericContracts.BodyFontPixels));
        _content.AddChild(Label(
            $"OWNED INFO RESULT: {state.AppliedInfoFormId}",
            Fo3OpeningFlowNumericContracts.BodyFontPixels));
        _content.AddChild(Label(
            $"{state.AppliedCommandCount} OWNED STAGE COMMANDS  •  AUTHORITATIVE STATE SAVED",
            Fo3OpeningFlowNumericContracts.BodyFontPixels));
        _content.AddChild(Label(
            "The owned stage result contains comments only. No dialogue, animation, actors, " +
            "or Vault 101 world scene are rendered here.",
            Fo3OpeningFlowNumericContracts.BodyFontPixels));
        _content.AddChild(Label(
            $"NEXT BOUNDARY: {state.NextBoundary}",
            Fo3OpeningFlowNumericContracts.BodyFontPixels));
        var menu = Button("MAIN MENU");
        menu.Pressed += () =>
        {
            StartMenuMusicAfterStop();
            ShowMainMenu();
        };
        _content.AddChild(menu);
        GD.Print(
            $"OPENNV_FO3_CG00_STAGE85_APPLIED profile={_profile.ProfileId} " +
            $"stage={state.Stage} info={state.AppliedInfoFormId} " +
            $"commands={state.AppliedCommandCount} dialoguePlayback=0 worldRendered=0 " +
            $"nextBoundary={state.NextBoundary}");
    }

    private void StartMenuMusicAfterStop()
    {
        if (!_music.Playing)
            _music.Play();
    }

    private static bool IsEscapePressed(InputEvent @event) =>
        @event is InputEventKey key &&
        key.Pressed &&
        !key.Echo &&
        (key.PhysicalKeycode == Key.Escape || key.Keycode == Key.Escape);


}
