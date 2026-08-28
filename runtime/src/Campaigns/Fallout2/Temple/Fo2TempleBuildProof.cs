using System.Text.Json;
using Godot;

namespace OpenNV.Runtime.Campaigns.Fallout2.Temple;

internal static class Fo2TempleBuildProof
{
    internal static async Task Run(
        Node host,
        Fo2TempleSceneCoverage coverage,
        string reportPath)
    {
        try
        {
            var output = Path.GetFullPath(reportPath);
            if (File.Exists(output) || Directory.Exists(output))
                throw new InvalidOperationException(
                    $"Refusing to overwrite Fallout 2 Temple build proof: {output}");
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            var report = new
            {
                schema = "opennv-fo2-temple-runtime-build-proof/v1",
                status = "pass-source-reference-3d-scene-built-headless-not-rendered",
                cacheManifest = coverage.ManifestPath,
                cacheManifestSha256 = coverage.ManifestSha256,
                sourceManifest = coverage.SourceManifestPath,
                sourceManifestSha256 = coverage.SourceManifestSha256,
                sourceProfileId = coverage.SourceProfileId,
                map = new
                {
                    index = Fo2TemplePresentationCatalog.MapIndex,
                    name = "ARTEMPLE.MAP",
                    sha256 = coverage.MapSha256,
                    entryTile = coverage.EntryTile,
                    entryElevation = coverage.EntryElevation,
                    entryRotation = coverage.EntryRotation,
                    entryWorldMeters = new[]
                    {
                        coverage.EntryWorldMeters.X,
                        coverage.EntryWorldMeters.Y,
                        coverage.EntryWorldMeters.Z,
                    },
                },
                verifiedArtifacts = coverage.VerifiedArtifacts,
                verifiedResources = coverage.VerifiedResources,
                tileBindings = coverage.TileBindings,
                objectArtifactBindings = coverage.ObjectArtifactBindings,
                constructedFloorPatches = coverage.ConstructedFloorPatches,
                constructedRoofPatches = coverage.ConstructedRoofPatches,
                placedTopLevelObjects = coverage.PlacedTopLevelObjects,
                inventoryObjectsNotPlaced = coverage.InventoryObjectsNotPlaced,
                sourcePixelsPerMeter = coverage.SourcePixelsPerMeter,
                floorMeshInstances = coverage.FloorMeshInstances,
                objectSpriteNodes = coverage.ObjectSpriteNodes,
                presentation = "source-bound 2.5D FRM planes in a 3D Godot hex coordinate space",
                promotion = new
                {
                    transported = true,
                    decodedPresentationAssets = true,
                    runtimeManifestValidated = true,
                    runtimeSceneConstructed = true,
                    rendered = false,
                    interactive = false,
                    characterFlow = false,
                    gameplay = false,
                    saveState = false,
                    parityReviewed = false,
                    headsetAccepted = false,
                    runtimeReady = false,
                },
                unsupported = new[]
                {
                    "molded wall shells, collision, and walk masks",
                    "MAP scripts, doors, combat, and actor behavior",
                    "Chosen One character creation and gameplay/save state",
                    "camera/lighting parity, retail differential, FPS, and OpenXR",
                },
                windowsAppControlUsed = false,
                foregroundInputInjected = false,
            };
            File.WriteAllText(
                output,
                JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) +
                    System.Environment.NewLine);
            GD.Print(
                $"OPENNV_FO2_TEMPLE_BUILD_PASS floor={coverage.ConstructedFloorPatches} " +
                $"objects={coverage.PlacedTopLevelObjects} pngs={coverage.VerifiedArtifacts}");
            host.GetTree().Quit(0);
        }
        catch (Exception exception)
        {
            GD.PushError($"OPENNV_FO2_TEMPLE_BUILD_FAIL {exception.Message}");
            host.GetTree().Quit(1);
        }
    }
}
