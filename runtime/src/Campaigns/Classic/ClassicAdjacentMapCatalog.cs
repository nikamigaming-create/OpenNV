using System.Security.Cryptography;
using System.Text.Json;

using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Campaigns.Classic;

internal sealed record ClassicReciprocalMapJoins(
    IReadOnlyList<ClassicMapJoin> Forward,
    IReadOnlyList<ClassicMapJoin> Reverse);

internal sealed record ClassicAdjacentMapCatalog(
    string Path,
    string Sha256,
    IReadOnlyList<ClassicReciprocalMapJoins> ReciprocalJoins)
{
    internal ClassicMapJoinState CommitAt(
        int mapIndex,
        string mapSha256,
        int tile,
        int elevation)
    {
        var candidates = ReciprocalJoins
            .SelectMany(row => row.Forward.Concat(row.Reverse))
            .Where(row =>
                row.Source.MapIndex == mapIndex &&
                row.Source.MapSha256.Equals(
                    mapSha256, StringComparison.OrdinalIgnoreCase) &&
                row.Source.Tile == tile && row.Source.Elevation == elevation)
            .OrderBy(row => row.SourceSerial)
            .ToArray();
        if (candidates.Length == 0)
            throw new InvalidOperationException(
                "Classic active MAP state has no compiled reciprocal join.");
        var join = candidates[0];
        if (candidates.Any(row => row.Destination != join.Destination))
            throw new InvalidOperationException(
                "Classic active MAP state has conflicting compiled destinations.");
        return ClassicMapJoinOwner.Commit(join, mapIndex, mapSha256, tile, elevation);
    }

    internal static ClassicAdjacentMapCatalog Load(string path)
    {
        var resolved = VerifiedGltfLoader.ResolvePath(path);
        var bytes = File.ReadAllBytes(resolved);
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        if (!root.TryGetProperty("mapJoins", out var rows) ||
            rows.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException(
                "Classic adjacent-MAP catalog has no compiled join array.");
        var joins = rows.EnumerateArray().Select(ReadReciprocal).ToArray();
        if (joins.Length == 0)
            throw new InvalidOperationException(
                "Classic adjacent-MAP catalog has no reciprocal joins.");
        return new ClassicAdjacentMapCatalog(
            resolved,
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            joins);
    }

    private static ClassicReciprocalMapJoins ReadReciprocal(
        JsonElement source)
    {
        if (!source.GetProperty("reciprocal").GetBoolean())
            throw new InvalidOperationException("Classic MAP join is not reciprocal.");
        var forwardRows = source.GetProperty("forwardExitGrids").EnumerateArray().ToArray();
        var reverseRows = source.GetProperty("reverseExitGrids").EnumerateArray().ToArray();
        if (forwardRows.Length == 0 || reverseRows.Length == 0)
            throw new InvalidOperationException(
                "Classic reciprocal MAP join has no exit-grid records.");
        var forward = forwardRows
            .Select(row => ReadJoin(row, source.GetProperty("destinationMap")))
            .ToArray();
        var reverse = reverseRows
            .Select(row => ReadJoin(row, source.GetProperty("sourceMap")))
            .ToArray();
        foreach (var join in forward)
            ClassicMapJoinOwner.ValidateReciprocal(join, reverse[0]);
        foreach (var join in reverse)
            ClassicMapJoinOwner.ValidateReciprocal(forward[0], join);
        return new ClassicReciprocalMapJoins(forward, reverse);
    }

    private static ClassicMapJoin ReadJoin(JsonElement source, JsonElement destinationMap)
    {
        var sourceMap = source.GetProperty("source");
        var destination = source.GetProperty("destination");
        return new ClassicMapJoin(
            source.GetProperty("serial").GetInt32(),
            new ClassicMapEndpoint(
                sourceMap.GetProperty("mapIndex").GetInt32(),
                sourceMap.GetProperty("mapName").GetString(),
                Required(sourceMap, "mapSha256"),
                sourceMap.GetProperty("tile").GetInt32(),
                sourceMap.GetProperty("elevation").GetInt32(),
                null),
            new ClassicMapEndpoint(
                destination.GetProperty("mapIndex").GetInt32(),
                destinationMap.GetProperty("mapName").GetString(),
                Required(destinationMap, "mapSha256"),
                destination.GetProperty("tile").GetInt32(),
                destination.GetProperty("elevation").GetInt32(),
                destination.GetProperty("rotation").GetInt32()));
    }

    private static string Required(JsonElement source, string property) =>
        source.GetProperty(property).GetString() is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException(
                $"Classic adjacent-MAP string is empty: {property}");
}
