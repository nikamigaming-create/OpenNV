using System.Security.Cryptography;
using System.Text.Json;
using Godot;

namespace OpenNV.Runtime.Campaigns.Fallout2.Temple;

internal static class Fo2ArroyoCavesRenderProof
{
    private const int ExpectedWidth = 1280;
    private const int ExpectedHeight = 720;
    private const int WarmupFrames = 8;
    private const double MinimumLuminanceDeviation = 0.015;
    private const int MinimumNonBackgroundPixels = 1000;
    private static readonly Color BackgroundColor = new("05080d");

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
            var output = Path.GetFullPath(captureRoot);
            if (Directory.Exists(output) || File.Exists(output))
                throw new InvalidOperationException(
                    $"Refusing to overwrite Fallout 2 Arroyo Caves render proof: {output}");
            Directory.CreateDirectory(output);

            VerifySceneIdentity(coverage);
            BuildPresentation(host, coverage.ArrivalWorldMeters);
            for (var frame = 0; frame < WarmupFrames; frame++)
                await host.ToSignal(
                    RenderingServer.Singleton,
                    RenderingServer.SignalName.FramePostDraw);

            var framePath = Path.Combine(output, "arroyo-caves-arrival.png");
            var image = host.GetViewport().GetTexture().GetImage();
            image.Convert(Image.Format.Rgba8);
            var metrics = Analyze(image);
            var error = image.SavePng(framePath);
            if (error != Error.Ok)
                throw new InvalidOperationException(
                    $"Could not save Fallout 2 Arroyo Caves render frame: {error}");
            using var frameStream = File.OpenRead(framePath);
            var frameSha256 = Convert.ToHexString(SHA256.HashData(frameStream)).ToLowerInvariant();
            var failure = metrics.Width != ExpectedWidth || metrics.Height != ExpectedHeight
                ? "unexpected-size"
                : metrics.LuminanceDeviation < MinimumLuminanceDeviation
                    ? "luminance-deviation"
                    : metrics.NonBackgroundPixels < MinimumNonBackgroundPixels
                        ? "source-pixels-not-visible"
                        : null;
            var report = new
            {
                schema = "opennv-fo2-arroyo-caves-native-render-proof/v1",
                status = failure is null
                    ? "pass-rendered-owned-map-presentation-no-player-or-gameplay"
                    : "fail-rendered-owned-map-presentation",
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
                    visualGatePassed = failure is null,
                    visualGateFailure = failure,
                },
                promotion = new
                {
                    transported = true,
                    rendered = failure is null,
                    interactive = false,
                    retailParityReviewed = false,
                    playerSpawned = false,
                    gameplayImplemented = false,
                    saveImplemented = false,
                    launcherPlayable = false,
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
                    $"OPENNV_FO2_ARROYO_RENDER_PASS map={coverage.MapIndex} " +
                    $"elevation={coverage.Elevation} arrival={coverage.ArrivalTile} " +
                    $"floors={coverage.ConstructedFloorPatches} " +
                    $"objects={coverage.PlacedTopLevelObjects} output={output}");
            else
                GD.PushError(
                    $"OPENNV_FO2_ARROYO_RENDER_VISUAL_FAIL failure={failure} output={output}");
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
            coverage.PlacedTopLevelObjects <= 0)
            throw new InvalidOperationException(
                "Fallout 2 Arroyo Caves rendered-arrival scene identity drifted.");
    }

    private static void BuildPresentation(Node3D host, Vector3 arrival)
    {
        var environment = new WorldEnvironment
        {
            Name = "ARROYO_RENDER_PROOF_ENVIRONMENT",
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = BackgroundColor,
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = Colors.White,
                AmbientLightEnergy = 1.0f,
            },
        };
        host.AddChild(environment);
        var camera = new Camera3D
        {
            Name = "ARROYO_EXACT_ARRIVAL_PROOF_CAMERA",
            Projection = Camera3D.ProjectionType.Orthogonal,
            Size = 24.0f,
            Position = arrival + new Vector3(-10.0f, 15.0f, 12.0f),
            Current = true,
        };
        host.AddChild(camera);
        camera.LookAt(arrival + Vector3.Up * 0.75f, Vector3.Up);
    }

    private static FrameMetrics Analyze(Image image)
    {
        var data = image.GetData();
        var pixels = image.GetWidth() * image.GetHeight();
        if (pixels <= 0 || data.Length != pixels * 4)
            throw new InvalidOperationException("Fallout 2 Arroyo Caves viewport is empty.");
        var background = new Vector3(
            BackgroundColor.R * byte.MaxValue,
            BackgroundColor.G * byte.MaxValue,
            BackgroundColor.B * byte.MaxValue);
        double luminance = 0.0;
        double luminanceSquared = 0.0;
        var nonBackgroundPixels = 0;
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
        }
        var mean = luminance / pixels;
        return new FrameMetrics(
            image.GetWidth(),
            image.GetHeight(),
            mean,
            Math.Sqrt(Math.Max(0.0, luminanceSquared / pixels - mean * mean)),
            nonBackgroundPixels);
    }

    private static float[] Vector(Vector3 value) => new[] { value.X, value.Y, value.Z };

    private readonly record struct FrameMetrics(
        int Width,
        int Height,
        double MeanLuminance,
        double LuminanceDeviation,
        int NonBackgroundPixels);
}
