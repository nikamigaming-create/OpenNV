using System.Security.Cryptography;
using System.Text.Json;
using Godot;

namespace OpenNV.Runtime.Campaigns.Fallout2.Temple;

internal sealed record Fo2ArroyoInputBinding(string Action, Key PhysicalKey);

internal sealed record Fo2ArroyoPlayerProfile(
    string ResourcePath,
    string Sha256,
    string Id,
    string FloorCollisionMode,
    string BlockedMovementMode,
    float CapsuleRadiusMeters,
    float CapsuleHeightMeters,
    float SpawnCenterHeightMeters,
    float SpeedMetersPerSecond,
    float GravityMetersPerSecondSquared,
    float GroundVelocityMetersPerSecond,
    float FloorSnapLengthMeters,
    float SafeMarginMeters,
    float MaximumFloorAngleRadians,
    float CameraSizeMeters,
    Vector3 CameraOffsetMeters,
    float CameraLookHeightMeters,
    float CameraNearMeters,
    float CameraFarMeters,
    Fo2ArroyoInputBinding MoveLeft,
    Fo2ArroyoInputBinding MoveRight,
    Fo2ArroyoInputBinding MoveForward,
    Fo2ArroyoInputBinding MoveBackward,
    Key AcceptanceKey,
    int AcceptanceFirstNeighborTile,
    int AcceptanceLastWalkableTile,
    int AcceptanceFirstRejectedTile,
    int AcceptanceBlockingObjectSerial,
    string AcceptanceBlockingObjectFid,
    int AcceptanceCoLocatedExitGridSerial,
    int AcceptanceCoLocatedExitGridObjectType,
    IReadOnlyList<int> AcceptanceCoLocatedExitGridDestination,
    float AcceptanceGroundHeightToleranceMeters,
    int AcceptanceMinimumRejectedPhysicsFrames,
    int AcceptanceMaximumPhysicsFrames)
{
    private const string ProfileResourcePath =
        "res://config/fo2-arroyo-player-runtime-v1.json";
    private const float MaximumCapsuleRadiusMeters = 0.5f;
    private const int FidHexCharacters = 8;

    internal static Fo2ArroyoPlayerProfile Load(Fo2ArroyoCavesPresentationCatalog catalog)
    {
        var bytes = Godot.FileAccess.GetFileAsBytes(ProfileResourcePath);
        if (bytes.Length == 0)
            throw new FileNotFoundException(
                "Fallout 2 Arroyo player runtime profile is missing.",
                ProfileResourcePath);
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        var map = root.GetProperty("map");
        var arrival = root.GetProperty("arrival");
        var semantics = root.GetProperty("sourceSemantics");
        var player = root.GetProperty("player");
        var camera = root.GetProperty("camera");
        var input = root.GetProperty("input");
        var acceptance = root.GetProperty("acceptanceTrack");
        var promotion = root.GetProperty("promotion");
        if (RequiredString(root, "schema") != "opennv-fo2-arroyo-player-runtime/v1" ||
            RequiredString(root, "campaign") != "Fallout2" ||
            map.GetProperty("index").GetInt32() != Fo2ArroyoCavesPresentationCatalog.MapIndex ||
            RequiredString(map, "name") != "ARCAVES.MAP" ||
            RequiredString(map, "sha256") != catalog.MapSha256 ||
            map.GetProperty("elevation").GetInt32() !=
                Fo2ArroyoCavesPresentationCatalog.Elevation ||
            RequiredString(arrival, "authority") !=
                "exact Map 126 exit-grid instance values" ||
            arrival.GetProperty("tile").GetInt32() != catalog.ArrivalTile ||
            arrival.GetProperty("rotation").GetInt32() != catalog.ArrivalRotation ||
            RequiredString(semantics, "walkMaskMode") !=
                "non-default-floor-art-minus-central-source-blocking-object-hexes-v1" ||
            RequiredString(semantics, "walkMaskSha256") != catalog.WalkMaskSha256 ||
            RequiredString(semantics, "floorCollisionMode") !=
                "non-default-source-floor-patch-trimesh-v1" ||
            RequiredString(semantics, "blockedMovementMode") !=
                "source-walk-mask-kinematic-gate-v1" ||
            RequiredString(semantics, "multihexCoverage") !=
                "central-source-hex-only-unresolved" ||
            RequiredString(camera, "projection") != "orthographic-follow" ||
            promotion.GetProperty("runtimeReady").GetBoolean() ||
            promotion.GetProperty("persistentInteraction").GetBoolean() ||
            promotion.GetProperty("playerArtLoaded").GetBoolean() ||
            promotion.GetProperty("playableCampaign").GetBoolean() ||
            promotion.GetProperty("collisionParity").GetBoolean() ||
            promotion.GetProperty("walkabilityParity").GetBoolean())
            throw new InvalidOperationException(
                "Unexpected Fallout 2 Arroyo player runtime profile.");

        var profile = new Fo2ArroyoPlayerProfile(
            ProfileResourcePath,
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            RequiredString(root, "id"),
            RequiredString(semantics, "floorCollisionMode"),
            RequiredString(semantics, "blockedMovementMode"),
            Finite(player, "capsuleRadiusMeters"),
            Finite(player, "capsuleHeightMeters"),
            Finite(player, "spawnCenterHeightMeters"),
            Finite(player, "speedMetersPerSecond"),
            Finite(player, "gravityMetersPerSecondSquared"),
            Finite(player, "groundVelocityMetersPerSecond"),
            Finite(player, "floorSnapLengthMeters"),
            Finite(player, "safeMarginMeters"),
            Mathf.DegToRad(Finite(player, "maximumFloorAngleDegrees")),
            Finite(camera, "sizeMeters"),
            ReadVector(camera.GetProperty("offsetMeters")),
            Finite(camera, "lookHeightMeters"),
            Finite(camera, "nearMeters"),
            Finite(camera, "farMeters"),
            ReadBinding(input, "moveLeft"),
            ReadBinding(input, "moveRight"),
            ReadBinding(input, "moveForward"),
            ReadBinding(input, "moveBackward"),
            ParseKey(RequiredString(acceptance, "physicalKey")),
            acceptance.GetProperty("firstNeighborTile").GetInt32(),
            acceptance.GetProperty("lastWalkableTile").GetInt32(),
            acceptance.GetProperty("firstRejectedTile").GetInt32(),
            acceptance.GetProperty("blockingObjectSerial").GetInt32(),
            RequiredString(acceptance, "blockingObjectFid"),
            acceptance.GetProperty("coLocatedExitGridSerial").GetInt32(),
            acceptance.GetProperty("coLocatedExitGridObjectType").GetInt32(),
            acceptance.GetProperty("coLocatedExitGridDestination")
                .EnumerateArray().Select(row => row.GetInt32()).ToArray(),
            Finite(acceptance, "groundHeightToleranceMeters"),
            acceptance.GetProperty("minimumRejectedPhysicsFrames").GetInt32(),
            acceptance.GetProperty("maximumPhysicsFrames").GetInt32());
        var bindings = new[]
        {
            profile.MoveLeft,
            profile.MoveRight,
            profile.MoveForward,
            profile.MoveBackward,
        };
        var blockingObject = catalog.ObjectPlacements.SingleOrDefault(row =>
            row.Serial == profile.AcceptanceBlockingObjectSerial);
        var exitGrid = catalog.ObjectPlacements.SingleOrDefault(row =>
            row.Serial == profile.AcceptanceCoLocatedExitGridSerial);
        if (profile.Id != "fo2-arroyo-map-3-player-runtime-v1" ||
            profile.CapsuleRadiusMeters is <= 0.0f or > MaximumCapsuleRadiusMeters ||
            profile.CapsuleHeightMeters <= profile.CapsuleRadiusMeters * 2.0f ||
            profile.SpawnCenterHeightMeters < profile.CapsuleHeightMeters / 2.0f ||
            profile.SpeedMetersPerSecond <= 0.0f ||
            profile.GravityMetersPerSecondSquared <= 0.0f ||
            profile.GroundVelocityMetersPerSecond >= 0.0f ||
            profile.FloorSnapLengthMeters <= 0.0f ||
            profile.SafeMarginMeters <= 0.0f ||
            profile.SafeMarginMeters > profile.FloorSnapLengthMeters ||
            profile.MaximumFloorAngleRadians is <= 0.0f or >= Mathf.Pi / 2.0f ||
            profile.CameraSizeMeters <= 0.0f ||
            profile.CameraOffsetMeters.Y <= 0.0f ||
            profile.CameraLookHeightMeters < 0.0f ||
            profile.CameraNearMeters <= 0.0f ||
            profile.CameraFarMeters <= profile.CameraNearMeters ||
            bindings.Select(row => row.Action).Distinct(StringComparer.Ordinal).Count() != 4 ||
            bindings.Select(row => row.PhysicalKey).Distinct().Count() != 4 ||
            profile.AcceptanceKey != profile.MoveBackward.PhysicalKey ||
            profile.AcceptanceFirstNeighborTile !=
                Fo1HexMath.TileInDirection(catalog.ArrivalTile, 2) ||
            !catalog.Walkable[profile.AcceptanceFirstNeighborTile] ||
            !catalog.Walkable[profile.AcceptanceLastWalkableTile] ||
            catalog.Walkable[profile.AcceptanceFirstRejectedTile] ||
            profile.AcceptanceFirstRejectedTile - profile.AcceptanceLastWalkableTile !=
                Fo1HexMath.Width ||
            blockingObject is null ||
            blockingObject.Tile != profile.AcceptanceFirstRejectedTile ||
            !blockingObject.Blocking(0x10) ||
            profile.AcceptanceBlockingObjectFid.Length != FidHexCharacters ||
            profile.AcceptanceBlockingObjectFid.Any(character => !Uri.IsHexDigit(character)) ||
            blockingObject.Fid != profile.AcceptanceBlockingObjectFid ||
            exitGrid is null ||
            exitGrid.Tile != profile.AcceptanceFirstRejectedTile ||
            exitGrid.ObjectType != profile.AcceptanceCoLocatedExitGridObjectType ||
            exitGrid.Blocking(0x10) ||
            profile.AcceptanceCoLocatedExitGridDestination.Count != 4 ||
            !exitGrid.InstanceValues.SequenceEqual(
                profile.AcceptanceCoLocatedExitGridDestination) ||
            profile.AcceptanceGroundHeightToleranceMeters <= 0.0f ||
            profile.AcceptanceGroundHeightToleranceMeters >
                profile.FloorSnapLengthMeters ||
            profile.AcceptanceMinimumRejectedPhysicsFrames <= 0 ||
            profile.AcceptanceMaximumPhysicsFrames <=
                profile.AcceptanceMinimumRejectedPhysicsFrames)
            throw new InvalidOperationException(
                "Fallout 2 Arroyo player runtime dimensions or acceptance track drifted.");
        return profile;
    }

    private static Fo2ArroyoInputBinding ReadBinding(JsonElement source, string property)
    {
        var binding = source.GetProperty(property);
        return new Fo2ArroyoInputBinding(
            RequiredString(binding, "action"),
            ParseKey(RequiredString(binding, "physicalKey")));
    }

    private static Key ParseKey(string value) =>
        Enum.TryParse<Key>(value, true, out var key) && key != Key.None
            ? key
            : throw new InvalidOperationException(
                $"Fallout 2 Arroyo physical key is invalid: {value}");

    private static string RequiredString(JsonElement source, string property)
    {
        var value = source.GetProperty(property).GetString();
        return !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidOperationException(
                $"Fallout 2 Arroyo player runtime string is empty: {property}");
    }

    private static float Finite(JsonElement source, string property)
    {
        var value = source.GetProperty(property).GetSingle();
        return float.IsFinite(value)
            ? value
            : throw new InvalidOperationException(
                $"Fallout 2 Arroyo player runtime number is invalid: {property}");
    }

    private static Vector3 ReadVector(JsonElement source)
    {
        var values = source.EnumerateArray().Select(row => row.GetSingle()).ToArray();
        if (values.Length != 3 || values.Any(value => !float.IsFinite(value)))
            throw new InvalidOperationException(
                "Fallout 2 Arroyo player runtime vector is invalid.");
        return new Vector3(values[0], values[1], values[2]);
    }
}
