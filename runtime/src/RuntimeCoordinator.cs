using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using OpenNV.Runtime.Diagnostics.Performance;
using OpenNV.Runtime.World.Interactions;
using OpenNV.Runtime.World.Portals;
using OpenNV.Runtime.Campaigns.TTW;

namespace OpenNV.Runtime;

internal static class RuntimeCoordinatorNumericContracts
{
    // Immutable format, source-art, geometry, and acceptance contracts.
    // Runtime-tunable Fallout 1 behavior remains in the versioned runtime recipe.
    internal const double PresentationDouble1000Point0 = 1000.0;
}

public partial class RuntimeCoordinator : Node3D
{
    private const string DefaultNewVegasOpeningSavePath =
        "user://saves/new-vegas-opening-v1.json";

    private static readonly HashSet<string> DirectPreparedContentOptions = new(
        new[]
        {
            "capture-root",
            "cell-recipe",
            "flat-controls-proof",
            "gameplay-proof",
            "gameplay-reload-proof",
            "new-game",
            "opening-character-video",
            "opening-proof",
            "open-proof-door",
            "pipboy-visual-proof",
            "pipboy-screenshot",
            "pool-proof",
            "portal-proof",
            "quit-after-load",
            "report",
            "route-travel-proof",
            "xr-simulator-proof",
            "world-interaction-proof",
        },
        StringComparer.OrdinalIgnoreCase);

