using System.Security.Cryptography;
using System.Text.Json;

namespace OpenNV.Runtime.Campaigns.Fallout1;

/// <summary>Verified MAP exit-grid boundary; it names, but never synthesizes, the next scene.</summary>
internal sealed record Fo1ExitGridTransitionContract(
    string Path, string Sha256, string SourceMapSha256, int SourceMapIndex, string SourceMapName,
    int DestinationMapIndex, string DestinationMapName, string DestinationMapSha256,
    int DestinationTile, int DestinationElevation, int DestinationRotation,
    IReadOnlyList<Fo1ExitGridTransitionContract.Trigger> Triggers)
{
    private const string Schema = "opennv-fo1-exit-grid-transition/v1";
    internal sealed record Trigger(int Serial, int Tile, string Pid, string PrototypeSha256);

    internal static Fo1ExitGridTransitionContract Load(string path)
    {
        var resolved = VerifiedGltfLoader.ResolvePath(path);
        var bytes = File.ReadAllBytes(resolved);
        var sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        if (Required(root, "schema") != Schema || Required(root, "status") != "compiled-owned-map-world-transition")
            throw new InvalidOperationException("Unexpected Fallout 1 exit-grid transition descriptor.");
        var source = root.GetProperty("sourceMap");
        var destination = root.GetProperty("destination");
        var triggers = root.GetProperty("triggers").EnumerateArray().Select(row => new Trigger(
            row.GetProperty("serial").GetInt32(), row.GetProperty("tile").GetInt32(),
            Required(row, "pid"), Required(row, "prototypeSha256"))).ToArray();
        var contract = new Fo1ExitGridTransitionContract(
            resolved, sha256, Required(source, "sha256"), source.GetProperty("mapIndex").GetInt32(), Required(source, "name"),
            destination.GetProperty("mapIndex").GetInt32(), Required(destination, "name"), Required(destination, "mapSha256"),
            destination.GetProperty("tile").GetInt32(), destination.GetProperty("elevation").GetInt32(),
            destination.GetProperty("rotation").GetInt32(), triggers);
        contract.Validate();
        return contract;
    }

    internal void ValidateAgainstScene(string mapSha256)
    {
        if (!string.Equals(SourceMapSha256, mapSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Fallout 1 exit-grid descriptor belongs to a different MAP source.");
    }

    internal bool IsTrigger(int tile) => Triggers.Any(trigger => trigger.Tile == tile);

    internal object Report(int? activatedTile, bool destinationSceneLoaded = false) => new
    {
        schema = Schema,
        path = Path,
        sha256 = Sha256,
        source = new { mapIndex = SourceMapIndex, name = SourceMapName, sha256 = SourceMapSha256 },
        destination = new { mapIndex = DestinationMapIndex, name = DestinationMapName, mapSha256 = DestinationMapSha256, tile = DestinationTile, elevation = DestinationElevation, rotation = DestinationRotation },
        triggers = Triggers.Select(trigger => new { trigger.Serial, trigger.Tile, trigger.Pid, trigger.PrototypeSha256 }).ToArray(),
        activatedTile,
        transitionCommitted = activatedTile is not null,
        destinationSceneLoaded,
    };

    private void Validate()
    {
        if (!Hash(SourceMapSha256) || !Hash(DestinationMapSha256) || SourceMapIndex < 0 || DestinationMapIndex < 0 ||
            SourceMapIndex == DestinationMapIndex || string.IsNullOrWhiteSpace(SourceMapName) || string.IsNullOrWhiteSpace(DestinationMapName) ||
            DestinationTile is < 0 or >= Fo1HexMath.Width * Fo1HexMath.Height || DestinationElevation < 0 ||
            DestinationRotation is < 0 or >= Fo1HexMath.DirectionCount || Triggers.Count == 0 ||
            Triggers.Select(trigger => trigger.Serial).Distinct().Count() != Triggers.Count ||
            Triggers.Select(trigger => trigger.Tile).Distinct().Count() != Triggers.Count ||
            Triggers.Any(trigger => trigger.Tile is < 0 or >= Fo1HexMath.Width * Fo1HexMath.Height || string.IsNullOrWhiteSpace(trigger.Pid) || !Hash(trigger.PrototypeSha256)))
            throw new InvalidOperationException("Fallout 1 exit-grid transition descriptor is incomplete.");
    }

    private static string Required(JsonElement source, string property) => source.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()) ? value.GetString()! :
        throw new InvalidOperationException($"Fallout 1 exit-grid descriptor is missing {property}.");
    private static bool Hash(string value) => value.Length == 64 && value.All(Uri.IsHexDigit);
}
