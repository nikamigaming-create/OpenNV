using System.Security.Cryptography;
using System.Text.Json;

namespace OpenNV.Runtime.Campaigns.Fallout1;

/// <summary>One explicitly compiled, source-MAP container interaction for a committed destination.</summary>
internal sealed record Fo1DestinationInventoryInteractionContract(
    string Path, string Sha256, Fo1TacticalSession.MapInventoryHost Host,
    IReadOnlyList<int> SourceWalkMaskRoute)
{
    private const string Schema = "opennv-fo1-destination-inventory-interaction/v1";

    internal static Fo1DestinationInventoryInteractionContract Load(
        string path,
        Fo1DestinationPresentationContract destination,
        Fo1ExitGridTransitionContract transition)
    {
        var resolved = VerifiedGltfLoader.ResolvePath(path);
        var bytes = File.ReadAllBytes(resolved);
        var sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        if (Required(root, "schema") != Schema ||
            Required(root, "status") != "compiled-owned-map-nearest-reachable-container-interaction")
            throw new InvalidOperationException("Unexpected Fallout destination inventory interaction descriptor.");
        var inputs = root.GetProperty("inputs");
        var presentation = inputs.GetProperty("presentation");
        var presentationMap = inputs.GetProperty("presentationMap");
        if (Required(presentation, "path") != destination.Catalog.CampaignPath ||
            Required(presentation, "sha256") != destination.Catalog.CampaignSha256 ||
            Required(presentationMap, "path") != destination.Catalog.Maps.Single().Path ||
            Required(presentationMap, "sha256") != destination.Catalog.Maps.Single().Sha256)
            throw new InvalidOperationException("Fallout destination inventory descriptor presentation join drifted.");
        var target = root.GetProperty("destination");
        if (Required(target, "mapId") != destination.Map.Id ||
            Required(target, "sourceFile") != transition.DestinationMapName ||
            Required(target, "sourceMapSha256") != transition.DestinationMapSha256 ||
            target.GetProperty("elevation").GetInt32() != transition.DestinationElevation ||
            target.GetProperty("entryTile").GetInt32() != transition.DestinationTile)
            throw new InvalidOperationException("Fallout destination inventory descriptor MAP join drifted.");
        var source = root.GetProperty("host");
        if (Required(source, "schema") != "opennv-fo1-map-inventory-host/v1")
            throw new InvalidOperationException("Unexpected Fallout destination inventory host schema.");
        var host = new Fo1TacticalSession.MapInventoryHost(
            source.GetProperty("serial").GetInt32(), source.GetProperty("tile").GetInt32(),
            Required(source, "pid"), Required(source, "prototypeSha256"),
            source.GetProperty("items").EnumerateArray().Select(item => new Fo1TacticalSession.MapInventoryItem(
                item.GetProperty("index").GetInt32(), item.GetProperty("serial").GetInt32(),
                Required(item, "symbol"), Required(item, "displayName"), Required(item, "pid"),
                item.GetProperty("quantity").GetInt32(), Required(item, "prototypeSha256"),
                Required(item.GetProperty("profile"), "subtypeName"))).ToArray());
        host.Validate();
        var route = root.GetProperty("sourceWalkMaskRoute").GetProperty("pathTiles")
            .EnumerateArray().Select(value => value.GetInt32()).ToArray();
        if (route.Length == 0 || route[0] != transition.DestinationTile ||
            route.Any(tile => tile is < 0 or >= Fo1HexMath.Width * Fo1HexMath.Height) ||
            !Fo1HexMath.AreNeighbors(route[^1], host.Tile) ||
            route.Zip(route.Skip(1)).Any(pair => !Fo1HexMath.AreNeighbors(pair.First, pair.Second)))
            throw new InvalidOperationException("Fallout destination inventory descriptor route is not source-adjacent.");
        return new Fo1DestinationInventoryInteractionContract(resolved, sha256, host, route);
    }

    internal object Report() => new
    {
        schema = Schema, path = Path, sha256 = Sha256,
        host = new { Host.Serial, Host.Tile, Host.Pid, Host.PrototypeSha256, items = Host.Items },
        sourceWalkMaskRoute = SourceWalkMaskRoute,
    };

    private static string Required(JsonElement source, string name) =>
        source.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String &&
        !string.IsNullOrWhiteSpace(value.GetString()) ? value.GetString()! :
        throw new InvalidOperationException($"Fallout destination inventory descriptor is missing {name}.");
}