    private Dictionary<string, string> _options = new(StringComparer.OrdinalIgnoreCase);
    private RuntimeConfiguration _configuration = null!;
    private LegalAssetSetupView? _setupView;
    private XRInterface? _openXr;
    private LoadingScreen? _loadingScreen;
    private string? _acceptedOpeningMenuAction;
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
            {
                var presentation = _options.TryGetValue("fo1-start-presentation", out var selectedPresentation)
                    ? selectedPresentation.Replace('-', ' ').ToUpperInvariant()
                    : "V13ENT";
                _loadingScreen?.SetTitle($"FALLOUT 1  //  {presentation}");
            }
            if (_options.ContainsKey("fo1-campaign-transport"))
                _loadingScreen?.SetTitle("FALLOUT 1  //  VERIFYING ALL MAPS");
            if (_options.ContainsKey("fo1-campaign-presentation"))
                _loadingScreen?.SetTitle("FALLOUT 1  //  VERIFYING CAMPAIGN ART");
            if (_options.ContainsKey("fo2-temple-cache"))
                _loadingScreen?.SetTitle("FALLOUT 2  //  VERIFYING TEMPLE MAP 126");
            if (_options.ContainsKey("fo3-profile"))
                _loadingScreen?.SetTitle("FALLOUT 3  //  CG00 CHARACTER SELECTION");
            if (_options.ContainsKey("ttw-fo3-opening-profile"))
                _loadingScreen?.SetTitle("TTW  //  VERIFYING CG00 TO CG01 STATE");
            if (_options.ContainsKey("xr-simulator-proof") &&
                (!_options.ContainsKey("vr") || !_options.ContainsKey("report")))
                throw new ArgumentException("--xr-simulator-proof requires --vr and --report.");
            if (_options.ContainsKey("flat-controls-proof") &&
                (_options.ContainsKey("vr") || !_options.ContainsKey("report") ||
                    !_options.ContainsKey("save-path")))
                throw new ArgumentException(
                    "--flat-controls-proof requires --report and --save-path and cannot use --vr.");
            if (_options.ContainsKey("pipboy-screenshot") &&
                !_options.ContainsKey("flat-controls-proof") &&
                !_options.ContainsKey("pipboy-visual-proof"))
                throw new ArgumentException(
                    "--pipboy-screenshot requires --flat-controls-proof or --pipboy-visual-proof.");
            if (_options.ContainsKey("pipboy-visual-proof") &&
                (_options.ContainsKey("vr") || !_options.ContainsKey("report") ||
                    !_options.ContainsKey("save-path") ||
                    !_options.ContainsKey("pipboy-screenshot")))
                throw new ArgumentException(
                    "--pipboy-visual-proof requires --report, --save-path, and " +
                    "--pipboy-screenshot and cannot use --vr.");
            var startsFromMenuNewGame =
                _options.TryGetValue("opening-menu-proof", out var configuredMenuProof) &&
                configuredMenuProof == "new-game";
            if (_options.TryGetValue("opening-proof", out var openingProofMode))
            {
                if (!_options.ContainsKey("report") || !_options.ContainsKey("save-path") ||
                    !_options.ContainsKey("opening-proof-name") ||
                    !_options.ContainsKey("opening-proof-timeout-seconds") ||
                    openingProofMode is not "checkpoint" and not "creator" and not "resume" ||
                    (openingProofMode is "checkpoint" or "creator") !=
                        (_options.ContainsKey("new-game") || startsFromMenuNewGame))
                    throw new ArgumentException(
                        "--opening-proof requires mode checkpoint or creator with --new-game or the owned " +
                        "menu new-game route, or mode resume without either, plus --report, " +
                        "--save-path, --opening-proof-name, and --opening-proof-timeout-seconds.");
            }
            if (_options.ContainsKey("opening-character-video") &&
                (!_options.ContainsKey("new-game") || !_options.ContainsKey("save-path")))
                throw new ArgumentException(
                    "--opening-character-video requires --new-game and an isolated --save-path.");
            if (_options.TryGetValue("opening-menu-proof", out var openingMenuAction))
            {
                var sharedMenuProofInvalid =
                    !_options.ContainsKey("report") ||
                    !_options.ContainsKey("save-path") ||
                    _options.ContainsKey("cell-scene") ||
                    _options.ContainsKey("portal-proof");
                var validContinue = openingMenuAction == "continue" &&
                    !_options.ContainsKey("new-game") &&
                    !_options.ContainsKey("opening-proof");
                var validNewGame = openingMenuAction == "new-game" &&
                    !_options.ContainsKey("new-game") &&
                    _options.TryGetValue("opening-proof", out var menuOpeningProof) &&
                    menuOpeningProof == "checkpoint";
                if (sharedMenuProofInvalid || (!validContinue && !validNewGame))
                    throw new ArgumentException(
                        "--opening-menu-proof accepts continue for a completed prepared save, " +
                        "or new-game with --opening-proof checkpoint; both require --report and " +
                        "--save-path and cannot combine with a direct CELL or portal proof.");
            }
            if (_options.TryGetValue("route-travel-proof", out var routeTravelMode) &&
                (routeTravelMode is not "first-run" and not "cold-reload" ||
                    !_options.TryGetValue("opening-menu-proof", out var routeMenuAction) ||
                    routeMenuAction != "continue" ||
                    !_options.ContainsKey("report") ||
                    !_options.ContainsKey("save-path") ||
                    _options.ContainsKey("vr")))
                throw new ArgumentException(
                    "--route-travel-proof first-run|cold-reload requires the owned " +
                    "--opening-menu-proof continue path, --report, and --save-path, and cannot use --vr.");
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
            if (_options.ContainsKey("fo1-destination-presentation") &&
                (!_options.ContainsKey("fo1-hex-scene") || !_options.ContainsKey("fo1-exit-grid-transition")))
                throw new ArgumentException(
                    "--fo1-destination-presentation requires --fo1-hex-scene and --fo1-exit-grid-transition.");
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
            var hasFo2TemplePresentation = _options.ContainsKey("fo2-temple-cache");
            var hasFo3Profile = _options.ContainsKey("fo3-profile");
            var hasTtwFo3OpeningProfile = _options.ContainsKey("ttw-fo3-opening-profile");
            var hasPreparedCache = _options.ContainsKey("reuse-cache");
            if (_options.ContainsKey("fo3-birth-presentation") && !hasFo3Profile)
                throw new ArgumentException(
                    "--fo3-birth-presentation requires --fo3-profile.");
            if (_options.ContainsKey("fo3-character-video") &&
                (!hasFo3Profile || !_options.ContainsKey("fo3-birth-presentation") ||
                    !_options.ContainsKey("save-path") ||
                    _options.ContainsKey("fo3-appearance-proof") ||
                    _options.ContainsKey("fo3-cg01-proof")))
                throw new ArgumentException(
                    "--fo3-character-video requires --fo3-profile, --fo3-birth-presentation, " +
                    "and an isolated --save-path; it cannot combine with acceptance modes.");
            if (_options.ContainsKey("fo3-retail-cg00-stage10-contract") && !hasFo3Profile)
                throw new ArgumentException(
                    "--fo3-retail-cg00-stage10-contract requires --fo3-profile.");
            if (_options.ContainsKey("fo3-ttw-cg00-stage10-presentation-contract") &&
                !hasFo3Profile)
                throw new ArgumentException(
                    "--fo3-ttw-cg00-stage10-presentation-contract requires --fo3-profile.");
            if (_options.ContainsKey("fo3-ttw-cg00-stage10-actor-set") &&
                !hasFo3Profile)
                throw new ArgumentException(
                    "--fo3-ttw-cg00-stage10-actor-set requires --fo3-profile.");
            if (hasTtwFo3OpeningProfile &&
                (!_options.TryGetValue("ttw-fo3-opening-proof", out var ttwProofMode) ||
                 ttwProofMode is not "apply" and not "restore" ||
                 !_options.ContainsKey("save-path") ||
                 !_options.ContainsKey("report")))
                throw new ArgumentException(
                    "--ttw-fo3-opening-profile requires --ttw-fo3-opening-proof " +
                    "apply|restore, --save-path, and --report.");
            if (_options.ContainsKey("ttw-fo3-opening-proof") && !hasTtwFo3OpeningProfile)
                throw new ArgumentException(
                    "--ttw-fo3-opening-proof requires --ttw-fo3-opening-profile.");
            var hasJamProfile = _options.ContainsKey("jam-profile");
            if (hasJamProfile && !hasDataRoot && !hasCellScene && !hasPreparedCache)
                throw new ArgumentException(
                    "--jam-profile requires --data-root, --cell-scene, or --reuse-cache.");
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
            if (hasFo2TemplePresentation &&
                (!_options.ContainsKey("fo2-temple-build-proof") ||
                    !_options.ContainsKey("report")))
                throw new ArgumentException(
                    "--fo2-temple-cache requires --fo2-temple-build-proof and --report.");
            if (_options.ContainsKey("fo2-temple-build-proof") && !hasFo2TemplePresentation)
                throw new ArgumentException(
                    "--fo2-temple-build-proof requires --fo2-temple-cache.");
            if (_options.ContainsKey("fo2-temple-transitions") && !hasFo2TemplePresentation)
                throw new ArgumentException(
                    "--fo2-temple-transitions requires --fo2-temple-cache.");
            if ((hasDataRoot ? 1 : 0) + (hasModel ? 1 : 0) + (hasCellScene ? 1 : 0) +
                    (hasStaticCellCompile ? 1 : 0) + (hasActorModel ? 1 : 0) +
                    (hasActorReviewScene ? 1 : 0) + (hasFo1HexScene ? 1 : 0) +
                    (hasFo1Campaign ? 1 : 0) + (hasFo1CampaignPresentation ? 1 : 0) +
                    (hasFo2TemplePresentation ? 1 : 0) + (hasFo3Profile ? 1 : 0) +
                    (hasTtwFo3OpeningProfile ? 1 : 0) > 1)
                throw new ArgumentException(
                    "Use only one of --data-root, --model/--sidecar, --cell-scene, " +
                    "--static-cell-compile, --actor-model/--actor-sidecar, " +
                    "--actor-review-scene, --fo1-hex-scene, --fo1-campaign-transport, or " +
                    "--fo1-campaign-presentation, --fo2-temple-cache, --fo3-profile, or " +
                    "--ttw-fo3-opening-profile.");
            var startsFo1NewGame = _options.ContainsKey("fo1-new-game") ||
                _options.ContainsKey("fo1-new-game-demo") ||
                _options.ContainsKey("fo1-character-video");
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
            if (_options.TryGetValue("fo1-character-video", out var fo1VideoCharacter) &&
                fo1VideoCharacter is not "max-stone" and not "natalia" and not "albert" and
                    not "custom-male" and not "custom-female")
                throw new ArgumentException(
                    "--fo1-character-video requires max-stone, natalia, albert, custom-male, or custom-female.");
            if (_options.ContainsKey("fo1-native-first-beat-proof") &&
                !_options.ContainsKey("fo1-new-game-demo"))
                throw new ArgumentException(
                    "--fo1-native-first-beat-proof requires --fo1-new-game-demo.");
            if (_options.ContainsKey("fo1-native-first-beat-proof") &&
                _options.ContainsKey("capture-root"))
                throw new ArgumentException(
                    "--fo1-native-first-beat-proof is JSON-only and cannot use --capture-root.");
            if (_options.ContainsKey("fo1-continue-menu-proof") &&
                (!_options.ContainsKey("fo1-new-game") ||
                    _options.ContainsKey("fo1-new-game-demo") ||
                    !_options.ContainsKey("report") ||
                    !_options.ContainsKey("save-path") ||
                    !_options.ContainsKey("fo1-exit-grid-transition") ||
                    !_options.ContainsKey("fo1-destination-presentation") ||
                    _options.ContainsKey("capture-root")))
                throw new ArgumentException(
                    "--fo1-continue-menu-proof requires the normal FO1 new-game menu route plus explicit " +
                    "save, exit-grid, destination presentation, and report paths; it cannot capture media.");
            if (_options.ContainsKey("fo1-continue-flare-use-proof") &&
                (!_options.ContainsKey("fo1-continue-menu-proof") ||
                    !_options.ContainsKey("fo1-destination-inventory-interaction") ||
                    !_options.ContainsKey("fo1-destination-flare-use")))
                throw new ArgumentException(
                    "--fo1-continue-flare-use-proof requires the normal Continue proof and explicit inventory/flare descriptors.");
            if (_options.ContainsKey("fo1-continue-generic-door-proof") &&
                (!_options.ContainsKey("fo1-continue-menu-proof") ||
                    !_options.ContainsKey("fo1-continue-flare-use-proof") ||
                    !_options.ContainsKey("fo1-destination-generic-door")))
                throw new ArgumentException(
                    "--fo1-continue-generic-door-proof requires normal Continue, restored flare, and an explicit generic-door descriptor.");
            if (_options.ContainsKey("fo1-destination-cold-restore-proof") &&
                (!hasFo1HexScene || !_options.ContainsKey("report") ||
                    !_options.ContainsKey("save-path") ||
                    !_options.ContainsKey("fo1-exit-grid-transition") ||
                    !_options.ContainsKey("fo1-destination-presentation") ||
                    _options.ContainsKey("fo1-new-game") ||
                    _options.ContainsKey("fo1-new-game-demo") ||
                    _options.ContainsKey("capture-root")))
                throw new ArgumentException(
                    "--fo1-destination-cold-restore-proof requires the explicit FO1 scene, save, " +
                    "exit-grid, destination presentation, and report paths; it cannot start a new game or capture media.");
            if ((_options.ContainsKey("fo1-destination-inventory-interaction-proof") ||
                    _options.ContainsKey("fo1-destination-inventory-interaction-cold-restore-proof")) &&
                (!hasFo1HexScene || !_options.ContainsKey("report") || !_options.ContainsKey("save-path") ||
                    !_options.ContainsKey("fo1-exit-grid-transition") || !_options.ContainsKey("fo1-destination-presentation") ||
                    !_options.ContainsKey("fo1-destination-inventory-interaction") ||
                    _options.ContainsKey("fo1-new-game") || _options.ContainsKey("fo1-new-game-demo") ||
                    _options.ContainsKey("capture-root")))
                throw new ArgumentException(
                    "Fallout destination inventory proof requires explicit scene, save, transition, presentation, and interaction paths; it cannot capture media.");
            if ((_options.ContainsKey("fo1-destination-medic-look-proof") ||
                    _options.ContainsKey("fo1-destination-medic-look-cold-restore-proof")) &&
                (!hasFo1HexScene || !_options.ContainsKey("report") || !_options.ContainsKey("save-path") ||
                    !_options.ContainsKey("fo1-exit-grid-transition") || !_options.ContainsKey("fo1-destination-presentation") ||
                    !_options.ContainsKey("fo1-destination-generic-door") ||
                    !_options.ContainsKey("fo1-destination-medic-look") ||
                    _options.ContainsKey("fo1-new-game") || _options.ContainsKey("fo1-new-game-demo") ||
                    _options.ContainsKey("capture-root")))
                throw new ArgumentException(
                    "Fallout destination Medic proof requires explicit scene, saved generic-door, transition, presentation, and Medic descriptors; it cannot capture media.");
            if ((_options.ContainsKey("fo1-destination-return-exit-proof") ||
                    _options.ContainsKey("fo1-destination-return-exit-cold-restore-proof")) &&
                (!hasFo1HexScene || !_options.ContainsKey("report") || !_options.ContainsKey("save-path") ||
                    !_options.ContainsKey("fo1-exit-grid-transition") || !_options.ContainsKey("fo1-destination-presentation") ||
                    !_options.ContainsKey("fo1-destination-generic-door") || !_options.ContainsKey("fo1-destination-medic-look") ||
                    !_options.ContainsKey("fo1-destination-return-exit-grid") || _options.ContainsKey("capture-root")))
                throw new ArgumentException("Fallout destination return exit proof requires explicit source-bound predecessor descriptors and cannot capture media.");
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

