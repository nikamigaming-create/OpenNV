using System.Security.Cryptography;
using System.Text.Json;
using Godot;

namespace OpenNV.Runtime.Campaigns.Fallout2.CharacterStart;

internal sealed record Fo2CharacterStartSaveState(
    string Path,
    string Sha256,
    string SourceProfileId,
    Fo2CharacterSelection Character,
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
    string PresentationMode,
    Fo2ArroyoExitTransition? LastTransition,
    Fo2TempleConfrontationState? TempleConfrontation)
{
    internal const string Schema = "opennv-fo2-character-arroyo-save/v5";
    internal const string RouteMode = "chosen-one-source-exit-route-v1";
    private const string ConfrontationSchema = "opennv-fo2-character-arroyo-save/v4";
    private const string RouteSchema = "opennv-fo2-character-arroyo-save/v3";
    private const string PreviousSchema = "opennv-fo2-character-arroyo-save/v2";
    private const string PreviousRouteMode = "chosen-one-taken-to-arroyo-map-3";
    private const string LegacySchema = "opennv-fo2-character-arroyo-save/v1";
    private const string LegacyRouteMode = "owned-premade-taken-to-arroyo-map-3";

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
        Fo2TemplePresentationCatalog temple,
        Fo2ArroyoCavesPlayerRuntimeCoverage runtime,
        Fo2CharacterSelection character,
        Fo2TempleConfrontationState? templeConfrontation)
    {
        character.Validate(characterStart);
        if (characterStart.SourceProfileId != arroyo.SourceProfileId ||
            temple.SourceProfileId != arroyo.SourceProfileId ||
            runtime.Profile.Id != "fo2-arroyo-map-3-player-runtime-v1" ||
            runtime.Player.MotionMode != CharacterBody3D.MotionModeEnum.Grounded ||
            !runtime.Player.CanOccupy(runtime.Player.CurrentTile))
            throw new InvalidOperationException(
                "Fallout 2 character/Arroyo save state is not authoritative.");
        var position = runtime.Player.Position;
        if (!Finite(position) ||
            Fo1HexMath.NearestTile(new Vector3(position.X, 0.0f, position.Z)) !=
                runtime.Player.CurrentTile)
            throw new InvalidOperationException(
                "Fallout 2 player position does not resolve to its saved source tile.");

        var player = runtime.Player;
        Fo2ArroyoExitTransition? lastTransition = player.CurrentMapIndex switch
        {
            Fo2ArroyoCavesPresentationCatalog.MapIndex => null,
            Fo2TemplePresentationCatalog.MapIndex
                when player.CurrentMapSha256 == temple.MapSha256 &&
                    player.ArrivalTile == arroyo.LiveExit.TargetTile => arroyo.LiveExit,
            _ => throw new InvalidOperationException(
                "Fallout 2 active map is outside the admitted Arroyo/Temple route."),
        };
        if (player.CurrentMapIndex == Fo2ArroyoCavesPresentationCatalog.MapIndex &&
                templeConfrontation is not null ||
            player.CurrentMapIndex == Fo2TemplePresentationCatalog.MapIndex &&
                templeConfrontation is null)
            throw new InvalidOperationException(
                "Fallout 2 confrontation state does not match the active map.");
        templeConfrontation?.Validate(
            temple.Confrontation,
            Fo2TempleConfrontationRuntime.MaximumActionPoints(character));
        return new Fo2CharacterStartSaveState(
            ResolvePath(configuredPath),
            "",
            characterStart.SourceProfileId,
            character,
            runtime.Profile.Id,
            runtime.Profile.Sha256,
            player.CurrentMapSha256,
            player.CurrentWalkMaskSha256,
            player.CurrentMapIndex,
            player.CurrentElevation,
            player.ArrivalTile,
            player.CurrentTile,
            player.Presentation.Direction,
            position,
            runtime.Player.MotionMode.ToString(),
            runtime.Profile.BlockedMovementMode,
            runtime.Profile.PlayerPresentationMode,
            lastTransition,
            templeConfrontation);
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
                    Character.Mode,
                    Character.Id,
                    Character.Role,
                    SourceId = Character.Source.Id,
                    SourceRole = Character.Source.Role,
                    Character.GcdSha256,
                    Character.BioSha256,
                    Character.Profile.Name,
                    Character.Profile.Age,
                    Character.Profile.Sex,
                    special = Character.Profile.Special,
                    taggedSkills = Character.Profile.TaggedSkills,
                    traits = Character.Profile.Traits,
                },
                appearance = new
                {
                    Character.Appearance.Schema,
                    Character.Appearance.BasisPremadeId,
                    Character.Appearance.SourcePanelLogicalPath,
                    Character.Appearance.SourcePanelSha256,
                    Character.Appearance.LocalPanelPngSha256,
                    Character.Appearance.PreviewMode,
                    Character.Appearance.PortraitState,
                    Character.Appearance.CustomFaceEdited,
                    Character.Appearance.CustomPortraitGenerated,
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
                lastTransition = LastTransition is null ? null : new
                {
                    sourceMapIndex = LastTransition.SourceMapIndex,
                    sourceMapSha256 = LastTransition.SourceMapSha256,
                    sourceTile = LastTransition.SourceTile,
                    sourceElevation = LastTransition.SourceElevation,
                    exitSerial = LastTransition.ExitSerial,
                    exitFid = LastTransition.ExitFid,
                    exitPid = LastTransition.ExitPid,
                    sourcePathSha256 = LastTransition.SourcePathSha256,
                    targetMapIndex = LastTransition.TargetMapIndex,
                    targetLogicalPath = LastTransition.TargetLogicalPath,
                    targetMapSha256 = LastTransition.TargetMapSha256,
                    targetTile = LastTransition.TargetTile,
                    targetElevation = LastTransition.TargetElevation,
                    targetRotation = LastTransition.TargetRotation,
                },
                templeConfrontation = TempleConfrontation is null ? null : new
                {
                    targetHitPoints = TempleConfrontation.TargetHitPoints,
                    playerActionPoints = TempleConfrontation.PlayerActionPoints,
                    combatActive = TempleConfrontation.CombatActive,
                    spearLooted = TempleConfrontation.SpearLooted,
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
        Fo2TemplePresentationCatalog temple,
        Fo2ArroyoPlayerProfile runtimeProfile)
    {
        var path = ResolvePath(configuredPath);
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        var root = document.RootElement;
        var schema = RequiredString(root, "schema");
        var legacy = schema == LegacySchema;
        var previous = schema == PreviousSchema;
        var route = schema == RouteSchema;
        var confrontation = schema == ConfrontationSchema;
        if (schema != Schema && schema != ConfrontationSchema && schema != RouteSchema &&
                schema != PreviousSchema && schema != LegacySchema ||
            RequiredString(root, "campaign") != "Fallout2" ||
            RequiredString(root, "routeMode") !=
                (legacy ? LegacyRouteMode : previous ? PreviousRouteMode : RouteMode) ||
            RequiredString(root, "sourceProfileId") != characterStart.SourceProfileId ||
            characterStart.SourceProfileId != arroyo.SourceProfileId ||
            temple.SourceProfileId != arroyo.SourceProfileId)
            throw new InvalidOperationException(
                "Fallout 2 save does not match the active owned source profile.");

        var savedCharacter = root.GetProperty("character");
        var mode = legacy
            ? Fo2CharacterSelection.PremadeMode
            : RequiredString(savedCharacter, "Mode");
        var sourceId = legacy
            ? RequiredString(savedCharacter, "Id")
            : RequiredString(savedCharacter, "SourceId");
        var source = characterStart.Characters.SingleOrDefault(row => row.Id == sourceId) ??
            throw new InvalidOperationException(
                $"Fallout 2 save names an unavailable owned source basis: {sourceId}");
        if (RequiredString(savedCharacter, legacy ? "Role" : "SourceRole") != source.Role ||
            RequiredString(savedCharacter, "GcdSha256") != source.GcdSha256 ||
            RequiredString(savedCharacter, "BioSha256") != source.BioSha256)
            throw new InvalidOperationException(
                "Fallout 2 saved character source basis differs from its owned GCD/BIO state.");
        var profile = new Fo2CharacterProfile(
            RequiredString(savedCharacter, "Name"),
            savedCharacter.GetProperty("Age").GetInt32(),
            RequiredString(savedCharacter, "Sex"),
            ReadInts(savedCharacter.GetProperty("special")),
            ReadStrings(savedCharacter.GetProperty("taggedSkills")),
            ReadStrings(savedCharacter.GetProperty("traits")));
        var character = new Fo2CharacterSelection(mode, source, profile);
        character.Validate(characterStart);
        if (RequiredString(savedCharacter, "Id") != character.Id ||
            RequiredString(savedCharacter, "Role") != character.Role)
            throw new InvalidOperationException(
                "Fallout 2 saved character route identity drifted.");
        ReadAppearance(root, schema, character);

        var world = root.GetProperty("world");
        var mapIndex = world.GetProperty("mapIndex").GetInt32();
        var elevation = world.GetProperty("elevation").GetInt32();
        var arrivalTile = world.GetProperty("arrivalTile").GetInt32();
        var currentTile = world.GetProperty("currentTile").GetInt32();
        var rotation = world.GetProperty("rotation").GetInt32();
        var position = ReadVector(world.GetProperty("position"));
        var lastTransition = ReadLastTransition(root, schema, arroyo);
        var templeConfrontation = ReadTempleConfrontation(
            root,
            schema,
            mapIndex,
            character,
            temple);
        var tileInRange = currentTile is >= 0 and < Fo1HexMath.Width * Fo1HexMath.Height;
        var mapIdentityValid = mapIndex switch
        {
            Fo2ArroyoCavesPresentationCatalog.MapIndex =>
                elevation == Fo2ArroyoCavesPresentationCatalog.Elevation &&
                arrivalTile == arroyo.ArrivalTile &&
                RequiredString(world, "mapSha256") == arroyo.MapSha256 &&
                RequiredString(world, "walkMaskSha256") == arroyo.WalkMaskSha256 &&
                tileInRange && arroyo.Walkable[currentTile] && lastTransition is null,
            Fo2TemplePresentationCatalog.MapIndex =>
                (schema == Schema || confrontation || route) &&
                elevation == arroyo.LiveExit.TargetElevation &&
                arrivalTile == arroyo.LiveExit.TargetTile &&
                RequiredString(world, "mapSha256") == temple.MapSha256 &&
                lastTransition == arroyo.LiveExit,
            _ => false,
        };
        if (!tileInRange ||
            !mapIdentityValid ||
            rotation is < 0 or >= Fo1HexMath.DirectionCount ||
            Fo1HexMath.NearestTile(new Vector3(position.X, 0.0f, position.Z)) != currentTile ||
            MathF.Abs(position.Y - runtimeProfile.SpawnCenterHeightMeters) >
                runtimeProfile.FloorSnapLengthMeters)
            throw new InvalidOperationException(
                "Fallout 2 saved player state is outside the admitted Arroyo/Temple route.");

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
            RequiredString(world, "mapSha256"),
            RequiredString(world, "walkMaskSha256"),
            mapIndex,
            elevation,
            arrivalTile,
            currentTile,
            rotation,
            position,
            motionMode,
            blockedMovementMode,
            presentationMode,
            lastTransition,
            templeConfrontation);
    }

    private static Fo2ArroyoExitTransition? ReadLastTransition(
        JsonElement root,
        string schema,
        Fo2ArroyoCavesPresentationCatalog arroyo)
    {
        if (schema != Schema && schema != ConfrontationSchema && schema != RouteSchema)
            return null;
        var value = root.GetProperty("lastTransition");
        if (value.ValueKind == JsonValueKind.Null)
            return null;
        var expected = arroyo.LiveExit;
        if (value.GetProperty("sourceMapIndex").GetInt32() != expected.SourceMapIndex ||
            RequiredString(value, "sourceMapSha256") != expected.SourceMapSha256 ||
            value.GetProperty("sourceTile").GetInt32() != expected.SourceTile ||
            value.GetProperty("sourceElevation").GetInt32() != expected.SourceElevation ||
            value.GetProperty("exitSerial").GetInt32() != expected.ExitSerial ||
            RequiredString(value, "exitFid") != expected.ExitFid ||
            RequiredString(value, "exitPid") != expected.ExitPid ||
            RequiredString(value, "sourcePathSha256") != expected.SourcePathSha256 ||
            value.GetProperty("targetMapIndex").GetInt32() != expected.TargetMapIndex ||
            RequiredString(value, "targetLogicalPath") != expected.TargetLogicalPath ||
            RequiredString(value, "targetMapSha256") != expected.TargetMapSha256 ||
            value.GetProperty("targetTile").GetInt32() != expected.TargetTile ||
            value.GetProperty("targetElevation").GetInt32() != expected.TargetElevation ||
            value.GetProperty("targetRotation").GetInt32() != expected.TargetRotation)
            throw new InvalidOperationException(
                "Fallout 2 saved transition differs from the owned exit-grid contract.");
        return expected;
    }

    private static Fo2TempleConfrontationState? ReadTempleConfrontation(
        JsonElement root,
        string schema,
        int mapIndex,
        Fo2CharacterSelection character,
        Fo2TemplePresentationCatalog temple)
    {
        if (schema != Schema && schema != ConfrontationSchema)
            return null;
        var value = root.GetProperty("templeConfrontation");
        if (mapIndex == Fo2ArroyoCavesPresentationCatalog.MapIndex)
        {
            if (value.ValueKind != JsonValueKind.Null)
                throw new InvalidOperationException(
                    "Fallout 2 Arroyo save contains Temple confrontation state.");
            return null;
        }
        if (mapIndex != Fo2TemplePresentationCatalog.MapIndex ||
            value.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException(
                "Fallout 2 Temple save is missing confrontation state.");
        var state = new Fo2TempleConfrontationState(
            value.GetProperty("targetHitPoints").GetInt32(),
            value.GetProperty("playerActionPoints").GetInt32(),
            value.GetProperty("combatActive").GetBoolean(),
            value.GetProperty("spearLooted").GetBoolean());
        state.Validate(
            temple.Confrontation,
            Fo2TempleConfrontationRuntime.MaximumActionPoints(character));
        return state;
    }

    private static void ReadAppearance(
        JsonElement root,
        string schema,
        Fo2CharacterSelection character)
    {
        if (schema != Schema)
            return;
        var value = root.GetProperty("appearance");
        var appearance = new Fo2CharacterAppearanceContract(
            RequiredString(value, "Schema"),
            RequiredString(value, "BasisPremadeId"),
            RequiredString(value, "SourcePanelLogicalPath"),
            RequiredString(value, "SourcePanelSha256"),
            RequiredString(value, "LocalPanelPngSha256"),
            RequiredString(value, "PreviewMode"),
            RequiredString(value, "PortraitState"),
            value.GetProperty("CustomFaceEdited").GetBoolean(),
            value.GetProperty("CustomPortraitGenerated").GetBoolean());
        appearance.Validate(character);
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
