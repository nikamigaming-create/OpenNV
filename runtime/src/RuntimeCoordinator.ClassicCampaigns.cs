using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using OpenNV.Runtime.Diagnostics.Performance;
using OpenNV.Runtime.World.Portals;
using OpenNV.Runtime.Campaigns.Classic;
using OpenNV.Runtime.Campaigns.TTW;
using OpenNV.Runtime.Campaigns.Fallout1;
using OpenNV.Runtime.Campaigns.Fallout2.Temple;
using OpenNV.Runtime.Campaigns.NewVegas.Opening;

namespace OpenNV.Runtime;

public partial class RuntimeCoordinator
{
    private void LoadFo1HexScene(
        string scenePath,
        IReadOnlyDictionary<string, string> options)
    {
        var loaded = Fo1HexSceneLoader.Load(
            scenePath,
            this,
            options.TryGetValue("save-path", out var savePath) ? savePath : null,
            Fo2HumanoidDonorContract.RequireFromOptions(options),
            options.TryGetValue("fo1-exit-grid-transition", out var exitGridTransitionPath)
                ? Fo1ExitGridTransitionContract.Load(exitGridTransitionPath)
                : null,
            options.TryGetValue("fo1-destination-presentation", out var destinationPresentationPath)
                ? destinationPresentationPath
                : null,
            options.TryGetValue("fo1-destination-inventory-interaction", out var destinationInventoryInteractionPath)
                ? destinationInventoryInteractionPath
                : null,
            options.TryGetValue("fo1-destination-flare-use", out var destinationFlareUsePath)
                ? destinationFlareUsePath
                : null,
            options.TryGetValue("fo1-destination-generic-door", out var destinationGenericDoorPath)
                ? destinationGenericDoorPath
                : null,
            options.TryGetValue("fo1-destination-medic-look", out var destinationMedicLookPath)
                ? destinationMedicLookPath
                : null,
            options.TryGetValue("fo1-destination-return-exit-grid", out var destinationReturnExitGridPath)
                ? destinationReturnExitGridPath
                : null);
        var report = new
        {
            schema = "opennv-fo1-hex-runtime/v1",
            status = "pass",
            renderer = RenderingServer.GetCurrentRenderingMethod().ToString(),
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
                unifiedCaveMaterialSurfaces = loaded.OwnedCave.UnifiedCaveMaterialSurfaces,
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
        if (options.TryGetValue("report", out var reportPath) &&
            !options.ContainsKey("fo1-continue-menu-proof"))
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
        if (options.ContainsKey("fo1-destination-cold-restore-proof"))
        {
            _ = Fo1NewGameFlow.RunDestinationColdRestoreProof(
                this,
                loaded,
                RequireOption(options, "report"));
            return;
        }
        if (options.ContainsKey("fo1-destination-inventory-interaction-proof"))
        {
            _ = Fo1NewGameFlow.RunDestinationInventoryInteractionProof(this, loaded, RequireOption(options, "report"));
            return;
        }
        if (options.ContainsKey("fo1-destination-inventory-interaction-cold-restore-proof"))
        {
            _ = Fo1NewGameFlow.RunDestinationInventoryInteractionColdRestoreProof(this, loaded, RequireOption(options, "report"));
            return;
        }
        if (options.ContainsKey("fo1-destination-medic-look-proof"))
        {
            _ = Fo1NewGameFlow.RunDestinationMedicLookProof(this, loaded, RequireOption(options, "report"));
            return;
        }
        if (options.ContainsKey("fo1-destination-medic-look-cold-restore-proof"))
        {
            _ = Fo1NewGameFlow.RunDestinationMedicLookColdRestoreProof(this, loaded, RequireOption(options, "report"));
            return;
        }
        if (options.ContainsKey("fo1-destination-return-exit-proof"))
        {
            _ = Fo1NewGameFlow.RunDestinationReturnExitProof(this, loaded, RequireOption(options, "report"));
            return;
        }
        if (options.ContainsKey("fo1-destination-return-exit-cold-restore-proof"))
        {
            _ = Fo1NewGameFlow.RunDestinationReturnExitColdRestoreProof(this, loaded, RequireOption(options, "report"));
            return;
        }
        if (options.ContainsKey("fo1-new-game") || options.ContainsKey("fo1-new-game-demo") ||
            options.ContainsKey("fo1-character-video"))
        {
            var characterStart = Fo1CharacterStartContract.Load(
                RequireOption(options, "fo1-character-start"),
                RequireOption(options, "fo1-character-start-sha256"));
            if (options.TryGetValue("fo1-character-video", out var videoCharacter))
                _ = Fo1NewGameFlow.RunCharacterVideo(
                    this,
                    loaded,
                    characterStart,
                    videoCharacter,
                    OpeningManifest.Load(
                        RequireOption(options, "character-reflectron-opening-manifest"),
                        _configuration));
            else if (options.ContainsKey("fo1-new-game-demo"))
                _ = Fo1NewGameFlow.RunDemo(
                    this,
                    loaded,
                    characterStart,
                    RequireOption(options, "demo-report"),
                    options.ContainsKey("fo1-demo-fast-opening"),
                    options.ContainsKey("fo1-demo-skip-opening"),
                    options.TryGetValue("capture-root", out var fo1CaptureRoot)
                        ? fo1CaptureRoot
                        : null,
                    options.ContainsKey("fo1-native-first-beat-proof"));
            else
                Fo1NewGameFlow.StartInteractive(
                    this,
                    loaded,
                    characterStart,
                    options.TryGetValue("fo1-start-presentation", out var startPresentation)
                        ? startPresentation
                        : "first-person",
                    options.ContainsKey("fo1-continue-menu-proof"),
                    options.TryGetValue("report", out var continueProofReport)
                        ? continueProofReport
                        : null,
                    options.ContainsKey("fo1-continue-flare-use-proof"),
                    options.ContainsKey("fo1-continue-generic-door-proof"));
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
            var selectedMap = options.TryGetValue("fo1-map", out var requestedMap)
                ? requestedMap
                : null;
            if (options.TryGetValue(
                    "classic-adjacent-map-catalog", out var adjacentCatalogPath))
            {
                var runtime = new Fo1CampaignAdjacentRuntime();
                AddChild(runtime);
                viewCoverage = runtime.Configure(
                    catalog,
                    ClassicAdjacentMapCatalog.Load(adjacentCatalogPath),
                    selectedMap ?? throw new ArgumentException(
                        "Playable Fallout adjacent maps require --fo1-map."),
                    elevation,
                    RequireOption(options, "save-path"));
                viewer = runtime.Viewer;
            }
            else
            {
                viewer = new Fo1CampaignPresentationViewer();
                AddChild(viewer);
                viewCoverage = viewer.Configure(catalog, selectedMap, elevation);
            }
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
}
