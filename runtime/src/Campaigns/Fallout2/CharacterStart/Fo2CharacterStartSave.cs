using System.Security.Cryptography;
using System.Text.Json;
using Godot;

namespace OpenNV.Runtime.Campaigns.Fallout2.CharacterStart;

internal sealed record Fo2CharacterStartSaveState(
    string Path,
    string Sha256,
    string SourceProfileId,
    Fo2PremadeCharacter Character,
    string RuntimeProfileId,
    string RuntimeProfileSha256,
    string MapSha256,
    string WalkMaskSha256,
    int MapIndex,
    int Elevation,
    int ArrivalTile,
    int CurrentTile,
    int Rotation,
    Vector3 Position,
    string MotionMode,
    string BlockedMovementMode,
    string PresentationMode)
{
    internal const string Schema = "opennv-fo2-character-arroyo-save/v1";
    internal const string RouteMode = "owned-premade-taken-to-arroyo-map-3";

    internal static string DefaultPath => System.IO.Path.Combine(
        System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
        "OpenNV",
        "saves",
        "fallout2",
        "character-arroyo-v1.json");

    internal static bool Exists(string configuredPath) => File.Exists(ResolvePath(configuredPath));

    internal static Fo2CharacterStartSaveState Capture(
        string configuredPath,
        Fo2CharacterStartCatalog characterStart,
        Fo2ArroyoCavesPresentationCatalog arroyo,
        Fo2ArroyoCavesPlayerRuntimeCoverage runtime,
        Fo2PremadeCharacter character)
    {
        if (!characterStart.Characters.Contains(character) ||
            characterStart.SourceProfileId != arroyo.SourceProfileId ||
            runtime.Profile.Id != "fo2-arroyo-map-3-player-runtime-v1" ||
            runtime.Player.MotionMode != CharacterBody3D.MotionModeEnum.Grounded ||
            !runtime.Player.CanOccupy(runtime.Player.CurrentTile))
            throw new InvalidOperationException(
                "Fallout 2 character/Arroyo save state is not authoritative.");
        character.Profile.Validate();
        var position = runtime.Player.Position;
        if (!Finite(position) ||
            Fo1HexMath.NearestTile(new Vector3(position.X, 0.0f, position.Z)) !=
                runtime.Player.CurrentTile)
            throw new InvalidOperationException(
                "Fallout 2 player position does not resolve to its saved source tile.");

        return new Fo2CharacterStartSaveState(
            ResolvePath(configuredPath),
            "",
            characterStart.SourceProfileId,
            character,
            runtime.Profile.Id,
            runtime.Profile.Sha256,
            arroyo.MapSha256,
            arroyo.WalkMaskSha256,
            Fo2ArroyoCavesPresentationCatalog.MapIndex,
            Fo2ArroyoCavesPresentationCatalog.Elevation,
            runtime.Player.ArrivalTile,
            runtime.Player.CurrentTile,
            runtime.Player.Presentation.Direction,
            position,
            runtime.Player.MotionMode.ToString(),
            runtime.Profile.BlockedMovementMode,
            runtime.Profile.PlayerPresentationMode);
    }

    internal Fo2CharacterStartSaveState Write()
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        var temporary = Path + ".tmp";
        try
        {
            var state = new
            {
                schema = Schema,
                campaign = "Fallout2",
                routeMode = RouteMode,
                sourceProfileId = SourceProfileId,
                character = new
                {
                    Character.Id,
                    Character.Role,
                    Character.GcdSha256,
                    Character.BioSha256,
                    Character.Profile.Name,
                    Character.Profile.Age,
                    Character.Profile.Sex,
                    special = Character.Profile.Special,
                    taggedSkills = Character.Profile.TaggedSkills,
                    traits = Character.Profile.Traits,
                },
                world = new
                {
                    mapIndex = MapIndex,
                    elevation = Elevation,
                    arrivalTile = ArrivalTile,
                    currentTile = CurrentTile,
                    rotation = Rotation,
                    position = new[] { Position.X, Position.Y, Position.Z },
                    mapSha256 = MapSha256,
                    walkMaskSha256 = WalkMaskSha256,
                },
                runtime = new
                {
                    profileId = RuntimeProfileId,
                    profileSha256 = RuntimeProfileSha256,
                    motionMode = MotionMode,
                    blockedMovementMode = BlockedMovementMode,
                    presentationMode = PresentationMode,
                },
            };
            File.WriteAllText(
                temporary,
                JsonSerializer.Serialize(
                    state,
                    new JsonSerializerOptions { WriteIndented = true }) +
                    System.Environment.NewLine);
            File.Move(temporary, Path, true);
            return this with { Sha256 = FileSha256(Path) };
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    internal static Fo2CharacterStartSaveState Load(
        string configuredPath,
        Fo2CharacterStartCatalog characterStart,
        Fo2ArroyoCavesPresentationCatalog arroyo,
        Fo2ArroyoPlayerProfile runtimeProfile)
    {
        var path = ResolvePath(configuredPath);
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        var root = document.RootElement;
        if (RequiredString(root, "schema") != Schema ||
            RequiredString(root, "campaign") != "Fallout2" ||
            RequiredString(root, "routeMode") != RouteMode ||
            RequiredString(root, "sourceProfileId") != characterStart.SourceProfileId ||
            characterStart.SourceProfileId != arroyo.SourceProfileId)
            throw new InvalidOperationException(
                "Fallout 2 save does not match the active owned source profile.");

        var savedCharacter = root.GetProperty("character");
        var id = RequiredString(savedCharacter, "Id");
        var character = characterStart.Characters.SingleOrDefault(row => row.Id == id) ??
            throw new InvalidOperationException(
                $"Fallout 2 save names an unavailable owned premade: {id}");
        var profile = character.Profile;
        if (RequiredString(savedCharacter, "Role") != character.Role ||
            RequiredString(savedCharacter, "GcdSha256") != character.GcdSha256 ||
            RequiredString(savedCharacter, "BioSha256") != character.BioSha256 ||
            RequiredString(savedCharacter, "Name") != profile.Name ||
            savedCharacter.GetProperty("Age").GetInt32() != profile.Age ||
            RequiredString(savedCharacter, "Sex") != profile.Sex ||
            !ReadInts(savedCharacter.GetProperty("special")).SequenceEqual(profile.Special) ||
            !ReadStrings(savedCharacter.GetProperty("taggedSkills"))
                .SequenceEqual(profile.TaggedSkills) ||
            !ReadStrings(savedCharacter.GetProperty("traits")).SequenceEqual(profile.Traits))
            throw new InvalidOperationException(
                "Fallout 2 saved premade state differs from its owned GCD/BIO source.");

        var world = root.GetProperty("world");
        var mapIndex = world.GetProperty("mapIndex").GetInt32();
        var elevation = world.GetProperty("elevation").GetInt32();
        var arrivalTile = world.GetProperty("arrivalTile").GetInt32();
        var currentTile = world.GetProperty("currentTile").GetInt32();
        var rotation = world.GetProperty("rotation").GetInt32();
        var position = ReadVector(world.GetProperty("position"));
        if (mapIndex != Fo2ArroyoCavesPresentationCatalog.MapIndex ||
            elevation != Fo2ArroyoCavesPresentationCatalog.Elevation ||
            arrivalTile != arroyo.ArrivalTile ||
            currentTile is < 0 or >= Fo1HexMath.Width * Fo1HexMath.Height ||
            !arroyo.Walkable[currentTile] ||
            rotation is < 0 or >= Fo1HexMath.DirectionCount ||
            RequiredString(world, "mapSha256") != arroyo.MapSha256 ||
            RequiredString(world, "walkMaskSha256") != arroyo.WalkMaskSha256 ||
            Fo1HexMath.NearestTile(new Vector3(position.X, 0.0f, position.Z)) != currentTile ||
            MathF.Abs(position.Y - runtimeProfile.SpawnCenterHeightMeters) >
                runtimeProfile.FloorSnapLengthMeters)
            throw new InvalidOperationException(
                "Fallout 2 saved Map 3 player state is outside the admitted Arroyo runtime.");

        var runtime = root.GetProperty("runtime");
        var motionMode = RequiredString(runtime, "motionMode");
        var blockedMovementMode = RequiredString(runtime, "blockedMovementMode");
        var presentationMode = RequiredString(runtime, "presentationMode");
        if (RequiredString(runtime, "profileId") != runtimeProfile.Id ||
            RequiredString(runtime, "profileSha256") != runtimeProfile.Sha256 ||
            motionMode != CharacterBody3D.MotionModeEnum.Grounded.ToString() ||
            blockedMovementMode != runtimeProfile.BlockedMovementMode ||
            presentationMode != runtimeProfile.PlayerPresentationMode)
            throw new InvalidOperationException(
                "Fallout 2 saved runtime mode differs from the admitted Map 3 profile.");

        return new Fo2CharacterStartSaveState(
            path,
            FileSha256(path),
            characterStart.SourceProfileId,
            character,
            runtimeProfile.Id,
            runtimeProfile.Sha256,
            arroyo.MapSha256,
            arroyo.WalkMaskSha256,
            mapIndex,
            elevation,
            arrivalTile,
            currentTile,
            rotation,
            position,
            motionMode,
            blockedMovementMode,
            presentationMode);
    }

    private static string ResolvePath(string configuredPath)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
            throw new ArgumentException("Fallout 2 save path is empty.", nameof(configuredPath));
        if (configuredPath.StartsWith("user://", StringComparison.Ordinal))
            return ProjectSettings.GlobalizePath(configuredPath);
        var path = System.IO.Path.GetFullPath(configuredPath);
        var localRoot = System.IO.Path.GetFullPath(System.IO.Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
            "OpenNV"));
        if (!path.StartsWith(
                localRoot + System.IO.Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Fallout 2 saves must stay in the OpenNV user-data sandbox.");
        return path;
    }

    private static string RequiredString(JsonElement source, string property)
    {
        var value = source.GetProperty(property).GetString();
        return !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidOperationException(
                $"Fallout 2 save string is empty: {property}");
    }

    private static int[] ReadInts(JsonElement source) =>
        source.EnumerateArray().Select(row => row.GetInt32()).ToArray();

    private static string[] ReadStrings(JsonElement source) =>
        source.EnumerateArray().Select(row => row.GetString() ?? "").ToArray();

    private static Vector3 ReadVector(JsonElement source)
    {
        var values = source.EnumerateArray().Select(row => row.GetSingle()).ToArray();
        if (values.Length != 3 || values.Any(value => !float.IsFinite(value)))
            throw new InvalidOperationException("Fallout 2 saved player position is invalid.");
        return new Vector3(values[0], values[1], values[2]);
    }

    private static bool Finite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private static string FileSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
