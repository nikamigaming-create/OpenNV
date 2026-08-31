using System.Security.Cryptography;
using System.Text.Json;

namespace OpenNV.Runtime.Campaigns.Fallout1;

internal sealed record Fo1DestinationPresentationContract(
    Fo1CampaignPresentationCatalog Catalog,
    Fo1CampaignMapPresentation Map,
    int DefaultTileId,
    string SourceMapSha256)
{
    internal static Fo1DestinationPresentationContract Load(
        string path,
        Fo1ExitGridTransitionContract transition)
    {
        var catalog = Fo1CampaignPresentationContract.Load(path);
        var mapId = Path.GetFileNameWithoutExtension(transition.DestinationMapName).ToLowerInvariant();
        if (catalog.Maps.Count != 1 || catalog.Maps[0].Id != mapId ||
            catalog.Maps[0].SourceFile != transition.DestinationMapName)
            throw new InvalidOperationException("Fallout destination presentation does not contain exactly the transition MAP.");
        var mapPath = catalog.Maps[0].Path;
        var bytes = File.ReadAllBytes(mapPath);
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        if (root.GetProperty("source").GetProperty("mapSha256").GetString() != transition.DestinationMapSha256)
            throw new InvalidOperationException("Fallout destination presentation MAP source hash drifted.");
        var defaultTileId = root.GetProperty("grid").GetProperty("defaultTileId").GetInt32();
        var map = Fo1CampaignPresentationContract.LoadMap(catalog, mapId);
        var result = new Fo1DestinationPresentationContract(
            catalog,
            map,
            defaultTileId,
            transition.DestinationMapSha256);
        result.Validate(transition);
        return result;
    }

    internal void Validate(Fo1ExitGridTransitionContract transition)
    {
        if (!string.Equals(SourceMapSha256, transition.DestinationMapSha256, StringComparison.OrdinalIgnoreCase) ||
            !Map.SourceFile.Equals(transition.DestinationMapName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Fallout destination presentation/exit-grid join drifted.");
        var elevation = Map.Elevations.SingleOrDefault(row => row.Elevation == transition.DestinationElevation)
            ?? throw new InvalidOperationException("Fallout destination elevation is absent from its presentation cache.");
        if (elevation.FloorIds[Fo1HexMath.FloorIndex(transition.DestinationTile)] == DefaultTileId ||
            elevation.Blockers.Any(blocker => blocker.Tile == transition.DestinationTile))
            throw new InvalidOperationException("Fallout destination entry tile is not controllable in its source MAP cache.");
    }

    internal object Report(Fo1ExitGridTransitionContract transition) => new
    {
        path = Catalog.CampaignPath,
        sha256 = Catalog.CampaignSha256,
        sourceMapSha256 = SourceMapSha256,
        map = Map.SourceFile,
        mapId = Map.Id,
        destinationTile = transition.DestinationTile,
        destinationElevation = transition.DestinationElevation,
        sourcePlayerFallback = false,
        sourceReferencePresentation = true,
    };
}
