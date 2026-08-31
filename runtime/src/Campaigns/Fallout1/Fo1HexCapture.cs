using System.Security.Cryptography;
using System.Text.Json;
using Godot;

namespace OpenNV.Runtime.Campaigns.Fallout1;

internal static class Fo1HexCaptureNumericContracts
{
    // Immutable format, source-art, geometry, and acceptance contracts.
    // Runtime-tunable Fallout 1 behavior remains in the versioned runtime recipe.
    internal const float AcceptanceFloatNEgativE1Point15f = -1.15f;
    internal const float AcceptanceFloatNEgativE26Point0f = -26.0f;
    internal const float AcceptanceFloatNEgativE30Point0f = -30.0f;
    internal const float AcceptanceFloatNEgativE38Point0f = -38.0f;
    internal const float AcceptanceFloatNEgativE45Point0f = -45.0f;
    internal const double AcceptanceDouble0Point025 = 0.025;
    internal const double AcceptanceDouble0Point035 = 0.035;
    internal const double AcceptanceDouble0Point0722 = 0.0722;
    internal const double AcceptanceDouble0Point08 = 0.08;
    internal const double AcceptanceDouble0Point12 = 0.12;
    internal const double AcceptanceDouble0Point2126 = 0.2126;
    internal const float AcceptanceFloat0Point42f = 0.42f;
    internal const float AcceptanceFloat0Point44f = 0.44f;
    internal const double AcceptanceDouble0Point7152 = 0.7152;
    internal const double AcceptanceDouble0Point75 = 0.75;
    internal const float AcceptanceFloat0Point86f = 0.86f;
    internal const int AcceptanceInt1280 = 1280;
    internal const float AcceptanceFloat135Point0f = 135.0f;
    internal const float AcceptanceFloat2Point5f = 2.5f;
    internal const double AcceptanceDouble255Point0 = 255.0;
    internal const int AcceptanceInt5 = 5;
    internal const float AcceptanceFloat5Point0f = 5.0f;
    internal const float AcceptanceFloat7Point5f = 7.5f;
    internal const int AcceptanceInt720 = 720;
}

