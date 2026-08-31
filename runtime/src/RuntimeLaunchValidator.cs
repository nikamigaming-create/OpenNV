namespace OpenNV.Runtime;

/// <summary>
/// Fail-closed relationships between launch switches. Validation is pure: it
/// allocates no Godot nodes, touches no save, and reads no retail content.
/// </summary>
internal static class RuntimeLaunchValidator
{
    internal static void ValidatePreflight(IReadOnlyDictionary<string, string> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.ContainsKey("xr-simulator-proof") &&
            (!options.ContainsKey("vr") || !options.ContainsKey("report")))
            throw new ArgumentException("--xr-simulator-proof requires --vr and --report.");
        if (options.ContainsKey("flat-controls-proof") &&
            (options.ContainsKey("vr") || !options.ContainsKey("report") ||
                !options.ContainsKey("save-path")))
            throw new ArgumentException(
                "--flat-controls-proof requires --report and --save-path and cannot use --vr.");
        if (options.ContainsKey("pipboy-screenshot") &&
            !options.ContainsKey("flat-controls-proof") &&
            !options.ContainsKey("pipboy-visual-proof"))
            throw new ArgumentException(
                "--pipboy-screenshot requires --flat-controls-proof or --pipboy-visual-proof.");
        if (options.ContainsKey("pipboy-visual-proof") &&
            (options.ContainsKey("vr") || !options.ContainsKey("report") ||
                !options.ContainsKey("save-path") ||
                !options.ContainsKey("pipboy-screenshot")))
            throw new ArgumentException(
                "--pipboy-visual-proof requires --report, --save-path, and " +
                "--pipboy-screenshot and cannot use --vr.");
        var startsFromMenuNewGame =
            options.TryGetValue("opening-menu-proof", out var configuredMenuProof) &&
            configuredMenuProof == "new-game";
        if (options.TryGetValue("opening-proof", out var openingProofMode))
        {
            if (!options.ContainsKey("report") || !options.ContainsKey("save-path") ||
                !options.ContainsKey("opening-proof-name") ||
                !options.ContainsKey("opening-proof-timeout-seconds") ||
                openingProofMode is not "checkpoint" and not "creator" and not "resume" ||
                (openingProofMode is "checkpoint" or "creator") !=
                    (options.ContainsKey("new-game") || startsFromMenuNewGame))
                throw new ArgumentException(
                    "--opening-proof requires mode checkpoint or creator with --new-game or the owned " +
                    "menu new-game route, or mode resume without either, plus --report, " +
                    "--save-path, --opening-proof-name, and --opening-proof-timeout-seconds.");
        }
        if (options.ContainsKey("opening-character-video") &&
            (!options.ContainsKey("new-game") || !options.ContainsKey("save-path")))
            throw new ArgumentException(
                "--opening-character-video requires --new-game and an isolated --save-path.");
        if (options.TryGetValue("opening-menu-proof", out var openingMenuAction))
        {
            var sharedMenuProofInvalid =
                !options.ContainsKey("report") ||
                !options.ContainsKey("save-path") ||
                options.ContainsKey("cell-scene") ||
                options.ContainsKey("portal-proof");
            var validContinue = openingMenuAction == "continue" &&
                !options.ContainsKey("new-game") &&
                !options.ContainsKey("opening-proof");
            var validNewGame = openingMenuAction == "new-game" &&
                !options.ContainsKey("new-game") &&
                options.TryGetValue("opening-proof", out var menuOpeningProof) &&
                menuOpeningProof == "checkpoint";
            if (sharedMenuProofInvalid || (!validContinue && !validNewGame))
                throw new ArgumentException(
                    "--opening-menu-proof accepts continue for a completed prepared save, " +
                    "or new-game with --opening-proof checkpoint; both require --report and " +
                    "--save-path and cannot combine with a direct CELL or portal proof.");
        }
        if (options.TryGetValue("route-travel-proof", out var routeTravelMode) &&
            (routeTravelMode is not "first-run" and not "cold-reload" ||
                !options.TryGetValue("opening-menu-proof", out var routeMenuAction) ||
                routeMenuAction != "continue" ||
                !options.ContainsKey("report") ||
                !options.ContainsKey("save-path") ||
                options.ContainsKey("vr")))
            throw new ArgumentException(
                "--route-travel-proof first-run|cold-reload requires the owned " +
                "--opening-menu-proof continue path, --report, and --save-path, and cannot use --vr.");
        if (options.ContainsKey("vr") && options.ContainsKey("xr-rig-proof"))
            throw new ArgumentException("Use --vr for a live OpenXR session or --xr-rig-proof for the headless layout gate, not both.");
        if ((options.ContainsKey("classic-diorama") || options.ContainsKey("classic-diorama-rig-proof")) &&
            (options.ContainsKey("vr") || options.ContainsKey("vr-layout-proof") ||
                options.ContainsKey("xr-rig-proof")))
            throw new ArgumentException("Classic Diorama and OpenXR require separate presentation adapters.");
        if (options.ContainsKey("fo1-hex-scene") && options.ContainsKey("vr") &&
            !options.ContainsKey("fo1-xr-simulator-preview"))
            throw new ArgumentException("The Fallout 1 tactical hex slice has not passed its OpenXR gate.");
        if (options.ContainsKey("fo1-xr-simulator-preview") &&
            (!options.ContainsKey("fo1-hex-scene") || !options.ContainsKey("vr")))
            throw new ArgumentException(
                "The Fallout 1 OpenXR simulator preview requires --fo1-hex-scene and --vr.");
        if (options.ContainsKey("fo1-xr-controls-proof") &&
            (!options.ContainsKey("fo1-xr-simulator-preview") ||
                !options.ContainsKey("report") || !options.ContainsKey("save-path")))
            throw new ArgumentException(
                "The Fallout 1 OpenXR controls proof requires the simulator preview, report, and isolated save path.");
        if (options.ContainsKey("fo1-destination-presentation") &&
            (!options.ContainsKey("fo1-hex-scene") || !options.ContainsKey("fo1-exit-grid-transition")))
            throw new ArgumentException(
                "--fo1-destination-presentation requires --fo1-hex-scene and --fo1-exit-grid-transition.");
    }

    internal static void ValidateContent(
        IReadOnlyDictionary<string, string> options,
        RuntimeLaunchRequest launch)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(launch);
        var hasDataRoot = launch.Is(RuntimeLaunchRoute.OwnedData);
        var hasModel = launch.Is(RuntimeLaunchRoute.Model);
        var hasCellScene = launch.Is(RuntimeLaunchRoute.CellScene);
        var hasStaticCellCompile = launch.Is(RuntimeLaunchRoute.StaticCellCompile);
        var hasActorModel = launch.Is(RuntimeLaunchRoute.ActorModel);
        var hasActorReviewScene = launch.Is(RuntimeLaunchRoute.ActorReviewScene);
        var hasFo1HexScene = launch.Is(RuntimeLaunchRoute.Fallout1HexScene);
        var hasFo1Campaign = launch.Is(RuntimeLaunchRoute.Fallout1CampaignTransport);
        var hasFo1CampaignPresentation = launch.Is(RuntimeLaunchRoute.Fallout1CampaignPresentation);
        var hasFo2TemplePresentation = launch.Is(RuntimeLaunchRoute.Fallout2TemplePresentation);
        var hasFo3Profile = launch.Is(RuntimeLaunchRoute.Fallout3Opening);
        var hasTtwFo3OpeningProfile = launch.Is(RuntimeLaunchRoute.TtwFallout3Opening);
        var hasPreparedCache = launch.Is(RuntimeLaunchRoute.PreparedCache);
        if (options.ContainsKey("fo3-birth-presentation") && !hasFo3Profile)
            throw new ArgumentException(
                "--fo3-birth-presentation requires --fo3-profile.");
        if (options.ContainsKey("fo3-character-video") &&
            (!hasFo3Profile || !options.ContainsKey("fo3-birth-presentation") ||
                !options.ContainsKey("save-path") ||
                options.ContainsKey("fo3-appearance-proof") ||
                options.ContainsKey("fo3-cg01-proof")))
            throw new ArgumentException(
                "--fo3-character-video requires --fo3-profile, --fo3-birth-presentation, " +
                "and an isolated --save-path; it cannot combine with acceptance modes.");
        if (options.ContainsKey("fo3-retail-cg00-stage10-contract") && !hasFo3Profile)
            throw new ArgumentException(
                "--fo3-retail-cg00-stage10-contract requires --fo3-profile.");
        if (options.ContainsKey("fo3-ttw-cg00-stage10-presentation-contract") &&
            !hasFo3Profile)
            throw new ArgumentException(
                "--fo3-ttw-cg00-stage10-presentation-contract requires --fo3-profile.");
        if (options.ContainsKey("fo3-ttw-cg00-stage10-actor-set") &&
            !hasFo3Profile)
            throw new ArgumentException(
                "--fo3-ttw-cg00-stage10-actor-set requires --fo3-profile.");
        if (hasTtwFo3OpeningProfile &&
            (!options.TryGetValue("ttw-fo3-opening-proof", out var ttwProofMode) ||
             ttwProofMode is not "apply" and not "restore" ||
             !options.ContainsKey("save-path") ||
             !options.ContainsKey("report")))
            throw new ArgumentException(
                "--ttw-fo3-opening-profile requires --ttw-fo3-opening-proof " +
                "apply|restore, --save-path, and --report.");
        if (options.ContainsKey("ttw-fo3-opening-proof") && !hasTtwFo3OpeningProfile)
            throw new ArgumentException(
                "--ttw-fo3-opening-proof requires --ttw-fo3-opening-profile.");
        var hasJamProfile = options.ContainsKey("jam-profile");
        if (hasJamProfile && !hasDataRoot && !hasCellScene && !hasPreparedCache)
            throw new ArgumentException(
                "--jam-profile requires --data-root, --cell-scene, or --reuse-cache.");
        if (hasJamProfile &&
            (options.ContainsKey("vr") || options.ContainsKey("vr-layout-proof") ||
                options.ContainsKey("classic-diorama")))
            throw new ArgumentException(
                "The bounded JVS sprint transport currently supports desktop first-person movement only.");
        if ((options.ContainsKey("fo1-map") || options.ContainsKey("fo1-elevation")) &&
            !hasFo1CampaignPresentation)
            throw new ArgumentException(
                "--fo1-map and --fo1-elevation require --fo1-campaign-presentation.");
        if (options.ContainsKey("fo1-campaign-build-proof") &&
            (!hasFo1CampaignPresentation || !options.ContainsKey("report")))
            throw new ArgumentException(
                "--fo1-campaign-build-proof requires --fo1-campaign-presentation and --report.");
        if (options.ContainsKey("fo1-campaign-build-proof") &&
            options.ContainsKey("capture-root"))
            throw new ArgumentException(
                "Fallout campaign headless build proof and visual capture are separate gates.");
        if (hasFo2TemplePresentation &&
            (!options.ContainsKey("fo2-temple-build-proof") ||
                !options.ContainsKey("report")))
            throw new ArgumentException(
                "--fo2-temple-cache requires --fo2-temple-build-proof and --report.");
        if (options.ContainsKey("fo2-temple-build-proof") && !hasFo2TemplePresentation)
            throw new ArgumentException(
                "--fo2-temple-build-proof requires --fo2-temple-cache.");
        if (options.ContainsKey("fo2-temple-transitions") && !hasFo2TemplePresentation)
            throw new ArgumentException(
                "--fo2-temple-transitions requires --fo2-temple-cache.");
        var startsFo1NewGame = options.ContainsKey("fo1-new-game") ||
            options.ContainsKey("fo1-new-game-demo") ||
            options.ContainsKey("fo1-character-video");
        if (startsFo1NewGame && !hasFo1HexScene)
            throw new ArgumentException("Fallout new game requires --fo1-hex-scene.");
        if (startsFo1NewGame &&
            (!options.ContainsKey("fo1-character-start") ||
                !options.ContainsKey("fo1-character-start-sha256")))
            throw new ArgumentException(
                "Fallout new game requires --fo1-character-start and --fo1-character-start-sha256.");
        if (options.TryGetValue("fo1-start-presentation", out var fo1StartPresentation) &&
            (!startsFo1NewGame ||
                fo1StartPresentation is not "hex-tactical" and not "first-person"))
            throw new ArgumentException(
                "--fo1-start-presentation requires Fallout new game and must be hex-tactical or first-person.");
        if (options.ContainsKey("fo1-new-game-demo") && !options.ContainsKey("demo-report"))
            throw new ArgumentException("Fallout new-game demo requires --demo-report.");
        if (options.TryGetValue("fo1-character-video", out var fo1VideoCharacter) &&
            fo1VideoCharacter is not "max-stone" and not "natalia" and not "albert" and
                not "custom-male" and not "custom-female")
            throw new ArgumentException(
                "--fo1-character-video requires max-stone, natalia, albert, custom-male, or custom-female.");
        if (options.ContainsKey("fo1-native-first-beat-proof") &&
            !options.ContainsKey("fo1-new-game-demo"))
            throw new ArgumentException(
                "--fo1-native-first-beat-proof requires --fo1-new-game-demo.");
        if (options.ContainsKey("fo1-native-first-beat-proof") &&
            options.ContainsKey("capture-root"))
            throw new ArgumentException(
                "--fo1-native-first-beat-proof is JSON-only and cannot use --capture-root.");
        if (options.ContainsKey("fo1-continue-menu-proof") &&
            (!options.ContainsKey("fo1-new-game") ||
                options.ContainsKey("fo1-new-game-demo") ||
                !options.ContainsKey("report") ||
                !options.ContainsKey("save-path") ||
                !options.ContainsKey("fo1-exit-grid-transition") ||
                !options.ContainsKey("fo1-destination-presentation") ||
                options.ContainsKey("capture-root")))
            throw new ArgumentException(
                "--fo1-continue-menu-proof requires the normal FO1 new-game menu route plus explicit " +
                "save, exit-grid, destination presentation, and report paths; it cannot capture media.");
        if (options.ContainsKey("fo1-continue-flare-use-proof") &&
            (!options.ContainsKey("fo1-continue-menu-proof") ||
                !options.ContainsKey("fo1-destination-inventory-interaction") ||
                !options.ContainsKey("fo1-destination-flare-use")))
            throw new ArgumentException(
                "--fo1-continue-flare-use-proof requires the normal Continue proof and explicit inventory/flare descriptors.");
        if (options.ContainsKey("fo1-continue-generic-door-proof") &&
            (!options.ContainsKey("fo1-continue-menu-proof") ||
                !options.ContainsKey("fo1-continue-flare-use-proof") ||
                !options.ContainsKey("fo1-destination-generic-door")))
            throw new ArgumentException(
                "--fo1-continue-generic-door-proof requires normal Continue, restored flare, and an explicit generic-door descriptor.");
        if (options.ContainsKey("fo1-destination-cold-restore-proof") &&
            (!hasFo1HexScene || !options.ContainsKey("report") ||
                !options.ContainsKey("save-path") ||
                !options.ContainsKey("fo1-exit-grid-transition") ||
                !options.ContainsKey("fo1-destination-presentation") ||
                options.ContainsKey("fo1-new-game") ||
                options.ContainsKey("fo1-new-game-demo") ||
                options.ContainsKey("capture-root")))
            throw new ArgumentException(
                "--fo1-destination-cold-restore-proof requires the explicit FO1 scene, save, " +
                "exit-grid, destination presentation, and report paths; it cannot start a new game or capture media.");
        if ((options.ContainsKey("fo1-destination-inventory-interaction-proof") ||
                options.ContainsKey("fo1-destination-inventory-interaction-cold-restore-proof")) &&
            (!hasFo1HexScene || !options.ContainsKey("report") || !options.ContainsKey("save-path") ||
                !options.ContainsKey("fo1-exit-grid-transition") || !options.ContainsKey("fo1-destination-presentation") ||
                !options.ContainsKey("fo1-destination-inventory-interaction") ||
                options.ContainsKey("fo1-new-game") || options.ContainsKey("fo1-new-game-demo") ||
                options.ContainsKey("capture-root")))
            throw new ArgumentException(
                "Fallout destination inventory proof requires explicit scene, save, transition, presentation, and interaction paths; it cannot capture media.");
        if ((options.ContainsKey("fo1-destination-medic-look-proof") ||
                options.ContainsKey("fo1-destination-medic-look-cold-restore-proof")) &&
            (!hasFo1HexScene || !options.ContainsKey("report") || !options.ContainsKey("save-path") ||
                !options.ContainsKey("fo1-exit-grid-transition") || !options.ContainsKey("fo1-destination-presentation") ||
                !options.ContainsKey("fo1-destination-generic-door") ||
                !options.ContainsKey("fo1-destination-medic-look") ||
                options.ContainsKey("fo1-new-game") || options.ContainsKey("fo1-new-game-demo") ||
                options.ContainsKey("capture-root")))
            throw new ArgumentException(
                "Fallout destination Medic proof requires explicit scene, saved generic-door, transition, presentation, and Medic descriptors; it cannot capture media.");
        if ((options.ContainsKey("fo1-destination-return-exit-proof") ||
                options.ContainsKey("fo1-destination-return-exit-cold-restore-proof")) &&
            (!hasFo1HexScene || !options.ContainsKey("report") || !options.ContainsKey("save-path") ||
                !options.ContainsKey("fo1-exit-grid-transition") || !options.ContainsKey("fo1-destination-presentation") ||
                !options.ContainsKey("fo1-destination-generic-door") || !options.ContainsKey("fo1-destination-medic-look") ||
                !options.ContainsKey("fo1-destination-return-exit-grid") || options.ContainsKey("capture-root")))
            throw new ArgumentException("Fallout destination return exit proof requires explicit source-bound predecessor descriptors and cannot capture media.");
        if (options.ContainsKey("fo1-new-game-demo") && options.ContainsKey("fo1-gameplay-demo"))
            throw new ArgumentException("Use only one Fallout gameplay demo mode.");
        if (!hasModel && options.ContainsKey("sidecar"))
            throw new ArgumentException("--sidecar requires --model.");
        if (options.ContainsKey("material-manifest") != options.ContainsKey("material-manifest-sha256"))
            throw new ArgumentException("Use --material-manifest together with --material-manifest-sha256.");
        if (!hasModel && options.ContainsKey("material-manifest"))
            throw new ArgumentException("--material-manifest requires --model.");
        if (!hasActorModel && options.ContainsKey("actor-sidecar"))
            throw new ArgumentException("--actor-sidecar requires --actor-model.");
        if (hasActorReviewScene && !options.ContainsKey("capture-root"))
            throw new ArgumentException("--actor-review-scene requires --capture-root.");
        if (options.ContainsKey("actor-review-background-cell") && !hasActorReviewScene)
            throw new ArgumentException(
                "--actor-review-background-cell requires --actor-review-scene.");
        if (options.ContainsKey("actor-scene") && options.ContainsKey("actor-scenes"))
            throw new ArgumentException("Use --actor-scene or --actor-scenes, not both.");
        if (options.ContainsKey("retail-state-contract") &&
            (!hasCellScene || !options.ContainsKey("capture-root") ||
                (!options.ContainsKey("actor-scene") && !options.ContainsKey("actor-scenes"))))
            throw new ArgumentException(
                "--retail-state-contract requires --cell-scene, actor scenes, and --capture-root.");
        if (options.ContainsKey("gallery-shot") &&
            (!hasCellScene || !options.ContainsKey("capture-root") ||
                !options.ContainsKey("actor-scene") ||
                options.ContainsKey("actor-scenes") ||
                options.ContainsKey("retail-state-contract")))
            throw new ArgumentException(
                "--gallery-shot requires --cell-scene, one --actor-scene, and " +
                "--capture-root, and cannot use retail-state-contract.");
    }
}
