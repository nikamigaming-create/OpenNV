using System.Text.Json;
using Godot;

namespace OpenNV.Runtime.Campaigns.Fallout1;

internal static class Fo1CampaignBuildProof
{
    internal static async Task Run(
        Node host,
        Fo1CampaignPresentationCatalog catalog,
        Fo1CampaignPresentationViewer viewer,
        string reportPath)
    {
        try
        {
            var output = Path.GetFullPath(reportPath);
            if (File.Exists(output) || Directory.Exists(output))
                throw new InvalidOperationException(
                    $"Refusing to overwrite Fallout campaign build proof: {output}");
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            viewer.SetStatusVisible(false);
            var views = new List<Fo1CampaignMapViewCoverage>();
            foreach (var mapRow in catalog.Maps)
            {
                var map = Fo1CampaignPresentationContract.LoadMap(catalog, mapRow.Id);
                for (var elevationIndex = 0; elevationIndex < map.Elevations.Count; elevationIndex++)
                {
                    var coverage = viewer.LoadForProof(map, elevationIndex);
                    views.Add(coverage);
                    await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
                }
                GD.Print(
                    $"OPENNV_FO1_CAMPAIGN_MAP_BUILD map={map.Id} " +
                    $"elevations={map.Elevations.Count} views={views.Count}");
            }
            var report = new
            {
                schema = "opennv-fo1-campaign-build-proof/v1",
                status = "pass-all-connected-wall-topology-views-built-headless-not-rendered",
                campaign = catalog.CampaignPath,
                campaignSha256 = catalog.CampaignSha256,
                maps = views.Select(row => row.MapId).Distinct(StringComparer.Ordinal).Count(),
                elevations = views.Count,
                renderedFloorPatches = views.Sum(row => row.RenderedFloorPatches),
                spritePlacements = views.Sum(row => row.SpritePlacements),
                mobs = views.Sum(row => row.Mobs),
                doors = views.Sum(row => row.Doors),
                blockers = views.Sum(row => row.Blockers),
                renderedWallHexes = views.Sum(row => row.RenderedWallHexes),
                wallComponents = views.Sum(row => row.WallComponents),
                wallBoundaryEdges = views.Sum(row => row.WallBoundaryEdges),
                wallTriangles = views.Sum(row => row.WallTriangles),
                blockingCollisionWallHexes = views.Sum(row => row.BlockingCollisionWallHexes),
                skippedSpriteObjects = views.Sum(row => row.SkippedSpriteObjects),
                views,
                promotion = new
                {
                    transportedMaps = catalog.Maps.Count,
                    sourceReferencePreparedMaps = catalog.Maps.Count,
                    runtimeValidatedMaps = catalog.Maps.Count,
                    runtimeConstructedMaps = catalog.Maps.Count,
                    runtimeConstructedElevations = views.Count,
                    renderedMaps = 0,
                    interactiveGameplayMaps = 0,
                    questExecutableMaps = 0,
                    firstPersonReadyMaps = 0,
                    openXrAcceptedMaps = 0,
                },
                windowsAppControlUsed = false,
                foregroundInputInjected = false,
            };
            File.WriteAllText(
                output,
                JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) +
                    System.Environment.NewLine);
            GD.Print(
                $"OPENNV_FO1_CAMPAIGN_BUILD_PASS maps={catalog.Maps.Count} " +
                $"elevations={views.Count} placements={views.Sum(row => row.SpritePlacements)}");
            host.GetTree().Quit(0);
        }
        catch (Exception exception)
        {
            GD.PushError($"OPENNV_FO1_CAMPAIGN_BUILD_FAIL {exception.Message}");
            host.GetTree().Quit(1);
        }
    }
}
