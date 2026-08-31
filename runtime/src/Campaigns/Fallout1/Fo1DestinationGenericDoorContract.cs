using System.Security.Cryptography;
using System.Text.Json;

namespace OpenNV.Runtime.Campaigns.Fallout1;

/// <summary>One explicit unscripted MAP door whose owned blocker becomes walkable when activated.</summary>
internal sealed record Fo1DestinationGenericDoorContract(
    string Path, string Sha256, Fo1TacticalSession.SourceDoorContract Door,
    IReadOnlyList<int> SourceWalkMaskRoute)
{
    private const string Schema = "opennv-fo1-destination-generic-door/v1";
    private const int NoScriptIndex = -1;
    private const string NoScriptId = "ffffffff";

    internal static Fo1DestinationGenericDoorContract Load(
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
            Required(root, "status") != "compiled-owned-map-unscripted-generic-door-open-passability")
            throw new InvalidOperationException("Unexpected Fallout destination generic-door descriptor.");
        var inputs = root.GetProperty("inputs");
        var presentation = inputs.GetProperty("presentation");
        var presentationMap = inputs.GetProperty("presentationMap");
        if (Required(presentation, "path") != destination.Catalog.CampaignPath ||
            Required(presentation, "sha256") != destination.Catalog.CampaignSha256 ||
            Required(presentationMap, "path") != destination.Catalog.Maps.Single().Path ||
            Required(presentationMap, "sha256") != destination.Catalog.Maps.Single().Sha256)
            throw new InvalidOperationException("Fallout generic-door descriptor presentation join drifted.");
        var target = root.GetProperty("destination");
        if (Required(target, "mapId") != destination.Map.Id ||
            Required(target, "sourceFile") != transition.DestinationMapName ||
            Required(target, "sourceMapSha256") != transition.DestinationMapSha256 ||
            target.GetProperty("elevation").GetInt32() != transition.DestinationElevation ||
            target.GetProperty("entryTile").GetInt32() != transition.DestinationTile)
            throw new InvalidOperationException("Fallout generic-door descriptor MAP join drifted.");
        var source = root.GetProperty("door");
        if (Required(source.GetProperty("prototype"), "subtypeName") != "door" ||
            Required(source.GetProperty("script"), "semantics") !=
                "no-script-boundary-generic-door-open-passability-only" ||
            source.GetProperty("script").GetProperty("mapScriptIndex").GetInt32() != NoScriptIndex ||
            Required(source.GetProperty("script"), "sid") != NoScriptId ||
            source.GetProperty("closed").GetProperty("walkable").GetBoolean() ||
            !source.GetProperty("open").GetProperty("walkable").GetBoolean() ||
            Required(source, "interactionActionPoints") != "not-source-backed" ||
            Required(source, "sound") != "unsupported-fail-closed" ||
            Required(source, "animationTiming") != "unsupported-fail-closed")
            throw new InvalidOperationException("Fallout generic-door descriptor has unsupported behavior.");
        var prototypeSha256 = Required(source.GetProperty("prototype"), "sha256");
        var artSha256 = Required(source.GetProperty("art"), "sha256");
        if (!Hash(prototypeSha256) || !Hash(artSha256) ||
            Required(source.GetProperty("art"), "filename").Length == 0)
            throw new InvalidOperationException("Fallout generic-door descriptor has invalid owned resource hashes.");
        var door = new Fo1TacticalSession.SourceDoorContract(
            source.GetProperty("serial").GetInt32(), source.GetProperty("tile").GetInt32(),
            Required(source, "pid"), Required(source, "fid"), prototypeSha256, InitiallyBlocked: true);
        door.Validate();
        var route = root.GetProperty("sourceWalkMaskRoute").GetProperty("pathTiles")
            .EnumerateArray().Select(value => value.GetInt32()).ToArray();
        if (route.Length == 0 || route[0] != transition.DestinationTile ||
            route.Any(tile => tile is < 0 or >= Fo1HexMath.Width * Fo1HexMath.Height) ||
            !Fo1HexMath.AreNeighbors(route[^1], door.Tile) ||
            route.Zip(route.Skip(1)).Any(pair => !Fo1HexMath.AreNeighbors(pair.First, pair.Second)))
            throw new InvalidOperationException("Fallout generic-door descriptor route is not source-adjacent.");
        return new Fo1DestinationGenericDoorContract(resolved, sha256, door, route);
    }

    internal object Report(bool open) => new
    {
        schema = Schema,
        path = Path,
        sha256 = Sha256,
        door = Door.Report(open),
        sourceWalkMaskRoute = SourceWalkMaskRoute,
        interactionActionPoints = "not-source-backed",
        sound = "unsupported-fail-closed",
        animationTiming = "unsupported-fail-closed",
    };

    private static string Required(JsonElement source, string name) =>
        source.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String &&
        !string.IsNullOrWhiteSpace(value.GetString()) ? value.GetString()! :
        throw new InvalidOperationException($"Fallout generic-door descriptor is missing {name}.");

    private static bool Hash(string value) => value.Length == 64 && value.All(Uri.IsHexDigit);
}