internal static class Fo1HexCapture
{
    internal static async Task Run(
        Node host,
        Fo1HexSceneLoader.LoadedFo1HexScene loaded,
        string captureRoot,
        object runtimeReport)
    {
        try
        {
            DisplayServer.WindowSetTitle(
                "OpenNV • Fallout 1 • Vault 13 Cave • bounded proof");
            var output = Path.GetFullPath(captureRoot);
            if (Directory.Exists(output) || File.Exists(output))
                throw new InvalidOperationException($"Refusing to overwrite Fallout hex capture: {output}");
            Directory.CreateDirectory(output);
            await WaitForFrames(host, Fo1HexCaptureNumericContracts.AcceptanceInt5);
            loaded.Camera.FrameEntryPair(loaded.Session.PlayerTile, loaded.DoorTile);
            await WaitForFrames(host, 4);
            var ui = SaveViewport(host, output, "v13ent-hex-tactical-ui.png", Fo1HexCaptureNumericContracts.AcceptanceDouble0Point025, Fo1HexCaptureNumericContracts.AcceptanceDouble0Point035);
            var combatTarget = loaded.Session.Mobs
                .Where(mob => mob.Alive)
                .OrderBy(mob => Fo1HexMath.Distance(loaded.Session.PlayerTile, mob.Tile))
                .First();
            loaded.Session.ActivateTile(combatTarget.Tile, false);
            loaded.Session.SetWorldGuidesVisible(false);
            loaded.Camera.FocusTileAtHeight(combatTarget.Tile, Fo1HexCaptureNumericContracts.AcceptanceFloat5Point0f, Fo1HexCaptureNumericContracts.AcceptanceFloat0Point42f);
            await WaitForFrames(host, 4);
            var combat = SaveViewport(host, output, "v13ent-combat-target.png", Fo1HexCaptureNumericContracts.AcceptanceDouble0Point025, Fo1HexCaptureNumericContracts.AcceptanceDouble0Point035);
            loaded.Session.ToggleGrid();
            await WaitForFrames(host, 3);
            var hexOverlay = SaveViewport(host, output, "v13ent-optional-hex-overlay.png", Fo1HexCaptureNumericContracts.AcceptanceDouble0Point025, Fo1HexCaptureNumericContracts.AcceptanceDouble0Point035);
            loaded.Session.ToggleGrid();
            loaded.Session.SetWorldGuidesVisible(false);
            loaded.Session.Hud.Visible = false;
            loaded.Camera.SetOrbitDegrees(Fo1HexCaptureNumericContracts.AcceptanceFloatNEgativE45Point0f, Fo1HexCaptureNumericContracts.AcceptanceFloatNEgativE26Point0f);
            loaded.Camera.FrameCombatPair(loaded.Session.PlayerTile, combatTarget.Tile);
            await WaitForFrames(host, Fo1HexCaptureNumericContracts.AcceptanceInt5);
            var atmosphere = SaveViewport(
                host,
                output,
                "v13ent-cave-atmosphere.png",
                Fo1HexCaptureNumericContracts.AcceptanceDouble0Point035,
                Fo1HexCaptureNumericContracts.AcceptanceDouble0Point035,
                Fo1HexCaptureNumericContracts.AcceptanceDouble0Point12);
            loaded.Camera.SetFirstPersonMode(true);
            var doorFacingYaw = loaded.Camera.TargetYawRadians;
            loaded.Camera.SetOrbitDegrees(
                Mathf.RadToDeg(doorFacingYaw + MathF.PI),
                Fo1HexCaptureNumericContracts.AcceptanceFloatNEgativE1Point15f);
            await WaitForFrames(host, Fo1HexCaptureNumericContracts.AcceptanceInt5);
            var firstPersonAtmosphere = SaveViewport(
                host,
                output,
                "v13ent-cave-first-person-atmosphere.png",
                Fo1HexCaptureNumericContracts.AcceptanceDouble0Point035,
                Fo1HexCaptureNumericContracts.AcceptanceDouble0Point025,
                Fo1HexCaptureNumericContracts.AcceptanceDouble0Point08);
            loaded.Camera.SetExplorationMode(false);
            var combatCutawayHidden = loaded.CaveCutaway.HiddenInstances;
            foreach (var mob in loaded.Session.Mobs)
                mob.SetReadabilityMarkersVisible(false);
            combatTarget.SetSelected(false);
            loaded.Camera.SetOrbitDegrees(Fo1HexCaptureNumericContracts.AcceptanceFloatNEgativE45Point0f, Fo1HexCaptureNumericContracts.AcceptanceFloatNEgativE38Point0f);
            loaded.Camera.FocusTileAtHeight(combatTarget.Tile, Fo1HexCaptureNumericContracts.AcceptanceFloat2Point5f, Fo1HexCaptureNumericContracts.AcceptanceFloat0Point44f);
            await WaitForFrames(host, 4);
            var creature = SaveViewport(host, output, "v13ent-giant-rat-3d.png", Fo1HexCaptureNumericContracts.AcceptanceDouble0Point025, Fo1HexCaptureNumericContracts.AcceptanceDouble0Point025);
            loaded.Camera.SetOrbitDegrees(Fo1HexCaptureNumericContracts.AcceptanceFloat135Point0f, Fo1HexCaptureNumericContracts.AcceptanceFloatNEgativE26Point0f);
            loaded.Camera.FocusTileAtHeight(loaded.Session.PlayerTile, 3.0f, Fo1HexCaptureNumericContracts.AcceptanceFloat0Point86f);
            await WaitForFrames(host, 4);
            var player = SaveViewport(host, output, "v13ent-vault-dweller-3d.png", Fo1HexCaptureNumericContracts.AcceptanceDouble0Point025, Fo1HexCaptureNumericContracts.AcceptanceDouble0Point035);
            loaded.Camera.SetOrbitDegrees(0.0f, Fo1HexCaptureNumericContracts.AcceptanceFloatNEgativE30Point0f);
            loaded.Camera.FocusTileAtHeight(loaded.DoorTile, Fo1HexCaptureNumericContracts.AcceptanceFloat7Point5f, 2.0f);
            await WaitForFrames(host, 4);
            var door = SaveViewport(host, output, "v13ent-vault-door-3d.png", Fo1HexCaptureNumericContracts.AcceptanceDouble0Point025, Fo1HexCaptureNumericContracts.AcceptanceDouble0Point035);
            loaded.Camera.ResetHome();
            await WaitForFrames(host, 3);
            var environment = SaveViewport(host, output, "v13ent-hex-map.png", Fo1HexCaptureNumericContracts.AcceptanceDouble0Point025, Fo1HexCaptureNumericContracts.AcceptanceDouble0Point025);
            foreach (var mob in loaded.Session.Mobs)
                mob.SetReadabilityMarkersVisible(true);
            combatTarget.SetSelected(true);
            loaded.Session.Hud.Visible = true;
            loaded.Session.SetWorldGuidesVisible(true);
            var status = ui.Passed && combat.Passed && hexOverlay.Passed && atmosphere.Passed &&
                firstPersonAtmosphere.Passed && creature.Passed && player.Passed &&
                door.Passed && environment.Passed
                ? "pass"
                : "fail";
            var report = new
            {
                schema = "opennv-fo1-hex-capture/v1",
                status,
                renderer = "forward_plus",
                scene = loaded.ScenePath,
                sceneSha256 = loaded.SceneSha256,
                entryTile = loaded.EntryTile,
                doorTile = loaded.DoorTile,
                floorEntries = loaded.FloorEntries,
                floorTextures = loaded.FloorTextures,
                renderedFloorTiles = loaded.RenderedFloorTiles,
                provisionalWalkableHexes = loaded.WalkableHexes,
                spriteArtifacts = loaded.SpriteArtifacts,
                spritePlacements = loaded.SpritePlacements,
                combatMobs = loaded.CombatMobs,
                player3d = new
                {
                    enabled = loaded.PlayerActor is not null,
                    formId = loaded.PlayerActor?.FormId,
                    meshes = loaded.PlayerActor?.Meshes ?? 0,
                    skeletons = loaded.PlayerActor?.Skeletons ?? 0,
                    animations = loaded.PlayerActor?.Animations ?? 0,
                    authoredSurfaces = loaded.PlayerActor?.AuthoredSurfaces ?? 0,
                    textures = loaded.PlayerActor?.AuthoredTextures ?? 0,
                    heightMeters = loaded.PlayerActor?.Bounds.Size.Y ?? 0.0f,
                },
                cave3d = new
                {
                    boundaryEdges = loaded.CaveBoundaryEdges,
                    obstacles = loaded.CaveObstacles,
                    triangles = loaded.CaveTriangles,
                    ownedManifestSha256 = loaded.OwnedCave.ManifestSha256,
                    ownedAssets = loaded.OwnedCave.Assets,
                    ownedInstances = loaded.OwnedCave.Instances,
                    ownedMeshInstances = loaded.OwnedCave.MeshInstances,
                    ownedSurfaceInstances = loaded.OwnedCave.SurfaceInstances,
                    ownedMaterialBindings = loaded.OwnedCave.MaterialBindings,
                    ownedLitMaterials = loaded.OwnedCave.LitMaterials,
                    unifiedCaveMaterialSurfaces = loaded.OwnedCave.UnifiedCaveMaterialSurfaces,
                    ownedRoles = loaded.OwnedCave.Roles,
                    continuousFloorHexes = loaded.OwnedCave.ContinuousFloorHexes,
                    continuousFloorTriangles = loaded.OwnedCave.ContinuousFloorTriangles,
                    continuousFloorMeshInstances = loaded.OwnedCave.ContinuousFloorMeshInstances,
                    cutawayCandidates = loaded.CaveCutaway.Candidates,
                    combatCutawayHidden,
                    cutawayShaderDriven = loaded.CaveCutaway.ShaderDriven,
                    cutawayMaterials = loaded.CaveCutaway.MeltMaterials,
                    cutawayFadedInstances = loaded.CaveCutaway.FadedInstances,
                    atmosphere = new
                    {
                        schema = loaded.Atmosphere.Schema,
                        backgroundColor = new[]
                        {
                            loaded.Atmosphere.BackgroundColor.R,
                            loaded.Atmosphere.BackgroundColor.G,
                            loaded.Atmosphere.BackgroundColor.B,
                        },
                        fogColor = new[]
                        {
                            loaded.Atmosphere.FogColor.R,
                            loaded.Atmosphere.FogColor.G,
                            loaded.Atmosphere.FogColor.B,
                        },
                        depthFogDensity = loaded.Atmosphere.FogDensity,
                        volumetricFogEnabled = loaded.Atmosphere.VolumetricFogEnabled,
                        volumetricFogDensity = loaded.Atmosphere.VolumetricFogDensity,
                        volumetricFogLengthMeters = loaded.Atmosphere.VolumetricFogLengthMeters,
                        practicalLights = loaded.Atmosphere.PracticalLights,
                        directionalLights = loaded.Atmosphere.DirectionalLights,
                        localFogVolumes = loaded.Atmosphere.LocalFogVolumes,
                        tacticalEnvelopeCutHeightMeters =
                            loaded.RuntimeProfile.Cutaway.TacticalEnvelopeCutHeightMeters,
                        lowerEnvelopeBackdropRetained = true,
                    },
                },
                camera = new
                {
                    projection = "orthogonal",
                    targetSizeMeters = loaded.Camera.TargetSizeMeters,
                    targetYawDegrees = Mathf.RadToDeg(loaded.Camera.TargetYawRadians),
                    targetPitchDegrees = Mathf.RadToDeg(loaded.Camera.TargetPitchRadians),
                    middleMouseOrbit = true,
                    rightMousePan = true,
                    wheelZoomTowardCursor = true,
                    edgePan = true,
                },
                tactical = loaded.Session.Report(),
                selectedCombatTarget = combatTarget.Report(),
                runtime = runtimeReport,
                files = new[]
                {
                    ui.Evidence,
                    combat.Evidence,
                    hexOverlay.Evidence,
                    atmosphere.Evidence,
                    firstPersonAtmosphere.Evidence,
                    creature.Evidence,
                    player.Evidence,
                    door.Evidence,
                    environment.Evidence,
                },
                windowsAppControlUsed = false,
                foregroundActivationUsed = false,
                foregroundInputInjected = false,
            };
            WriteReport(Path.Combine(output, "fo1-hex-capture-report.json"), report);
            if (status == "pass")
                GD.Print($"OPENNV_FO1_HEX_CAPTURE_PASS output={output} files=9");
            else
                GD.PushError($"OPENNV_FO1_HEX_CAPTURE_VISUAL_FAIL output={output}");
            host.GetTree().Quit(status == "pass" ? 0 : 1);
        }
        catch (Exception exception)
        {
            GD.PushError($"OPENNV_FO1_HEX_CAPTURE_FAIL {exception.Message}");
            host.GetTree().Quit(1);
        }
    }

