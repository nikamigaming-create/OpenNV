using System.Security.Cryptography;
using System.Text.Json;
using Godot;

namespace OpenNV.Runtime.Campaigns.Fallout2.Temple;

internal sealed record Fo2TempleFloorSupportProfile(string Mode, float SurfaceMeters);

internal sealed record Fo2TempleWalkMaskProfile(
    string Mode,
    bool OverlayVisibleByDefault,
    float OverlayHeightMeters,
    float OverlayRadiusScale,
    Color OverlayColor);

internal sealed record Fo2TempleWallProfile(
    string Mode,
    int SourceObjectType,
    string CollisionMode,
    float CellRadiusScale,
    float HeightMeters,
    float GroundSinkMeters,
    float Roughness,
    float Metallic,
    Color UnresolvedSourceAlbedo);

internal sealed record Fo2TempleTopologyProfile(
    string ResourcePath,
    string Sha256,
    string Id,
    int DefaultFloorTileId,
    uint ObjectNoBlockFlag,
    uint ObjectMultihexFlag,
    string MultihexCoverage,
    Fo2TempleFloorSupportProfile FloorSupport,
    Fo2TempleWalkMaskProfile WalkMask,
    Fo2TempleWallProfile Wall)
{
    private const string ProfileResourcePath = "res://config/fo2-temple-topology-v1.json";

    internal static Fo2TempleTopologyProfile Load(Fo2TemplePresentationCatalog catalog)
    {
        var bytes = Godot.FileAccess.GetFileAsBytes(ProfileResourcePath);
        if (bytes.Length == 0)
            throw new FileNotFoundException(
                "Fallout 2 Temple topology profile is missing.",
                ProfileResourcePath);
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        var map = root.GetProperty("map");
        var semantics = root.GetProperty("sourceSemantics");
        var floor = root.GetProperty("floorSupport");
        var walk = root.GetProperty("walkMask");
        var wall = root.GetProperty("wallGeometry");
        var promotion = root.GetProperty("promotion");
        if (RequiredString(root, "schema") != "opennv-fo2-temple-topology-runtime/v1" ||
            RequiredString(root, "campaign") != "Fallout2" ||
            map.GetProperty("index").GetInt32() != Fo2TemplePresentationCatalog.MapIndex ||
            RequiredString(map, "name") != "ARTEMPLE.MAP" ||
            RequiredString(map, "sha256") != catalog.MapSha256 ||
            RequiredString(semantics, "mode") !=
                "classic-fallout-map-floor-and-object-flags-v1" ||
            RequiredString(floor, "mode") != "non-default-source-floor-patches-v1" ||
            RequiredString(walk, "mode") !=
                "non-default-floor-art-minus-source-blocking-object-hexes-v1" ||
            RequiredString(wall, "mode") != "source-wall-hex-union-v1" ||
            RequiredString(wall, "collisionMode") != "blocking-wall-hex-union-v1" ||
            promotion.GetProperty("runtimeReady").GetBoolean() ||
            promotion.GetProperty("collisionParity").GetBoolean() ||
            promotion.GetProperty("walkabilityParity").GetBoolean())
            throw new InvalidOperationException(
                "Unexpected Fallout 2 Temple topology runtime profile.");

        var profile = new Fo2TempleTopologyProfile(
            ProfileResourcePath,
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            RequiredString(root, "id"),
            semantics.GetProperty("defaultFloorTileId").GetInt32(),
            semantics.GetProperty("objectNoBlockFlag").GetUInt32(),
            semantics.GetProperty("objectMultihexFlag").GetUInt32(),
            RequiredString(semantics, "multihexCoverage"),
            new Fo2TempleFloorSupportProfile(
                RequiredString(floor, "mode"),
                Finite(floor, "surfaceMeters")),
            new Fo2TempleWalkMaskProfile(
                RequiredString(walk, "mode"),
                walk.GetProperty("overlayVisibleByDefault").GetBoolean(),
                Finite(walk, "overlayHeightMeters"),
                Finite(walk, "overlayRadiusScale"),
                ReadColor(walk.GetProperty("overlayColor"))),
            new Fo2TempleWallProfile(
                RequiredString(wall, "mode"),
                wall.GetProperty("sourceObjectType").GetInt32(),
                RequiredString(wall, "collisionMode"),
                Finite(wall, "cellRadiusScale"),
                Finite(wall, "heightMeters"),
                Finite(wall, "groundSinkMeters"),
                Finite(wall, "roughness"),
                Finite(wall, "metallic"),
                ReadColor(wall.GetProperty("unresolvedSourceAlbedo"))));
        if (profile.DefaultFloorTileId != catalog.DefaultFloorTileId ||
            profile.ObjectNoBlockFlag != 0x10 ||
            profile.ObjectMultihexFlag != 0x800 ||
            profile.MultihexCoverage != "central-source-hex-only-unresolved" ||
            profile.Wall.SourceObjectType != 3 ||
            profile.FloorSupport.SurfaceMeters != 0.0f ||
            profile.WalkMask.OverlayHeightMeters <= 0.0f ||
            profile.WalkMask.OverlayRadiusScale is <= 0.0f or > 1.0f ||
            profile.Wall.CellRadiusScale is < 1.0f or > 1.08f ||
            profile.Wall.HeightMeters <= 1.0f ||
            profile.Wall.GroundSinkMeters is < 0.0f ||
            profile.Wall.GroundSinkMeters >= profile.Wall.HeightMeters ||
            profile.Wall.Roughness is < 0.0f or > 1.0f ||
            profile.Wall.Metallic is < 0.0f or > 1.0f)
            throw new InvalidOperationException(
                "Fallout 2 Temple topology runtime dimensions drifted.");
        return profile;
    }

    private static string RequiredString(JsonElement source, string property)
    {
        var value = source.GetProperty(property).GetString();
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException(
                $"Fallout 2 Temple topology string is empty: {property}");
        return value;
    }

    private static float Finite(JsonElement source, string property)
    {
        var value = source.GetProperty(property).GetSingle();
        if (!float.IsFinite(value))
            throw new InvalidOperationException(
                $"Fallout 2 Temple topology number is invalid: {property}");
        return value;
    }

    private static Color ReadColor(JsonElement source)
    {
        var values = source.EnumerateArray().Select(row => row.GetSingle()).ToArray();
        if (values.Length != 4 || values.Any(value => !float.IsFinite(value) || value is < 0.0f or > 1.0f))
            throw new InvalidOperationException("Fallout 2 Temple topology color is invalid.");
        return new Color(values[0], values[1], values[2], values[3]);
    }
}
