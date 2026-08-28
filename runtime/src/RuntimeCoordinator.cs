using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using OpenNV.Runtime.Diagnostics.Performance;

namespace OpenNV.Runtime;

internal static class RuntimeCoordinatorNumericContracts
{
    // Immutable format, source-art, geometry, and acceptance contracts.
    // Runtime-tunable Fallout 1 behavior remains in the versioned runtime recipe.
    internal const double PresentationDouble1000Point0 = 1000.0;
}

public partial class RuntimeCoordinator : Node3D
{
    private static readonly HashSet<string> DirectPreparedContentOptions = new(
        new[]
        {
            "capture-root",
            "cell-recipe",
            "flat-controls-proof",
            "gameplay-proof",
            "gameplay-reload-proof",
            "new-game",
            "opening-proof",
            "open-proof-door",
            "pool-proof",
            "portal-proof",
            "quit-after-load",
            "report",
            "xr-simulator-proof",
        },
        StringComparer.OrdinalIgnoreCase);

    private Dictionary<string, string> _options = new(StringComparer.OrdinalIgnoreCase);
    private RuntimeConfiguration _configuration = null!;
    private LegalAssetSetupView? _setupView;
    private XRInterface? _openXr;
    private LoadingScreen? _loadingScreen;
    private ulong _loadingStartedMilliseconds;
    private const double MinimumLoadingScreenSeconds = 0.85;

    public override void _Ready()
    {
        if (DisplayServer.GetName() != "headless")
        {
            _loadingScreen = new LoadingScreen();
            _loadingScreen.Configure("STARTING VERIFIED RUNTIME");
            AddChild(_loadingScreen);
            _loadingStartedMilliseconds = Time.GetTicksMsec();
        }
        Callable.From(StartRuntimeAfterLoadingFrame).CallDeferred();
    }

    private async void StartRuntimeAfterLoadingFrame()
    {
        if (_loadingScreen is not null)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        StartRuntime();
    }

    private void StartRuntime()
    {
        try
        {
            _configuration = RuntimeConfiguration.Load();
            DesktopInputMap.Configure(_configuration.Player.DesktopInput);
            GetWindow().Size = new Vector2I(
                _configuration.Capture.ExpectedWidthPixels,
                _configuration.Capture.ExpectedHeightPixels);
            RenderingServer.SetDefaultClearColor(_configuration.Renderer.BackgroundColorRgba.Color());
            Engine.PhysicsTicksPerSecond = _configuration.Simulation.PhysicsTicksPerSecond;
            _options = ParseOptions(OS.GetCmdlineUserArgs());
            var performanceReportPath = _options.TryGetValue(
                "perf-report",
                out var configuredPerformanceReportPath)
                ? ValidatePerformanceReportPath(configuredPerformanceReportPath)
                : null;
            var performanceObserver = new RuntimePerformanceObserver();
            performanceObserver.Configure(
                _configuration.Performance,
                RuntimeConfiguration.ExpectedSchema,
                _configuration.Sha256,
                performanceReportPath);
            AddChild(performanceObserver);
            if (_options.ContainsKey("fo1-hex-scene"))
                _loadingScreen?.SetTitle("FALLOUT 1  //  V13ENT HEX TACTICAL");
            if (_options.ContainsKey("fo1-campaign-transport"))
                _loadingScreen?.SetTitle("FALLOUT 1  //  VERIFYING ALL MAPS");
            if (_options.ContainsKey("fo1-campaign-presentation"))
                _loadingScreen?.SetTitle("FALLOUT 1  //  VERIFYING CAMPAIGN ART");
            if (_options.ContainsKey("fo3-profile"))
                _loadingScreen?.SetTitle("FALLOUT 3  //  CG00 CHARACTER SELECTION");
            if (_options.ContainsKey("xr-simulator-proof") &&
                (!_options.ContainsKey("vr") || !_options.ContainsKey("report")))
                throw new ArgumentException("--xr-simulator-proof requires --vr and --report.");
            if (_options.ContainsKey("flat-controls-proof") &&
                (_options.ContainsKey("vr") || !_options.ContainsKey("report") ||
                    !_options.ContainsKey("save-path")))
                throw new ArgumentException(
                    "--flat-controls-proof requires --report and --save-path and cannot use --vr.");
            if (_options.TryGetValue("opening-proof", out var openingProofMode))
            {
                if (!_options.ContainsKey("report") || !_options.ContainsKey("save-path") ||
                    !_options.ContainsKey("opening-proof-name") ||
                    !_options.ContainsKey("opening-proof-timeout-seconds") ||
                    openingProofMode is not "checkpoint" and not "resume" ||
                    openingProofMode == "checkpoint" != _options.ContainsKey("new-game"))
                    throw new ArgumentException(
                        "--opening-proof requires mode checkpoint with --new-game or mode resume " +
                        "without it, plus --report, --save-path, --opening-proof-name, and " +
                        "--opening-proof-timeout-seconds.");
            }
            if (_options.ContainsKey("vr") && _options.ContainsKey("xr-rig-proof"))
                throw new ArgumentException("Use --vr for a live OpenXR session or --xr-rig-proof for the headless layout gate, not both.");
            if ((_options.ContainsKey("classic-diorama") || _options.ContainsKey("classic-diorama-rig-proof")) &&
                (_options.ContainsKey("vr") || _options.ContainsKey("vr-layout-proof") ||
                    _options.ContainsKey("xr-rig-proof")))
                throw new ArgumentException("Classic Diorama and OpenXR require separate presentation adapters.");
            if (_options.ContainsKey("fo1-hex-scene") && _options.ContainsKey("vr") &&
                !_options.ContainsKey("fo1-xr-simulator-preview"))
                throw new ArgumentException("The Fallout 1 tactical hex slice has not passed its OpenXR gate.");
            if (_options.ContainsKey("fo1-xr-simulator-preview") &&
                (!_options.ContainsKey("fo1-hex-scene") || !_options.ContainsKey("vr")))
                throw new ArgumentException(
                    "The Fallout 1 OpenXR simulator preview requires --fo1-hex-scene and --vr.");
            if (_options.ContainsKey("fo1-xr-controls-proof") &&
                (!_options.ContainsKey("fo1-xr-simulator-preview") ||
                    !_options.ContainsKey("report") || !_options.ContainsKey("save-path")))
                throw new ArgumentException(
                    "The Fallout 1 OpenXR controls proof requires the simulator preview, report, and isolated save path.");
            if (_options.ContainsKey("vr"))
                EnableOpenXr();
            if (_options.ContainsKey("xr-rig-proof"))
            {
                CompleteXrRigProof(_options);
                return;
            }
            if (_options.ContainsKey("classic-diorama-rig-proof"))
            {
                CompleteClassicDioramaRigProof(_options);
                return;
            }
            var hasDataRoot = _options.TryGetValue("data-root", out var dataRoot);
            var hasModel = _options.ContainsKey("model");
            var hasCellScene = _options.ContainsKey("cell-scene");
            var hasStaticCellCompile = _options.ContainsKey("static-cell-compile");
            var hasActorModel = _options.ContainsKey("actor-model");
            var hasActorReviewScene = _options.ContainsKey("actor-review-scene");
            var hasFo1HexScene = _options.ContainsKey("fo1-hex-scene");
            var hasFo1Campaign = _options.ContainsKey("fo1-campaign-transport");
            var hasFo1CampaignPresentation = _options.ContainsKey("fo1-campaign-presentation");
            var hasFo3Profile = _options.ContainsKey("fo3-profile");
            var hasJamProfile = _options.ContainsKey("jam-profile");
            if (hasJamProfile && (!hasDataRoot && !hasCellScene))
                throw new ArgumentException(
                    "--jam-profile requires --data-root or --cell-scene.");
            if (hasJamProfile &&
                (_options.ContainsKey("vr") || _options.ContainsKey("vr-layout-proof") ||
                    _options.ContainsKey("classic-diorama")))
                throw new ArgumentException(
                    "The bounded JVS sprint transport currently supports desktop first-person movement only.");
            if ((_options.ContainsKey("fo1-map") || _options.ContainsKey("fo1-elevation")) &&
                !hasFo1CampaignPresentation)
                throw new ArgumentException(
                    "--fo1-map and --fo1-elevation require --fo1-campaign-presentation.");
            if (_options.ContainsKey("fo1-campaign-build-proof") &&
                (!hasFo1CampaignPresentation || !_options.ContainsKey("report")))
                throw new ArgumentException(
                    "--fo1-campaign-build-proof requires --fo1-campaign-presentation and --report.");
            if (_options.ContainsKey("fo1-campaign-build-proof") &&
                _options.ContainsKey("capture-root"))
                throw new ArgumentException(
                    "Fallout campaign headless build proof and visual capture are separate gates.");
            if ((hasDataRoot ? 1 : 0) + (hasModel ? 1 : 0) + (hasCellScene ? 1 : 0) +
                    (hasStaticCellCompile ? 1 : 0) + (hasActorModel ? 1 : 0) +
                    (hasActorReviewScene ? 1 : 0) + (hasFo1HexScene ? 1 : 0) +
                    (hasFo1Campaign ? 1 : 0) + (hasFo1CampaignPresentation ? 1 : 0) +
                    (hasFo3Profile ? 1 : 0) > 1)
                throw new ArgumentException(
                    "Use only one of --data-root, --model/--sidecar, --cell-scene, " +
                    "--static-cell-compile, --actor-model/--actor-sidecar, " +
                    "--actor-review-scene, --fo1-hex-scene, --fo1-campaign-transport, or " +
                    "--fo1-campaign-presentation, or --fo3-profile.");
            var startsFo1NewGame = _options.ContainsKey("fo1-new-game") ||
                _options.ContainsKey("fo1-new-game-demo");
            if (startsFo1NewGame && !hasFo1HexScene)
                throw new ArgumentException("Fallout new game requires --fo1-hex-scene.");
            if (startsFo1NewGame &&
                (!_options.ContainsKey("fo1-character-start") ||
                    !_options.ContainsKey("fo1-character-start-sha256")))
                throw new ArgumentException(
                    "Fallout new game requires --fo1-character-start and --fo1-character-start-sha256.");
            if (_options.TryGetValue("fo1-start-presentation", out var fo1StartPresentation) &&
                (!startsFo1NewGame ||
                    fo1StartPresentation is not "hex-tactical" and not "first-person"))
                throw new ArgumentException(
                    "--fo1-start-presentation requires Fallout new game and must be hex-tactical or first-person.");
            if (_options.ContainsKey("fo1-new-game-demo") && !_options.ContainsKey("demo-report"))
                throw new ArgumentException("Fallout new-game demo requires --demo-report.");
            if (_options.ContainsKey("fo1-new-game-demo") && _options.ContainsKey("fo1-gameplay-demo"))
                throw new ArgumentException("Use only one Fallout gameplay demo mode.");
            if (!hasModel && _options.ContainsKey("sidecar"))
                throw new ArgumentException("--sidecar requires --model.");
            if (_options.ContainsKey("material-manifest") != _options.ContainsKey("material-manifest-sha256"))
                throw new ArgumentException("Use --material-manifest together with --material-manifest-sha256.");
            if (!hasModel && _options.ContainsKey("material-manifest"))
                throw new ArgumentException("--material-manifest requires --model.");
            if (!hasActorModel && _options.ContainsKey("actor-sidecar"))
                throw new ArgumentException("--actor-sidecar requires --actor-model.");
            if (hasActorReviewScene && !_options.ContainsKey("capture-root"))
                throw new ArgumentException("--actor-review-scene requires --capture-root.");
            if (_options.ContainsKey("actor-review-background-cell") && !hasActorReviewScene)
                throw new ArgumentException(
                    "--actor-review-background-cell requires --actor-review-scene.");
            if (_options.ContainsKey("actor-scene") && _options.ContainsKey("actor-scenes"))
                throw new ArgumentException("Use --actor-scene or --actor-scenes, not both.");
            if (_options.ContainsKey("retail-state-contract") &&
                (!hasCellScene || !_options.ContainsKey("capture-root") ||
                    (!_options.ContainsKey("actor-scene") && !_options.ContainsKey("actor-scenes"))))
                throw new ArgumentException(
                    "--retail-state-contract requires --cell-scene, actor scenes, and --capture-root.");
            if (_options.ContainsKey("gallery-shot") &&
                (!hasCellScene || !_options.ContainsKey("capture-root") ||
                    !_options.ContainsKey("actor-scene") ||
                    _options.ContainsKey("actor-scenes") ||
                    _options.ContainsKey("retail-state-contract")))
                throw new ArgumentException(
                    "--gallery-shot requires --cell-scene, one --actor-scene, and " +
                    "--capture-root, and cannot use retail-state-contract.");

            if (hasDataRoot)
            {
                var prepared = LegalAssetPreparer.Prepare(dataRoot!, _options, _configuration);
                LoadPrepared(prepared, _options);
                DismissLoadingScreen();
                return;
            }

            if (hasModel)
            {
                SetLoadingStatus(
                    _options.ContainsKey("classic-diorama")
                        ? "LOADING CLASSIC DIORAMA MODEL"
                        : "VERIFYING HASHED 3D MODEL");
                LoadModel(RequireOption(_options, "model"), RequireOption(_options, "sidecar"), _options);
                DismissLoadingScreen();
                return;
            }

            if (hasCellScene)
            {
                SetLoadingStatus(
                    _options.ContainsKey("classic-diorama")
                        ? "LOADING CLASSIC DIORAMA CELL"
                        : "LOADING VERIFIED 3D CELL");
                LoadCellScene(RequireOption(_options, "cell-scene"), _options);
                DismissLoadingScreen();
                return;
            }

            if (hasStaticCellCompile)
            {
                LoadStaticCellCompile(
                    RequireOption(_options, "static-cell-compile"),
                    _options);
                return;
            }

            if (hasActorModel)
            {
                SetLoadingStatus("VERIFYING HASHED ACTOR MODEL");
                LoadActorModel(
                    RequireOption(_options, "actor-model"),
                    RequireOption(_options, "actor-sidecar"),
                    _options);
                DismissLoadingScreen();
                return;
            }

            if (hasFo1HexScene)
            {
                SetLoadingStatus("LOADING V13ENT 200×200 HEX MAP");
                LoadFo1HexScene(RequireOption(_options, "fo1-hex-scene"), _options);
                DismissLoadingScreen();
                return;
            }

            if (hasFo1Campaign)
            {
                SetLoadingStatus("HASHING AND VALIDATING 96 MAP CONTRACTS");
                LoadFo1CampaignTransport(
                    RequireOption(_options, "fo1-campaign-transport"),
                    _options);
                DismissLoadingScreen();
                return;
            }

            if (hasFo1CampaignPresentation)
            {
                SetLoadingStatus("VERIFYING ALL MAPS AND SOURCE ARTIFACTS");
                LoadFo1CampaignPresentation(
                    RequireOption(_options, "fo1-campaign-presentation"),
                    _options);
                DismissLoadingScreen();
                return;
            }

            if (hasActorReviewScene)
            {
                LoadActorReviewScene(
                    RequireOption(_options, "actor-review-scene"),
                    _options);
                return;
            }

            if (hasFo3Profile)
            {
                SetLoadingStatus("VERIFYING OWNED FALLOUT 3 CG00 CONTRACT");
                LoadFo3Opening(RequireOption(_options, "fo3-profile"), _options);
                DismissLoadingScreen();
                return;
            }

            if (_options.ContainsKey("reuse-cache"))
            {
                if (!LegalAssetPreparer.TryRestore(
                        _options,
                        _configuration,
                        out var restored,
                        out var restoreError))
                    throw new InvalidOperationException(restoreError ?? "No prepared legal-asset cache exists.");
                LoadPrepared(restored, _options);
                DismissLoadingScreen();
                return;
            }

            if (_options.TryGetValue("report", out var startupReportPath))
                WriteStartupReport(startupReportPath);
            GD.Print("OPENNV_GODOT_EXPERIMENTAL_READY playable=0 playableSandbox=1 openxr=experimental");
            if (DisplayServer.GetName() == "headless")
                GetTree().Quit(0);
            else if (LegalAssetPreparer.TryRestore(
                         _options,
                         _configuration,
                         out var restored,
                         out var restoreError))
            {
                LoadPrepared(restored, _options);
                DismissLoadingScreen();
            }
            else
            {
                DismissLoadingScreen();
                ShowExperimentalStatus(restoreError);
            }
        }
        catch (Exception exception)
        {
            GD.PushError($"OPENNV_GODOT_RUNTIME_FAIL {exception}");
            GetTree().Quit(1);
        }
    }