            if (hasFo2TemplePresentation)
            {
                SetLoadingStatus("VERIFYING MAP 126 SOURCE AND PNG HASHES");
                LoadFo2TemplePresentation(
                    RequireOption(_options, "fo2-temple-cache"),
                    RequireOption(_options, "report"),
                    _options.TryGetValue("fo2-temple-transitions", out var transitions)
                        ? transitions
                        : null);
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

            if (hasTtwFo3OpeningProfile)
            {
                SetLoadingStatus("APPLYING ISOLATED TTW CG00 TO CG01 STATE");
                LoadTtwFo3Opening(
                    RequireOption(_options, "ttw-fo3-opening-profile"),
                    _options);
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

    internal async Task WaitForLoadingScreenDismissal()
    {
        while (GetNodeOrNull<LoadingScreen>("OwnedDataLoadingScreen") is { } loading &&
               GodotObject.IsInstanceValid(loading) &&
               !loading.IsQueuedForDeletion())
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
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
        LoadPreparedGameplay(
            prepared,
            options,
            options.ContainsKey("new-game") || options.ContainsKey("opening-proof"));
    }

    private void ShowOpening(
        LegalAssetPreparer.PreparedContent prepared,
        IReadOnlyDictionary<string, string> options)
    {
        var manifest = OpeningManifest.Load(prepared.OpeningManifestPath, _configuration);
        var savePath = options.TryGetValue("save-path", out var configuredSavePath)
            ? ResolveRuntimePath(configuredSavePath)
            : ResolveRuntimePath(DefaultNewVegasOpeningSavePath);
        var expectedCellFormId = ReadPreparedCellFormId(prepared.CellScenePath);
        var allowedActiveCellFormIds = ReadPreparedCellFormIds(prepared.CellScenePath);
        var canContinue = GameplaySession.CanContinueOpening(
            savePath,
            expectedCellFormId,
            allowedActiveCellFormIds,
            manifest.NewGameFlow.Character.Vitals,
            state => OpeningQuestRuntime.MatchesFlow(manifest.NewGameFlow, state));
        var opening = new RetailOpening();
        AddChild(opening);
        opening.Configure(
            manifest,
            canContinue,
            _configuration.Player.DesktopInput.Cancel.Action,
            () =>
            {
                if (options.TryGetValue("opening-menu-proof", out var acceptedAction))
                {
                    if (acceptedAction != "new-game")
                        throw new InvalidOperationException(
                            $"Owned main-menu new-game dispatched while expecting {acceptedAction}.");
                    _acceptedOpeningMenuAction = acceptedAction;
                }
                var newGameOptions = options.ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value,
                    StringComparer.OrdinalIgnoreCase);
                newGameOptions["new-game"] = "";
                LoadPreparedGameplay(prepared, newGameOptions, useOpeningCampaign: true);
            },
            action =>
            {
                if (action is "continue" or "load")
                {
                    if (options.TryGetValue("opening-menu-proof", out var acceptedAction))
                    {
                        if (!action.Equals(acceptedAction, StringComparison.Ordinal))
                            throw new InvalidOperationException(
                                $"Owned main-menu acceptance dispatched {action}, expected {acceptedAction}.");
                        _acceptedOpeningMenuAction = action;
                    }
                    opening.QueueFree();
                    LoadPreparedGameplay(prepared, options, useOpeningCampaign: true);
                    return;
                }
                GD.Print($"OPENNV_OWNED_MENU_ACTION action={action} status=ui-route-pending");
            });
        GD.Print(
            $"OPENNV_OWNED_OPENING_READY campaign={manifest.Campaign} " +
            $"quest={manifest.EntryQuestEditorId} stage={manifest.EntryStage} " +
            $"buttons={manifest.Buttons.Count} continue={canContinue}");
        if (options.TryGetValue("opening-menu-proof", out var action))
        {
            if (action == "continue" && !canContinue)
                throw new InvalidOperationException(
                    "Owned main-menu Continue acceptance requires a valid completed campaign save.");
            _ = RunOwnedOpeningMenuAcceptance(opening, action);
        }
        if (options.ContainsKey("quit-after-load"))
            GetTree().Quit(0);
    }

    private async Task RunOwnedOpeningMenuAcceptance(RetailOpening opening, string action)
    {
        try
        {
            await ToSignal(GetTree().CreateTimer(2.0), SceneTreeTimer.SignalName.Timeout);
            GD.Print(
                $"OPENNV_OWNED_MENU_ACCEPTANCE action={action} transport=godot-button-signal");
            opening.PressActionForAcceptance(action);
            if (action != "new-game")
                return;
            await ToSignal(GetTree().CreateTimer(2.0), SceneTreeTimer.SignalName.Timeout);
            Input.ParseInputEvent(new InputEventKey
            {
                Keycode = Key.Escape,
                PhysicalKeycode = Key.Escape,
                Pressed = true,
            });
            GD.Print("OPENNV_OWNED_INTRO_ACCEPTANCE action=escape transport=godot-input-event");
        }
        catch (Exception exception)
        {
            GD.PushError($"OPENNV_OWNED_MENU_ACCEPTANCE_FAIL {exception}");
            GetTree().Quit(1);
        }
    }

    private void LoadPreparedGameplay(
        LegalAssetPreparer.PreparedContent prepared,
        IReadOnlyDictionary<string, string> options,
        bool useOpeningCampaign)
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
            if (useOpeningCampaign && !preparedOptions.ContainsKey("opening-manifest"))
                preparedOptions["opening-manifest"] = prepared.OpeningManifestPath;
            if (useOpeningCampaign && !preparedOptions.ContainsKey("save-path"))
                preparedOptions["save-path"] = DefaultNewVegasOpeningSavePath;
            LoadCellScene(prepared.CellScenePath, preparedOptions);
        }
        else
            LoadModel(prepared.ModelPath, prepared.SidecarPath, options);
    }

    private static bool ShouldShowOpening(IReadOnlyDictionary<string, string> options) =>
        options.ContainsKey("opening-menu") ||
        options.ContainsKey("opening-menu-proof") ||
        !options.Keys.Any(DirectPreparedContentOptions.Contains);

    private static string ResolveRuntimePath(string path) =>
        path.StartsWith("res://", StringComparison.Ordinal) ||
        path.StartsWith("user://", StringComparison.Ordinal)
            ? ProjectSettings.GlobalizePath(path)
            : Path.GetFullPath(path);

    private static string ReadPreparedCellFormId(string? scenePath)
    {
        if (scenePath is null)
            throw new InvalidOperationException(
                "Owned opening menu requires a prepared campaign CELL scene.");
        using var document = JsonDocument.Parse(File.ReadAllText(scenePath));
        var formId = document.RootElement
            .GetProperty("cell")
            .GetProperty("formId")
            .GetString();
        return string.IsNullOrWhiteSpace(formId)
            ? throw new InvalidOperationException("Prepared campaign CELL has no FormID.")
            : formId;
    }

    private static IReadOnlySet<string> ReadPreparedCellFormIds(string? scenePath)
    {
        if (scenePath is null)
            throw new InvalidOperationException(
                "Owned opening menu requires a prepared campaign CELL scene.");
        using var document = JsonDocument.Parse(File.ReadAllText(scenePath));
        var root = document.RootElement;
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            root.GetProperty("cell").GetProperty("formId").GetString()
                ?? throw new InvalidOperationException("Prepared campaign CELL has no FormID."),
        };
        if (root.TryGetProperty("linkedCells", out var linkedCells))
            foreach (var linked in linkedCells.EnumerateArray())
                result.Add(
                    linked.GetProperty("cellFormId").GetString()
                    ?? throw new InvalidOperationException("Prepared linked CELL has no FormID."));
        return result;
    }

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
        var birthPresentation = options.TryGetValue(
                "fo3-birth-presentation",
                out var configuredBirthPresentation)
            ? Fo3Vault101BirthPresentationContract.Load(
                profile.BirthSlice,
                profile.Cg01Stage0Transition,
                profile.Stage65Appearance,
                profile.Cg01Stage10Transition,
                profile.Cg01Stage12DadResponse,
                ResolveRuntimePath(configuredBirthPresentation))
            : null;
        var savePath = options.TryGetValue("save-path", out var configuredSavePath)
            ? ResolveRuntimePath(configuredSavePath)
            : ResolveRuntimePath("user://profiles/fallout3/cg00-character-v2.json");
        var cg01ProofMode = options.TryGetValue("fo3-cg01-proof", out var configuredCg01Proof)
            ? configuredCg01Proof
            : null;
        var cg01CapturePath = options.TryGetValue(
                "fo3-cg01-capture",
                out var configuredCg01Capture)
            ? ResolveRuntimePath(configuredCg01Capture)
            : null;
        var appearanceProofMode = options.TryGetValue(
                "fo3-appearance-proof",
                out var configuredAppearanceProof)
            ? configuredAppearanceProof
            : null;
        var appearanceCaptureRoot = options.TryGetValue(
                "fo3-appearance-capture-root",
                out var configuredAppearanceCaptureRoot)
            ? ResolveRuntimePath(configuredAppearanceCaptureRoot)
            : null;
        var retailCg00Stage10Contract = options.TryGetValue(
                "fo3-retail-cg00-stage10-contract",
                out var configuredRetailCg00Stage10Contract)
            ? Fo3Cg00RetailStage10Contract.Load(
                ResolveRuntimePath(configuredRetailCg00Stage10Contract))
            : null;
        var ttwCg00Stage10PresentationContract = options.TryGetValue(
                "fo3-ttw-cg00-stage10-presentation-contract",
                out var configuredTtwCg00Stage10PresentationContract)
            ? Fo3TtwCg00Stage10PresentationContract.Load(
                ResolveRuntimePath(configuredTtwCg00Stage10PresentationContract))
            : null;
        var ttwCg00Stage10SurfaceContract = options.TryGetValue(
                "fo3-ttw-cg00-stage10-surface-contract",
                out var configuredTtwCg00Stage10SurfaceContract)
            ? Fo3TtwCg00Stage10SurfaceContract.Load(
                ResolveRuntimePath(configuredTtwCg00Stage10SurfaceContract))
            : null;
        if ((ttwCg00Stage10PresentationContract is null) !=
            (ttwCg00Stage10SurfaceContract is null))
            throw new ArgumentException(
                "TTW stage-10 presentation and per-surface depth contracts must be paired.");
        if (options.TryGetValue(
                "fo3-ttw-cg00-stage10-actor-set",
                out var configuredTtwCg00Stage10ActorSet))
        {
            if (birthPresentation is null ||
                ttwCg00Stage10PresentationContract is null ||
                ttwCg00Stage10SurfaceContract is null)
                throw new ArgumentException(
                    "TTW stage-10 actor routing requires birth, presentation, and " +
                    "per-surface depth contracts.");
            birthPresentation = TtwFo3Cg00Stage10ActorSetAdapter.Apply(
                birthPresentation,
                ResolveRuntimePath(configuredTtwCg00Stage10ActorSet),
                ttwCg00Stage10SurfaceContract);
        }
        var requiresRetailStage10Contract = appearanceProofMode == "stage10-presentation";
        if (appearanceProofMode is not null &&
            (appearanceProofMode is not "apply" and not "restore" and
             not "early-apply" and not "early-restore" and not "early-presentation" and
             not "stage10-presentation" ||
             birthPresentation is null ||
             !options.ContainsKey("report") ||
             appearanceProofMode is "apply" or "restore" or "early-presentation" or
                 "stage10-presentation" &&
             appearanceCaptureRoot is null))
            throw new ArgumentException(
                "--fo3-appearance-proof requires apply|restore|early-apply|early-restore|" +
                "early-presentation|stage10-presentation, " +
                "--fo3-birth-presentation, --report, and a capture root for visual proofs.");
        if (requiresRetailStage10Contract &&
            retailCg00Stage10Contract is null &&
            ttwCg00Stage10PresentationContract is null)
            throw new ArgumentException(
                "Fallout 3 CG00 visual proof requires " +
                "--fo3-retail-cg00-stage10-contract or " +
                "--fo3-ttw-cg00-stage10-presentation-contract from an exact live observation.");
        if (retailCg00Stage10Contract is not null &&
            ttwCg00Stage10PresentationContract is not null)
            throw new ArgumentException(
                "Fallout 3 stage-10 proof cannot mix standalone and TTW observations.");
        if (appearanceProofMode is "apply" or "restore" or "early-presentation")
            throw new ArgumentException(
                "Fallout 3 creator/birth visual proof is disabled until its exact live " +
                "stage-specific camera/participant contract exists; the CG00 stage-10 " +
                "contract authorizes only stage10-presentation.");
        if (cg01ProofMode is not null &&
            (cg01ProofMode is not "apply" and not "restore" ||
             birthPresentation is null ||
             !options.ContainsKey("report")))
            throw new ArgumentException(
                "--fo3-cg01-proof requires apply|restore, --fo3-birth-presentation, and --report.");
        if (cg01CapturePath is not null && cg01ProofMode != "apply")
            throw new ArgumentException(
                "--fo3-cg01-capture requires --fo3-cg01-proof apply.");
        var opening = new Fo3OpeningFlow();
        opening.Configure(
            profile,
            savePath,
            this,
            _configuration,
            birthPresentation,
            appearanceProofMode,
            appearanceProofMode is null ? null : ResolveRuntimePath(RequireOption(options, "report")),
            appearanceCaptureRoot,
            cg01ProofMode,
            cg01ProofMode is null ? null : ResolveRuntimePath(RequireOption(options, "report")),
            cg01CapturePath,
            retailCg00Stage10Contract,
            ttwCg00Stage10PresentationContract,
            ttwCg00Stage10SurfaceContract,
            options.TryGetValue(
                    "character-reflectron-opening-manifest",
                    out var fo3ReflectronManifest)
                ? OpeningManifest.Load(fo3ReflectronManifest, _configuration)
                : null,
            options.ContainsKey("fo3-character-video"));
        AddChild(opening);
        if (options.ContainsKey("quit-after-load") &&
            !options.ContainsKey("fo3-appearance-proof") &&
            cg01ProofMode is null)
            GetTree().Quit(0);
    }

    private void LoadTtwFo3Opening(
        string profilePath,
        IReadOnlyDictionary<string, string> options)
    {
        var contract = TtwFo3OpeningContract.Load(ResolveRuntimePath(profilePath));
        TtwFo3OpeningProof.Run(
            contract,
            RequireOption(options, "ttw-fo3-opening-proof"),
            ResolveRuntimePath(RequireOption(options, "save-path")),
            ResolveRuntimePath(RequireOption(options, "report")));
        GetTree().Quit(0);
    }

    private void LoadFo2TemplePresentation(
        string cacheManifestPath,
        string reportPath,
        string? transitionManifestPath)
    {
        var catalog = Fo2TemplePresentationCatalog.Load(cacheManifestPath);
        var transitions = transitionManifestPath is null
            ? null
            : Fo2TempleTransitionCatalog.Load(transitionManifestPath, catalog);
        var coverage = Fo2TempleScene.Build(catalog, this);
        _ = Fo2TempleBuildProof.Run(this, coverage, reportPath, transitions);
    }

    private void LoadCellScene(string scenePath, IReadOnlyDictionary<string, string> options)
    {
        var runTraversalProof = options.ContainsKey("portal-proof");
        var useXrLayout = options.ContainsKey("vr") || options.ContainsKey("vr-layout-proof");
        var galleryContract = options.TryGetValue("gallery-shot", out var galleryShotPath)
            ? GalleryShotContract.Load(galleryShotPath, _configuration)
            : null;
        var usesCampaignState = options.ContainsKey("opening-manifest");
        var openingManifest = usesCampaignState
            ? OpeningManifest.Load(
                RequireOption(options, "opening-manifest"),
                _configuration)
            : null;
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
            !options.ContainsKey("capture-root") ||
                options.ContainsKey("gallery-shot") ||
                usesCampaignState && options.ContainsKey("opening-proof"),
            applyCellEnvironment,
            !options.ContainsKey("new-game"),
            true,
            options.ContainsKey("classic-diorama"),
            openingManifest?.GameplayUi,
            openingManifest?.NewGameFlow.Character.Vitals);
        if (options.TryGetValue("jam-profile", out var jamProfilePath))
        {
            var jamProfile = JamProfileContract.Load(jamProfilePath);
            var sprint = JamJvsSprintContract.Load(jamProfile);
            var bulletTime = JamJbtBulletTimeContract.Load(jamProfile);
            DesktopInputMap.ConfigureJamSprint(sprint);
            DesktopInputMap.ConfigureJamBulletTime(bulletTime);
            loaded.Player.ConfigureJamJvsSprint(sprint);
            loaded.Player.ConfigureJamJbtBulletTime(bulletTime);
            GD.Print(
                $"OPENNV_JAM_CAPABILITY id={JamJvsSprintContract.CapabilityId} " +
                $"profile={sprint.ProfileId} key={sprint.DesktopPhysicalKey} " +
                $"speedMultiplier={sprint.SpeedMultiplier:F2} " +
                $"missingDependencies={sprint.MissingDependencyCount} completeJamReady=False");
            GD.Print(
                $"OPENNV_JAM_CAPABILITY id={JamJbtBulletTimeContract.CapabilityId} " +
                $"profile={bulletTime.ProfileId} key={bulletTime.DesktopPhysicalKey} " +
                $"timeMultiplier={bulletTime.EffectiveTimeMultiplier:F2} " +
                $"missingDependencies={bulletTime.MissingDependencyCount} completeJamReady=False");
        }
        var startsNewGame = options.ContainsKey("new-game");
        var restoredOpening = startsNewGame ? null : loaded.Session.OpeningState;
        if (usesCampaignState && !startsNewGame && restoredOpening is null)
            throw new InvalidOperationException(
                "Campaign Continue requires a valid campaign save; choose New Game instead.");
        if (usesCampaignState && restoredOpening is not null &&
            (openingManifest is null ||
             !OpeningQuestRuntime.MatchesFlow(openingManifest.NewGameFlow, restoredOpening) ||
             !loaded.Session.HasConsistentOpeningGameplayState()))
            throw new InvalidOperationException(
                "Campaign Continue save does not match the prepared owned New Game flow.");
        if (usesCampaignState && restoredOpening is { Completed: true } completedOpening)
            OpeningQuestRuntime.ApplyPlayerControlPolicy(
                loaded.Player,
                completedOpening.PlayerControls,
                true);
        if (usesCampaignState)
            loaded.Session.SetGameplayUiVisible(
                restoredOpening is not null &&
                OpeningQuestRuntime.GameplayUiEnabled(restoredOpening));
        OpeningQuestRuntime? openingFlow = null;
        if (startsNewGame || restoredOpening is { Completed: false })
        {
            if (openingManifest is null)
                throw new InvalidOperationException(
                    "Opening campaign state requires an owned opening manifest.");
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
        if (options.ContainsKey("opening-character-video"))
        {
            if (openingFlow is null)
                throw new InvalidOperationException(
                    "Opening character video did not create an active New Vegas opening flow.");
            _ = RunOpeningCharacterVideo(openingFlow, loaded);
            return;
        }
        if (options.TryGetValue("route-travel-proof", out var routeTravelMode))
        {
            _ = RunCellRouteTravelAcceptance(
                loaded,
                scenePath,
                routeTravelMode,
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
        if (options.ContainsKey("pipboy-visual-proof"))
        {
            _ = RunPipBoyVisualAcceptance(loaded, scenePath, options);
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
        if (options.ContainsKey("world-interaction-proof"))
        {
            _ = WorldInteractionProof.Run(
                this,
                loaded,
                _configuration,
                scenePath,
                options.TryGetValue("report", out var worldReport) ? worldReport : null);
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
            renderer = RenderingServer.GetCurrentRenderingMethod().ToString(),
            scene = scenePath,
            cellFormId = loaded.FormId,
            cellEditorId = loaded.EditorId,
            assets = loaded.Assets,
            textures = loaded.Textures,
            materialBindings = loaded.MaterialBindings,
            references = loaded.References,
            doors = loaded.Doors,
            authoredLights = loaded.AuthoredLights,
            actors = loaded.MainContent.Actors.Count,
            actorPlacements = loaded.Actors.Select(actor => new
            {
                referenceFormId = actor.ReferenceFormId,
                baseFormId = actor.BaseFormId,
                initiallyDisabled = actor.InitiallyDisabled,
                proofEnabled = actor.ProofEnabled,
            }).ToArray(),
            openingMenuProof = _acceptedOpeningMenuAction is null
                ? null
                : new
                {
                    action = _acceptedOpeningMenuAction,
                    inputTransport = "godot-owned-button-signal",
                    windowsAppControlUsed = false,
                    foregroundInputInjected = false,
                    restoredStage = loaded.Session.OpeningState?.Stage,
                    restoredCompleted = loaded.Session.OpeningState?.Completed,
                },
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
            activeSet = new
            {
                policy = "authoritative-current-cell-only-linked-cells-preloaded",
                currentCellFormId = loaded.Session.ActiveCellFormId,
                activeCellFormIds = loaded.ActiveSet.ActiveCellFormIds
                    .OrderBy(value => value, StringComparer.OrdinalIgnoreCase),
                spaces = loaded.ActiveSet.Snapshot().Select(space => new
                {
                    cellFormId = space.FormId,
                    space.Active,
                    roots = space.Roots,
                    sourceVisibleRoots = space.SourceVisibleRoots,
                    visibleRoots = space.VisibleRoots,
                    sourceProcessingRoots = space.SourceProcessingRoots,
                    processingRoots = space.ProcessingRoots,
                    collisionObjects = space.CollisionObjects,
                    sourceEnabledCollisionObjects = space.SourceEnabledCollisionObjects,
                    enabledCollisionObjects = space.EnabledCollisionObjects,
                    rigidBodies = space.RigidBodies,
                    sourceFrozenRigidBodies = space.SourceFrozenRigidBodies,
                    frozenRigidBodies = space.FrozenRigidBodies,
                    lights = space.Lights,
                    sourceVisibleLights = space.SourceVisibleLights,
                    visibleLights = space.VisibleLights,
                }),
            },
            actorGrounding = new
            {
                policy = "nearest-authored-collision-current-posed-visual-support",
                results = loaded.ActorGrounding.Results.Select(result => new
                {
                    result.ReferenceFormId,
                    result.CellFormId,
                    rootBefore = Vector(result.RootBefore),
                    rootAfter = Vector(result.RootAfter),
                    result.CorrectionMeters,
                    result.CorrectionGameUnits,
                    groundPosition = Vector(result.GroundPosition),
                    result.ColliderPath,
                    result.Derivation,
                }),
            },
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
            spawnAtFloorOrigin = traversalProof is not null &&
                MathF.Abs(traversalProof.Value.FloorY) <=
                    _configuration.Proof.SpawnFloorToleranceMeters,
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
                    floorNormal = Vector(traversalProof.Value.FloorNormal),
                    floorCollider = traversalProof.Value.FloorCollider,
                    floorOwnedCellCollision = traversalProof.Value.FloorOwnedCellCollision,
                    floorWithinProbe = traversalProof.Value.FloorWithinProbe,
                    floorWalkable = traversalProof.Value.FloorWalkable,
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
                    portals = traversalProof.Value.Portals.Select(portal => new
                    {
                        fromDoorReferenceFormId = portal.FromDoorReferenceFormId,
                        toDoorReferenceFormId = portal.ToDoorReferenceFormId,
                        closedHit = portal.ClosedHit,
                        closedHitDoor = portal.ClosedHitDoor,
                        openBlockedByPortalDoor = portal.OpenBlockedByPortalDoor,
                        openRayPortalClear = portal.OpenRayPortalClear,
                        projectilePortalClear = portal.ProjectilePortalClear,
                        floorHit = portal.FloorHit,
                        floorWalkable = portal.FloorWalkable,
                        floorY = portal.FloorY,
                        capsuleWalkForward = portal.CapsuleWalkForward,
                        capsuleWalkBackward = portal.CapsuleWalkBackward,
                        capsuleWalkThrough = portal.CapsuleWalkThrough,
                    }).ToArray(),
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
        Vector3 FloorNormal,
        string FloorCollider,
        bool FloorOwnedCellCollision,
        bool FloorWithinProbe,
        bool FloorWalkable,
        bool ClosedHit,
        bool ClosedHitDoor,
        bool OpenHit,
        bool OpenBlockedByPortalDoor,
        bool ProjectilePortalClear,
        bool CapsuleWalkForward,
        bool CapsuleWalkBackward,
        bool CapsuleWalkThrough,
        int LinkedCells,
        float? MaximumPortalAlignmentErrorMeters,
        IReadOnlyList<PortalTraversalProof> Portals);

    private readonly record struct PortalTraversalProof(
        string FromDoorReferenceFormId,
        string? ToDoorReferenceFormId,
        bool ClosedHit,
        bool ClosedHitDoor,
        bool OpenBlockedByPortalDoor,
        bool OpenRayPortalClear,
        bool ProjectilePortalClear,
        bool FloorHit,
        bool FloorWalkable,
        float FloorY,
        bool CapsuleWalkForward,
        bool CapsuleWalkBackward,
        bool CapsuleWalkThrough)
    {
        internal bool Passed =>
            ClosedHit &&
            ClosedHitDoor &&
            !OpenBlockedByPortalDoor &&
            OpenRayPortalClear &&
            ProjectilePortalClear &&
            FloorHit &&
            FloorWalkable &&
            CapsuleWalkForward &&
            CapsuleWalkBackward &&
            CapsuleWalkThrough;
    }
}
