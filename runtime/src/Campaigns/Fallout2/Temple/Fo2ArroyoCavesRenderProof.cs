using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using OpenNV.Runtime.Campaigns.Fallout1;

namespace OpenNV.Runtime.Campaigns.Fallout2.Temple;

internal static class Fo2ArroyoCavesRenderProof
{
    private const int ExpectedWidth = 1280;
    private const int ExpectedHeight = 720;
    private const int WarmupFrames = 8;

    internal static async Task Run(
        Node3D host,
        Fo2ArroyoCavesSceneCoverage coverage,
        string captureRoot)
    {
        try
        {
            if (DisplayServer.GetName() == "headless")
                throw new InvalidOperationException(
                    "Fallout 2 Arroyo Caves render proof requires a rendering display driver.");
            DisplayServer.WindowSetTitle(
                "OpenNV • Fallout 2 • Arroyo Caves • bounded proof");
            var output = Path.GetFullPath(captureRoot);
            if (Directory.Exists(output) || File.Exists(output))
                throw new InvalidOperationException(
                    $"Refusing to overwrite Fallout 2 Arroyo Caves render proof: {output}");
            Directory.CreateDirectory(output);

            VerifySceneIdentity(coverage);
            BuildCamera(host, coverage.ArrivalWorldMeters, coverage.Molded3D.Profile.StaticCapture);
            for (var frame = 0; frame < WarmupFrames; frame++)
                await host.ToSignal(
                    RenderingServer.Singleton,
                    RenderingServer.SignalName.FramePostDraw);

            var framePath = Path.Combine(output, "arroyo-caves-arrival.png");
            var image = host.GetViewport().GetTexture().GetImage();
            image.Convert(Image.Format.Rgba8);
            var capture = coverage.Molded3D.Profile.StaticCapture;
            var metrics = Analyze(
                image,
                coverage.Molded3D.Profile.Atmosphere.BackgroundColor);
            var error = image.SavePng(framePath);
            if (error != Error.Ok)
                throw new InvalidOperationException(
                    $"Could not save Fallout 2 Arroyo Caves render frame: {error}");
            using var frameStream = File.OpenRead(framePath);
            var frameSha256 = Convert.ToHexString(SHA256.HashData(frameStream)).ToLowerInvariant();
            var failure = metrics.Width != ExpectedWidth || metrics.Height != ExpectedHeight
                ? "unexpected-size"
                : metrics.LuminanceDeviation < capture.MinimumLuminanceDeviation
                    ? "luminance-deviation"
                    : metrics.NonBackgroundPixels < capture.MinimumNonBackgroundPixels
                        ? "source-pixels-not-visible"
                        : metrics.BackgroundPixelFraction > capture.MaximumBackgroundPixelFraction
                            ? "clear-background-coverage"
                        : null;
            var report = new
            {
                schema = "opennv-fo2-arroyo-caves-native-render-proof/v3",
                status = failure is null
                    ? "pass-source-bound-molded-3d-construction-frame-presentation-unaccepted"
                    : "fail-source-bound-molded-3d-construction-frame",
                campaign = "Fallout2",
                slice = "ArroyoCaves",
                renderer = RenderingServer.GetCurrentRenderingMethod(),
                displayDriver = DisplayServer.GetName(),
                source = new
                {
                    profileId = coverage.SourceProfileId,
                    cacheManifest = coverage.ManifestPath,
                    cacheManifestSha256 = coverage.ManifestSha256,
                    sourceManifest = coverage.SourceManifestPath,
                    sourceManifestSha256 = coverage.SourceManifestSha256,
                    mapSha256 = coverage.MapSha256,
                    transitionManifestSha256 = coverage.SourceTransitionSha256,
                    walkMaskSha256 = coverage.WalkMaskSha256,
                    exactElevationZeroCoverage = new
                    {
                        nonDefaultFloorPatches = coverage.Molded3D.SourceFloorPatches,
                        floorBoundaryEdges = coverage.Molded3D.SourceFloorBoundaryEdges,
                        topLevelObjects = coverage.Molded3D.SourceTopLevelObjects,
                        objectTypes = coverage.Molded3D.SourceObjectTypes,
                        wallObjects = coverage.Molded3D.SourceWallObjects,
                        uniqueWallTiles = coverage.Molded3D.UniqueWallTiles,
                        wallComponents = coverage.Molded3D.WallComponents,
                        largestWallComponentTiles =
                            coverage.Molded3D.LargestWallComponentTiles,
                        wallBoundaryEdges = coverage.Molded3D.WallBoundaryEdges,
                    },
                },
                arrival = new
                {
                    mapIndex = coverage.MapIndex,
                    elevation = coverage.Elevation,
                    tile = coverage.ArrivalTile,
                    rotation = coverage.ArrivalRotation,
                    worldMeters = Vector(coverage.ArrivalWorldMeters),
                    authority = "exact Map 126 exit-grid instance values",
                },
                construction = new
                {
                    verifiedArtifacts = coverage.VerifiedArtifacts,
                    verifiedResources = coverage.VerifiedResources,
                    tileBindings = coverage.TileBindings,
                    floorPatches = coverage.ConstructedFloorPatches,
                    floorMeshInstances = coverage.FloorMeshInstances,
                    topLevelObjects = coverage.PlacedTopLevelObjects,
                    objectSpriteNodes = coverage.ObjectSpriteNodes,
                    sourcePixelsPerMeter = coverage.SourcePixelsPerMeter,
                    walkableHexes = coverage.WalkableHexes,
                    arrivalComponentHexes = coverage.ArrivalComponentHexes,
                    moldedFloorPatches = coverage.Molded3D.MoldedFloorPatches,
                    moldedFloorTriangles = coverage.Molded3D.MoldedFloorTriangles,
                    moldedFloorMeshes = coverage.Molded3D.MoldedFloorMeshes,
                    floorBoundaryClosureTriangles =
                        coverage.Molded3D.FloorBoundaryClosureTriangles,
                    floorBoundaryClosureMeshes = coverage.Molded3D.FloorBoundaryClosureMeshes,
                    sourceWallComponents = coverage.Molded3D.WallComponents,
                    fusedCaveShellComponents = coverage.Molded3D.CaveShellComponents,
                    caveShellWallObjects = coverage.Molded3D.CaveShellWallObjects,
                    sourceFrmStonePostInstances = coverage.Molded3D.StonePostInstances,
                    fusedWallTriangles = coverage.Molded3D.WallTriangles,
                    fusedWallMeshInstances = coverage.Molded3D.WallMeshInstances,
                    ceilingClosure = coverage.Molded3D.Profile.WallGeometry.CeilingClosure,
                    componentMeshMode =
                        coverage.Molded3D.Profile.WallGeometry.ComponentMeshMode,
                    hiddenWallSpriteCards = coverage.Molded3D.HiddenWallSpriteCards,
                    hiddenNonWallBlockCards =
                        coverage.Molded3D.HiddenNonWallBlockCards,
                    hiddenSourceMarkerCards = coverage.Molded3D.HiddenSourceMarkerCards,
                    visibleSourceProps = coverage.Molded3D.VisibleSourceProps,
                    groundedSourceProps = coverage.Molded3D.GroundedSourceProps,
                    maximumGroundErrorMeters =
                        coverage.Molded3D.MaximumGroundErrorMeters,
                    visibleSourceTorchProps = coverage.Molded3D.VisibleSourceTorchProps,
                    sourceTorchPostLayeredAssemblies =
                        coverage.Molded3D.SourceTorchPostLayeredAssemblies,
                    sourceMapLightRecords = coverage.Molded3D.SourceMapLightRecords,
                    sourceMapLights = coverage.Molded3D.SourceMapLights,
                    sourceTorchMotivatedMapLights =
                        coverage.Molded3D.SourceTorchMotivatedMapLights,
                    sourceWalkMaskUnchanged = coverage.Molded3D.SourceWalkMaskUnchanged,
                },
                presentation = new
                {
                    recipe = coverage.Molded3D.Profile.ResourcePath,
                    recipeSha256 = coverage.Molded3D.Profile.Sha256,
                    recipeId = coverage.Molded3D.Profile.Id,
                    worldSpaceMaterial = coverage.Molded3D.WorldSpaceMaterialContract,
                    ownedFrmSurfaces = new
                    {
                        textureSha256 = coverage.Molded3D.SourceWallTextureSha256,
                        normalTextureSha256 =
                            coverage.Molded3D.SourceWallNormalTextureSha256,
                        floorTextureSha256 = coverage.Molded3D.SourceFloorTextureSha256,
                        floorNormalTextureSha256 =
                            coverage.Molded3D.SourceFloorNormalTextureSha256,
                        provenanceSha256 = coverage.Molded3D.SourceWallProvenanceSha256,
                        sourceWallObjects = coverage.Molded3D.SourceWallObjects,
                        sourceWallArtifacts = coverage.Molded3D.SourceWallMaterialArtifacts,
                        opaqueSourceWallArtifacts =
                            coverage.Molded3D.OpaqueSourceWallMaterialArtifacts,
                        sourceFloorPatches = coverage.Molded3D.SourceFloorPatches,
                        sourceFloorArtifacts = coverage.Molded3D.SourceFloorMaterialArtifacts,
                        distributionAllowed = false,
                    },
                    depthFogEnabled = true,
                    sourcePlacedPracticalLights = coverage.Molded3D.SourceMapLights,
                    generatedAssetLane = new
                    {
                        used = coverage.Molded3D.GeneratedAssetsUsed,
                        trellisCandidatesAdmitted =
                            coverage.Molded3D.Profile.GeneratedAssetLane
                                .TrellisCandidatesAdmitted,
                        ownedOrGeneratedMeshesPackaged =
                            coverage.Molded3D.Profile.GeneratedAssetLane
                                .OwnedOrGeneratedMeshesPackaged,
                        reason = coverage.Molded3D.Profile.GeneratedAssetLane.Reason,
                    },
                },
                frame = new
                {
                    path = framePath,
                    bytes = frameStream.Length,
                    sha256 = frameSha256,
                    width = metrics.Width,
                    height = metrics.Height,
                    meanLuminance = metrics.MeanLuminance,
                    luminanceDeviation = metrics.LuminanceDeviation,
                    nonBackgroundPixels = metrics.NonBackgroundPixels,
                    backgroundPixels = metrics.BackgroundPixels,
                    backgroundPixelFraction = metrics.BackgroundPixelFraction,
                    frameIntegrityGatePassed = failure is null,
                    frameIntegrityGateFailure = failure,
                    presentationVisualGatePassed = false,
                    presentationVisualBlockers =
                        coverage.Molded3D.Profile.Promotion.PresentationBlockers,
                },
                promotion = new
                {
                    transported = true,
                    constructionFrameRendered = failure is null,
                    presentationAccepted = false,
                    interactive = false,
                    retailParityReviewed = false,
                    playerSpawned = false,
                    gameplayImplemented = false,
                    saveImplemented = false,
                    launcherPlayable = false,
                    pairReady = false,
                    fo1QualityParity = false,
                },
                cinematicHandoff = new
                {
                    reviewed = false,
                    verdict = "not-claimed-static-arrival-only",
                    reason = "No owned movie frame or control-release sequence is part of this static PNG proof.",
                },
                windowsAppControlUsed = false,
                foregroundActivationUsed = false,
                foregroundInputInjected = false,
            };
            var reportPath = Path.Combine(output, "arroyo-caves-native-render-proof.json");
            File.WriteAllText(
                reportPath,
                JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) +
                    System.Environment.NewLine);
            if (failure is null)
                GD.Print(
                    $"OPENNV_FO2_ARROYO_CONSTRUCTION_CAPTURE_PASS map={coverage.MapIndex} " +
                    $"elevation={coverage.Elevation} arrival={coverage.ArrivalTile} " +
                    $"floors={coverage.ConstructedFloorPatches} " +
                    $"objects={coverage.PlacedTopLevelObjects} output={output}");
            else
                GD.PushError(
                    $"OPENNV_FO2_ARROYO_CONSTRUCTION_CAPTURE_FAIL failure={failure} output={output}");
            host.GetTree().Quit(failure is null ? 0 : 1);
        }
        catch (Exception exception)
        {
            GD.PushError($"OPENNV_FO2_ARROYO_RENDER_FAIL {exception.Message}");
            host.GetTree().Quit(1);
        }
    }

    private static void VerifySceneIdentity(Fo2ArroyoCavesSceneCoverage coverage)
    {
        var marker = coverage.Root.GetNode<Node3D>(
            "MAP_3_SOURCE_ARRIVAL_MARKER_NO_PLAYER_OBJECT");
        var expectedRotation = new Vector3(
            0.0f,
            -coverage.ArrivalRotation * Mathf.Tau / Fo1HexMath.DirectionCount,
            0.0f);
        if (coverage.MapIndex != Fo2ArroyoCavesPresentationCatalog.MapIndex ||
            coverage.Elevation != Fo2ArroyoCavesPresentationCatalog.Elevation ||
            coverage.ArrivalTile != 28707 ||
            coverage.ArrivalRotation != 0 ||
            !coverage.ArrivalWorldMeters.IsEqualApprox(Fo1HexMath.Center(coverage.ArrivalTile)) ||
            !marker.Position.IsEqualApprox(coverage.ArrivalWorldMeters) ||
            !marker.Rotation.IsEqualApprox(expectedRotation) ||
            !marker.GetMeta("temple_exit_grid_arrival").AsBool() ||
            coverage.ConstructedFloorPatches <= 0 ||
            coverage.PlacedTopLevelObjects <= 0 ||
            coverage.Molded3D.SourceFloorPatches != coverage.ConstructedFloorPatches ||
            coverage.Molded3D.SourceTopLevelObjects != coverage.PlacedTopLevelObjects ||
            coverage.Molded3D.MoldedFloorPatches != coverage.ConstructedFloorPatches ||
            coverage.Molded3D.WallMeshInstances != coverage.Molded3D.CaveShellComponents ||
            coverage.Molded3D.StonePostInstances !=
                coverage.Molded3D.Profile.WallGeometry.Roles.ExpectedStonePostInstances ||
            coverage.Molded3D.HiddenWallSpriteCards != coverage.Molded3D.SourceWallObjects ||
            coverage.Molded3D.VisibleSourceProps != coverage.Molded3D.GroundedSourceProps ||
            !coverage.Molded3D.SourceWalkMaskUnchanged ||
            coverage.Molded3D.GeneratedAssetsUsed ||
            coverage.Molded3D.Profile.Promotion.PairReady ||
            coverage.Molded3D.Profile.Promotion.Fo1QualityParity)
            throw new InvalidOperationException(
                "Fallout 2 Arroyo Caves rendered-arrival scene identity drifted.");
    }

    private static void BuildCamera(
        Node3D host,
        Vector3 arrival,
        Fo2ArroyoStaticCaptureProfile profile)
    {
        var camera = new Camera3D
        {
            Name = "ARROYO_RECIPE_STATIC_ARRIVAL_CAMERA",
            Projection = Camera3D.ProjectionType.Perspective,
            Position = arrival + profile.PositionOffsetMeters,
            Fov = profile.FovDegrees,
            Near = profile.NearClipMeters,
            Far = profile.FarClipMeters,
            Current = true,
        };
        host.AddChild(camera);
        camera.LookAt(arrival + profile.FocusOffsetMeters, Vector3.Up);
    }

    private static FrameMetrics Analyze(Image image, Color backgroundColor)
    {
        var data = image.GetData();
        var pixels = image.GetWidth() * image.GetHeight();
        if (pixels <= 0 || data.Length != pixels * 4)
            throw new InvalidOperationException("Fallout 2 Arroyo Caves viewport is empty.");
        var background = new Vector3(
            backgroundColor.R * byte.MaxValue,
            backgroundColor.G * byte.MaxValue,
            backgroundColor.B * byte.MaxValue);
        double luminance = 0.0;
        double luminanceSquared = 0.0;
        var nonBackgroundPixels = 0;
        var backgroundPixels = 0;
        for (var offset = 0; offset < data.Length; offset += 4)
        {
            var value = (0.2126 * data[offset] + 0.7152 * data[offset + 1] +
                0.0722 * data[offset + 2]) / byte.MaxValue;
            luminance += value;
            luminanceSquared += value * value;
            var delta = new Vector3(data[offset], data[offset + 1], data[offset + 2]) -
                background;
            if (delta.LengthSquared() > 16.0f)
                nonBackgroundPixels++;
            else
                backgroundPixels++;
        }
        var mean = luminance / pixels;
        return new FrameMetrics(
            image.GetWidth(),
            image.GetHeight(),
            mean,
            Math.Sqrt(Math.Max(0.0, luminanceSquared / pixels - mean * mean)),
            nonBackgroundPixels,
            backgroundPixels,
            (double)backgroundPixels / pixels);
    }

    private static float[] Vector(Vector3 value) => new[] { value.X, value.Y, value.Z };

    private readonly record struct FrameMetrics(
        int Width,
        int Height,
        double MeanLuminance,
        double LuminanceDeviation,
        int NonBackgroundPixels,
        int BackgroundPixels,
        double BackgroundPixelFraction);
}