    private void SetLoadingStatus(string status)
    {
        _loadingScreen?.SetStatus(status);
    }

    private void DismissLoadingScreen()
    {
        var loading = _loadingScreen;
        _loadingScreen = null;
        if (loading is null)
            return;
        var elapsedSeconds = (Time.GetTicksMsec() - _loadingStartedMilliseconds) / RuntimeCoordinatorNumericContracts.PresentationDouble1000Point0;
        var remainingSeconds = _options.ContainsKey("fo1-gameplay-demo") ||
            _options.ContainsKey("fo1-new-game-demo")
            ? 1.0
            : MinimumLoadingScreenSeconds - elapsedSeconds;
        if (remainingSeconds <= 0.0 || _options.ContainsKey("capture-root"))
        {
            loading.QueueFree();
            return;
        }
        var timer = GetTree().CreateTimer(remainingSeconds);
        timer.Timeout += loading.QueueFree;
    }

    private void LoadPrepared(
        LegalAssetPreparer.PreparedContent prepared,
        IReadOnlyDictionary<string, string> options)
    {
        if (ShouldShowOpening(options))
        {
            ShowOpening(prepared, options);
            return;
        }
        LoadPreparedGameplay(prepared, options);
    }

    private void ShowOpening(
        LegalAssetPreparer.PreparedContent prepared,
        IReadOnlyDictionary<string, string> options)
    {
        var manifest = OpeningManifest.Load(prepared.OpeningManifestPath, _configuration);
        var savePath = options.TryGetValue("save-path", out var configuredSavePath)
            ? ResolveRuntimePath(configuredSavePath)
            : ResolveRuntimePath(_configuration.Hud.DefaultSavePath);
        var opening = new RetailOpening();
        AddChild(opening);
        opening.Configure(
            manifest,
            File.Exists(savePath),
            _configuration.Player.DesktopInput.Cancel.Action,
            () =>
            {
                var newGameOptions = options.ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value,
                    StringComparer.OrdinalIgnoreCase);
                newGameOptions["new-game"] = "";
                LoadPreparedGameplay(prepared, newGameOptions);
            },
            action =>
            {
                if (action is "continue" or "load")
                {
                    opening.QueueFree();
                    LoadPreparedGameplay(prepared, options);
                    return;
                }
                GD.Print($"OPENNV_OWNED_MENU_ACTION action={action} status=ui-route-pending");
            });
        GD.Print(
            $"OPENNV_OWNED_OPENING_READY campaign={manifest.Campaign} " +
            $"quest={manifest.EntryQuestEditorId} stage={manifest.EntryStage} " +
            $"buttons={manifest.Buttons.Count}");
        if (options.ContainsKey("quit-after-load"))
            GetTree().Quit(0);
    }

    private void LoadPreparedGameplay(
        LegalAssetPreparer.PreparedContent prepared,
        IReadOnlyDictionary<string, string> options)
    {
        if (prepared.CellScenePath is not null)
        {
            var preparedOptions = options.ToDictionary(
                pair => pair.Key,
                pair => pair.Value,
                StringComparer.OrdinalIgnoreCase);
            if (prepared.ActorScenesPath is not null &&
                !preparedOptions.ContainsKey("actor-scene") &&
                !preparedOptions.ContainsKey("actor-scenes"))
                preparedOptions["actor-scenes"] = prepared.ActorScenesPath;
            if (!preparedOptions.ContainsKey("opening-manifest"))
                preparedOptions["opening-manifest"] = prepared.OpeningManifestPath;
            LoadCellScene(prepared.CellScenePath, preparedOptions);
        }
        else
            LoadModel(prepared.ModelPath, prepared.SidecarPath, options);
    }

    private static bool ShouldShowOpening(IReadOnlyDictionary<string, string> options) =>
        options.ContainsKey("opening-menu") ||
        !options.Keys.Any(DirectPreparedContentOptions.Contains);

    private static string ResolveRuntimePath(string path) =>
        path.StartsWith("res://", StringComparison.Ordinal) ||
        path.StartsWith("user://", StringComparison.Ordinal)
            ? ProjectSettings.GlobalizePath(path)
            : Path.GetFullPath(path);

    private static string ValidatePerformanceReportPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path == "true")
            throw new ArgumentException("--perf-report requires an explicit JSON output path.");
        string resolved;
        try
        {
            resolved = ResolveRuntimePath(path);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new ArgumentException("--perf-report path syntax is invalid.", exception);
        }
        if (!string.Equals(Path.GetExtension(resolved), ".json", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(Path.GetFileNameWithoutExtension(resolved)))
            throw new ArgumentException("--perf-report requires a .json output file.");
        return resolved;
    }

    private void LoadFo3Opening(
        string profilePath,
        IReadOnlyDictionary<string, string> options)
    {
        var profile = Fo3OwnedProfile.Load(profilePath);
        var savePath = options.TryGetValue("save-path", out var configuredSavePath)
            ? ResolveRuntimePath(configuredSavePath)
            : ResolveRuntimePath("user://profiles/fallout3/cg00-character-v2.json");
        var opening = new Fo3OpeningFlow();
        opening.Configure(profile, savePath, options.ContainsKey("fo3-appearance-proof"));
        AddChild(opening);
        if (options.ContainsKey("quit-after-load") && !options.ContainsKey("fo3-appearance-proof"))
            GetTree().Quit(0);
    }

    private void LoadCellScene(string scenePath, IReadOnlyDictionary<string, string> options)
    {
        var runTraversalProof = options.ContainsKey("portal-proof");
        var useXrLayout = options.ContainsKey("vr") || options.ContainsKey("vr-layout-proof");
        var galleryContract = options.TryGetValue("gallery-shot", out var galleryShotPath)
            ? GalleryShotContract.Load(galleryShotPath, _configuration)
            : null;
        var usesCampaignState = options.ContainsKey("opening-manifest");
        var applyCellEnvironment = galleryContract?.LocationClass != "exterior";
        if (galleryContract is not null)
            GD.Print($"OPENNV_GALLERY_STAGE id={galleryContract.Id} stage=cell-load-start");
        var loaded = CellSceneLoader.Load(
            scenePath,
            this,
            _configuration,
            !runTraversalProof && options.ContainsKey("open-proof-door"),
            options.TryGetValue("proof-door", out var proofDoor) ? proofDoor : null,
            options.TryGetValue("save-path", out var savePath) ? savePath : null,
            useXrLayout,
            !options.ContainsKey("capture-root") && !usesCampaignState,
            options.TryGetValue("actor-scene", out var actorScene) ? actorScene : null,
            options.TryGetValue("actor-scenes", out var actorScenes) ? actorScenes : null,
            options.ContainsKey("proof-enable-actor"),
            !options.ContainsKey("capture-root") || options.ContainsKey("gallery-shot"),
            applyCellEnvironment,
            !options.ContainsKey("new-game"),
            !usesCampaignState,
            options.ContainsKey("classic-diorama"));
        if (options.TryGetValue("jam-profile", out var jamProfilePath))
        {
            var sprint = JamJvsSprintContract.Load(jamProfilePath);
            DesktopInputMap.ConfigureJamSprint(sprint);
            loaded.Player.ConfigureJamJvsSprint(sprint);
            GD.Print(
                $"OPENNV_JAM_CAPABILITY id={JamJvsSprintContract.CapabilityId} " +
                $"profile={sprint.ProfileId} key={sprint.DesktopPhysicalKey} " +
                $"speedMultiplier={sprint.SpeedMultiplier:F2} " +
                $"missingDependencies={sprint.MissingDependencyCount} completeJamReady=False");
        }
        var startsNewGame = options.ContainsKey("new-game");
        var restoredOpening = startsNewGame ? null : loaded.Session.OpeningState;
        OpeningQuestRuntime? openingFlow = null;
        if (startsNewGame || restoredOpening is { Completed: false })
        {
            var openingManifest = OpeningManifest.Load(
                RequireOption(options, "opening-manifest"),
                _configuration);
            openingFlow = new OpeningQuestRuntime();
            AddChild(openingFlow);
            openingFlow.Configure(
                openingManifest,
                loaded,
                _configuration,
                restoredOpening);
        }
        if (options.TryGetValue("opening-proof", out var openingProof))
        {
            if (openingFlow is null)
                throw new InvalidOperationException(
                    "Opening acceptance did not create an active opening flow.");
            _ = RunOpeningAcceptance(
                openingFlow,
                loaded,
                scenePath,
                openingProof,
                options);
            return;
        }
        if (galleryContract is not null)
            GD.Print($"OPENNV_GALLERY_STAGE id={galleryContract.Id} stage=cell-load-complete");
        if (options.ContainsKey("xr-simulator-proof"))
        {
            _ = RunXrSimulatorAcceptance(loaded, scenePath, options);
            return;
        }
        if (options.ContainsKey("flat-controls-proof"))
        {
            _ = RunFlatControlsAcceptance(loaded, scenePath, options);
            return;
        }
        if (options.TryGetValue("capture-root", out var captureRoot))
        {
            if (galleryContract is not null)
                GD.Print($"OPENNV_GALLERY_STAGE id={galleryContract.Id} stage=capture-dispatch");
            var captureTask = EnvironmentCapture.Run(
                this,
                loaded,
                _configuration,
                captureRoot,
                scenePath,
                options.TryGetValue("report", out var captureReport) ? captureReport : null,
                options.TryGetValue("retail-state-contract", out var retailState) ? retailState : null,
                options.TryGetValue("gallery-shot", out var galleryShot) ? galleryShot : null);
            if (galleryContract is not null)
                GD.Print(
                    $"OPENNV_GALLERY_STAGE id={galleryContract.Id} " +
                    $"stage=capture-task-created status={captureTask.Status}");
            if (captureTask.IsCompleted)
                captureTask.GetAwaiter().GetResult();
            return;
        }
        if (options.ContainsKey("pool-proof"))
        {
            _ = RunPoolProof(loaded, scenePath, options);
            return;
        }
        if (options.ContainsKey("gameplay-proof"))
        {
            _ = RunGameplayProof(loaded, scenePath, options);
            return;
        }
        if (options.ContainsKey("gameplay-reload-proof"))
        {
            CompleteGameplayReloadProof(loaded, scenePath, options);
            return;
        }
        if (runTraversalProof)
        {
            _ = RunDoorTraversalProof(loaded, scenePath, options);
            return;
        }
        CompleteCellLoad(loaded, scenePath, options, null);
    }

    private async Task RunOpeningAcceptance(
        OpeningQuestRuntime opening,
        CellSceneLoader.LoadedCell loaded,
        string scenePath,
        string mode,
        IReadOnlyDictionary<string, string> options)
    {
        try
        {
            if (!double.TryParse(
                    RequireOption(options, "opening-proof-timeout-seconds"),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var timeoutSeconds) || timeoutSeconds <= 0.0)
                throw new ArgumentException("Opening acceptance timeout is invalid.");
            var initialState = loaded.Session.OpeningState;
            var state = await opening.RunAcceptance(
                mode,
                RequireOption(options, "opening-proof-name"),
                timeoutSeconds);
            if (!File.Exists(loaded.Session.SavePath))
                throw new InvalidOperationException(
                    "Opening acceptance did not produce the canonical save.");
            var saveSha256 = Convert.ToHexString(
                    SHA256.HashData(File.ReadAllBytes(loaded.Session.SavePath)))
                .ToLowerInvariant();
            WriteReport(
                RequireOption(options, "report"),
                new
                {
                    schema = "opennv-opening-acceptance/v1",
                    status = "pass",
                    mode,
                    inputTransport = "godot-authored-ui-signals-plus-configured-input-map",
                    windowsAppControlUsed = false,
                    foregroundInputInjected = false,
                    configurationSchema = RuntimeConfiguration.ExpectedSchema,
                    configurationSha256 = _configuration.Sha256,
                    scene = Path.GetFullPath(scenePath),
                    save = new
                    {
                        path = loaded.Session.SavePath,
                        sha256 = saveSha256,
                    },
                    initial = initialState is null
                        ? null
                        : new
                        {
                            stage = initialState.Stage,
                            completed = initialState.Completed,
                        },
                    final = new
                    {
                        schema = state.Schema,
                        questFormId = state.QuestFormId,
                        stage = state.Stage,
                        completed = state.Completed,
                        playerName = state.PlayerName,
                        specialTotal = state.SpecialValues.Values.Sum(),
                        tagSkills = state.TagSkillFormIds.Count,
                        traits = state.TraitFormIds.Count,
                        quests = state.Quests.Count,
                        globals = state.Globals.Count,
                        objectives = state.Objectives.Count,
                        inventory = state.Inventory.Count,
                        equippedItems = state.EquippedItemFormIds.Count,
                        achievements = state.Achievements.Count,
                    },
                    gameplay = loaded.Session.Report(),
                });
            GD.Print(
                $"OPENNV_OPENING_ACCEPTANCE_PASS mode={mode} stage={state.Stage} " +
                $"completed={state.Completed} save={saveSha256}");
            GetTree().Quit(0);
        }
        catch (Exception exception)
        {
            GD.PushError($"OPENNV_OPENING_ACCEPTANCE_FAIL {exception}");
            GetTree().Quit(1);
        }
    }

    private async Task RunPoolProof(
        CellSceneLoader.LoadedCell loaded,
        string scenePath,
        IReadOnlyDictionary<string, string> options)
    {
        try
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            if (loaded.Pools.Count != 1)
                throw new InvalidOperationException("Pool proof requires one configured table.");
            var table = loaded.Pools.Values.Single();
            if (table.BallCount < 1 || table.Balls.Any(ball =>
                    ball.Mass <= 0.0f || ball.CollisionRadiusMeters <= 0.0f))
                throw new InvalidOperationException("Pool proof found an incomplete authored ball body.");
            var authoredPositions = table.Balls.ToDictionary(
                ball => ball.ReferenceFormId,
                ball => ball.AuthoredTransform.Origin,
                StringComparer.OrdinalIgnoreCase);
            loaded.Player.EnterPoolForProof(table);
            if (!loaded.Player.HasHeldPoolCue)
                throw new InvalidOperationException("Pool proof did not mount the authored cue.");
            var cueMounted = loaded.Player.HasHeldPoolCue;

            var objectBall = table.Balls
                .Where(ball => ball.Role == "object-ball")
                .OrderBy(ball => new Vector2(
                    ball.GlobalPosition.X - table.CueBall.GlobalPosition.X,
                    ball.GlobalPosition.Z - table.CueBall.GlobalPosition.Z).LengthSquared())
                .First();
            var direction = objectBall.GlobalPosition - table.CueBall.GlobalPosition;
            direction.Y = 0.0f;
            if (direction.IsZeroApprox())
                throw new InvalidOperationException("Authored pool-ball placement has no strike direction.");
            direction = direction.Normalized();
            table.CueBall.ClearBallCollisionEvidence();
            var xrLayout = loaded.Player.UsesXr;
            bool struck;
            if (xrLayout)
            {
                var radius = table.CueBall.CollisionRadiusMeters;
                var timestep = 1.0 / _configuration.Simulation.PhysicsTicksPerSecond;
                table.UpdateTrackedCue(
                    table.CueBall.GlobalPosition - direction * radius * 2.0f,
                    true,
                    timestep);
                struck = table.UpdateTrackedCue(
                    table.CueBall.GlobalPosition + direction * radius,
                    true,
                    timestep);
            }
            else
            {
                table.SelectMaximumFlatPowerForProof();
                struck = table.StrikeFlat(direction);
            }
            if (!struck)
                throw new InvalidOperationException("Pool input adapter did not produce a shared strike.");
            for (var frame = 0;
                 frame < _configuration.Pool.ProofMaximumPhysicsFrames &&
                    table.CueBall.BallCollisionCount == 0;
                 frame++)
                await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            if (table.CueBall.BallCollisionCount == 0)
                throw new InvalidOperationException(
                    "Pool cue ball did not collide with an authored object ball: " +
                    $"cuePosition={table.CueBall.GlobalPosition} " +
                    $"cueVelocity={table.CueBall.LinearVelocity} " +
                    $"cuePocketed={table.CueBall.IsPocketed} " +
                    $"targetPosition={objectBall.GlobalPosition} " +
                    $"targetPocketed={objectBall.IsPocketed} " +
                    $"travelFromAuthored={table.CueBall.GlobalPosition.DistanceTo(authoredPositions[table.CueBall.ReferenceFormId]):F4}");
            var cueBallBallCollisions = table.CueBall.BallCollisionCount;
            var travelled = table.CueBall.GlobalPosition.DistanceTo(
                authoredPositions[table.CueBall.ReferenceFormId]);
            if (Mathf.IsZeroApprox(travelled))
                throw new InvalidOperationException("Pool cue ball did not move after the accepted strike.");

            table.ResetAuthored();
            if (table.Balls.Any(ball => !ball.GlobalPosition.IsEqualApprox(
                    authoredPositions[ball.ReferenceFormId])))
                throw new InvalidOperationException("Pool reset did not restore authored reference transforms.");
            loaded.Player.ExitPoolForProof();
            loaded.Session.Save();
            if (!File.Exists(loaded.Session.SavePath))
                throw new InvalidOperationException("Pool state was not persisted by the shared session.");

            var report = new
            {
                schema = "opennv-pool-practice/v1",
                status = "pass",
                configurationSchema = RuntimeConfiguration.ExpectedSchema,
                configurationSha256 = _configuration.Sha256,
                scene = scenePath,
                cellFormId = loaded.FormId,
                tableReferenceFormId = table.ReferenceFormId,
                presentationModelPath = table.PresentationModelPath,
                gameplayCollisionSource = table.GameplayCollisionSource,
                authoredBalls = table.BallCount,
                dynamicConvexBodies = table.Balls.Count,
                massKilograms = table.Balls.Select(ball => ball.Mass).ToArray(),
                collisionRadiusMeters = table.Balls.Select(ball => ball.CollisionRadiusMeters).ToArray(),
                inputAdapter = xrLayout ? "openxr-tracked-cue-layout" : "desktop-look-and-power",
                configuredDesktopStrikeMetersPerSecond = table.SelectedFlatPowerMetersPerSecond,
                sharedSimulation = true,
                cueMounted,
                strikeAccepted = struck,
                cueBallBallCollisions,
                cueBallTravelMeters = travelled,
                authoredReset = true,
                savePath = loaded.Session.SavePath,
                hardwareValidated = false,
            };
            if (options.TryGetValue("report", out var reportPath))
                WriteReport(reportPath, report);
            GD.Print(
                $"OPENNV_POOL_PRACTICE_PASS adapter={report.inputAdapter} " +
                $"balls={table.BallCount} travel={travelled:F4}");
            GetTree().Quit(0);
        }
        catch (Exception exception)
        {
            GD.PushError($"OPENNV_POOL_PRACTICE_FAIL {exception.Message}");
            GetTree().Quit(1);
        }
    }

    private void EnableOpenXr()
    {
        _openXr = XRServer.FindInterface("OpenXR");
        if (_openXr is null || !_openXr.IsInitialized())
            throw new InvalidOperationException(
                "OpenXR was requested but no initialized runtime is available. " +
                "Launch with --xr-mode on before --, connect the headset, and verify the active OpenXR runtime.");
        GetViewport().UseXR = true;
        Engine.PhysicsTicksPerSecond = _configuration.Simulation.PhysicsTicksPerSecond;
        DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
        GD.Print(
            $"OPENNV_OPENXR_READY interface=OpenXR worldScale={_configuration.Xr.WorldScale} " +
            $"physicsHz={_configuration.Simulation.PhysicsTicksPerSecond}");
    }

    private async Task RunXrSimulatorAcceptance(
        CellSceneLoader.LoadedCell loaded,
        string scenePath,
        IReadOnlyDictionary<string, string> options)
    {
        try
        {
            await XrSimulatorAcceptance.Run(this, loaded, scenePath, options, _configuration);
            QuitOpenXr(0);
        }
        catch (Exception exception)
        {
            GD.PushError($"OPENNV_XR_SIMULATOR_FAIL {exception.Message}");
            QuitOpenXr(1);
        }
    }

    private void QuitOpenXr(int exitCode)
    {
        GetViewport().UseXR = false;
        _openXr?.Uninitialize();
        _openXr = null;
        GetTree().Quit(exitCode);
    }

    private async Task RunFlatControlsAcceptance(
        CellSceneLoader.LoadedCell loaded,
        string scenePath,
        IReadOnlyDictionary<string, string> options)
    {
        try
        {
            await FlatControlsAcceptance.Run(this, loaded, scenePath, options, _configuration);
            GetTree().Quit(0);
        }
        catch (Exception exception)
        {
            GD.PushError($"OPENNV_FLAT_CONTROLS_FAIL {exception.Message}");
            GetTree().Quit(1);
        }
    }

    private void CompleteXrRigProof(IReadOnlyDictionary<string, string> options)
    {
        XrRigLayoutAcceptance.Run(this, options, _configuration);
        GetTree().Quit(0);
    }

    private void CompleteClassicDioramaRigProof(IReadOnlyDictionary<string, string> options)
    {
        var session = new GameplaySession();
        session.Configure(
            "classic-diorama-rig-proof",
            "ClassicDioramaRigProof",
            "classic-diorama-proof-door",
            _configuration,
            options.TryGetValue("save-path", out var savePath) ? savePath : null,
            false,
            false,
            true,
            true,
            "CLASSIC DIORAMA  •  PRESENTATION PROOF");
        AddChild(session);
        var player = new CellPlayer();
        player.Configure(0.0f, session, _configuration, false, true);
        AddChild(player);

        if (!player.UsesClassicDiorama || player.UsesXr || player.Camera is XRCamera3D ||
            player.Camera.Projection != Camera3D.ProjectionType.Orthogonal ||
            player.DioramaOrbit is null ||
            !Mathf.IsEqualApprox(player.Camera.Size, CellPlayer.DioramaInitialSizeMeters))
            throw new InvalidOperationException("Classic Diorama camera hierarchy or projection is invalid.");

        var initialYaw = player.DioramaTargetYawRadians;
        var initialSize = player.DioramaTargetSizeMeters;
        player._UnhandledInput(new InputEventKey
        {
            PhysicalKeycode = Key.E,
            Pressed = true,
        });
        player._UnhandledInput(new InputEventMouseButton
        {
            ButtonIndex = MouseButton.WheelUp,
            Pressed = true,
        });
        var expectedYaw = initialYaw - CellPlayer.DioramaYawStepRadians;
        if (!Mathf.IsEqualApprox(player.DioramaTargetYawRadians, expectedYaw) ||
            player.DioramaTargetSizeMeters >= initialSize ||
            player.DioramaTargetSizeMeters < CellPlayer.DioramaMinimumSizeMeters)
            throw new InvalidOperationException("Classic Diorama rotation or zoom input contract failed.");

        var report = new
        {
            schema = "opennv-classic-diorama-rig/v1",
            status = "pass",
            presentation = "classic-diorama",
            simulation = "shared-gameplay-session",
            cameraType = player.Camera.GetType().Name,
            cameraName = player.Camera.Name.ToString(),
            orbitName = player.DioramaOrbit.Name.ToString(),
            projection = "orthogonal",
            initialSizeMeters = CellPlayer.DioramaInitialSizeMeters,
            minimumSizeMeters = CellPlayer.DioramaMinimumSizeMeters,
            maximumSizeMeters = CellPlayer.DioramaMaximumSizeMeters,
            zoomedSizeMeters = player.DioramaTargetSizeMeters,
            yawStepDegrees = Mathf.RadToDeg(CellPlayer.DioramaYawStepRadians),
            targetYawAfterProofDegrees = Mathf.RadToDeg(player.DioramaTargetYawRadians),
            panSpeedMetersPerSecond = CellPlayer.DioramaPanSpeedMetersPerSecond,
            panKeys = new[] { "W", "A", "S", "D" },
            rotationKeys = new[] { "Q", "E" },
            zoomInput = "mouse-wheel",
            resetKey = "Home",
            gameplaySession = session.Report(),
            turnSimulationConnected = false,
            noRetailData = true,
        };
        if (options.TryGetValue("report", out var reportPath))
            WriteReport(reportPath, report);
        GD.Print(
            $"OPENNV_CLASSIC_DIORAMA_RIG_PASS projection=orthogonal " +
            $"size={CellPlayer.DioramaInitialSizeMeters:F1} yawStep=60 panKeys=WASD");
        GetTree().Quit(0);
    }

    private async Task RunGameplayProof(
        CellSceneLoader.LoadedCell loaded,
        string scenePath,
        IReadOnlyDictionary<string, string> options)
    {
        try
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            var route = _configuration.Proof.GameplayRoute;
            var revolver = loaded.Pickups.Values.Single(
                pickup => pickup.ItemFormId == route.WeaponPickupFormId);
            loaded.Session.Collect(revolver);
            loaded.Session.Fire(loaded.Player.Camera);
            var aid = loaded.Pickups.Values.First(
                pickup => pickup.EditorId == route.AidPickupEditorId);
            loaded.Session.Collect(aid);
            var container = loaded.Containers.Values.Single(
                candidate => candidate.EditorId == route.ContainerEditorId);
            loaded.Session.OpenContainer(container);
            loaded.ProofDoor.SetOpen(true);
            loaded.Session.DoorChanged(loaded.ProofDoor);
            if (!loaded.Session.ObjectiveComplete ||
                loaded.Session.ShotsFired != route.ExpectedShotsFired ||
                loaded.Session.AmmoInMagazine != route.ExpectedAmmoInMagazine ||
                loaded.Session.EmptiedContainersCount != route.ExpectedEmptiedContainers ||
                loaded.Session.OpenDoorsCount != route.ExpectedOpenDoors ||
                !loaded.Session.HasItem(route.ExpectedInventoryItemFormId) ||
                !loaded.Session.IsContainerEmptied(route.ExpectedContainerReferenceFormId) ||
                !File.Exists(loaded.Session.SavePath))
                throw new InvalidOperationException("Playable route did not reach its persisted completion state.");
            WriteGameplayReport("first-run", loaded, scenePath, options);
            GetTree().Quit(0);
        }
        catch (Exception exception)
        {
            GD.PushError($"OPENNV_GODOT_GAMEPLAY_PROOF_FAIL {exception.Message}");
            GetTree().Quit(1);
        }
    }

    private void CompleteGameplayReloadProof(
        CellSceneLoader.LoadedCell loaded,
        string scenePath,
        IReadOnlyDictionary<string, string> options)
    {
        var route = _configuration.Proof.GameplayRoute;
        if (!loaded.Session.ObjectiveComplete ||
            loaded.Session.ShotsFired != route.ExpectedShotsFired ||
            loaded.Session.AmmoInMagazine != route.ExpectedAmmoInMagazine || !loaded.ProofDoor.IsOpen ||
            loaded.Session.EmptiedContainersCount != route.ExpectedEmptiedContainers ||
            loaded.Session.OpenDoorsCount != route.ExpectedOpenDoors ||
            !loaded.Session.HasItem(route.ExpectedInventoryItemFormId) ||
            !loaded.Session.IsContainerEmptied(route.ExpectedContainerReferenceFormId) ||
            loaded.Pickups.Values.Any(pickup => pickup.ItemFormId == route.WeaponPickupFormId))
            throw new InvalidOperationException("Cold reload did not restore the completed playable route.");
        WriteGameplayReport("cold-reload", loaded, scenePath, options);
        GetTree().Quit(0);
    }

    private void WriteGameplayReport(
        string phase,
        CellSceneLoader.LoadedCell loaded,
        string scenePath,
        IReadOnlyDictionary<string, string> options)
    {
        var report = new
        {
            schema = "opennv-godot-playable-route/v1",
            status = "pass",
            configurationSchema = RuntimeConfiguration.ExpectedSchema,
            configurationSha256 = _configuration.Sha256,
            phase,
            scene = scenePath,
            cellFormId = loaded.FormId,
            cellEditorId = loaded.EditorId,
            route = new[]
            {
                "pickup-revolver",
                "fire-physical-ray",
                "pickup-aid",
                "open-resolved-container",
                "open-entry-door",
            },
            session = loaded.Session.Report(),
            noHostControl = true,
        };
        if (options.TryGetValue("report", out var reportPath))
            WriteReport(reportPath, report);
        GD.Print($"OPENNV_GODOT_PLAYABLE_ROUTE_PASS phase={phase} save={loaded.Session.SavePath}");
    }

    private async Task RunDoorTraversalProof(
        CellSceneLoader.LoadedCell loaded,
        string scenePath,
        IReadOnlyDictionary<string, string> options)
    {
        try
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            var floor = CellSceneLoader.CastSpawnFloor(
                GetWorld3D().DirectSpaceState,
                _configuration.Proof,
                loaded.Player.CollisionMask,
                loaded.Player.GetRid());
            if (!floor.Hit || MathF.Abs(floor.Y) > _configuration.Proof.SpawnFloorToleranceMeters)
                throw new InvalidOperationException(
                    $"XTEL floor contract failed: hit={floor.Hit} y={floor.Y} collider={floor.ColliderPath}");
            var ray = CellSceneLoader.BuildProofRay(loaded.ProofDoor, _configuration.Proof);
            var closed = CellSceneLoader.CastProofRay(
                GetWorld3D().DirectSpaceState,
                loaded.ProofDoor,
                ray,
                _configuration.Player.CollisionMask);
            loaded.ProofDoor.SetOpen(true);
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            var opened = CellSceneLoader.CastProofRay(GetWorld3D().DirectSpaceState, loaded.ProofDoor, ray);
            var portalDirection = (ray.To - ray.From).Normalized();
            var portalCenter = (ray.From + ray.To) / 2.0f;
            var projectileRay = new CellSceneLoader.DoorRay(
                portalCenter - portalDirection * _configuration.Proof.ProjectileRayStartMeters,
                portalCenter + portalDirection * _configuration.Proof.ProjectileRayEndMeters,
                ray.LocalSize,
                ray.LocalNormal);
            var projectileHit = CellSceneLoader.CastProofRay(
                GetWorld3D().DirectSpaceState,
                loaded.ProofDoor,
                projectileRay);
            var projectileBlockedByDoor = projectileHit.HitProofDoor || loaded.PortalLinks.Any(portal =>
                projectileHit.ColliderPath.StartsWith(portal.ToDoor.GetPath().ToString(), StringComparison.Ordinal));
            portalCenter.Y = _configuration.Proof.PortalCapsuleCenterHeightMeters;
            var portalMotion = portalDirection * _configuration.Proof.PortalCapsuleMotionMeters;
            var forwardCollision = new KinematicCollision3D();
            var walkForwardBlocked = loaded.PortalLinks.Count > 0 && loaded.Player.TestMove(
                new Transform3D(Basis.Identity, portalCenter - portalMotion / 2.0f),
                portalMotion,
                forwardCollision);
            var backwardCollision = new KinematicCollision3D();
            var walkBackwardBlocked = loaded.PortalLinks.Count > 0 && loaded.Player.TestMove(
                new Transform3D(Basis.Identity, portalCenter + portalMotion / 2.0f),
                -portalMotion,
                backwardCollision);
            var forwardCollider = walkForwardBlocked
                ? (forwardCollision.GetCollider() as Node)?.GetPath().ToString() ?? "unknown"
                : "";
            var backwardCollider = walkBackwardBlocked
                ? (backwardCollision.GetCollider() as Node)?.GetPath().ToString() ?? "unknown"
                : "";
            var forwardNormal = walkForwardBlocked ? forwardCollision.GetNormal() : Vector3.Zero;
            var backwardNormal = walkBackwardBlocked ? backwardCollision.GetNormal() : Vector3.Zero;
            var linkedDoorBlocked = loaded.PortalLinks.Any(portal =>
                opened.ColliderPath.StartsWith(portal.ToDoor.GetPath().ToString(), StringComparison.Ordinal));
            var requiresEmptyOpenRay = loaded.PortalLinks.Count == 0;
            if (!closed.Hit || !closed.HitProofDoor || opened.HitProofDoor || linkedDoorBlocked ||
                (requiresEmptyOpenRay && opened.Hit) ||
                projectileBlockedByDoor ||
                walkForwardBlocked ||
                (walkBackwardBlocked &&
                    backwardNormal.Y < _configuration.Proof.WalkableSurfaceNormalYMinimum) ||
                loaded.PortalLinks.Any(portal => !portal.FromDoor.IsOpen || !portal.ToDoor.IsOpen ||
                    portal.AlignmentErrorMeters > _configuration.Proof.PortalAlignmentToleranceMeters))
                throw new InvalidOperationException(
                    $"Door traversal contract failed: closedHit={closed.Hit} " +
                    $"closedHitDoor={closed.HitProofDoor} closedCollider={closed.ColliderPath} " +
                    $"openHit={opened.Hit} openCollider={opened.ColliderPath} " +
                    $"projectileHit={projectileHit.Hit} projectileCollider={projectileHit.ColliderPath} " +
                    $"projectileBlockedByDoor={projectileBlockedByDoor} " +
                    $"walkForwardBlocked={walkForwardBlocked} forwardCollider={forwardCollider} " +
                    $"forwardNormal={forwardNormal} walkBackwardBlocked={walkBackwardBlocked} " +
                    $"backwardCollider={backwardCollider} backwardNormal={backwardNormal} " +
                    $"linkedCells={loaded.LinkedCells.Count} portals={loaded.PortalLinks.Count} " +
                    $"localSize={ray.LocalSize} localNormal={ray.LocalNormal} from={ray.From} to={ray.To}");
            CompleteCellLoad(
                loaded with { ProofDoorOpen = true },
                scenePath,
                options,
                new DoorTraversalProof(
                    floor.Hit,
                    floor.Y,
                    floor.ColliderPath,
                    closed.Hit,
                    closed.HitProofDoor,
                    opened.Hit,
                    opened.HitProofDoor || linkedDoorBlocked,
                    !projectileBlockedByDoor,
                    !walkForwardBlocked,
                    !walkBackwardBlocked ||
                        backwardNormal.Y >= _configuration.Proof.WalkableSurfaceNormalYMinimum,
                    !walkForwardBlocked &&
                        (!walkBackwardBlocked ||
                            backwardNormal.Y >= _configuration.Proof.WalkableSurfaceNormalYMinimum),
                    loaded.LinkedCells.Count,
                    loaded.PortalLinks.Count == 0
                        ? null
                        : loaded.PortalLinks.Max(portal => portal.AlignmentErrorMeters)));
            GetTree().Quit(0);
        }
        catch (Exception exception)
        {
            GD.PushError($"OPENNV_GODOT_DOOR_TRAVERSAL_FAIL {exception.Message}");
            GetTree().Quit(1);
        }
    }

    private void CompleteCellLoad(
        CellSceneLoader.LoadedCell loaded,
        string scenePath,
        IReadOnlyDictionary<string, string> options,
        DoorTraversalProof? traversalProof)
    {
        var report = new
        {
            schema = "opennv-godot-cell/v1",
            status = "pass",
            configurationSchema = RuntimeConfiguration.ExpectedSchema,
            configurationSha256 = _configuration.Sha256,
            renderer = "forward_plus",
            scene = scenePath,
            cellFormId = loaded.FormId,
            cellEditorId = loaded.EditorId,
            assets = loaded.Assets,
            textures = loaded.Textures,
            materialBindings = loaded.MaterialBindings,
            references = loaded.References,
            doors = loaded.Doors,
            authoredLights = loaded.AuthoredLights,
            actors = loaded.Actors.Count,
            poolTables = loaded.Pools.Values.Select(table => new
            {
                referenceFormId = table.ReferenceFormId,
                presentationModelPath = table.PresentationModelPath,
                gameplayCollisionSource = table.GameplayCollisionSource,
                authoredBalls = table.BallCount,
                pocketedBalls = table.PocketedBallCount,
            }).ToArray(),
            linkedCells = loaded.LinkedCells.Select(linked => new
            {
                cellFormId = linked.Content.FormId,
                cellEditorId = linked.Content.EditorId,
                sourceCellFormIds = linked.Content.SourceCellFormIds.OrderBy(value => value).ToArray(),
                assets = linked.Content.Assets,
                references = linked.Content.References,
                actors = linked.Content.Actors.Count,
                collisionMeshes = linked.Content.CollisionMeshes,
            }).ToArray(),
            portals = loaded.PortalLinks.Select(portal => new
            {
                fromDoorReferenceFormId = portal.FromDoor.ReferenceFormId,
                toDoorReferenceFormId = portal.ToDoor.ReferenceFormId,
                reciprocal = portal.FromDoor.DestinationReferenceFormId == portal.ToDoor.ReferenceFormId &&
                    portal.ToDoor.DestinationReferenceFormId == portal.FromDoor.ReferenceFormId,
                alignmentErrorMeters = portal.AlignmentErrorMeters,
                normalAgreement = portal.NormalAgreement,
                bothOpen = portal.FromDoor.IsOpen && portal.ToDoor.IsOpen,
            }).ToArray(),
            xrPresentation = !loaded.Player.UsesXr
                ? null
                : new
                {
                    heldWeapon = loaded.Player.HasHeldWeapon,
                    muzzleFeedback = loaded.Player.HasMuzzleFeedback,
                    leftHandVisible = loaded.Player.HasLeftHand,
                    rightHandVisible = loaded.Player.HasRightHand,
                    visibleHandProvider = loaded.Player.HandProvider,
                    leftGripPose = loaded.Player.LeftGrip?.Pose.ToString(),
                    rightGripPose = loaded.Player.RightGrip?.Pose.ToString(),
                    leftAimPose = loaded.Player.LeftAim?.Pose.ToString(),
                    rightAimPose = loaded.Player.RightAim?.Pose.ToString(),
                    wristHud = loaded.Session.HasXrHud,
                    wristHudPixelSize = loaded.Session.XrHudPixelSize,
                    startingLoadout = loaded.Session.Report(),
                },
            classicDioramaPresentation = !loaded.Player.UsesClassicDiorama
                ? null
                : new
                {
                    projection = "orthogonal",
                    cameraName = loaded.Player.Camera.Name.ToString(),
                    orbitName = loaded.Player.DioramaOrbit!.Name.ToString(),
                    sizeMeters = loaded.Player.Camera.Size,
                    targetSizeMeters = loaded.Player.DioramaTargetSizeMeters,
                    yawStepDegrees = Mathf.RadToDeg(CellPlayer.DioramaYawStepRadians),
                    panSpeedMetersPerSecond = CellPlayer.DioramaPanSpeedMetersPerSecond,
                    framingBoundsPosition = loaded.Player.DioramaFramingBounds is Aabb bounds
                        ? new[] { bounds.Position.X, bounds.Position.Y, bounds.Position.Z }
                        : null,
                    framingBoundsSize = loaded.Player.DioramaFramingBounds is Aabb framing
                        ? new[] { framing.Size.X, framing.Size.Y, framing.Size.Z }
                        : null,
                    cameraFill = loaded.Player.Camera.FindChild(
                        "ClassicDioramaCameraFill",
                        true,
                        false) is DirectionalLight3D,
                    turnSimulationConnected = false,
                },
            collisionMeshes = loaded.CollisionMeshes,
            surfaces = loaded.Surfaces,
            vertices = loaded.Vertices,
            spawnSource = "XTEL",
            spawnAtFloorOrigin = true,
            proofDoorFormId = loaded.ProofDoorFormId,
            proofDoorOpen = loaded.ProofDoorOpen,
            wholeCellVisible = true,
            connectedAuthoredSpaces = loaded.LinkedCells.Count > 0,
            doorTraversal = traversalProof is null
                ? null
                : new
                {
                    status = "pass",
                    floorHit = traversalProof.Value.FloorHit,
                    floorY = traversalProof.Value.FloorY,
                    floorCollider = traversalProof.Value.FloorCollider,
                    closedHit = traversalProof.Value.ClosedHit,
                    closedHitDoor = traversalProof.Value.ClosedHitDoor,
                    openHit = traversalProof.Value.OpenHit,
                    openBlockedByPortalDoor = traversalProof.Value.OpenBlockedByPortalDoor,
                    projectilePortalClear = traversalProof.Value.ProjectilePortalClear,
                    capsuleWalkForward = traversalProof.Value.CapsuleWalkForward,
                    capsuleWalkBackward = traversalProof.Value.CapsuleWalkBackward,
                    capsuleWalkThrough = traversalProof.Value.CapsuleWalkThrough,
                    linkedCells = traversalProof.Value.LinkedCells,
                    maximumPortalAlignmentErrorMeters = traversalProof.Value.MaximumPortalAlignmentErrorMeters,
                },
        };
        if (options.TryGetValue("report", out var reportPath))
            WriteReport(reportPath, report);
        GD.Print(
            $"OPENNV_GODOT_CELL_PASS cell={loaded.FormId} assets={loaded.Assets} textures={loaded.Textures} " +
            $"materials={loaded.MaterialBindings} " +
            $"references={loaded.References} doors={loaded.Doors} lights={loaded.AuthoredLights} " +
            $"collision={loaded.CollisionMeshes} " +
            $"surfaces={loaded.Surfaces} vertices={loaded.Vertices} proofDoorOpen={loaded.ProofDoorOpen} " +
            $"linkedCells={loaded.LinkedCells.Count} portals={loaded.PortalLinks.Count} " +
            $"doorTraversal={(traversalProof is null ? "not-requested" : "pass")}");
        if (options.ContainsKey("quit-after-load"))
            GetTree().Quit(0);
    }

    private void LoadFo1HexScene(
        string scenePath,
        IReadOnlyDictionary<string, string> options)
    {
        var loaded = Fo1HexSceneLoader.Load(
            scenePath,
            this,
            options.TryGetValue("save-path", out var savePath) ? savePath : null);
        var report = new
        {
            schema = "opennv-fo1-hex-runtime/v1",
            status = "pass",
            renderer = "forward_plus",
            scene = loaded.ScenePath,
            sceneSha256 = loaded.SceneSha256,
            grid = new
            {
                width = Fo1HexMath.Width,
                height = Fo1HexMath.Height,
                flatToFlatMeters = Fo1HexMath.FlatToFlatMeters,
                layout = "fallout-even-column-offset-flat-v1",
            },
            floorEntries = loaded.FloorEntries,
            floorTextures = loaded.FloorTextures,
            renderedFloorTiles = loaded.RenderedFloorTiles,
            provisionalWalkableHexes = loaded.WalkableHexes,
            spriteArtifacts = loaded.SpriteArtifacts,
            spritePlacements = loaded.SpritePlacements,
            combatMobs = loaded.CombatMobs,
            cave3d = new
            {
                boundaryEdges = loaded.CaveBoundaryEdges,
                obstacles = loaded.CaveObstacles,
                triangles = loaded.CaveTriangles,
                sourceStaticSpriteOverlayVisible = loaded.OwnedCave.Instances == 0,
                ownedManifestSha256 = loaded.OwnedCave.ManifestSha256,
                ownedAssets = loaded.OwnedCave.Assets,
                ownedInstances = loaded.OwnedCave.Instances,
                ownedMeshInstances = loaded.OwnedCave.MeshInstances,
                ownedSurfaceInstances = loaded.OwnedCave.SurfaceInstances,
                ownedMaterialBindings = loaded.OwnedCave.MaterialBindings,
                ownedRoles = loaded.OwnedCave.Roles,
                continuousFloorHexes = loaded.OwnedCave.ContinuousFloorHexes,
                continuousFloorTriangles = loaded.OwnedCave.ContinuousFloorTriangles,
                continuousFloorMeshInstances = loaded.OwnedCave.ContinuousFloorMeshInstances,
            },
            entryTile = loaded.EntryTile,
            entryWorldMeters = Vector(Fo1HexMath.Center(loaded.EntryTile)),
            doorTile = loaded.DoorTile,
            doorWorldMeters = Vector(Fo1HexMath.Center(loaded.DoorTile)),
            doorRotation = loaded.DoorRotation,
            doorMaterialBindings = loaded.Door.MaterialBindings,
            doorBoundsPosition = Vector(loaded.Door.Bounds.Position),
            doorBoundsSize = Vector(loaded.Door.Bounds.Size),
            sourceFrameMeters = new[]
            {
                loaded.Door.FrameWidthMeters,
                loaded.Door.FrameHeightMeters,
            },
            topLevelObjects = loaded.TopLevelObjects,
            sourceDoors = loaded.SourceDoors,
            camera = new
            {
                type = loaded.Camera.Camera.GetType().Name,
                projection = "orthogonal",
                middleMouseOrbit = true,
                controlKeyOrbit = true,
                rightMousePan = true,
                wheelZoomTowardCursor = true,
                edgePan = true,
                keyboardPan = new[] { "W", "A", "S", "D", "arrows" },
                playerFocusKey = "F",
                routeResetKey = "Home",
            },
            tactical = loaded.Session.Report(),
            turnSimulation = "bounded-movement-attack-rat-turn-proof",
            collision = "floor-art-presence-minus-MAP-OBJECT_NO_BLOCK-central-hex",
            windowsAppControlUsed = false,
            foregroundInputInjected = false,
        };
        if (options.TryGetValue("report", out var reportPath))
            WriteReport(reportPath, report);
        GD.Print(
            $"OPENNV_FO1_HEX_PASS scene={loaded.SceneSha256} entry={loaded.EntryTile} " +
            $"door={loaded.DoorTile} floor={loaded.RenderedFloorTiles} " +
            $"walkable={loaded.WalkableHexes} sprites={loaded.SpritePlacements}");
        if (options.ContainsKey("fo1-xr-simulator-preview"))
        {
            _ = Fo1XrSimulatorPreview.Run(this, loaded, options, _configuration);
            return;
        }
        if (options.ContainsKey("fo1-new-game") || options.ContainsKey("fo1-new-game-demo"))
        {
            var characterStart = Fo1CharacterStartContract.Load(
                RequireOption(options, "fo1-character-start"),
                RequireOption(options, "fo1-character-start-sha256"));
            if (options.ContainsKey("fo1-new-game-demo"))
                _ = Fo1NewGameFlow.RunDemo(
                    this,
                    loaded,
                    characterStart,
                    RequireOption(options, "demo-report"),
                    options.ContainsKey("fo1-demo-fast-opening"),
                    options.ContainsKey("fo1-demo-skip-opening"));
            else
                Fo1NewGameFlow.StartInteractive(
                    this,
                    loaded,
                    characterStart,
                    options.TryGetValue("fo1-start-presentation", out var startPresentation)
                        ? startPresentation
                        : "first-person");
            return;
        }
        if (options.ContainsKey("fo1-tactical-proof"))
        {
            _ = Fo1HexProof.Run(this, loaded, RequireOption(options, "report"));
            return;
        }
        if (options.ContainsKey("fo1-gameplay-demo"))
        {
            _ = Fo1HexDemo.Run(this, loaded, RequireOption(options, "demo-report"));
            return;
        }
        if (options.TryGetValue("capture-root", out var captureRoot))
        {
            _ = Fo1HexCapture.Run(this, loaded, captureRoot, report);
            return;
        }
        if (options.ContainsKey("quit-after-load"))
            GetTree().Quit(0);
    }

    private void LoadFo1CampaignTransport(
        string campaignPath,
        IReadOnlyDictionary<string, string> options)
    {
        var coverage = Fo1CampaignTransportContract.Load(campaignPath);
        if (options.TryGetValue("report", out var reportPath))
            WriteReport(reportPath, coverage.Report());
        GD.Print(
            $"OPENNV_FO1_CAMPAIGN_TRANSPORT_PASS maps={coverage.MapCoverage.Count} " +
            $"elevations={coverage.Elevations} objects={coverage.TopLevelObjects} " +
            $"doors={coverage.Doors} resources={coverage.Resources}");
        if (DisplayServer.GetName() == "headless" || options.ContainsKey("quit-after-load"))
            GetTree().Quit(0);
    }

    private void LoadFo1CampaignPresentation(
        string campaignPath,
        IReadOnlyDictionary<string, string> options)
    {
        var catalog = Fo1CampaignPresentationContract.Load(campaignPath);
        Fo1CampaignMapViewCoverage? viewCoverage = null;
        Fo1CampaignPresentationViewer? viewer = null;
        if (DisplayServer.GetName() != "headless" || options.ContainsKey("fo1-map") ||
            options.ContainsKey("fo1-campaign-build-proof"))
        {
            int? elevation = null;
            if (options.TryGetValue("fo1-elevation", out var requestedElevation))
            {
                if (!int.TryParse(requestedElevation, out var parsedElevation) ||
                    parsedElevation is < 0 or > 2)
                    throw new ArgumentException(
                        $"Fallout campaign elevation must be 0, 1, or 2: {requestedElevation}");
                elevation = parsedElevation;
            }
            viewer = new Fo1CampaignPresentationViewer();
            AddChild(viewer);
            viewCoverage = viewer.Configure(
                catalog,
                options.TryGetValue("fo1-map", out var requestedMap) ? requestedMap : null,
                elevation);
            GD.Print(
                $"OPENNV_FO1_CAMPAIGN_MAP_VIEW_PASS map={viewCoverage.MapId} " +
                $"elevation={viewCoverage.Elevation} " +
                $"floor={viewCoverage.RenderedFloorPatches} " +
                $"placements={viewCoverage.SpritePlacements} mobs={viewCoverage.Mobs} " +
                $"doors={viewCoverage.Doors}");
        }
        if (options.TryGetValue("report", out var reportPath) &&
            !options.ContainsKey("fo1-campaign-build-proof") &&
            !options.ContainsKey("capture-root"))
            WriteReport(
                reportPath,
                viewCoverage is null
                    ? catalog.Report()
                    : new
                    {
                        schema = "opennv-fo1-campaign-map-view-runtime-proof/v1",
                        status = "pass-selected-connected-wall-topology-view-built",
                        campaign = catalog.Report(),
                        selectedMap = viewCoverage,
                        promotion = new
                        {
                            runtimeValidatedMaps = catalog.Maps.Count,
                            selectedMapViewBuilt = true,
                            renderedMaps = 0,
                            interactiveGameplayMaps = 0,
                            questExecutableMaps = 0,
                            firstPersonReadyMaps = 0,
                            openXrAcceptedMaps = 0,
                        },
                    });
        GD.Print(
            $"OPENNV_FO1_CAMPAIGN_PRESENTATION_PASS maps={catalog.Maps.Count} " +
            $"elevations={catalog.MapCoverage.Sum(row => row.Elevations)} " +
            $"placements={catalog.MapCoverage.Sum(row => row.SpritePlacements)} " +
            $"tiles={catalog.TileArtifacts.Count} sprites={catalog.SpriteArtifacts.Count}");
        if (options.ContainsKey("fo1-campaign-build-proof"))
        {
            _ = Fo1CampaignBuildProof.Run(
                this,
                catalog,
                viewer ?? throw new InvalidOperationException(
                    "Fallout campaign build proof has no viewer."),
                RequireOption(options, "report"));
            return;
        }
        if (options.TryGetValue("capture-root", out var captureRoot))
        {
            _ = Fo1CampaignPresentationCapture.Run(
                this,
                catalog,
                viewer ?? throw new InvalidOperationException(
                    "Fallout campaign visual capture has no viewer."),
                viewCoverage ?? throw new InvalidOperationException(
                    "Fallout campaign visual capture has no selected map."),
                captureRoot,
                options.TryGetValue("report", out var captureReport) ? captureReport : null);
            return;
        }
        if (DisplayServer.GetName() == "headless" || options.ContainsKey("quit-after-load"))
            GetTree().Quit(0);
    }

    private void LoadModel(
        string modelPath,
        string sidecarPath,
        IReadOnlyDictionary<string, string> options)
    {
        var loaded = StaticModelSlice.Load(
            modelPath,
            sidecarPath,
            this,
            _configuration,
            options.TryGetValue("material-manifest", out var materials) ? materials : null,
            options.TryGetValue("material-manifest-sha256", out var materialsHash)
                ? materialsHash
                : null,
            options.ContainsKey("classic-diorama"));
        var report = new
        {
            schema = "opennv-godot-static-model/v1",
            status = "pass",
            renderer = "forward_plus",
            model = modelPath,
            sidecar = sidecarPath,
            sourceSha256 = loaded.SourceSha256,
            meshes = loaded.Meshes,
            surfaces = loaded.Surfaces,
            vertices = loaded.Vertices,
            materialBindings = loaded.MaterialBindings,
            presentation = options.ContainsKey("classic-diorama") ? "classic-diorama" : "reference",
            projection = loaded.Projection,
            boundsPosition = new[] { loaded.Bounds.Position.X, loaded.Bounds.Position.Y, loaded.Bounds.Position.Z },
            boundsSize = new[] { loaded.Bounds.Size.X, loaded.Bounds.Size.Y, loaded.Bounds.Size.Z },
        };
        if (options.TryGetValue("report", out var reportPath))
            WriteReport(reportPath, report);
        GD.Print(
            $"OPENNV_GODOT_STATIC_MODEL_PASS source={loaded.SourceSha256} " +
            $"meshes={loaded.Meshes} surfaces={loaded.Surfaces} vertices={loaded.Vertices} " +
            $"materials={loaded.MaterialBindings} projection={loaded.Projection}");
        if (options.TryGetValue("capture-root", out var captureRoot))
        {
            _ = StaticModelCapture.Run(
                this,
                loaded,
                modelPath,
                captureRoot,
                options.TryGetValue("report", out var captureReport) ? captureReport : null);
            return;
        }
        if (options.ContainsKey("quit-after-load"))
            GetTree().Quit(0);
    }

    private void LoadStaticCellCompile(
        string compilePath,
        IReadOnlyDictionary<string, string> options)
    {
        var loaded = StaticCellCompileLoader.Load(
            compilePath,
            this,
            _configuration,
            !options.ContainsKey("no-collision"));
        var report = new
        {
            schema = "opennv-godot-static-cell-runtime/v2",
            status = "pass",
            scope = "compiled-static-presentation",
            playable = false,
            parity = false,
            configurationSchema = RuntimeConfiguration.ExpectedSchema,
            configurationSha256 = _configuration.Sha256,
            manifest = loaded.ManifestPath,
            manifestSha256 = loaded.ManifestSha256,
            cellFormKey = loaded.FormKey,
            cellEditorId = loaded.EditorId,
            assets = loaded.Assets,
            textures = loaded.Textures,
            materialBindings = loaded.MaterialBindings,
            placements = loaded.Placements,
            authoredLights = loaded.AuthoredLights,
            authoredLandscapes = loaded.AuthoredLandscapes,
            collisionMeshes = loaded.CollisionMeshes,
            surfaces = loaded.Surfaces,
            vertices = loaded.Vertices,
        };
        if (options.TryGetValue("report", out var reportPath))
            WriteReport(reportPath, report);
        GD.Print(
            $"OPENNV_GODOT_STATIC_CELL_PASS cell={loaded.FormKey} assets={loaded.Assets} " +
            $"textures={loaded.Textures} materials={loaded.MaterialBindings} " +
            $"placements={loaded.Placements} lights={loaded.AuthoredLights} " +
            $"landscapes={loaded.AuthoredLandscapes} " +
            $"collision={loaded.CollisionMeshes} " +
            $"surfaces={loaded.Surfaces} vertices={loaded.Vertices}");
        if (options.ContainsKey("quit-after-load"))
            GetTree().Quit(0);
    }

    private void LoadActorModel(
        string modelPath,
        string sidecarPath,
        IReadOnlyDictionary<string, string> options)
    {
        var loaded = ActorModelSlice.Load(modelPath, sidecarPath, this, _configuration);
        var report = new
        {
            schema = "opennv-godot-actor/v1",
            status = "pass",
            renderer = "forward_plus",
            model = modelPath,
            sidecar = sidecarPath,
            actorFormId = loaded.FormId,
            actorName = loaded.Name,
            meshes = loaded.Meshes,
            skeletons = loaded.Skeletons,
            animations = loaded.Animations,
            playingAnimation = loaded.PlayingAnimation,
            boundsMinimum = new[] { loaded.Bounds.Position.X, loaded.Bounds.Position.Y, loaded.Bounds.Position.Z },
            boundsSize = new[] { loaded.Bounds.Size.X, loaded.Bounds.Size.Y, loaded.Bounds.Size.Z },
            heightMeters = loaded.Bounds.Size.Y,
            authoredSurfaces = loaded.AuthoredSurfaces,
            authoredTextures = loaded.AuthoredTextures,
        };
        if (options.TryGetValue("report", out var reportPath))
            WriteReport(reportPath, report);
        GD.Print(
            $"OPENNV_GODOT_ACTOR_PASS form={loaded.FormId} meshes={loaded.Meshes} " +
            $"skeletons={loaded.Skeletons} animations={loaded.Animations} playing={loaded.PlayingAnimation}");
        if (options.ContainsKey("quit-after-load"))
            GetTree().Quit(0);
    }

    private void LoadActorReviewScene(
        string scenePath,
        IReadOnlyDictionary<string, string> options)
    {
        var scene = ActorReviewScene.Load(scenePath, _configuration);
        var background = options.TryGetValue("actor-review-background-cell", out var backgroundPath)
            ? ActorReviewBackground.Load(backgroundPath, this, _configuration)
            : null;
        var actor = ActorModelSlice.Load(
            scene.ModelPath,
            scene.SidecarPath,
            this,
            _configuration,
            boundsContract: ActorModelSlice.BoundsContract.AnyActor);
        if (!actor.FormId.Equals(scene.ReviewKey, StringComparison.Ordinal))
            throw new InvalidOperationException(
                "Actor review scene and compiled actor identify different review outcomes.");
        _ = ActorReviewCapture.Run(
            this,
            actor,
            _configuration,
            scene.RetailContractPath,
            RequireOption(options, "capture-root"),
            options.TryGetValue("report", out var report) ? report : null,
            background);
    }

    private void ShowExperimentalStatus(string? restoreError)
    {
        _setupView = new LegalAssetSetupView();
        _setupView.Configure(restoreError, OnDataRootSelected, _configuration.SetupView);
        AddChild(_setupView);
    }

    private async void OnDataRootSelected(string dataRoot)
    {
        _setupView!.SetPreparing();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        PrepareSelectedData(dataRoot);
    }

    private void PrepareSelectedData(string dataRoot)
    {
        try
        {
            var prepared = LegalAssetPreparer.Prepare(dataRoot, _options, _configuration);
            LoadPrepared(prepared, _options);
            _setupView?.QueueFree();
            _setupView = null;
        }
        catch (Exception exception)
        {
            _setupView!.ShowError(exception.Message);
            GD.PushError($"OPENNV_LEGAL_ASSET_SETUP_FAIL {exception.Message}");
        }
    }

    private void WriteStartupReport(string reportPath)
    {
        WriteReport(reportPath, new
        {
            schema = "opennv-godot-startup/v1",
            status = "experimental",
            playable = false,
            playableSandbox = true,
            openXrLaunchable = true,
            openXrHardwareValidated = false,
            engine = Engine.GetVersionInfo()["string"].AsString(),
            renderer = "forward_plus",
            configurationSchema = RuntimeConfiguration.ExpectedSchema,
            configurationSha256 = _configuration.Sha256,
        });
    }

    internal static void WriteReport(string reportPath, object report)
    {
        var fullReportPath = Path.GetFullPath(reportPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullReportPath)!);
        File.WriteAllText(fullReportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions
        {
            WriteIndented = true,
        }) + System.Environment.NewLine);
    }

    private static Dictionary<string, string> ParseOptions(string[] arguments)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < arguments.Length; index++)
        {
            var argument = arguments[index];
            if (!argument.StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException($"Unexpected runtime argument: {argument}");
            var name = argument[2..];
            var value = index + 1 < arguments.Length && !arguments[index + 1].StartsWith("--", StringComparison.Ordinal)
                ? arguments[++index]
                : "true";
            result.Add(name, value);
        }
        return result;
    }

    internal static string RequireOption(IReadOnlyDictionary<string, string> options, string name) =>
        options.TryGetValue(name, out var value)
            ? value
            : throw new ArgumentException($"Missing required --{name} option.");

    private static float[] Vector(Vector3 value) => [value.X, value.Y, value.Z];

    private readonly record struct DoorTraversalProof(
        bool FloorHit,
        float FloorY,
        string FloorCollider,
        bool ClosedHit,
        bool ClosedHitDoor,
        bool OpenHit,
        bool OpenBlockedByPortalDoor,
        bool ProjectilePortalClear,
        bool CapsuleWalkForward,
        bool CapsuleWalkBackward,
        bool CapsuleWalkThrough,
        int LinkedCells,
        float? MaximumPortalAlignmentErrorMeters);
}