    private static async Task WaitForFrames(Node host, int count)
    {
        for (var index = 0; index < count; index++)
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private static CaptureResult SaveViewport(
        Node host,
        string output,
        string filename,
        double minimumMean,
        double minimumDeviation,
        double maximumDarkFraction = Fo1HexCaptureNumericContracts.AcceptanceDouble0Point75)
    {
        var path = Path.Combine(output, filename);
        var image = host.GetViewport().GetTexture().GetImage();
        image.Convert(Image.Format.Rgba8);
        var data = image.GetData();
        var pixels = image.GetWidth() * image.GetHeight();
        double luminance = 0.0;
        double luminanceSquared = 0.0;
        var darkPixels = 0;
        for (var offset = 0; offset < data.Length; offset += 4)
        {
            var value = (Fo1HexCaptureNumericContracts.AcceptanceDouble0Point2126 * data[offset] + Fo1HexCaptureNumericContracts.AcceptanceDouble0Point7152 * data[offset + 1] + Fo1HexCaptureNumericContracts.AcceptanceDouble0Point0722 * data[offset + 2]) / Fo1HexCaptureNumericContracts.AcceptanceDouble255Point0;
            luminance += value;
            luminanceSquared += value * value;
            if (value < Fo1HexCaptureNumericContracts.AcceptanceDouble0Point035)
                darkPixels++;
        }
        var mean = luminance / pixels;
        var deviation = Math.Sqrt(Math.Max(0.0, luminanceSquared / pixels - mean * mean));
        var darkFraction = (double)darkPixels / pixels;
        var failure = image.GetWidth() != Fo1HexCaptureNumericContracts.AcceptanceInt1280 || image.GetHeight() != Fo1HexCaptureNumericContracts.AcceptanceInt720
            ? "unexpected-size"
            : mean < minimumMean
                ? "mean-luminance"
                : deviation < minimumDeviation
                    ? "luminance-deviation"
                    : darkFraction > maximumDarkFraction
                        ? "dark-fraction"
                        : null;
        var error = image.SavePng(path);
        if (error != Error.Ok)
            throw new InvalidOperationException($"Could not save Fallout hex capture: {error}");
        using var stream = File.OpenRead(path);
        var evidence = new
        {
            path,
            bytes = stream.Length,
            width = image.GetWidth(),
            height = image.GetHeight(),
            meanLuminance = mean,
            luminanceDeviation = deviation,
            darkFraction,
            visualGatePassed = failure is null,
            visualGateFailure = failure,
            sha256 = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant(),
        };
        return new CaptureResult(failure is null, evidence);
    }

    private static void WriteReport(string path, object report)
    {
        File.WriteAllText(
            path,
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) +
                System.Environment.NewLine);
    }

    private readonly record struct CaptureResult(bool Passed, object Evidence);
}
